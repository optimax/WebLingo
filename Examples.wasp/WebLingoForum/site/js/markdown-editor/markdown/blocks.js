// The paragraph types as edits of the source (the web counterpart of MaxPad's ParagraphService): a paragraph made a
// heading, a quote, an item of a list or a task, a block of code -- or back into a paragraph -- and the blocks a page
// inserts (a rule, a table, a block of code, a link, a picture). Each gives `{ source, caret, anchor }` or null.

import { MarkdownState, lineEndOf } from './state.js';
import { contentOf } from './commands.js';

export const BLOCK_TYPES = ['paragraph', 'h1', 'h2', 'h3', 'h4', 'h5', 'h6', 'quote', 'bullet', 'number', 'task', 'code'];

const order = (selection) =>
    selection.anchor <= selection.caret ? [selection.anchor, selection.caret] : [selection.caret, selection.anchor];

/** What the paragraph at the caret is: one of BLOCK_TYPES, or 'table', 'rule', 'html', 'blank'. */
export function blockTypeAt(state, offset) {
    const p = state.paragraphAt(offset);
    if (!p) return 'paragraph';
    switch (p.kind) {
        case 'heading': return 'h' + p.block.level;
        case 'code': case 'fence': case 'fence-close': return 'code';
        case 'cell': return 'table';
        case 'rule': return 'rule';
        case 'html': return 'html';
        case 'blank': return 'blank';
        default: break;
    }
    const item = itemOf(p);
    if (item) return item.task ? 'task' : item.parent.ordered ? 'number' : 'bullet';
    if (state.marks(p.firstLine).some((mark) => mark.kind === 'quote')) return 'quote';
    return 'paragraph';
}

function itemOf(p) {
    for (let up = p.block?.parent; up; up = up.parent) if (up.type === 'item') return up;
    return null;
}

const textParagraph = (p) => p.kind === 'paragraph' || p.kind === 'heading';

/**
 * Makes the paragraphs the selection touches (or the caret's) `type`; when they all are already, makes them paragraphs
 * again (a quote loses one level of quote).
 */
export function setBlockType(state, selection, type) {
    const [a, b] = order(selection);
    const first = state.paragraphIndexAt(a), last = state.paragraphIndexAt(b);
    const touched = state.paragraphs.slice(first, last + 1);
    if (type === 'code') return toggleCodeBlock(state, touched, a);
    const paragraphs = touched.filter(textParagraph);
    if (!paragraphs.length) return null;
    const all = paragraphs.every((p) => blockTypeAt(state, p.start) === type);
    const target = all ? 'paragraph' : type;
    return rewriteAll(state, selection, paragraphs, () => target, all && type === 'quote');
}

// The heading level of a paragraph of text: 1 to 6 for a heading, 0 for any other.
const levelOf = (p) => (p.kind === 'heading' ? p.block.level : 0);

const typeOfLevel = (level) => (level === 0 ? 'paragraph' : 'h' + level);

/**
 * MaxPad's Ctrl+1 to Ctrl+6, and Ctrl+0: the paragraphs the selection touches (the caret's) made headings of `level`, or
 * paragraphs for 0. Not a toggle: a paragraph that is that already is left as it is.
 */
export function setHeadingLevel(state, selection, level) {
    if (!(level >= 0 && level <= 6)) return null;
    const paragraphs = touchedText(state, selection).filter((p) => levelOf(p) !== level);
    return rewriteAll(state, selection, paragraphs, () => typeOfLevel(level), false);
}

/**
 * MaxPad's Ctrl+= (`by` 1: one level deeper, a paragraph becoming Heading 1) and Ctrl+- (`by` -1: one level shallower,
 * Heading 1 becoming a paragraph), for each paragraph the selection touches; one that can go no further stays.
 */
export function stepHeadingLevel(state, selection, by) {
    const paragraphs = touchedText(state, selection).filter((p) => levelOf(p) + by >= 0 && levelOf(p) + by <= 6);
    return rewriteAll(state, selection, paragraphs, (p) => typeOfLevel(levelOf(p) + by), false);
}

function touchedText(state, selection) {
    const [a, b] = order(selection);
    return state.paragraphs.slice(state.paragraphIndexAt(a), state.paragraphIndexAt(b) + 1).filter(textParagraph);
}

// The paragraphs written again, each as `targetOf` says, in one edit; the caret and the selection keep their places in
// their paragraphs' text.
function rewriteAll(state, selection, paragraphs, targetOf, unquote) {
    let source = state.source;
    const changes = [];
    let number = 1;
    for (const p of paragraphs) {
        const change = rewrite(state, p, targetOf(p), number, unquote);
        if (change) {
            changes.push(change);
            number++;
        }
    }
    if (!changes.length) return null;
    // (the caret keeps its place in its paragraph's text)
    const mapOffset = (offset) => {
        let shift = 0;
        for (const change of changes) {
            if (offset >= change.end) shift += change.text.length - (change.end - change.start);
            else if (offset >= change.start) {
                const inText = Math.max(0, offset - change.contentStart);
                return change.start + shift + Math.min(change.text.length, change.newContentStart + inText);
            }
        }
        return offset + shift;
    };
    for (const change of [...changes].reverse()) source = source.slice(0, change.start) + change.text + source.slice(change.end);
    return { source, caret: mapOffset(selection.caret), anchor: mapOffset(selection.anchor) };
}

// One paragraph written again as `target`: its lines, what its containers put in front of them kept.
function rewrite(state, p, target, number, unquote) {
    const own = p.itemLine ?? null;
    const lastLine = p.block.setext ? p.lastLine - 1 : p.lastLine;
    const lines = [];
    for (let line = p.firstLine; line <= lastLine; line++) {
        let marks = state.marks(line).filter((mark) => !(own && mark.block === own));
        if (unquote) {
            const quotes = marks.filter((mark) => mark.kind === 'quote');
            if (quotes.length) marks = marks.filter((mark) => mark !== quotes[quotes.length - 1]);
        }
        const outer = marks.map((mark) =>
            (mark.kind === 'item' ? ' '.repeat(mark.end - mark.start) : state.source.slice(mark.start, mark.end))).join('');
        let start = line === p.firstLine ? p.contentStart : state.marksEnd(line);
        let end = line === p.firstLine && p.kind === 'heading' && !p.block.setext ? p.block.contentEnd : state.lineEnd(line);
        if (line === p.firstLine && p.task) start = p.contentStart;
        while (start < end && (state.source[start] === ' ' || state.source[start] === '\t')) start++;
        lines.push({ outer, content: state.source.slice(start, end), contentStart: start });
    }
    const [head, ...rest] = lines;
    let text, contentOffset;
    const continuation = (width) => rest.map((line) => '\n' + line.outer + ' '.repeat(width) + line.content).join('');
    switch (target) {
        case 'quote':
            text = head.outer + '> ' + head.content + rest.map((line) => '\n' + line.outer + '> ' + line.content).join('');
            contentOffset = head.outer.length + 2;
            break;
        case 'bullet':
            text = head.outer + '- ' + head.content + continuation(2);
            contentOffset = head.outer.length + 2;
            break;
        case 'task':
            text = head.outer + '- [ ] ' + head.content + continuation(2);
            contentOffset = head.outer.length + 6;
            break;
        case 'number': {
            const marker = number + '. ';
            text = head.outer + marker + head.content + continuation(marker.length);
            contentOffset = head.outer.length + marker.length;
            break;
        }
        case 'paragraph':
            text = head.outer + head.content + rest.map((line) => '\n' + line.outer + line.content).join('');
            contentOffset = head.outer.length;
            break;
        default: {
            const level = /^h([1-6])$/.exec(target);
            if (!level) return null;
            const hashes = '#'.repeat(+level[1]) + ' ';
            text = head.outer + hashes + [head.content, ...rest.map((line) => line.content)].join(' ');
            contentOffset = head.outer.length + hashes.length;
        }
    }
    const start = state.lineStart(p.firstLine);
    let end = state.lineEnd(p.lastLine);
    // (an item made something else after another item: a blank line keeps it from going on in that item)
    if (own && target !== 'bullet' && target !== 'number' && target !== 'task' && p.firstLine > 0 &&
        !state.isBlankLine(p.firstLine - 1)) {
        const bp = state.blankPrefix(p.firstLine, state.marks(p.firstLine).find((mark) => mark.block === own)?.start);
        text = bp + '\n' + text;
        contentOffset += bp.length + 1;
    }
    if (text === state.source.slice(start, end)) return null;
    return { start, end, text, contentStart: head.contentStart, newContentStart: contentOffset };
}

// A block of code round the paragraphs touched, or the fences of the block the caret is in taken away.
function toggleCodeBlock(state, touched, caret) {
    const code = touched.find((p) => p.kind === 'code' || p.kind === 'fence' || p.kind === 'fence-close');
    if (code && code.block.fenced) {
        const block = code.block;
        let source = state.source;
        let shift = 0;
        for (const line of [...block.fenceLines].sort((x, y) => y - x)) {
            const start = line > 0 ? state.lineEnd(line - 1) : 0;
            const end = line > 0 ? state.lineEnd(line) : Math.min(state.source.length, state.lineEnd(line) + 1);
            source = source.slice(0, start) + source.slice(end);
            if (start < caret) shift += end - start;
        }
        const at = Math.max(0, caret - shift);
        return { source, caret: at, anchor: at };
    }
    const text = touched.filter((p) => p.kind !== 'cell');
    if (!text.length) return null;
    const firstLine = text[0].firstLine, lastLine = text[text.length - 1].lastLine;
    const prefix = state.continuationPrefix(firstLine);
    const start = state.lineStart(firstLine), end = state.lineEnd(lastLine);
    if (text.length === 1 && text[0].kind === 'blank') {
        const insert = prefix + '```\n' + prefix + '\n' + prefix + '```';
        const source = state.replaced(start, end, insert);
        const at = start + prefix.length + 4 + prefix.length;
        return { source, caret: at, anchor: at };
    }
    const body = state.source.slice(start, end);
    const source = state.replaced(start, end, prefix + '```\n' + body + '\n' + prefix + '```');
    const at = start + prefix.length + 3;
    return { source, caret: at, anchor: at };
}

// ---- Inserting blocks --------------------------------------------------------------------------------------------

// `lines` (each written after the containers' prefix) put after the block the caret is in, as a block of their own; the
// caret where `place` says ({ line, column } of what was inserted).
function insertAfterBlock(state, offset, lines, place) {
    const p = state.paragraphAt(offset);
    const bp = state.blankPrefix(p.firstLine);
    const lp = state.continuationPrefix(p.firstLine);
    if (p.kind === 'blank') {
        // (on an empty paragraph: in its place, a blank line after it if a block follows)
        const start = state.lineStart(p.firstLine);
        const body = lines.map((line) => lp + line).join('\n');
        const after = p.lastLine + 1 < state.lines.length && !state.isBlankLine(p.lastLine + 1) ? '\n' + bp : '';
        const source = state.replaced(start, state.lineEnd(p.lastLine), body + after);
        const before = state.source.slice(0, start);
        const lineIndex = before.split('\n').length - 1 + place.line;
        return { source, caret: lineStartOf(source, lineIndex) + lp.length + place.column };
    }
    const at = state.lineEnd(p.lastLine);
    const body = lines.map((line) => lp + line).join('\n');
    const separated = state.isBlankLineOf(p.lastLine + 1, bp) || p.lastLine + 1 >= state.lines.length;
    const insert = '\n' + bp + '\n' + body + (separated ? '' : '\n' + bp);
    const source = state.replaced(at, at, insert);
    const lineIndex = p.lastLine + 2 + place.line;
    return { source, caret: lineStartOf(source, lineIndex) + lp.length + place.column };
}

function lineStartOf(source, line) {
    return line === 0 ? 0 : lineEndOf(source, line - 1) + 1;
}

/** A horizontal rule after the block at the caret. */
export function insertRule(state, selection) {
    const placed = insertAfterBlock(state, order(selection)[1], ['---', ''], { line: 1, column: 0 });
    return { ...placed, anchor: placed.caret };
}

/** A table of `columns` columns and `rows` empty rows after the block at the caret, the caret in its first cell. */
export function insertTable(state, selection, columns = 3, rows = 2) {
    const header = '|' + Array.from({ length: columns }, (_, k) => ` Column ${k + 1} |`).join('');
    const delimiter = '|' + ' --- |'.repeat(columns);
    const body = Array.from({ length: rows }, () => '|' + '  |'.repeat(columns));
    const placed = insertAfterBlock(state, order(selection)[1], [header, delimiter, ...body], { line: 0, column: 2 });
    const caret = placed.caret;
    return { source: placed.source, caret: caret + 'Column 1'.length, anchor: caret };
}

/** A block of code (the selection's paragraphs in one, or an empty one after the caret's block). */
export function insertCodeBlock(state, selection, language = '') {
    const [a, b] = order(selection);
    if (a !== b) return setBlockType(state, selection, 'code');
    const placed = insertAfterBlock(state, b, ['```' + language, '', '```'], { line: 1, column: 0 });
    return { ...placed, anchor: placed.caret };
}

/**
 * A link (MaxPad's SetLink, Ctrl+K). In a link -- the caret, or the whole of the selection, inside it -- its address
 * changed: its text and its title kept (the title given instead, if one is), a reference link or an autolink made an
 * inline link, and its text selected; an empty address takes the link away and leaves its text, selected. Elsewhere the
 * selection made the text of a link to the address (or the address its own text), `[text](address)`, the caret after it.
 */
export function insertLink(state, selection, address, title = '') {
    const [a, b] = order(selection);
    const link = linkAt(state, a, b);
    if (link) {
        const [textStart, textEnd] = contentOf(link);
        const label = state.source.slice(textStart, textEnd);
        const written = address ? writtenLink(label, address, title || link.title || '') : label;
        const labelStart = address ? link.start + 1 : link.start;
        const source = state.replaced(link.start, link.end, written);
        return { source, anchor: labelStart, caret: labelStart + label.length };
    }
    if (!address) return null;
    const written = writtenLink(state.source.slice(a, b) || address, address, title);
    const source = state.replaced(a, b, written);
    return { source, caret: a + written.length, anchor: a + written.length };
}

/**
 * The link the caret, or the whole of a selection, is inside of (MaxPad's LinkAt): an inline or a reference link or an
 * autolink -- not a picture -- whose syntax is round [a, b] on both sides. Null when there is none.
 */
export function linkAt(state, a, b = a) {
    const block = state.paragraphAt(a)?.block;
    if (!block) return null;
    let found = null;
    const walk = (nodes) => {
        for (const node of nodes ?? []) {
            if (node.end < a || node.start > b) continue;
            if ((node.type === 'link' || node.type === 'autolink') && node.start < a && b < node.end) found = node;
            if (node.children) walk(node.children);
        }
    };
    walk(block.inlines);
    if (block.rows) for (const row of block.rows) for (const cell of row.cells) walk(cell.inlines);
    return found;
}

// A link as it is written: `[label](address "title")`.
function writtenLink(label, address, title) {
    const titled = title ? ` "${title.replace(/\\/g, '\\\\').replace(/"/g, '\\"')}"` : '';
    return `[${label}](${writtenDestination(address)}${titled})`;
}

// A link's or a picture's address as it is written: in angle brackets when it has a space, a parenthesis or an angle
// bracket, its angle brackets percent-encoded (an address in angle brackets can have none).
function writtenDestination(address) {
    return /[\s()<>]/.test(address) ? `<${address.replace(/</g, '%3C').replace(/>/g, '%3E')}>` : address;
}

/**
 * A picture, `![description](address)`, at the caret: the description is text, its brackets and backslashes escaped, and
 * the address is written as a link's.
 */
export function insertImage(state, selection, address, description = '') {
    const [a, b] = order(selection);
    const text = String(description ?? '').replace(/[\\[\]]/g, '\\$&');
    const image = `![${text}](${writtenDestination(String(address))})`;
    const source = state.replaced(a, b, image);
    return { source, caret: a + image.length, anchor: a + image.length };
}

export { MarkdownState };
