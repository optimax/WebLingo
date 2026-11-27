// The projection: the Markdown DOM as the paragraphs a VirtualTextView shows, and the map between a position in the view
// and a place in the source. The web counterpart of MaxPad.Projection, by its rules (Documents/maxpad-projection.md):
//
// - A paragraph's text is the source of the lines of its block, with the Markdown syntax in it as hidden runs. A line
//   ending inside a paragraph is one character of the view: a space for a soft break, the control's line break
//   (U+2028) for a hard one. So a position in a paragraph is its start in the source plus the position: the map is
//   offset arithmetic. (A picture is drawn by the run of its "!", never as a character of its own.)
// - Every character of a block's lines that no inline covers is syntax: hashes, delimiters, a link's address, a quote's
//   "> ", an item's marker, indentation. Each stretch of syntax knows whose it is: an inline construct's, a line's, a
//   code fence's or a rule's -- which is what the syntax shown at the caret is worked out from (`runsAt`).
// - The first blank line after a block only separates it from what follows and is in no paragraph; each further blank
//   line is an empty paragraph. Inside a quote or a list the same, the empty paragraph being the container's own.
// - Code is one paragraph per line; a fence's line is hidden until the caret is on either fence of its block.
// - A table is one paragraph per cell, row by row; its pipes and its delimiter row are in no paragraph.

import { isEmphasis } from './inlines.js';

export const SOFT_BREAK = '\u2028';

const F_BOLD = 1, F_ITALIC = 2, F_UNDERLINE = 4, F_STRIKE = 8, F_SUB = 16, F_SUP = 32, F_CODE = 64, F_HTML = 128,
    F_TASK = 256, F_TASK_DONE = 512;
const FORMAT_OF = { strong: F_BOLD, em: F_ITALIC, underline: F_UNDERLINE, strike: F_STRIKE, sub: F_SUB, sup: F_SUP };
const STYLE_NAMES = [[F_CODE, 'code'], [F_UNDERLINE, 'underline'], [F_STRIKE, 'strike'], [F_SUB, 'sub'], [F_SUP, 'sup'],
    [F_HTML, 'html'], [F_TASK, 'task'], [F_TASK_DONE, 'task-done']];

const BULLETS = ['\u2022', '\u25E6', '\u25AA'];

/** The paragraph style ID of a paragraph of `kind` in `quotes` quotes and `lists` lists: "p", "h2", "bullet.q1.l2". */
export function styleIdOf(kind, quotes, lists) {
    return kind + (quotes > 0 ? '.q' + quotes : '') + (lists > 0 ? '.l' + lists : '');
}

/** Takes a paragraph style ID apart again: `{ kind, quotes, lists }`. */
export function parseStyleId(id) {
    const match = /^([a-z0-9-]+)(?:\.q(\d+))?(?:\.l(\d+))?$/.exec(id ?? '');
    if (!match) return { kind: id ?? 'p', quotes: 0, lists: 0 };
    return { kind: match[1], quotes: +(match[2] ?? 0), lists: +(match[3] ?? 0) };
}

/**
 * Projects a parsed document. `options.caret` (a source offset) is where the caret is: a table cell with nothing but
 * spaces between its text and the caret shows them, so that a space typed at the end of a cell's text can be seen.
 */
export function project(doc, options = {}) {
    return new Projector(doc, options).run();
}

/**
 * The projection of a document read again in part (`reparseMarkdown`'s `{ doc, window }`), from the old one's: the
 * paragraphs of the lines read again are projected, those before them kept as they are, those after them kept and moved
 * with the change (their offsets and lines; their runs' places are their paragraph's, and their keys are of what they
 * show, so the view sees them unchanged). A table that the caret was or is at the end of a cell of, outside the lines
 * read again, is projected again too (spaces at the end of a cell show while the caret is after them).
 */
export function reproject(old, doc, window, options = {}) {
    const { from, to, oldTo, delta, lineDelta } = window;
    const projector = new Projector(doc, options);
    projector.tables = old.tableCount ?? 0;
    projector.cover = coverOf(doc);
    const paragraphs = old.paragraphs;
    const firstAt = (line) => {
        let low = 0, high = paragraphs.length;
        while (low < high) {
            const middle = (low + high) >> 1;
            if (paragraphs[middle].firstLine < line) low = middle + 1;
            else high = middle;
        }
        return low;
    };
    // (a blank line the window begins with may go on a run of blank lines before it: which of them is the one that
    // separates blocks is worked out from the run's first, so the run is projected again whole)
    const blankLine = (line) => !projector.cover[line] && !projector.itemStartingOn(line) && projector.isBlank(line);
    let start = from;
    if (blankLine(start)) {
        const quote = projector.quoteOf(start);
        while (start > 0 && blankLine(start - 1) && projector.quoteOf(start - 1) === quote) start--;
    }
    const a = firstAt(start), b = firstAt(oldTo);
    const after = paragraphs.slice(b);
    if (delta || lineDelta) {
        const owners = new Set();
        for (const paragraph of after) {
            paragraph.start += delta;
            paragraph.end += delta;
            paragraph.contentStart += delta;
            paragraph.firstLine += lineDelta;
            paragraph.lastLine += lineDelta;
            if (paragraph.task) paragraph.task.start += delta;
            for (const run of paragraph.runs) {
                const owner = run.owner;
                if (owner && (owner.kind === 'line' || owner.kind === 'rule') && !owners.has(owner)) {
                    owners.add(owner);
                    owner.line += lineDelta;
                }
            }
        }
    }
    projector.runLines(start, to);
    let out = [...paragraphs.slice(0, a), ...projector.out, ...after];
    // (a table outside the lines read again whose projection depends on the caret: the old caret's, or the new one's)
    const tables = new Set();
    if (old.caretTable && !inWindow(old.caretTable, from, to)) tables.add(old.caretTable);
    const caret = options.caret ?? -1;
    if (caret >= 0) {
        const leaf = projector.cover[projector.lineAt(caret)];
        if (leaf?.type === 'table' && !inWindow(leaf, from, to)) tables.add(leaf);
    }
    for (const table of tables) {
        if (!doc.leaves.includes(table)) continue;
        const first = out.findIndex((paragraph) => paragraph.block === table);
        if (first < 0) continue;
        let last = first;
        while (last + 1 < out.length && out[last + 1].block === table) last++;
        const mark = projector.out.length;
        projector.table(table);
        out = [...out.slice(0, first), ...projector.out.slice(mark), ...out.slice(last + 1)];
    }
    projector.out = out; // (its caretTable: the table projected here, if any, that the caret is at the end of a cell of)
    return projector.finish();
}

const inWindow = (block, from, to) => block.firstLine >= from && block.lastLine < to;

// The leaf each line is in (null for a line in none).
function coverOf(doc) {
    const cover = new Array(doc.lines.length).fill(null);
    for (const leaf of doc.leaves) {
        for (let line = leaf.firstLine; line <= leaf.lastLine; line++) cover[line] ??= leaf;
    }
    return cover;
}

class Projector {
    constructor(doc, options) {
        this.doc = doc;
        this.source = doc.source;
        this.lines = doc.lines;
        this.caret = options.caret ?? -1;
        this.out = [];
        this.lineOwners = [];
        this.tables = 0;
    }

    lineOwner(line) {
        return this.lineOwners[line] ??= { kind: 'line', line };
    }

    run() {
        this.cover = coverOf(this.doc);
        this.runLines(0, this.lines.length);
        if (this.out.length === 0) this.blank(0, null);
        return this.finish();
    }

    finish() {
        const projection = new Projection(this.doc, this.out);
        projection.tableCount = this.tables;
        projection.caretTable = this.caretTable ?? null;
        projection.cover = this.cover;
        return projection;
    }

    // The paragraphs of lines [from, to): every leaf that begins on them, and every line in no leaf. (`to` is the end
    // of the document, or a line a block begins on, so that a run of blank lines never goes past it.)
    runLines(from, to) {
        const { lines, cover } = this;
        let i = from;
        while (i < to) {
            const leaf = cover[i];
            if (leaf && leaf.firstLine === i) {
                this.projectLeaf(leaf);
                i = leaf.lastLine + 1;
                continue;
            }
            if (leaf) {
                i++; // (a line of a leaf that began on a line already projected: never, but never stuck either)
                continue;
            }
            const item = this.itemStartingOn(i);
            if (item) {
                this.emptyItem(i, item);
                i++;
                continue;
            }
            if (!this.isBlank(i)) {
                this.other(i);
                i++;
                continue;
            }
            const quote = this.quoteOf(i);
            let end = i;
            while (end + 1 < lines.length && !cover[end + 1] && !this.itemStartingOn(end + 1) && this.isBlank(end + 1) &&
                this.quoteOf(end + 1) === quote) end++;
            const separated = quote ? i > quote.firstLine : i > 0;
            for (let line = separated ? i + 1 : i; line <= end; line++) this.blank(line, quote);
            i = end + 1;
        }
    }

    // ---- What a line is ------------------------------------------------------------------------------------------

    marksOf(line) {
        return this.doc.lineMarks[line] ?? [];
    }

    // Where the containers' marks of a line end (its content starts).
    prefixEnd(line) {
        const marks = this.marksOf(line);
        return marks.length ? marks[marks.length - 1].end : this.lines[line].start;
    }

    isBlank(line) {
        const text = this.source.slice(this.prefixEnd(line), this.lines[line].end);
        return !/\S/.test(text);
    }

    itemStartingOn(line) {
        const marks = this.marksOf(line);
        for (let m = marks.length - 1; m >= 0; m--) if (marks[m].kind === 'item') return marks[m].block;
        return null;
    }

    quoteOf(line) {
        const marks = this.marksOf(line);
        for (let m = marks.length - 1; m >= 0; m--) if (marks[m].kind === 'quote') return marks[m].block;
        return null;
    }

    // How many quotes and lists a block is in, and the kind its paragraphs take from the innermost item.
    placeOf(block) {
        let quotes = 0, lists = 0, item = null;
        for (let up = block.parent; up; up = up.parent) {
            if (up.type === 'blockquote') quotes++;
            else if (up.type === 'list') lists++;
            else if (up.type === 'item' && !item) item = up;
        }
        if (block.type === 'blockquote') quotes++;
        if (block.type === 'item') {
            item = block;
            lists++;
        }
        return { quotes, lists, item };
    }

    itemKind(item) {
        if (item.task) return 'check';
        return item.parent.ordered ? 'list' : 'bullet';
    }

    // ---- Paragraphs ----------------------------------------------------------------------------------------------

    add(paragraph) {
        this.out.push(paragraph);
        return paragraph;
    }

    // A paragraph over [start, end) of the source: every character syntax of its line to begin with.
    builder(start, end) {
        const builder = new Builder(this, start, end);
        for (let line = this.lineAt(start); line < this.lines.length && this.lines[line].start <= end; line++) {
            const from = Math.max(start, this.lines[line].start), to = Math.min(end, this.lines[line].end + 1);
            builder.syntax(from, to, this.lineOwner(line));
        }
        for (let at = start; at < end; at++) if (this.source[at] === '\n') builder.display[at - start] = SOFT_BREAK;
        return builder;
    }

    lineAt(offset) {
        let low = 0, high = this.lines.length - 1;
        while (low < high) {
            const middle = (low + high + 1) >> 1;
            if (this.lines[middle].start <= offset) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    projectLeaf(leaf) {
        switch (leaf.type) {
            case 'paragraph': return this.textBlock(leaf, 'p');
            case 'heading': return this.textBlock(leaf, 'h' + leaf.level);
            case 'thematic_break': return this.rule(leaf);
            case 'code_block': return leaf.fenced ? this.fencedCode(leaf) : this.indentedCode(leaf);
            case 'html_block': return this.verbatim(leaf, 'html');
            case 'definitions': return this.verbatim(leaf, 'definition');
            case 'table': return this.table(leaf);
            case 'front_matter': return this.frontMatter(leaf);
            default: return this.verbatim(leaf, 'other');
        }
    }

    textBlock(leaf, kind) {
        const { quotes, lists, item } = this.placeOf(leaf);
        const first = leaf.firstLine, last = leaf.lastLine;
        const start = this.lines[first].start, end = this.lines[last].end;
        const builder = this.builder(start, end);
        builder.inlines(leaf.inlines ?? []);
        let contentStart = kind === 'p' ? leaf.lines[0].contentStart : leaf.contentStart ?? leaf.lines[0].contentStart;
        let listMarker = null;
        let styleKind = kind;
        if (item) {
            if (kind === 'p') styleKind = this.itemKind(item);
            if (item.firstLine === first && kind === 'p' && leaf === firstLeafOf(item)) {
                const task = this.taskBox(leaf, builder);
                if (task) {
                    item.task = true;
                    styleKind = 'check';
                    contentStart = task;
                } else {
                    listMarker = this.markerOf(item, lists);
                }
            }
        }
        if (kind === 'p' && !item && isImageOnly(leaf.inlines ?? [])) styleKind = 'image';
        const paragraph = builder.finish({
            kind: kind === 'p' ? 'paragraph' : 'heading', styleId: styleIdOf(styleKind, quotes, lists), block: leaf,
            firstLine: first, lastLine: last, listMarker, contentStart,
            itemLine: item && item.firstLine === first ? item : null,
        });
        // (content that is nothing but syntax keeps its line: an empty heading)
        if (!paragraph.runs.some((run) => run.owner === null)) paragraph.keepsLine = true;
        return this.add(paragraph);
    }

    // A task's box ("[ ]" or "[x]" and a space) at the start of an item's first paragraph: drawn as a box; returns where
    // the task's text starts, or 0 when there is none.
    taskBox(leaf, builder) {
        const at = leaf.lines[0].contentStart;
        const match = /^\[([ xX])\](?=[ \t]|$)/.exec(this.source.slice(at, at + 4));
        if (!match) return 0;
        builder.shown(at, at + 3, match[1] === ' ' ? F_TASK : F_TASK | F_TASK_DONE, null);
        builder.task = { start: at, checked: match[1] !== ' ' };
        let end = at + 3;
        while (this.source[end] === ' ' || this.source[end] === '\t') end++;
        return end;
    }

    markerOf(item, depth) {
        if (item.parent.ordered) {
            const number = (item.parent.start ?? 1) + item.parent.children.indexOf(item);
            return number + (item.delimiter ?? '.');
        }
        return BULLETS[(Math.max(1, depth) - 1) % BULLETS.length];
    }

    rule(leaf) {
        const { quotes, lists } = this.placeOf(leaf);
        const line = leaf.firstLine;
        const builder = this.builder(this.lines[line].start, this.lines[line].end);
        builder.syntax(this.lines[line].start, this.lines[line].end, { kind: 'rule', line });
        return this.add(builder.finish({
            kind: 'rule', styleId: styleIdOf('hr', quotes, lists), block: leaf, firstLine: line, lastLine: line,
        }));
    }

    fencedCode(leaf) {
        const { quotes, lists } = this.placeOf(leaf);
        const fence = { kind: 'fence', block: leaf };
        const fenceLine = (line, kind, style) => {
            const builder = this.builder(this.lines[line].start, this.lines[line].end);
            builder.syntax(this.lines[line].start, this.lines[line].end, fence);
            this.add(builder.finish({
                kind, styleId: styleIdOf(style, quotes, lists), block: leaf, firstLine: line, lastLine: line,
            }));
        };
        fenceLine(leaf.openLine, 'fence', 'code-fence');
        for (const entry of leaf.lines) this.codeLine(leaf, entry, 'code', quotes, lists);
        if (leaf.closeLine !== undefined) fenceLine(leaf.closeLine, 'fence-close', 'code-fence-close');
    }

    indentedCode(leaf) {
        const { quotes, lists } = this.placeOf(leaf);
        for (const entry of leaf.lines) this.codeLine(leaf, entry, 'code-indented', quotes, lists);
    }

    codeLine(leaf, entry, style, quotes, lists) {
        const { start, end } = this.lines[entry.line];
        const builder = this.builder(start, end);
        builder.shown(Math.min(entry.contentStart, end), end, 0, null);
        this.add(builder.finish({
            kind: 'code', styleId: styleIdOf(style, quotes, lists), block: leaf, firstLine: entry.line,
            lastLine: entry.line, contentStart: Math.min(entry.contentStart, end),
        }));
    }

    // HTML, definitions and whatever else: each line as written (what the containers put in front of it hidden).
    verbatim(leaf, kind) {
        const { quotes, lists } = this.placeOf(leaf);
        for (let line = leaf.firstLine; line <= leaf.lastLine; line++) {
            const { start, end } = this.lines[line];
            const builder = this.builder(start, end);
            const content = Math.min(this.prefixEnd(line), end);
            builder.shown(content, end, 0, null);
            this.add(builder.finish({
                kind, styleId: styleIdOf(kind, quotes, lists), block: leaf, firstLine: line, lastLine: line,
                contentStart: content,
            }));
        }
    }

    frontMatter(leaf) {
        const fence = { kind: 'fence', block: leaf };
        for (let line = leaf.firstLine; line <= leaf.lastLine; line++) {
            const { start, end } = this.lines[line];
            const builder = this.builder(start, end);
            const isFence = line === leaf.firstLine || line === leaf.lastLine;
            if (isFence) builder.syntax(start, end, fence);
            else builder.shown(start, end, 0, null);
            this.add(builder.finish({
                kind: isFence ? (line === leaf.firstLine ? 'fence' : 'fence-close') : 'code',
                styleId: isFence ? 'front-matter-fence' : 'front-matter', block: leaf, firstLine: line, lastLine: line,
                contentStart: start,
            }));
        }
    }

    table(leaf) {
        const { quotes, lists } = this.placeOf(leaf);
        const table = { id: ++this.tables, columns: leaf.columns, alignments: leaf.alignments, block: leaf };
        let rowIndex = 0;
        for (const row of leaf.rows) {
            if (row.delimiter) continue;
            const lineEnd = this.lines[row.line].end;
            for (let column = 0; column < leaf.columns; column++) {
                const cell = row.cells[column];
                let start, end;
                if (cell) {
                    start = cell.start;
                    end = cell.end;
                    if (start === end && cell.left < cell.right) start = end = cell.left + 1; // (after a space of padding)
                    if (this.caret > end && this.caret <= cell.right && !/\S/.test(this.source.slice(end, this.caret))) {
                        end = this.caret; // (spaces typed at the end of the cell's text, shown while the caret is after them)
                        this.caretTable = leaf; // (a projection of this table holds only while the caret is there)
                    }
                } else {
                    start = end = lineEnd; // (a cell the row does not have: empty, at the row's end)
                }
                const builder = this.builder(start, end);
                builder.inlines(cell?.inlines ?? []);
                if (end > (cell?.end ?? end)) builder.shown(cell.end, end, 0, null);
                const header = rowIndex === 0;
                this.add(builder.finish({
                    kind: 'cell', styleId: styleIdOf(header ? 'table-header' : 'table-cell', quotes, lists), block: leaf,
                    firstLine: row.line, lastLine: row.line, contentStart: start,
                    cell: { table, row: rowIndex, column, align: leaf.alignments[column] ?? null, header },
                }));
            }
            rowIndex++;
        }
    }

    emptyItem(line, item) {
        const { quotes, lists } = this.placeOf(item);
        const { start, end } = this.lines[line];
        const builder = this.builder(start, end);
        const paragraph = builder.finish({
            kind: 'paragraph', styleId: styleIdOf(this.itemKind(item), quotes, lists), block: item, firstLine: line,
            lastLine: line, listMarker: this.markerOf(item, lists), contentStart: end, itemLine: item, emptyItem: true,
        });
        paragraph.keepsLine = true;
        this.add(paragraph);
    }

    other(line) {
        const { start, end } = this.lines[line];
        const builder = this.builder(start, end);
        builder.shown(this.prefixEnd(line), end, 0, null);
        this.add(builder.finish({ kind: 'other', styleId: 'other', block: null, firstLine: line, lastLine: line }));
    }

    // An empty paragraph: a blank line more than the one that separates blocks. Its white space is its text; what a
    // quote puts in front of it is syntax, concealed, so that the line keeps its height.
    blank(line, quote) {
        const { start, end } = this.lines[line];
        const builder = this.builder(start, end);
        const content = Math.min(this.prefixEnd(line), end);
        builder.shown(content, end, 0, null);
        let styleId = 'blank';
        if (quote) {
            const { quotes, lists, item } = this.placeOf(quote);
            styleId = styleIdOf(item ? this.itemKind(item) : 'p', quotes, lists);
        } else {
            const list = this.listAround(line);
            if (list) {
                const { quotes, lists } = this.placeOf(list);
                styleId = styleIdOf(list.ordered ? 'list' : 'bullet', quotes, lists + 1);
            }
        }
        const paragraph = builder.finish({
            kind: 'blank', styleId, block: quote, firstLine: line, lastLine: line, contentStart: content,
        });
        paragraph.keepsLine = true;
        this.add(paragraph);
    }

    // The list a blank line is between two items of (or in an item of), if any.
    listAround(line) {
        let before = line - 1, after = line + 1;
        while (before >= 0 && !this.cover[before] && !this.itemStartingOn(before)) before--;
        while (after < this.lines.length && !this.cover[after] && !this.itemStartingOn(after)) after++;
        if (before < 0 || after >= this.lines.length) return null;
        const listsOf = (at) => {
            const lists = [];
            let block = this.cover[at] ?? this.itemStartingOn(at);
            for (; block; block = block.parent) if (block.type === 'list') lists.push(block);
            return lists;
        };
        const above = listsOf(before), below = new Set(listsOf(after));
        return above.find((list) => below.has(list)) ?? null;
    }
}

function firstLeafOf(block) {
    let child = block.children[0];
    while (child && child.children.length) child = child.children[0];
    return child ?? null;
}

/**
 * The plain text of inlines, as CommonMark reads a picture's description for its `alt`: the text without its syntax --
 * an escape's character, a code span's or an autolink's content, the text of emphasis, of a link and of a picture in
 * it -- and a line break a space.
 */
export function plainText(nodes, source) {
    let text = '';
    for (const node of nodes) {
        switch (node.type) {
            case 'text':
            case 'html':
                text += source.slice(node.start, node.end);
                break;
            case 'escape':
                text += source.slice(node.end - 1, node.end);
                break;
            case 'code':
            case 'autolink':
                text += source.slice(node.contentStart, node.contentEnd);
                break;
            case 'softbreak':
            case 'hardbreak':
                text += ' ';
                break;
            default:
                if (node.children) text += plainText(node.children, source);
        }
    }
    return text;
}

// A paragraph of one picture and nothing else (a link around it, and white space, allowed).
function isImageOnly(inlines) {
    const meaningful = inlines.filter((node) => !(node.type === 'text' && node.start === node.end));
    if (meaningful.length !== 1) return false;
    const [node] = meaningful;
    if (node.type === 'image') return true;
    return node.type === 'link' && node.children.length === 1 && node.children[0].type === 'image';
}

// What a paragraph is made of, character by character, before it is made runs: who owns each character of syntax
// (null for text), its formats, its link.
class Builder {
    constructor(projector, start, end) {
        this.projector = projector;
        this.source = projector.source;
        this.start = start;
        this.end = end;
        const length = end - start;
        this.owner = new Array(length).fill(null);
        this.format = new Uint16Array(length);
        this.link = new Array(length).fill(null);
        this.display = new Array(length).fill(null);
        this.images = new Map(); // (the index of an image's "!", and the image, drawn after it)
        this.task = null;
    }

    syntax(from, to, owner) {
        for (let at = Math.max(from, this.start); at < Math.min(to, this.end); at++) this.owner[at - this.start] = owner;
    }

    shown(from, to, format, link) {
        for (let at = Math.max(from, this.start); at < Math.min(to, this.end); at++) {
            const k = at - this.start;
            this.owner[k] = null;
            this.format[k] |= format;
            if (link) this.link[k] = link;
        }
    }

    inlines(nodes, format = 0, link = null) {
        for (const node of nodes) this.inline(node, format, link);
    }

    inline(node, format, link) {
        switch (node.type) {
            case 'text':
                this.shown(node.start, node.end, format, link);
                break;
            case 'code':
                this.syntax(node.start, node.contentStart, node);
                this.shown(node.contentStart, node.contentEnd, format | F_CODE, link);
                this.syntax(node.contentEnd, node.end, node);
                break;
            case 'escape':
                this.syntax(node.start, node.start + 1, node);
                this.shown(node.start + 1, node.end, format, link);
                break;
            case 'html':
                this.shown(node.start, node.end, format | F_HTML, link);
                break;
            case 'autolink': {
                const target = { target: node.destination, title: null };
                this.syntax(node.start, node.contentStart, node);
                this.shown(node.contentStart, node.contentEnd, format, target);
                this.syntax(node.contentEnd, node.end, node);
                break;
            }
            case 'softbreak':
                this.shown(node.start, node.end, format, link);
                this.display[node.start - this.start] = ' ';
                break;
            case 'hardbreak':
                this.syntax(node.start, node.end - 1, node);
                this.shown(node.end - 1, node.end, format, link);
                this.display[node.end - 1 - this.start] = SOFT_BREAK;
                break;
            case 'link': {
                const target = { target: node.destination ?? '', title: node.title ?? null };
                this.syntax(node.start, node.textStart, node);
                this.inlines(node.children ?? [], format, target);
                this.syntax(node.textEnd, node.end, node);
                break;
            }
            case 'image':
                this.syntax(node.start, node.end, node);
                if (node.start >= this.start && node.start < this.end) {
                    this.images.set(node.start - this.start, {
                        src: node.destination ?? '', alt: plainText(node.children ?? [], this.source),
                        title: node.title ?? null, link,
                    });
                }
                break;
            default:
                if (isEmphasis(node.type)) {
                    this.syntax(node.start, node.start + node.delimiter, node);
                    this.inlines(node.children ?? [], format | FORMAT_OF[node.type], link);
                    this.syntax(node.end - node.delimiter, node.end, node);
                }
        }
    }

    finish(properties) {
        const runs = [];
        const length = this.end - this.start;
        let k = 0;
        while (k < length) {
            const owner = this.owner[k], format = this.format[k], link = this.link[k];
            let j = k + 1;
            if (!this.images.has(k)) { // (an image's "!" is a run of its own: the picture is drawn after it)
                while (j < length && !this.images.has(j) && this.owner[j] === owner && this.format[j] === format &&
                    this.link[j] === link) j++;
            }
            let text = '';
            for (let at = k; at < j; at++) text += this.display[at] ?? this.source[this.start + at];
            runs.push({ text, start: k, owner, format, link, image: this.images.get(k) ?? null });
            k = j;
        }
        return new ProjectedParagraph({
            start: this.start, end: this.end, runs, task: this.task, listMarker: null, contentStart: this.start,
            ...properties,
        });
    }
}

/**
 * One paragraph of the view: [start, end) of the source, its runs (each `{ text, start, owner, format, link, image }`,
 * owner null for text and the owner of the syntax otherwise), its style, and what it is (`kind`: paragraph, heading,
 * code, fence, fence-close, rule, html, definition, cell, blank, other).
 */
export class ProjectedParagraph {
    constructor(properties) {
        Object.assign(this, properties);
        this.keepsLine ??= false;
    }

    get length() {
        return this.end - this.start;
    }

    /** The text the view shows (line endings as the view's characters). */
    get text() {
        return this.runs.map((run) => run.text).join('');
    }

    /** Whether the paragraph's own text (not its syntax) is empty. */
    get isEmpty() {
        return !this.runs.some((run) => run.owner === null && run.text.length > 0) && !this.runs.some((run) => run.image);
    }

    /** What makes the paragraph look as it does, to tell one that changed from one that did not. */
    get key() {
        return this._key ??= JSON.stringify([this.styleId, this.listMarker, this.keepsLine, this.cell ? [this.cell.row,
            this.cell.column, this.cell.align, this.cell.table.columns] : null, this.runs.map((run) => [run.text,
            run.owner ? 1 : 0, run.format, run.link?.target ?? null, run.image?.src ?? null])]);
    }
}

/** The projected document: its paragraphs, and the map between view positions and source offsets. */
export class Projection {
    constructor(doc, paragraphs) {
        this.doc = doc;
        this.paragraphs = paragraphs;
    }

    get source() {
        return this.doc.source;
    }

    /** The source offset of a view position `{ paragraph, index }` (clamped to the paragraph). */
    toSource(position) {
        const paragraph = this.paragraphs[Math.max(0, Math.min(position.paragraph, this.paragraphs.length - 1))];
        return paragraph.start + Math.max(0, Math.min(position.index, paragraph.length));
    }

    /**
     * The view position of a source offset. A place the view has nothing for (the blank line that separates two blocks,
     * a table's pipes) goes to the start of the next paragraph; one past the last paragraph to the end of it.
     */
    toView(offset) {
        const paragraphs = this.paragraphs;
        let low = 0, high = paragraphs.length - 1;
        while (low < high) {
            const middle = (low + high + 1) >> 1;
            if (paragraphs[middle].start <= offset) low = middle;
            else high = middle - 1;
        }
        const paragraph = paragraphs[low];
        if (offset < paragraph.start) return { paragraph: low, index: 0 };
        if (offset <= paragraph.end) return { paragraph: low, index: offset - paragraph.start };
        if (low + 1 < paragraphs.length) return { paragraph: low + 1, index: 0 };
        return { paragraph: low, index: paragraph.length };
    }

    /** The index of the paragraph a source offset is in (or the next one, between paragraphs). */
    paragraphAt(offset) {
        return this.toView(offset).paragraph;
    }
}

// ---- Runs for the view, with the syntax at the caret shown ---------------------------------------------------------

/**
 * Whether the syntax of `owner` is shown with the caret at source offset `caret` (null: no caret): an inline construct's
 * while the caret is in it or at either end of it; a line's while the caret is on that line; a code fence's while the
 * caret is on either fence of its block.
 */
export function isRevealed(owner, caret, doc) {
    if (caret === null || caret === undefined || !owner) return false;
    switch (owner.kind) {
        case 'line':
        case 'rule': {
            const line = doc.lines[owner.line];
            return caret >= line.start && caret <= line.end;
        }
        case 'fence':
            return owner.block.fenceLines.some((line) => caret >= doc.lines[line].start && caret <= doc.lines[line].end);
        default:
            return caret >= owner.start && caret <= owner.end;
    }
}

/**
 * The runs the view shows for `paragraph` with the caret at source offset `caret` (null for none), its list marker,
 * and how many characters of its leading syntax hang where the marker is drawn: `{ runs, listMarker, markerTextLength }`.
 * A view run is `{ text, bold, italic, hidden, styleId, link, image }`.
 */
export function viewRunsOf(paragraph, caret, doc, options = {}) {
    const reveal = options.revealSyntax !== false;
    const onLine = caret !== null && caret !== undefined && caret >= doc.lines[paragraph.firstLine].start &&
        caret <= doc.lines[paragraph.lastLine].end;
    const runs = [];
    for (const run of paragraph.runs) {
        const revealed = reveal && run.owner !== null && isRevealed(run.owner, caret, doc);
        const styles = [];
        for (const [flag, name] of STYLE_NAMES) if (run.format & flag) styles.push(name);
        let hidden = false;
        if (run.owner !== null) {
            if (revealed) styles.push(run.owner.kind === 'rule' ? 'rule-syntax' : 'syntax');
            else if (paragraph.keepsLine) styles.push('concealed');
            else hidden = true;
        }
        const viewRun = {
            text: run.text,
            bold: (run.format & F_BOLD) !== 0,
            italic: (run.format & F_ITALIC) !== 0,
            hidden,
            styleId: styles.length ? styles.join('+') : null,
            link: run.link ?? null,
            image: run.image ?? null,
        };
        const last = runs[runs.length - 1];
        if (last && !last.image && !viewRun.image && last.bold === viewRun.bold && last.italic === viewRun.italic &&
            last.hidden === viewRun.hidden && last.styleId === viewRun.styleId && last.link === viewRun.link) {
            last.text += viewRun.text;
        } else {
            runs.push(viewRun);
        }
    }
    let listMarker = paragraph.listMarker, markerTextLength = 0;
    if (listMarker && reveal && onLine && caret <= doc.lines[paragraph.firstLine].end) {
        listMarker = null; // (the item's own marker is shown as text, where the drawn one was)
        markerTextLength = Math.max(0, paragraph.contentStart - paragraph.start);
    }
    return { runs, listMarker, markerTextLength };
}
