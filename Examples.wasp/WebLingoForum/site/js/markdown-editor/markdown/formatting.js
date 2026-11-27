// The inline formats as edits of the source (the web counterpart of MaxPad.Editing's InlineFormatting; the rules are in
// MarkdownTools/Documents/maxpad-editing.md, "The formats"):
//
// - A selection is in a format if all of its text is in a construct of it; then the toggle takes the format off, else
//   it puts it on all of the text. The text of a selection is what it takes of each paragraph, heading or cell, without
//   the white space at its ends.
// - A construct is never cut: text that starts inside a construct and goes on outside it is formatted in two places,
//   each in its own construct (`*a «b* c»` made bold is `*a **b*** **c**`). A code span, a picture and an autolink are
//   kept whole.
// - Putting a format on takes away the same format inside what it goes round; taking it off part of a construct splits
//   the construct round that part.
// - A caret in a construct of the format (or at either end of it) takes the construct off; in a word it puts the format
//   on the word; elsewhere in text it puts in a pair of delimiters with the caret between them.

import { isWordCharacter } from '../view/text.js';

export const DELIMITERS = { strong: '**', em: '*', underline: '==', strike: '~~', code: '`', sub: '~', sup: '^' };
export const FORMATS = Object.keys(DELIMITERS);

const isSpace = (ch) => ch === ' ' || ch === '\t' || ch === '\n';

/** The inlines of a projected paragraph's own text (a cell's of its cell). */
export function inlinesOf(paragraph) {
    if (!paragraph.block) return [];
    if (paragraph.kind === 'cell') {
        const rows = paragraph.block.rows.filter((row) => !row.delimiter);
        return rows[paragraph.cell.row]?.cells[paragraph.cell.column]?.inlines ?? [];
    }
    if (paragraph.kind === 'paragraph' || paragraph.kind === 'heading') return paragraph.block.inlines ?? [];
    return [];
}

const formattable = (paragraph) => paragraph.kind === 'paragraph' || paragraph.kind === 'heading' || paragraph.kind === 'cell';

// The pieces of text a selection [a, b) takes, each with the constructs it is in (outermost first), by paragraph.
function piecesIn(state, a, b) {
    const pieces = [];
    const first = state.paragraphIndexAt(a), last = state.paragraphIndexAt(b);
    const seen = new Set();
    for (let index = first; index <= last; index++) {
        const paragraph = state.paragraphs[index];
        if (!formattable(paragraph)) continue;
        const inlines = inlinesOf(paragraph);
        if (seen.has(inlines)) continue;
        seen.add(inlines);
        const walk = (nodes, ancestors) => {
            for (const node of nodes) {
                if (node.end <= a || node.start >= b) {
                    if (!(a === b && node.start <= a && node.end >= a)) continue;
                }
                switch (node.type) {
                    case 'text':
                    case 'html':
                    case 'escape': {
                        const start = node.type === 'escape' ? node.start : Math.max(a, node.start);
                        const end = node.type === 'escape' ? node.end : Math.min(b, node.end);
                        if (end > start) pieces.push({ start, end, ancestors, paragraph: index, node });
                        break;
                    }
                    case 'code':
                    case 'image':
                    case 'autolink':
                        pieces.push({ start: node.start, end: node.end, ancestors, paragraph: index, node, atomic: true });
                        break;
                    case 'softbreak':
                    case 'hardbreak':
                        break;
                    default:
                        if (node.children) walk(node.children, [...ancestors, node]);
                }
            }
        };
        walk(inlines, []);
    }
    return pieces.sort((x, y) => x.start - y.start);
}

const hasFormat = (piece, format) =>
    format === 'code' ? piece.node.type === 'code' : piece.ancestors.some((node) => node.type === format);

const textOf = (state, piece) => state.source.slice(piece.start, piece.end);

/** Whether the selection (or the caret) is in `format`: what a menu item shows as checked. */
export function isInline(state, selection, format) {
    const [a, b] = order(selection);
    if (a === b) return constructAt(state, a, format) !== null;
    const pieces = piecesIn(state, a, b).filter((piece) => /\S/.test(textOf(state, piece)));
    return pieces.length > 0 && pieces.every((piece) => hasFormat(piece, format));
}

const order = (selection) =>
    selection.anchor <= selection.caret ? [selection.anchor, selection.caret] : [selection.caret, selection.anchor];

// The innermost construct of `format` at `offset` (its ends included), or null.
function constructAt(state, offset, format) {
    const paragraph = state.paragraphAt(offset);
    if (!paragraph || !formattable(paragraph)) return null;
    let found = null;
    const walk = (nodes) => {
        for (const node of nodes) {
            if (node.start > offset || node.end < offset) continue;
            if (node.type === format) found = node;
            if (node.children) walk(node.children);
        }
    };
    walk(inlinesOf(paragraph));
    return found;
}

/**
 * Toggles `format` ('strong', 'em', 'underline', 'strike', 'code', 'sub', 'sup') on the selection, or at the caret:
 * `{ source, caret, anchor }`, the selection staying on the same text; null when nothing can be done there.
 */
export function toggleInline(state, selection, format) {
    const [a, b] = order(selection);
    const edits = a === b ? caretEdits(state, a, format) : selectionEdits(state, a, b, format);
    if (!edits || edits.length === 0) return null;
    const source = applyEdits(state.source, edits);
    const backward = selection.anchor > selection.caret;
    if (a === b) {
        const caret = mapPosition(edits, a, 'caret');
        return { source, caret, anchor: caret };
    }
    const start = mapPosition(edits, a, 'start'), end = mapPosition(edits, b, 'end');
    return backward ? { source, caret: start, anchor: end } : { source, caret: end, anchor: start };
}

// ---- With a selection ------------------------------------------------------------------------------------------------

function selectionEdits(state, a, b, format) {
    const pieces = piecesIn(state, a, b);
    const meaningful = pieces.filter((piece) => /\S/.test(textOf(state, piece)));
    if (!meaningful.length) return null;
    if (meaningful.every((piece) => hasFormat(piece, format))) return removeEdits(state, a, b, format, meaningful);
    return applyFormatEdits(state, a, b, format, pieces);
}

// The format taken off the selection: a construct it covers loses its delimiters, one it covers part of is split.
function removeEdits(state, a, b, format, pieces) {
    const edits = [];
    if (format === 'code') {
        const spans = new Set(pieces.map((piece) => piece.node).filter((node) => node.type === 'code'));
        for (const span of spans) {
            edits.push({ at: span.start, length: span.contentStart - span.start, text: '' });
            edits.push({ at: span.contentEnd, length: span.end - span.contentEnd, text: '' });
        }
        return edits;
    }
    const constructs = new Set();
    for (const piece of pieces) for (const node of piece.ancestors) if (node.type === format) constructs.add(node);
    for (const node of constructs) {
        const d = node.delimiter;
        const contentStart = node.start + d, contentEnd = node.end - d;
        const delimiter = state.source.slice(node.start, contentStart);
        let x = Math.max(a, contentStart), y = Math.min(b, contentEnd);
        while (x > contentStart && isSpace(state.source[x - 1])) x--;
        while (y < contentEnd && isSpace(state.source[y])) y++;
        if (x <= contentStart) edits.push({ at: node.start, length: d, text: '' });
        else edits.push({ at: x, length: 0, text: delimiter });
        if (y >= contentEnd) edits.push({ at: contentEnd, length: d, text: '' });
        else edits.push({ at: y, length: 0, text: delimiter });
    }
    return edits;
}

// The format put on the selection: each stretch of its text that is in the same constructs (none of them cut) gets a
// pair of delimiters, and the same format inside a stretch is taken away.
function applyFormatEdits(state, a, b, format, pieces) {
    const within = (node) => node.start >= a && node.end <= b;
    const segments = [];
    for (const piece of pieces) {
        // (the outermost construct the selection takes whole is the unit; what holds it is where the pair goes)
        let unit = piece, anchor = null;
        for (let k = piece.ancestors.length - 1; k >= 0; k--) {
            const node = piece.ancestors[k];
            if (within(node)) unit = node;
            else {
                anchor = node;
                break;
            }
        }
        const last = segments[segments.length - 1];
        if (last && last.anchor === anchor && last.paragraph === piece.paragraph) {
            last.start = Math.min(last.start, unit.start);
            last.end = Math.max(last.end, unit.end);
            last.pieces.push(piece);
        } else {
            segments.push({ start: unit.start, end: unit.end, anchor, paragraph: piece.paragraph, pieces: [piece] });
        }
    }
    const edits = [];
    const delimiter = DELIMITERS[format];
    for (const segment of segments) {
        let { start, end } = segment;
        while (start < end && isSpace(state.source[start])) start++;
        while (end > start && isSpace(state.source[end - 1])) end--;
        if (start >= end) continue;
        if (format === 'code') {
            const content = state.source.slice(start, end);
            const longest = Math.max(0, ...(content.match(/`+/g) ?? []).map((run) => run.length));
            const ticks = '`'.repeat(longest + 1);
            const pad = content.startsWith('`') || content.endsWith('`') ? ' ' : '';
            edits.push({ at: start, length: 0, text: ticks + pad }, { at: end, length: 0, text: pad + ticks });
            continue;
        }
        // (the same format inside the stretch goes; one that meets it at either end is joined to it)
        const inside = new Set();
        for (const piece of segment.pieces) {
            for (const node of piece.ancestors) {
                if (node.type === format && node.start >= start && node.end <= end) inside.add(node);
            }
        }
        for (const node of inside) {
            edits.push({ at: node.start, length: node.delimiter, text: '' });
            edits.push({ at: node.end - node.delimiter, length: node.delimiter, text: '' });
        }
        const before = adjacentConstruct(state, start, format, 'before');
        const after = adjacentConstruct(state, end, format, 'after');
        if (before) edits.push({ at: before.end - before.delimiter, length: before.delimiter, text: '' });
        else edits.push({ at: start, length: 0, text: delimiter });
        if (after) edits.push({ at: after.start, length: after.delimiter, text: '' });
        else edits.push({ at: end, length: 0, text: delimiter });
    }
    return edits;
}

// A construct of `format` that ends right at `offset` ('before') or starts right at it ('after').
function adjacentConstruct(state, offset, format, side) {
    const paragraph = state.paragraphAt(offset);
    if (!paragraph || !formattable(paragraph)) return null;
    let found = null;
    const walk = (nodes) => {
        for (const node of nodes) {
            if (node.type === format && (side === 'before' ? node.end === offset : node.start === offset)) found = node;
            if (node.children && node.start <= offset && node.end >= offset) walk(node.children);
        }
    };
    walk(inlinesOf(paragraph));
    return found;
}

// ---- At a caret ------------------------------------------------------------------------------------------------------

function caretEdits(state, c, format) {
    const paragraph = state.paragraphAt(c);
    if (!paragraph || !formattable(paragraph)) return null;
    const delimiter = DELIMITERS[format];
    const construct = constructAt(state, c, format);
    if (construct) {
        if (format === 'code') {
            return [{ at: construct.start, length: construct.contentStart - construct.start, text: '' },
                { at: construct.contentEnd, length: construct.end - construct.contentEnd, text: '' }];
        }
        const d = construct.delimiter;
        return [{ at: construct.start, length: d, text: '' }, { at: construct.end - d, length: d, text: '' }];
    }
    if (insideAtomic(state, paragraph, c)) return null;
    const source = state.source;
    let start = c, end = c;
    const word = (at) => at >= paragraph.contentStart && at < paragraph.end && (isWordCharacter(source[at]) ||
        (source[at] === "'" && isWordCharacter(source[at - 1]) && isWordCharacter(source[at + 1])));
    while (start > paragraph.contentStart && word(start - 1)) start--;
    while (end < paragraph.end && word(end)) end++;
    if (end > start) {
        return [
            { at: start, length: 0, text: delimiter, opening: true }, { at: end, length: 0, text: delimiter, opening: false },
        ];
    }
    // (no word: a pair of delimiters, the caret between -- not where they would be all there is, or run into syntax)
    if (paragraph.isEmpty) return null;
    if (/[*_~^=`]/.test(source[c - 1] ?? '') || /[*_~^=`]/.test(source[c] ?? '')) return null;
    if (format === 'strike' && c === paragraph.contentStart) return null;
    return [{ at: c, length: 0, text: delimiter + delimiter, caretInside: delimiter.length }];
}

// Whether the caret is inside a code span, an address, a picture or an escape (where no delimiter can go).
function insideAtomic(state, paragraph, c) {
    let inside = false;
    const walk = (nodes) => {
        for (const node of nodes) {
            if (node.start < c && node.end > c) {
                if (['code', 'image', 'autolink', 'escape', 'html'].includes(node.type)) inside = true;
                if (node.type === 'link' && c > node.textEnd) inside = true;
                if (node.children) walk(node.children);
            }
        }
    };
    walk(inlinesOf(paragraph));
    return inside;
}

// ---- Edits ---------------------------------------------------------------------------------------------------------

function applyEdits(source, edits) {
    const sorted = [...edits].sort((x, y) => y.at - x.at || y.length - x.length);
    let out = source;
    for (const edit of sorted) out = out.slice(0, edit.at) + edit.text + out.slice(edit.at + edit.length);
    return out;
}

// Where a position is after the edits. An insertion at the very position goes before a selection's start and after its
// end, so that the selection stays on the same text and the delimiters go round it; at a caret, an opening delimiter
// goes before it and a closing one after it, so that the caret keeps its place in the word.
function mapPosition(edits, position, role) {
    let shift = 0;
    for (const edit of edits) {
        if (edit.caretInside !== undefined && edit.at === position && role === 'caret') {
            shift += edit.caretInside;
            continue;
        }
        if (edit.length > 0) {
            if (edit.at + edit.length <= position) shift += edit.text.length - edit.length;
            else if (edit.at < position) shift += edit.at - position + edit.text.length;
        } else if (edit.at < position) {
            shift += edit.text.length;
        } else if (edit.at === position && (role === 'start' || (role === 'caret' && edit.opening))) {
            shift += edit.text.length;
        }
    }
    return position + shift;
}
