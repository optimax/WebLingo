// What each edit command does to the Markdown source: pure functions of a state (the source, read) and a selection
// ({ anchor, caret }, offsets in the source) that give the source after the command and where the caret is in it --
// `{ source, caret, anchor }` -- or null when the command does nothing there. The rules are MaxPad's
// (MarkdownTools/Documents/maxpad-editing.md, section 2): Enter and Shift+Enter by the block, Backspace and Delete at
// a block's edges, lists, quotes, code, tables, and blank lines (the first blank line after a block only separates it
// from what follows; each further one is an empty paragraph).

import { MarkdownState, lineEndOf } from './state.js';
import { isRevealed } from './projection.js';
import { backspaceStart, deleteEnd, previousWordBoundary, nextWordBoundary } from '../view/text.js';

const ordered = (selection) =>
    selection.anchor <= selection.caret ? [selection.anchor, selection.caret] : [selection.caret, selection.anchor];

const result = (source, caret, anchor = caret) => ({ source, caret, anchor });

const isTextKind = (paragraph) => paragraph.kind === 'paragraph' || paragraph.kind === 'heading';

// ---- Selections ------------------------------------------------------------------------------------------------------

/**
 * The range a selection takes of the source when it is deleted, cut or copied: a construct whose text the selection takes
 * all of is taken with its syntax (selecting "bold" in **bold** takes the asterisks too), and a selection from the start
 * of a paragraph's text takes what its block puts in front of it ("# ", "- ").
 */
export function selectedRange(state, selection) {
    let [a, b] = ordered(selection);
    if (a === b) return [a, b];
    for (let round = 0; round < 8; round++) {
        let changed = false;
        for (const construct of constructsAround(state, a, b)) {
            const [contentStart, contentEnd] = contentOf(construct);
            if (a <= contentStart && b >= contentEnd && (a > construct.start || b < construct.end)) {
                a = Math.min(a, construct.start);
                b = Math.max(b, construct.end);
                changed = true;
            }
        }
        if (!changed) break;
    }
    const first = state.paragraphAt(a);
    if (first && a <= first.contentStart && a > first.start && b > first.contentStart && isTextKind(first)) a = first.start;
    return [a, b];
}

// The inline constructs of the paragraphs from a to b that reach either of them.
function constructsAround(state, a, b) {
    const out = [];
    const firstIndex = state.paragraphIndexAt(a), lastIndex = state.paragraphIndexAt(b);
    const leaves = new Set();
    for (let i = firstIndex; i <= lastIndex; i++) {
        const block = state.paragraphs[i]?.block;
        if (block && !leaves.has(block)) leaves.add(block);
    }
    const walk = (nodes) => {
        for (const node of nodes ?? []) {
            if (node.end < a || node.start > b) continue;
            if (hasDelimiters(node)) out.push(node);
            if (node.children) walk(node.children);
        }
    };
    for (const leaf of leaves) {
        walk(leaf.inlines);
        if (leaf.rows) for (const row of leaf.rows) for (const cell of row.cells) walk(cell.inlines);
    }
    return out;
}

const hasDelimiters = (node) => ['em', 'strong', 'underline', 'strike', 'sub', 'sup', 'code', 'link', 'image', 'autolink']
    .includes(node.type);

/** Where a construct's own text is: [start, end) inside its delimiters. */
export function contentOf(node) {
    switch (node.type) {
        case 'code':
        case 'autolink':
            return [node.contentStart, node.contentEnd];
        case 'link':
        case 'image':
            return [node.textStart, node.textEnd];
        default:
            return [node.start + (node.delimiter ?? 0), node.end - (node.delimiter ?? 0)];
    }
}

/** The Markdown a selection copies (its source, with the syntax of what it takes whole). */
export function copiedText(state, selection) {
    const [a, b] = selectedRange(state, selection);
    return state.source.slice(a, b);
}

/** Deletes the selection (Delete, Backspace or Cut over it). */
export function deleteSelection(state, selection) {
    const [a, b] = selectedRange(state, selection);
    if (a === b) return null;
    const cell = state.paragraphAt(a);
    if (cell?.kind === 'cell' && cell === state.paragraphAt(b)) return cellEdit(state, a, b, '');
    return result(state.replaced(a, b, ''), a);
}

// ---- Typing ------------------------------------------------------------------------------------------------------

/** Text typed or pasted at the caret, over the selection. */
export function insertText(state, selection, text) {
    let [a, b] = ordered(selection);
    text = text.replace(/\r\n?/g, '\n');
    const paragraph = state.paragraphAt(a);
    if (paragraph?.kind === 'cell') {
        // (a table cell is one line, and a pipe in it is text: the cell's own pipes stay the cell's)
        return cellEdit(state, a, b, cellText(text.replace(/\n/g, ' '), backslashesBefore(state.source, a)));
    }
    if (a === b && paragraph?.kind === 'blank' && /\S/.test(text) && paragraph.lastLine + 1 < state.lines.length) {
        // (text typed on an empty paragraph is a block of its own: the blank line after it is kept)
        const at = Math.max(a, paragraph.contentStart);
        const after = '\n' + state.blankPrefix(paragraph.firstLine);
        return result(state.replaced(at, at, text + after), at + text.length);
    }
    return result(state.replaced(a, b, text), a + text.length);
}

/**
 * Text put into a table cell, whose pipes are all text: each pipe of it escaped that is not escaped already -- after an
 * odd number of backslashes, `before` of them the source's in front of it -- ("a||b" is "a\|\|b").
 */
function cellText(text, before = 0) {
    let out = '', backslashes = before;
    for (const ch of text) {
        if (ch === '|' && backslashes % 2 === 0) out += '\\';
        out += ch;
        backslashes = ch === '\\' ? backslashes + 1 : 0;
    }
    return out;
}

/**
 * [from, to) of a table cell replaced by `text` (typed, pasted, or nothing: a delete), the cell's pipes kept what they
 * were. Only the first pipe after the edit can be changed by it, past the backslashes that follow the edit: a pipe is
 * escaped by an odd number of them. The cell's own pipe (not escaped) that the edit would escape gets a space before it,
 * padding of the cell ("x\|" of "x\\|" Backspaced is "x\ |", which shows "x\"); a pipe of the cell's text (escaped) that
 * it would leave bare gets back a backslash at the end of `text` (the caret before it). Null when that leaves the
 * source as it was (the backslash of an escaped pipe taken out: a cell has no bare pipe in its text).
 */
function cellEdit(state, from, to, text) {
    const source = state.source;
    let run = 0; // (the backslashes the edit is followed by)
    while (source[to + run] === '\\') run++;
    let insert = text, pad = '';
    if (source[to + run] === '|') {
        const escaped = (run + backslashesBefore(source, to)) % 2 === 1;
        const ending = text.length - text.replace(/\\+$/, '').length;
        const before = ending === text.length ? ending + backslashesBefore(source, from) : ending;
        const escapes = (run + before) % 2 === 1;
        if (escapes && !escaped) pad = ' ';
        else if (!escapes && escaped) insert += '\\';
    }
    const after = source.slice(0, from) + insert + source.slice(to, to + run) + pad + source.slice(to + run);
    if (after === source) return null;
    return result(after, from + text.length);
}

const backslashesBefore = (source, at) => {
    let count = 0;
    while (at - count > 0 && source[at - count - 1] === '\\') count++;
    return count;
};

// ---- Enter ---------------------------------------------------------------------------------------------------------

/** Enter: the paragraph or line operation for the block the caret is in. */
export function enter(state, selection) {
    const [a, b] = ordered(selection);
    if (a !== b) {
        const deleted = deleteSelection(state, selection);
        if (!deleted) return null;
        const next = new MarkdownState(deleted.source);
        return enter(next, { anchor: deleted.caret, caret: deleted.caret }) ?? deleted;
    }
    const c = a;
    const index = state.paragraphIndexAt(c);
    const p = state.paragraphs[index];
    switch (p.kind) {
        case 'code':
        case 'html':
        case 'definition':
        case 'other':
            return newLine(state, p, c);
        case 'fence':
            return enterOnOpeningFence(state, p, c);
        case 'fence-close':
            if (c >= state.lineEnd(p.firstLine)) return endOfBlock(state, p);
            return newCodeLineBefore(state, p);
        case 'cell':
            return addTableRow(state, p);
        case 'rule':
            return endOfBlock(state, p);
        case 'blank':
            return enterOnBlank(state, p, c);
        default:
            break;
    }
    if (p.itemLine) return enterInItem(state, p, c);
    if (p.kind === 'heading' && p.isEmpty) return endOfBlock(state, p);
    const rest = state.source.slice(c, p.end);
    if (!/\S/.test(rest)) return endOfBlock(state, p);
    if (c <= p.contentStart) return emptyParagraphBefore(state, p, c);
    return split(state, p, c);
}

// A new empty paragraph after the block of `p`, the caret in it: one more than there were.
function endOfBlock(state, p) {
    const bp = state.blankPrefix(p.firstLine);
    const last = p.lastLine;
    const at = state.lineEnd(last);
    const insert = state.isBlankLineOf(last + 1, bp) ? '\n' + bp : '\n' + bp + '\n' + bp;
    const source = state.replaced(at, at, insert);
    return result(source, lineEndOf(source, last + 2));
}

// An empty paragraph before the one the caret is at the start of; the caret stays with its text.
function emptyParagraphBefore(state, p, c) {
    const bp = state.blankPrefix(p.firstLine);
    const line = p.firstLine;
    const insert = line > 0 && !state.isBlankLineOf(line - 1, bp) ? bp + '\n' + bp + '\n' : bp + '\n';
    const at = state.lineStart(line);
    return result(state.replaced(at, at, insert), c + insert.length);
}

// The paragraph split in two at the caret: the second goes on in the same containers, a blank line between.
function split(state, p, c) {
    const bp = state.blankPrefix(p.firstLine);
    const lp = state.continuationPrefix(p.firstLine);
    let end = c;
    while (end < p.end && (state.source[end] === ' ' || state.source[end] === '\t')) end++; // (no spaces to start it)
    let start = c;
    while (start > p.contentStart && (state.source[start - 1] === ' ' || state.source[start - 1] === '\t')) start--;
    const insert = '\n' + bp + '\n' + lp;
    return result(state.replaced(start, end, insert), start + insert.length);
}

// The line of a block of code (or HTML) goes on in a new line, indented as this one.
function newLine(state, p, c) {
    const line = state.lineAt(c);
    const prefix = state.continuationPrefix(line) + state.source.slice(state.marksEnd(line), p.contentStart);
    const indent = /^[ \t]*/.exec(state.source.slice(p.contentStart, state.lineEnd(line)))[0];
    const insert = '\n' + prefix + indent;
    return result(state.replaced(c, c, insert), c + insert.length);
}

function newCodeLineBefore(state, p) {
    const line = p.firstLine;
    const prefix = state.continuationPrefix(line) + ' '.repeat(p.block.fenceOffset ?? 0);
    const at = state.lineStart(line);
    return result(state.replaced(at, at, prefix + '\n'), at + prefix.length);
}

// Enter on a code fence's opening line: at its end, with nothing to close it, the block is made -- a line for the code,
// the caret in it, and the closing fence -- so that what follows is out of the code again. Else a new first line of code.
function enterOnOpeningFence(state, p, c) {
    const block = p.block;
    if (block.type === 'front_matter') return newLineAfter(state, p, '');
    const line = p.firstLine;
    const prefix = state.continuationPrefix(line) + ' '.repeat(block.fenceOffset ?? 0);
    const at = state.lineEnd(line);
    if (block.closeLine === undefined && c >= at) {
        const fence = block.fenceChar.repeat(block.fenceLength);
        const source = state.replaced(at, at, '\n' + prefix + '\n' + prefix + fence);
        return result(source, at + 1 + prefix.length);
    }
    return newLineAfter(state, p, prefix);
}

function newLineAfter(state, p, prefix) {
    const at = state.lineEnd(p.firstLine);
    return result(state.replaced(at, at, '\n' + prefix), at + 1 + prefix.length);
}

// Enter on an empty paragraph: in a quote it leaves the quote; anywhere else it makes one more.
function enterOnBlank(state, p, c) {
    const line = p.firstLine;
    const marks = state.marks(line);
    const quotes = marks.filter((mark) => mark.kind === 'quote');
    if (quotes.length) {
        const outer = state.blankPrefix(line, quotes[quotes.length - 1].start);
        return emptyParagraphAt(state, line, outer);
    }
    const bp = state.blankPrefix(line);
    const at = state.lineEnd(line);
    return result(state.replaced(at, at, '\n' + bp), at + 1 + bp.length);
}

// Line `line` made an empty paragraph of the containers `bp` says (a blank line more before it, where it would else be
// only the separator), the caret in it.
function emptyParagraphAt(state, line, bp) {
    const extra = line > 0 && !state.isBlankLineOf(line - 1, bp);
    const text = extra ? bp + '\n' + bp : bp;
    const start = state.lineStart(line);
    const source = state.replaced(start, state.lineEnd(line), text);
    return result(source, start + text.length);
}

// Enter in a list item: a new item after the caret (a number one more, a task unchecked); in an empty item it leaves
// the list, an empty paragraph where the item was.
function enterInItem(state, p, c) {
    const item = p.itemLine;
    const line = p.firstLine;
    const mark = state.marks(line).find((each) => each.kind === 'item' && each.block === item);
    if (!mark) return split(state, p, c);
    if (p.emptyItem || !/\S/.test(state.source.slice(p.contentStart, p.end))) {
        return emptyParagraphAt(state, line, state.blankPrefix(line, mark.start));
    }
    const outer = state.continuationPrefix(line, mark.start);
    let marker = state.source.slice(mark.start, mark.end);
    if (item.parent.ordered) {
        const number = (item.parent.start ?? 1) + item.parent.children.indexOf(item) + 1;
        marker = marker.replace(/\d+/, String(number));
    }
    if (p.task) marker += '[ ] ';
    let start = c;
    while (start > p.contentStart && state.source[start - 1] === ' ') start--;
    let end = c;
    while (end < p.end && state.source[end] === ' ') end++;
    const insert = '\n' + outer + marker;
    return result(state.replaced(start, end, insert), start + insert.length);
}

// Enter in a table cell: a row of empty cells under the caret's (under the delimiter row, from the header), the caret
// in its first cell.
export function addTableRow(state, p, atEnd = false) {
    const table = p.cell.table.block;
    const rows = table.rows;
    let rowLine = atEnd ? rows[rows.length - 1].line : p.firstLine;
    if (!atEnd && p.cell.row === 0) rowLine = rows[1].line; // (after the delimiter row)
    const prefix = state.source.slice(state.lineStart(rowLine), state.marksEnd(rowLine));
    const row = '|' + '  |'.repeat(table.columns);
    const at = state.lineEnd(rowLine);
    const insert = '\n' + prefix + row;
    return result(state.replaced(at, at, insert), at + 1 + prefix.length + 2);
}

// ---- Shift+Enter ---------------------------------------------------------------------------------------------------

/** Shift+Enter: a hard break in a paragraph (a backslash and a line ending); a new line in code; nothing elsewhere. */
export function softBreak(state, selection) {
    const [a, b] = ordered(selection);
    if (a !== b) {
        const deleted = deleteSelection(state, selection);
        if (!deleted) return null;
        return softBreak(new MarkdownState(deleted.source), { anchor: deleted.caret, caret: deleted.caret }) ?? deleted;
    }
    const p = state.paragraphAt(a);
    if (['code', 'html', 'fence', 'fence-close', 'definition', 'other'].includes(p.kind)) return enter(state, selection);
    if (p.kind !== 'paragraph' || p.isEmpty) return null;
    const lp = state.continuationPrefix(p.firstLine);
    const insert = '\\\n' + lp;
    return result(state.replaced(a, a, insert), a + insert.length);
}

// ---- Backspace and Delete -------------------------------------------------------------------------------------------

/** Backspace (Ctrl+Backspace with `word`). */
export function backspace(state, selection, word = false) {
    const [a, b] = ordered(selection);
    if (a !== b) return deleteSelection(state, selection);
    const c = a;
    const index = state.paragraphIndexAt(c);
    const p = state.paragraphs[index];
    if (c < p.start) return null;
    const blockStart = p.kind === 'cell' ? p.start : p.contentStart;
    if (c > blockStart || (c > p.start && c < blockStart)) {
        // (a character -- or a word -- of the text, or of the syntax shown at the caret)
        const i = c - p.start;
        let from = p.start + (word ? previousWordBoundary(p.text, i) : backspaceStart(p.text, i));
        if (c > blockStart && from < blockStart) from = blockStart;
        return takeOut(state, p, from, c);
    }
    if (p.kind === 'cell') return null;
    if (p.kind === 'blank') return removeBlank(state, index, c);
    if (p.kind === 'code') {
        const previous = state.paragraphs[index - 1];
        if (previous?.kind === 'code' && previous.block === p.block) {
            return result(state.replaced(previous.end, c, ''), previous.end);
        }
        return null;
    }
    if (p.kind !== 'paragraph' && p.kind !== 'heading') return null;
    if (c === p.contentStart && p.contentStart > p.start) {
        if (p.itemLine) return removeItemMarker(state, p);
        if (p.kind === 'heading' && p.isEmpty) return takeOutLine(state, p, state.blankPrefix(p.firstLine));
        const quotes = state.marks(p.firstLine).filter((mark) => mark.kind === 'quote');
        if (quotes.length && p.kind === 'paragraph') return unquote(state, p);
    }
    return joinWithPrevious(state, index, c);
}

// [from, to) of a paragraph taken out; a paragraph on a line of its own left blank by it goes with the line ending
// after it when a blank line and more follow, so that one empty paragraph is left, not two. A table cell's pipes stay
// what they were (cellEdit).
function takeOut(state, p, from, to) {
    if (p.kind === 'cell') return cellEdit(state, from, to, '');
    let start = from, end = to;
    const line = p.firstLine;
    if (p.kind === 'paragraph' && p.firstLine === p.lastLine && !p.itemLine) {
        const rest = state.source.slice(p.contentStart, from) + state.source.slice(to, p.end);
        const bp = state.blankPrefix(line);
        if (!/\S/.test(rest) && from <= p.contentStart && state.isBlankLineOf(line + 1, bp) && line + 2 < state.lines.length) {
            start = state.lineStart(line);
            end = state.lineEnd(line);
            const source = state.replaced(start, state.lineEnd(line + 1), bp);
            return result(source, start + bp.length);
        }
    }
    return result(state.replaced(start, end, ''), start);
}

// An empty heading's hashes (or the like) taken off: the line left blank, one empty paragraph.
function takeOutLine(state, p, bp) {
    const line = p.firstLine;
    const start = state.lineStart(line);
    if (state.isBlankLineOf(line + 1, bp) && line + 2 < state.lines.length) {
        return result(state.replaced(start, state.lineEnd(line + 1), bp), start + bp.length);
    }
    return result(state.replaced(start, state.lineEnd(line), bp), start + bp.length);
}

// Backspace on an empty paragraph: the paragraph goes, the caret to the end of the one before.
function removeBlank(state, index, c) {
    const p = state.paragraphs[index];
    if (index === 0) return null;
    const previous = state.paragraphs[index - 1];
    const start = state.lineStart(p.firstLine) - 1;
    const source = state.replaced(start, state.lineEnd(p.firstLine), '');
    return result(source, Math.min(previous.end, start));
}

// Backspace at the start of an item's text: its marker comes off (a task's box with it); after another item, a blank
// line goes before it, so that it is a paragraph of its own and not more of that item.
function removeItemMarker(state, p) {
    const item = p.itemLine;
    const line = p.firstLine;
    const mark = state.marks(line).find((each) => each.kind === 'item' && each.block === item);
    if (!mark) return null;
    const outer = state.continuationPrefix(line, mark.start);
    const lineStart = state.lineStart(line);
    const bp = state.blankPrefix(line, mark.start);
    const replacement = (line > 0 && !state.isBlankLine(line - 1) ? bp + '\n' : '') + outer;
    const source = state.replaced(lineStart, p.contentStart, replacement);
    return result(source, lineStart + replacement.length);
}

// Backspace at the start of a quote's paragraph: one level of quote comes off its lines.
function unquote(state, p) {
    let source = state.source;
    let caret = p.contentStart;
    for (let line = p.lastLine; line >= p.firstLine; line--) {
        const quotes = state.marks(line).filter((mark) => mark.kind === 'quote');
        if (!quotes.length) continue;
        const mark = quotes[quotes.length - 1];
        source = source.slice(0, mark.start) + source.slice(mark.end);
        if (line === p.firstLine) caret -= mark.end - mark.start;
    }
    return result(source, caret);
}

// Backspace at the very start of a paragraph: it joins the one before it, what its block puts in front of its text
// going; an empty paragraph before it goes instead; a rule before it goes; code, a table or a fence is not joined.
function joinWithPrevious(state, index, c) {
    const p = state.paragraphs[index];
    const previous = state.paragraphs[index - 1];
    if (!previous) {
        if (p.contentStart > p.start && isTextKind(p) && c <= p.contentStart) {
            return result(state.replaced(p.start, p.contentStart, ''), p.start); // (a first heading made a paragraph)
        }
        return null;
    }
    if (previous.kind === 'blank') {
        const start = state.lineStart(previous.firstLine) - (previous.firstLine > 0 ? 1 : 0);
        const end = previous.firstLine > 0 ? state.lineEnd(previous.firstLine) : state.lineStart(previous.firstLine + 1);
        return result(state.replaced(start, end, ''), c - (end - start));
    }
    if (previous.kind === 'rule') {
        const start = state.lineStart(previous.firstLine);
        return result(state.replaced(start, state.lineStart(p.firstLine), ''), start + (c - state.lineStart(p.firstLine)));
    }
    if (!isTextKind(previous) || previous.kind === 'cell') return result(state.source, previous.end);
    return result(state.replaced(previous.end, Math.max(c, p.contentStart), ''), previous.end);
}

/** Delete (Ctrl+Delete with `word`). */
export function deleteForward(state, selection, word = false) {
    const [a, b] = ordered(selection);
    if (a !== b) return deleteSelection(state, selection);
    const c = a;
    const index = state.paragraphIndexAt(c);
    const p = state.paragraphs[index];
    if (c < p.end) {
        const i = c - p.start;
        let to = p.start + (word ? nextWordBoundary(p.text, i) : deleteEnd(p.text, i));
        // (a line ending inside a paragraph goes with what the next line starts with: a quote's marker, an indent)
        if (state.source[to - 1] === '\n' || state.source.slice(c, to).includes('\n')) {
            const line = state.lineAt(to);
            const content = Math.min(p.end, firstNonSpace(state, state.marksEnd(line), state.lineEnd(line)));
            to = Math.max(to, content);
        }
        return takeOut(state, p, c, to);
    }
    if (p.kind === 'cell') return null;
    const next = state.paragraphs[index + 1];
    if (!next) return null;
    if (next.kind === 'blank') {
        return result(state.replaced(state.lineStart(next.firstLine) - 1, state.lineEnd(next.firstLine), ''), c);
    }
    if (!isTextKind(p) && p.kind !== 'blank') return null;
    if (!isTextKind(next)) return null;
    return result(state.replaced(c, next.contentStart, ''), c);
}

function firstNonSpace(state, from, to) {
    let at = from;
    while (at < to && (state.source[at] === ' ' || state.source[at] === '\t')) at++;
    return at;
}

// ---- Tab ---------------------------------------------------------------------------------------------------------

/**
 * Tab and Shift+Tab: at the start of a list item's text the item goes one level in or out; at the start of a heading's
 * text, one level deeper or shallower; in the last cell of a table a row is added; elsewhere a tab character.
 */
export function tab(state, selection, back = false) {
    const [a, b] = ordered(selection);
    const p = state.paragraphAt(a);
    if (p.kind === 'cell') return back ? null : addTableRow(state, p, true);
    if (a === b && a <= p.contentStart) {
        if (p.itemLine) return back ? outdentItem(state, p) : indentItem(state, p);
        if (p.kind === 'heading' && p.block.markerStart !== undefined) return headingLevel(state, p, back ? -1 : 1);
    }
    if (back) {
        if (a === b && state.source[a - 1] === '\t') return result(state.replaced(a - 1, a, ''), a - 1);
        return null;
    }
    return insertText(state, selection, '\t');
}

function headingLevel(state, p, change) {
    const block = p.block;
    const level = block.level + change;
    if (level < 1 || level > 6) return null;
    const start = block.markerStart;
    return result(state.replaced(start, start + block.level, '#'.repeat(level)), p.contentStart + change);
}

// An item into the item above it (indented by that item's marker), with all its lines.
function indentItem(state, p) {
    const item = p.itemLine;
    const siblings = item.parent.children;
    const position = siblings.indexOf(item);
    if (position <= 0) return null;
    const above = siblings[position - 1];
    const width = above.markerOffset + above.padding - item.markerOffset;
    if (width <= 0) return null;
    let source = state.source;
    const inserts = [];
    for (let line = item.firstLine; line <= item.lastLine; line++) {
        const mark = state.marks(line).find((each) => each.block === item && (each.kind === 'item' || each.kind === 'indent'));
        if (mark) inserts.push(mark.start);
    }
    let renumber = null;
    if (item.parent.ordered && !above.children.some((child) => child.type === 'list')) {
        const mark = state.marks(p.firstLine).find((each) => each.kind === 'item' && each.block === item);
        const digits = /\d+/.exec(state.source.slice(mark.start, mark.end));
        if (digits) renumber = { at: mark.start + digits.index, length: digits[0].length, text: '1' };
    }
    let caret = p.contentStart;
    const edits = inserts.map((at) => ({ at, length: 0, text: ' '.repeat(width) }));
    if (renumber) edits.push({ at: renumber.at, length: renumber.length, text: renumber.text });
    // (from the end; at the same offset, the number replaced before the indentation goes in front of it)
    edits.sort((x, y) => y.at - x.at || y.length - x.length);
    for (const edit of edits) {
        source = source.slice(0, edit.at) + edit.text + source.slice(edit.at + edit.length);
        if (edit.at < caret) caret += edit.text.length - edit.length;
    }
    return result(source, caret);
}

// An item out of the item it is in, with all its lines: the parent's indentation taken off them.
function outdentItem(state, p) {
    const item = p.itemLine;
    const parentItem = item.parent.parent;
    if (!parentItem || parentItem.type !== 'item') return null;
    let source = state.source;
    let caret = p.contentStart;
    const removals = [];
    for (let line = item.firstLine; line <= item.lastLine; line++) {
        const mark = state.marks(line).find((each) => each.kind === 'indent' && each.block === parentItem);
        if (mark) removals.push(mark);
    }
    if (!removals.length) return null;
    if (item.parent.ordered) {
        const mark = state.marks(p.firstLine).find((each) => each.kind === 'item' && each.block === item);
        const digits = mark && /\d+/.exec(state.source.slice(mark.start, mark.end));
        if (parentItem.parent.ordered && digits) {
            const number = (parentItem.parent.start ?? 1) + parentItem.parent.children.indexOf(parentItem) + 1;
            const start = mark.start + digits.index;
            removals.push({ start, end: start + digits[0].length, text: String(number) });
        }
    }
    removals.sort((x, y) => y.start - x.start);
    for (const removal of removals) {
        const text = removal.text ?? '';
        source = source.slice(0, removal.start) + text + source.slice(removal.end);
        if (removal.start < caret) caret += text.length - (removal.end - removal.start);
    }
    return result(source, caret);
}

// ---- Clicks ----------------------------------------------------------------------------------------------------------

/** A task's box checked or unchecked (the source offset of its "["). */
export function toggleTask(state, offset) {
    const box = state.source.slice(offset, offset + 3);
    if (!/^\[[ xX]\]$/.test(box)) return null;
    const checked = box[1] !== ' ';
    return { source: state.replaced(offset + 1, offset + 2, checked ? ' ' : 'x'), caret: null, anchor: null };
}

/** Whether the syntax of `owner` shows at `caret` (for a host that asks of a construct). */
export { isRevealed };
