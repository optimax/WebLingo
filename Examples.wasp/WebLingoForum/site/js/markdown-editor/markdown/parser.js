// The Markdown DOM: CommonMark's block structure and inlines, read from the source with the place in the source of every
// part kept, so that the view can show the source and every position in the view is a position in the source. The web
// counterpart of MaxPad.Dom (which is Markdig's tree): the same block types, Markdig's extensions that MaxPad uses
// (pipe tables, task lists, YAML front matter, ==underline==, ~~strikethrough~~, ~subscript~, ^superscript^).
//
// The source is read as lines joined by "\n" (the editor normalizes line endings on load and restores them on save).
// Every block knows the lines it is on; every line knows what the containers put at its start (`lineMarks`).

import { parseInlines, normalizeLabel, parseLinkDestination, parseLinkTitle, OPENTAG, CLOSETAG } from './inlines.js';

const TAB = '\t';

const reThematicBreak = /^(?:\*[ \t]*){3,}$|^(?:_[ \t]*){3,}$|^(?:-[ \t]*){3,}$/;
const reAtxHeading = /^#{1,6}(?:[ \t]+|$)/;
const reCodeFence = /^`{3,}(?!.*`)|^~{3,}/;
const reClosingCodeFence = /^(?:`{3,}|~{3,})(?=[ \t]*$)/;
const reSetextUnderline = /^(?:=+|-+)[ \t]*$/;
const reMaybeSpecial = /^[#`~*+_=<>0-9|-]/;
const reNonSpace = /[^ \t\f\v\r\n]/;
const reBulletMarker = /^[*+-]/;
const reOrderedMarker = /^(\d{1,9})([.)])/;

const reHtmlBlockOpen = [
    /./,
    /^<(?:script|pre|textarea|style)(?:\s|>|$)/i,
    /^<!--/,
    /^<[?]/,
    /^<![A-Za-z]/,
    /^<!\[CDATA\[/,
    new RegExp('^<[/]?(?:address|article|aside|base|basefont|blockquote|body|caption|center|col|colgroup|dd|details|' +
        'dialog|dir|div|dl|dt|fieldset|figcaption|figure|footer|form|frame|frameset|h[123456]|head|header|hr|html|' +
        'iframe|legend|li|link|main|menu|menuitem|nav|noframes|ol|optgroup|option|p|param|search|section|summary|' +
        'table|tbody|td|tfoot|th|thead|title|tr|track|ul)(?:\\s|[/]?[>]|$)', 'i'),
    new RegExp('^(?:' + OPENTAG + '|' + CLOSETAG + ')\\s*$', 'i'),
];
const reHtmlBlockClose = [/./, /<\/(?:script|pre|textarea|style)>/i, /-->/, /\?>/, />/, /\]\]>/];

const isSpaceOrTab = (ch) => ch === ' ' || ch === TAB;

// A block of the tree. `firstLine` and `lastLine` are line indexes; a leaf keeps its lines in `lines` as
// { line, contentStart } (an absolute offset where the block's own text starts on that line, after what its
// containers and its own syntax put there).
class Block {
    constructor(type, parent, firstLine) {
        this.type = type;
        this.parent = parent;
        this.children = [];
        this.firstLine = firstLine;
        this.lastLine = firstLine;
        this.open = true;
        this.lines = [];
    }

    get lastChild() {
        return this.children.length ? this.children[this.children.length - 1] : null;
    }
}

/**
 * Reads `source` (lines joined by "\n") into a document: `{ source, lines, root, leaves, definitions, lineMarks }`.
 * `lines[i]` is `{ start, end }` (end before the line's "\n"); `leaves` are the blocks that hold text, in the order of
 * the source; `lineMarks[i]` lists what the containers put at the start of line i: `{ kind: 'quote'|'item'|'indent',
 * start, end, block }` (absolute offsets).
 */
export function parseMarkdown(source) {
    return new BlockParser(source).parse();
}

/**
 * The document of `source`, which is `old.source` with `change.removed` characters at `change.start` replaced by
 * `change.inserted` characters, read again only where the change can have made a difference (2026-11-04, the user:
 * "Implement incremental parsing to support long documents"). Returns `{ doc, window }`, or null when that cannot be
 * told (the whole source is then to be read: `parseMarkdown`).
 *
 * The parse of a line depends on the lines before it only through the blocks open when it begins. So the lines are read
 * again from the last line, at or before the change, that the old parse began with no block open and that begins a
 * block (`from`); and up to the first line after the change that both parses begin with no block open and that begins
 * a block (`window.to`): from there on the text is the same, and so is everything read of it. The old document's
 * blocks before the window are kept as they are, those after it kept and moved (`shiftBlock`: their offsets by the
 * difference in length, their lines by the difference in lines). The inlines depend on the link reference definitions
 * of the whole document: a change that has definitions on either side of it is read whole. So is one that may make
 * front matter (a source that begins with "---" and had none).
 *
 * `window`: `{ from, to }` (the lines read again, in the new document), `oldTo` (the old line `to` was), `delta`,
 * `lineDelta`. The old document is not to be used afterwards: what was kept of it is the new one's.
 */
export function reparseMarkdown(old, source, change) {
    if (!old?.fresh || !change) return null;
    const { start, removed, inserted } = change;
    const delta = inserted - removed;
    if (old.source.length + delta !== source.length) return null;
    const oldLines = old.lines, oldFresh = old.fresh;
    const hasFrontMatter = old.root.children[0]?.type === 'front_matter';
    if (!hasFrontMatter && /^---[ \t]*(?:\n|$)/.test(source)) return null;
    // (the first line to read again: one the old parse began with no block open, and with something on it)
    let from = lineIndexAt(oldLines, start);
    const hasText = (lines, text, line) => reNonSpace.test(text.slice(lines[line].start, lines[line].end));
    while (from > 0 && !(oldFresh[from] && hasText(oldLines, old.source, from))) from--;
    if (from === 0 && (hasFrontMatter || !oldFresh[0])) return null;

    const parser = new BlockParser(source);
    const lines = parser.lines;
    const lineDelta = lines.length - oldLines.length;
    const changeEnd = start + inserted;
    const to = parser.parseWindow(from, (i) => {
        if (lines[i].start < changeEnd) return false; // (a line the change is in, or before it)
        const j = i - lineDelta;
        return j > from && j < oldLines.length && oldFresh[j] === 1 && oldLines[j].start === lines[i].start - delta &&
            hasText(lines, source, i);
    });
    if (parser.definitions.size > 0) return null; // (definitions read again: every link may mean another thing)
    const oldTo = to - lineDelta;
    const top = old.root.children;
    let a = 0;
    while (a < top.length && top[a].lastLine < from) a++;
    let b = a;
    while (b < top.length && top[b].firstLine < oldTo) b++;
    for (let k = a; k < b; k++) if (hasDefinitions(top[k])) return null;

    // (the inlines of what was read again, with the document's definitions)
    parser.definitions = old.definitions;
    const windowLeaves = [];
    parser.collectLeaves(parser.root, windowLeaves);
    for (const leaf of windowLeaves) parser.parseInlinesOf(leaf);

    // (what is kept after the window moves with the change)
    const after = top.slice(b);
    if (delta || lineDelta) {
        for (const block of after) shiftBlock(block, delta, lineDelta);
        for (let j = oldTo; j < oldLines.length; j++) {
            for (const mark of old.lineMarks[j]) {
                mark.start += delta;
                mark.end += delta;
            }
        }
    }
    const root = new Block('document', null, 0);
    root.children = [...top.slice(0, a), ...parser.root.children, ...after];
    for (const child of root.children) child.parent = root;
    root.lastLine = lines.length - 1;
    root.open = false;
    const leaves = [];
    parser.collectLeaves(root, leaves);
    const lineMarks = parser.lineMarks;
    const fresh = parser.fresh;
    for (let i = 0; i < from; i++) {
        lineMarks[i] = old.lineMarks[i];
        fresh[i] = oldFresh[i];
    }
    for (let i = to; i < lines.length; i++) {
        lineMarks[i] = old.lineMarks[i - lineDelta];
        fresh[i] = oldFresh[i - lineDelta];
    }
    const doc = { source, lines, root, leaves, definitions: old.definitions, lineMarks, fresh };
    return { doc, window: { from, to, oldTo, delta, lineDelta } };
}

// The line an offset is on.
function lineIndexAt(lines, offset) {
    let low = 0, high = lines.length - 1;
    while (low < high) {
        const middle = (low + high + 1) >> 1;
        if (lines[middle].start <= offset) low = middle;
        else high = middle - 1;
    }
    return low;
}

// Whether a block has link reference definitions in it (a block of them, at any depth).
function hasDefinitions(block) {
    if (block.type === 'definitions') return true;
    return block.children.some(hasDefinitions);
}

// A block kept after a change moves with it: every offset in it by `delta`, every line by `lines`.
function shiftBlock(block, delta, lines) {
    block.firstLine += lines;
    block.lastLine += lines;
    for (const entry of block.lines) {
        entry.line += lines;
        entry.contentStart += delta;
    }
    if (block.fenceLines) for (let k = 0; k < block.fenceLines.length; k++) block.fenceLines[k] += lines;
    for (const key of LINE_KEYS) if (block[key] !== undefined) block[key] += lines;
    for (const key of OFFSET_KEYS) if (block[key] !== undefined) block[key] += delta;
    if (block.rows) {
        for (const row of block.rows) {
            row.line += lines;
            row.start += delta;
            for (const cell of row.cells) {
                cell.left += delta;
                cell.right += delta;
                cell.start += delta;
                cell.end += delta;
                if (cell.inlines) shiftInlines(cell.inlines, delta);
            }
        }
    }
    if (block.inlines) shiftInlines(block.inlines, delta);
    for (const child of block.children) shiftBlock(child, delta, lines);
}

// (a block's line numbers, and its offsets, besides its lines and its rows: a list's and an item's `start` is the
// number an ordered list starts at, no offset)
const LINE_KEYS = ['openLine', 'closeLine', 'underlineLine'];
const OFFSET_KEYS = ['fenceStart', 'markerStart', 'markerEnd', 'prefixStart', 'contentStart', 'contentEnd'];

function shiftInlines(nodes, delta) {
    for (const node of nodes) {
        node.start += delta;
        node.end += delta;
        for (const key of INLINE_OFFSET_KEYS) if (node[key] !== undefined) node[key] += delta;
        if (node.children) shiftInlines(node.children, delta);
    }
}

const INLINE_OFFSET_KEYS = ['contentStart', 'contentEnd', 'textStart', 'textEnd'];

class BlockParser {
    constructor(source) {
        this.source = source;
        this.lines = [];
        let start = 0;
        for (;;) {
            const end = source.indexOf('\n', start);
            if (end < 0) {
                this.lines.push({ start, end: source.length });
                break;
            }
            this.lines.push({ start, end });
            start = end + 1;
        }
        this.lineMarks = this.lines.map(() => []);
        this.definitions = new Map();
    }

    parse() {
        this.root = new Block('document', null, 0);
        this.root.lastLine = this.lines.length - 1;
        this.tip = this.root;
        // (which lines the parse began with no block open: where it can begin again, reading only part of the source)
        this.fresh = new Uint8Array(this.lines.length);
        let first = this.frontMatter();
        for (let i = first; i < this.lines.length; i++) {
            this.fresh[i] = this.tip === this.root ? 1 : 0;
            this.incorporateLine(i);
        }
        while (this.tip) this.finalize(this.tip, this.lines.length - 1);
        this.root.open = false;
        const leaves = [];
        this.collectLeaves(this.root, leaves);
        for (const leaf of leaves) this.parseInlinesOf(leaf);
        return {
            source: this.source, lines: this.lines, root: this.root, leaves,
            definitions: this.definitions, lineMarks: this.lineMarks, fresh: this.fresh,
        };
    }

    // Reads the lines from `from` (a line the whole parse began with no block open) as the whole parse reads them, up
    // to the first line after it that begins with no block open and that `canStop` accepts (or the end): the blocks of
    // those lines, in `this.root`. Their inlines are not read yet. Returns the first line not read.
    parseWindow(from, canStop) {
        this.root = new Block('document', null, from);
        this.tip = this.root;
        this.fresh = new Uint8Array(this.lines.length);
        let i = from;
        for (; i < this.lines.length; i++) {
            const fresh = this.tip === this.root;
            if (i > from && fresh && canStop(i)) break;
            this.fresh[i] = fresh ? 1 : 0;
            this.incorporateLine(i);
        }
        while (this.tip) this.finalize(this.tip, i - 1);
        return i;
    }

    // YAML front matter (Markdig's extension): "---" on the first line, up to a line of "---" or "..." -- or nothing.
    frontMatter() {
        if (this.lines.length < 2 || !/^---[ \t]*$/.test(this.lineText(0))) return 0;
        for (let i = 1; i < this.lines.length; i++) {
            if (/^(?:---|\.\.\.)[ \t]*$/.test(this.lineText(i))) {
                if (i === 1) return 0; // (nothing between: a rule, as Markdig reads it)
                const block = new Block('front_matter', this.root, 0);
                block.lastLine = i;
                block.open = false;
                block.fenceLines = [0, i];
                for (let line = 1; line < i; line++) block.lines.push({ line, contentStart: this.lines[line].start });
                this.root.children.push(block);
                return i + 1;
            }
        }
        return 0;
    }

    lineText(i) {
        return this.source.slice(this.lines[i].start, this.lines[i].end);
    }

    // ---- The state of the line being read --------------------------------------------------------------------------

    findNextNonspace() {
        const line = this.line;
        let i = this.offset, cols = this.column, ch;
        while (i < line.length && (ch = line[i]) !== undefined) {
            if (ch === ' ') { i++; cols++; } else if (ch === TAB) { i++; cols += 4 - (cols % 4); } else break;
        }
        this.blank = i >= line.length;
        this.nextNonspace = i;
        this.nextNonspaceColumn = cols;
        this.indent = cols - this.column;
        this.indented = this.indent >= 4;
    }

    // Moves on by `count` characters, or (`columns`) by that many columns: a tab is taken whole (CommonMark would take
    // part of it; the difference is in where the rest of a tab's columns go, never in what is syntax).
    advanceOffset(count, columns) {
        const line = this.line;
        while (count > 0 && this.offset < line.length) {
            const ch = line[this.offset];
            if (ch === TAB && columns) {
                const width = 4 - (this.column % 4);
                this.column += width;
                count -= width;
            } else {
                this.column += ch === TAB ? 4 - (this.column % 4) : 1;
                count--;
            }
            this.offset++;
        }
    }

    advanceNextNonspace() {
        this.offset = this.nextNonspace;
        this.column = this.nextNonspaceColumn;
    }

    get abs() {
        return this.lineStart + this.offset;
    }

    mark(kind, start, block) {
        if (this.abs > start) this.lineMarks[this.lineIndex].push({ kind, start, end: this.abs, block });
    }

    // ---- Lines -------------------------------------------------------------------------------------------------------

    incorporateLine(index) {
        this.lineIndex = index;
        this.lineStart = this.lines[index].start;
        this.line = this.lineText(index);
        this.offset = 0;
        this.column = 0;
        this.blank = false;
        this.lineDone = false; // (set by a block start that has used the whole line: a heading, a fence, a rule)

        let allMatched = true;
        let container = this.root;
        this.oldtip = this.tip;
        let last;
        while ((last = container.lastChild) && last.open) {
            container = last;
            this.findNextNonspace();
            const result = this.continueBlock(container);
            if (result === 2) return; // (the line was the block's end, and is used)
            if (result === 1) {
                allMatched = false;
                container = container.parent;
                break;
            }
        }
        this.allClosed = container === this.oldtip;
        this.lastMatchedContainer = container;

        let matchedLeaf = acceptsLinesWhole(container.type);
        while (!matchedLeaf) {
            this.findNextNonspace();
            if (!this.indented && !reMaybeSpecial.test(this.line.slice(this.nextNonspace))) {
                this.advanceNextNonspace();
                break;
            }
            let started = 0;
            for (const start of blockStarts) {
                started = start(this, container);
                if (started) break;
            }
            if (started === 1) {
                container = this.tip;
            } else if (started === 2) {
                container = this.tip;
                matchedLeaf = true;
            } else {
                this.advanceNextNonspace();
                break;
            }
        }

        if (this.lineDone) return;
        if (!this.allClosed && !this.blank && this.tip.type === 'paragraph') {
            this.addLine(this.tip); // (a lazy continuation of the paragraph)
            return;
        }
        this.closeUnmatchedBlocks();
        const type = container.type;
        if (acceptsLinesWhole(type)) {
            this.addLine(container);
            if (type === 'html_block' && container.htmlType >= 1 && container.htmlType <= 5 &&
                reHtmlBlockClose[container.htmlType].test(this.line.slice(this.offset))) {
                this.finalize(container, index);
            }
        } else if (type === 'table' && !this.blank) {
            this.addTableRow(container);
        } else if (type === 'paragraph' && !this.blank) {
            this.addLine(container);
        } else if (this.offset < this.line.length && !this.blank) {
            const paragraph = this.addChild('paragraph');
            this.advanceNextNonspace();
            this.addLine(paragraph);
        }
    }

    continueBlock(block) {
        switch (block.type) {
            case 'blockquote': {
                if (!this.indented && this.line[this.nextNonspace] === '>') {
                    const start = this.abs;
                    this.advanceNextNonspace();
                    this.advanceOffset(1, false);
                    if (isSpaceOrTab(this.line[this.offset])) this.advanceOffset(1, true);
                    this.mark('quote', start, block);
                    block.lastLine = this.lineIndex;
                    return 0;
                }
                return 1;
            }
            case 'item': {
                if (this.blank) {
                    if (block.children.length === 0) return 1; // (a blank line after an empty item ends it)
                    this.advanceNextNonspace();
                } else if (this.indent >= block.markerOffset + block.padding) {
                    const start = this.abs;
                    this.advanceOffset(block.markerOffset + block.padding, true);
                    this.mark('indent', start, block);
                    block.lastLine = this.lineIndex;
                } else {
                    return 1;
                }
                return 0;
            }
            case 'heading':
            case 'thematic_break':
                return 1;
            case 'code_block': {
                if (block.fenced) {
                    const rest = this.line.slice(this.nextNonspace);
                    const match = this.indent <= 3 && rest[0] === block.fenceChar && rest.match(reClosingCodeFence);
                    if (match && match[0].length >= block.fenceLength) {
                        block.closeLine = this.lineIndex;
                        block.fenceLines.push(this.lineIndex);
                        block.lastLine = this.lineIndex;
                        this.finalize(block, this.lineIndex);
                        return 2;
                    }
                    let i = block.fenceOffset;
                    while (i > 0 && isSpaceOrTab(this.line[this.offset])) {
                        this.advanceOffset(1, true);
                        i--;
                    }
                } else if (this.indent >= 4) {
                    this.advanceOffset(4, true);
                } else if (this.blank) {
                    this.advanceNextNonspace();
                } else {
                    return 1;
                }
                return 0;
            }
            case 'html_block':
                return this.blank && (block.htmlType === 6 || block.htmlType === 7) ? 1 : 0;
            case 'paragraph':
            case 'table':
                return this.blank ? 1 : 0;
            default:
                return 0;
        }
    }

    addChild(type) {
        while (!canContain(this.tip.type, type)) this.finalize(this.tip, this.lineIndex - 1);
        const block = new Block(type, this.tip, this.lineIndex);
        this.tip.children.push(block);
        this.tip = block;
        return block;
    }

    addLine(block) {
        block.lines.push({ line: this.lineIndex, contentStart: this.abs });
        block.lastLine = this.lineIndex;
    }

    closeUnmatchedBlocks() {
        if (this.allClosed) return;
        while (this.oldtip !== this.lastMatchedContainer) {
            const parent = this.oldtip.parent;
            this.finalize(this.oldtip, this.lineIndex - 1);
            this.oldtip = parent;
        }
        this.allClosed = true;
    }

    finalize(block, lastLine) {
        const above = block.parent;
        block.open = false;
        if (block.type === 'paragraph') this.finalizeParagraph(block);
        if (block.type === 'code_block' && !block.fenced) {
            // (the blank lines at the end of indented code are not code)
            while (block.lines.length > 1 && this.isBlankFrom(block.lines[block.lines.length - 1])) block.lines.pop();
            block.lastLine = block.lines[block.lines.length - 1].line;
        }
        if (block.type === 'item' || block.type === 'list' || block.type === 'blockquote') {
            block.lastLine = Math.max(block.firstLine, block.lastLine, ...block.children.map((child) => child.lastLine));
        }
        this.tip = above;
    }

    isBlankFrom(entry) {
        const text = this.source.slice(entry.contentStart, this.lines[entry.line].end);
        return !reNonSpace.test(text);
    }

    // Link reference definitions at the start of a paragraph are taken out of it; what is left is the paragraph (or
    // nothing, when it was only definitions: then it is a block of definitions, shown as written).
    finalizeParagraph(block) {
        const taken = this.takeDefinitions(block);
        if (taken === 0) return;
        if (taken === block.lines.length) {
            block.type = 'definitions';
            return;
        }
        const definitions = new Block('definitions', block.parent, block.firstLine);
        definitions.open = false;
        definitions.lines = block.lines.slice(0, taken);
        definitions.lastLine = definitions.lines[taken - 1].line;
        block.lines = block.lines.slice(taken);
        block.firstLine = block.lines[0].line;
        const siblings = block.parent.children;
        siblings.splice(siblings.indexOf(block), 0, definitions);
    }

    // How many whole lines at the start of the paragraph are link reference definitions (each added to the map).
    takeDefinitions(block) {
        const { text, lineOf } = this.contentOf(block, 0);
        let pos = 0, lines = 0;
        while (pos < text.length && text[pos] === '[') {
            const end = parseReference(text, pos, this.definitions);
            if (end === 0) break;
            pos = end;
            lines = lineOf(end - 1) + 1;
            if (text[pos] === '\n') pos++;
        }
        return lines;
    }

    // The text of a leaf's lines from line `from` on, joined by "\n", and the line (of the block's) each character is on.
    contentOf(block, from) {
        let text = '';
        const starts = [];
        for (let i = from; i < block.lines.length; i++) {
            const entry = block.lines[i];
            if (i > from) text += '\n';
            starts.push(text.length);
            text += this.source.slice(entry.contentStart, this.lines[entry.line].end);
        }
        const lineOf = (index) => {
            let line = 0;
            while (line + 1 < starts.length && starts[line + 1] <= index) line++;
            return from + line;
        };
        return { text, lineOf };
    }

    // ---- Tables (Markdig's pipe tables, as GFM has them) -----------------------------------------------------------

    addTableRow(table) {
        table.rows.push(this.rowOf(this.lineIndex, this.abs));
        table.lastLine = this.lineIndex;
    }

    // The cells of a row: each its content's [start, end) without the white space around it, and [left, right), the
    // room between its pipes (absolute offsets).
    rowOf(line, from) {
        const end = this.lines[line].end;
        const text = this.source;
        const pipes = [];
        let codeTicks = 0;
        for (let i = from; i < end; i++) {
            const ch = text[i];
            if (ch === '\\') { i++; continue; }
            if (ch === '`') {
                let n = 1;
                while (text[i + n] === '`') n++;
                // (a code span, with a closing run of as many backticks on the line, keeps its pipes)
                if (codeTicks === n) codeTicks = 0;
                else if (!codeTicks && closingTicksAt(text, i + n, end, n)) codeTicks = n;
                i += n - 1;
                continue;
            }
            if (ch === '|' && !codeTicks) pipes.push(i);
        }
        let first = from;
        while (first < end && isSpaceOrTab(text[first])) first++;
        let last = end;
        while (last > first && isSpaceOrTab(text[last - 1])) last--;
        const bounds = [];
        let left = first;
        let index = 0;
        if (pipes.length && pipes[0] === first) {
            left = first + 1;
            index = 1;
        }
        for (; index < pipes.length; index++) {
            bounds.push([left, pipes[index]]);
            left = pipes[index] + 1;
        }
        if (left < last || !(pipes.length && pipes[pipes.length - 1] === last - 1)) bounds.push([left, last]);
        const cells = bounds.map(([l, r]) => {
            let s = l, e = r;
            while (s < e && isSpaceOrTab(text[s])) s++;
            while (e > s && isSpaceOrTab(text[e - 1])) e--;
            return { left: l, right: r, start: s, end: e };
        });
        return { line, start: this.lines[line].start, cells };
    }

    collectLeaves(block, out) {
        for (const child of block.children) {
            if (child.children.length || isContainer(child.type)) this.collectLeaves(child, out);
            else out.push(child);
        }
    }

    // ---- Inlines -------------------------------------------------------------------------------------------------

    parseInlinesOf(leaf) {
        if (leaf.type === 'paragraph' || (leaf.type === 'heading' && !leaf.setext)) {
            const entries = leaf.type === 'heading' ? [{ line: leaf.firstLine, contentStart: leaf.contentStart }] : leaf.lines;
            leaf.inlines = this.inlinesOf(entries, leaf.type === 'heading' ? leaf.contentEnd : undefined);
        } else if (leaf.type === 'heading') {
            leaf.inlines = this.inlinesOf(leaf.lines);
        } else if (leaf.type === 'table') {
            for (const row of leaf.rows) {
                if (row.delimiter) continue;
                for (const cell of row.cells) cell.inlines = this.inlinesOfRange(cell.start, cell.end);
            }
        }
    }

    // The inlines of a leaf's lines, each line's text from its content start (white space at its start skipped), the
    // last one's up to `lastEnd` (a heading's closing sequence left out).
    inlinesOf(entries, lastEnd) {
        let text = '';
        const map = [];
        entries.forEach((entry, i) => {
            const lineEnd = i === entries.length - 1 && lastEnd !== undefined ? lastEnd : this.lines[entry.line].end;
            let start = entry.contentStart;
            while (start < lineEnd && isSpaceOrTab(this.source[start])) start++;
            if (i > 0) {
                map.push(this.lines[entry.line].start - 1); // (the "\n" that ended the line before)
                text += '\n';
            }
            for (let at = start; at < lineEnd; at++) map.push(at);
            text += this.source.slice(start, lineEnd);
        });
        map.push(entries.length ? (lastEnd ?? this.lines[entries[entries.length - 1].line].end) : 0);
        return absolute(parseInlines(text, this.definitions), map);
    }

    inlinesOfRange(start, end) {
        const map = [];
        for (let at = start; at <= end; at++) map.push(at);
        return absolute(parseInlines(this.source.slice(start, end), this.definitions), map);
    }
}

// The inlines' places made offsets in the source, through `map` (an index of the text read to its place in the source).
function absolute(nodes, map) {
    const at = (index) => map[Math.min(index, map.length - 1)];
    const walk = (node) => {
        for (const key of ['start', 'end', 'contentStart', 'contentEnd', 'textStart', 'textEnd']) {
            if (node[key] !== undefined) node[key] = key === 'end' || key === 'contentEnd' || key === 'textEnd'
                ? endOf(node[key]) : at(node[key]);
        }
        if (node.children) node.children.forEach(walk);
    };
    // An end is one past a character: the place after the character before it, so that an end at a line's end is the
    // end of that line, not the start of the next one.
    const endOf = (index) => (index === 0 ? at(0) : at(index - 1) + 1);
    nodes.forEach(walk);
    return nodes;
}

function isContainer(type) {
    return type === 'document' || type === 'blockquote' || type === 'list' || type === 'item';
}

function canContain(parentType, childType) {
    if (parentType === 'document' || parentType === 'blockquote' || parentType === 'item') return childType !== 'item';
    if (parentType === 'list') return childType === 'item';
    return false;
}

// Blocks that take every line given to them, with no block start looked for (code and HTML).
function acceptsLinesWhole(type) {
    return type === 'code_block' || type === 'html_block';
}

// ---- Block starts: 0 nothing, 1 a container (look on for more), 2 a leaf (the line is its) ---------------------------

const blockStarts = [
    // the delimiter row of a table, under its header row (before a list item can take "- | -")
    (parser, container) => {
        if (container.type !== 'paragraph' || container !== parser.tip || container.lines.length !== 1) return 0;
        if (parser.indented) return 0;
        const rest = parser.line.slice(parser.nextNonspace);
        if (!/^\|?[ \t]*:?-+:?[ \t]*(\|[ \t]*:?-+:?[ \t]*)*\|?[ \t]*$/.test(rest) || !rest.includes('-')) return 0;
        const header = parser.rowOf(container.lines[0].line, container.lines[0].contentStart);
        const delimiter = parser.rowOf(parser.lineIndex, parser.lineStart + parser.nextNonspace);
        const headerText = parser.source.slice(container.lines[0].contentStart, parser.lines[container.lines[0].line].end);
        if (header.cells.length !== delimiter.cells.length) return 0;
        if (!rest.includes('|') && !headerText.includes('|')) return 0;
        parser.closeUnmatchedBlocks();
        container.type = 'table';
        header.header = true;
        delimiter.delimiter = true;
        container.alignments = delimiter.cells.map(({ start, end }) => {
            const cell = parser.source.slice(start, end);
            const left = cell.startsWith(':'), right = cell.endsWith(':');
            return left && right ? 'center' : right ? 'right' : left ? 'left' : null;
        });
        container.rows = [header, delimiter];
        container.columns = header.cells.length;
        container.lastLine = parser.lineIndex;
        parser.lineDone = true;
        return 2;
    },
    // block quote
    (parser) => {
        if (parser.indented || parser.line[parser.nextNonspace] !== '>') return 0;
        const start = parser.abs;
        parser.advanceNextNonspace();
        parser.advanceOffset(1, false);
        if (isSpaceOrTab(parser.line[parser.offset])) parser.advanceOffset(1, true);
        parser.closeUnmatchedBlocks();
        const block = parser.addChild('blockquote');
        parser.mark('quote', start, block);
        return 1;
    },
    // ATX heading
    (parser) => {
        if (parser.indented) return 0;
        const rest = parser.line.slice(parser.nextNonspace);
        const match = rest.match(reAtxHeading);
        if (!match) return 0;
        parser.advanceNextNonspace();
        const markerStart = parser.abs;
        parser.advanceOffset(match[0].length, false);
        parser.closeUnmatchedBlocks();
        const heading = parser.addChild('heading');
        heading.level = match[0].trim().length;
        heading.markerStart = markerStart;
        heading.markerEnd = parser.abs;
        const lineEnd = parser.lines[parser.lineIndex].end;
        let text = parser.source.slice(parser.abs, lineEnd);
        // (a closing sequence of #s, and the white space around it, is no part of the text)
        let end = lineEnd;
        const trailing = text.match(/(?:^|[ \t]+)#+[ \t]*$/);
        if (trailing) end = lineEnd - trailing[0].length;
        else end = lineEnd - (text.length - text.replace(/[ \t]+$/, '').length);
        if (/^#+[ \t]*$/.test(text)) end = parser.abs;
        heading.contentStart = parser.abs;
        heading.contentEnd = Math.max(parser.abs, end);
        heading.lines.push({ line: parser.lineIndex, contentStart: parser.abs });
        parser.finalize(heading, parser.lineIndex);
        parser.lineDone = true;
        return 2;
    },
    // fenced code
    (parser) => {
        if (parser.indented) return 0;
        const rest = parser.line.slice(parser.nextNonspace);
        const match = rest.match(reCodeFence);
        if (!match) return 0;
        parser.closeUnmatchedBlocks();
        const block = parser.addChild('code_block');
        block.fenced = true;
        block.fenceChar = match[0][0];
        block.fenceLength = match[0].length;
        block.fenceOffset = parser.indent;
        block.info = rest.slice(match[0].length).trim();
        block.openLine = parser.lineIndex;
        block.fenceLines = [parser.lineIndex];
        block.fenceStart = parser.lineStart + parser.nextNonspace;
        parser.lineDone = true; // (the opening fence is no line of code)
        return 2;
    },
    // HTML block
    (parser, container) => {
        if (parser.indented || parser.line[parser.nextNonspace] !== '<') return 0;
        const rest = parser.line.slice(parser.nextNonspace);
        for (let type = 1; type <= 7; type++) {
            if (reHtmlBlockOpen[type].test(rest) && (type < 7 || (container.type !== 'paragraph' &&
                !(!parser.allClosed && !parser.blank && parser.tip.type === 'paragraph')))) {
                parser.closeUnmatchedBlocks();
                const block = parser.addChild('html_block');
                block.htmlType = type;
                return 2;
            }
        }
        return 0;
    },
    // setext heading underline
    (parser, container) => {
        if (parser.indented || container.type !== 'paragraph') return 0;
        const rest = parser.line.slice(parser.nextNonspace);
        const match = rest.match(reSetextUnderline);
        if (!match) return 0;
        parser.closeUnmatchedBlocks();
        const taken = parser.takeDefinitions(container);
        if (taken === container.lines.length) return 0; // (nothing left to be a heading: the line is a rule or text)
        if (taken > 0) {
            parser.finalizeParagraph(container); // (the definitions go before; what is left is the heading's text)
            container.open = true;
            parser.tip = container;
        }
        container.type = 'heading';
        container.setext = true;
        container.level = match[0][0] === '=' ? 1 : 2;
        container.underlineLine = parser.lineIndex;
        container.lastLine = parser.lineIndex;
        parser.finalize(container, parser.lineIndex);
        parser.lineDone = true;
        return 2;
    },
    // thematic break
    (parser) => {
        if (parser.indented || !reThematicBreak.test(parser.line.slice(parser.nextNonspace))) return 0;
        parser.closeUnmatchedBlocks();
        const block = parser.addChild('thematic_break');
        block.lines.push({ line: parser.lineIndex, contentStart: parser.lineStart + parser.nextNonspace });
        parser.finalize(block, parser.lineIndex);
        parser.lineDone = true;
        return 2;
    },
    // list item
    (parser, container) => {
        if (parser.indented && container.type !== 'list') return 0;
        const data = parseListMarker(parser, container);
        if (!data) return 0;
        parser.closeUnmatchedBlocks();
        if (parser.tip.type !== 'list' || !listsMatch(parser.tip, data)) {
            const list = parser.addChild('list');
            const { ordered, bulletChar, delimiter, start } = data;
            Object.assign(list, { ordered, bulletChar, delimiter, start });
        }
        const item = parser.addChild('item');
        Object.assign(item, data);
        parser.mark('item', data.prefixStart, item);
        return 1;
    },
    // indented code
    (parser) => {
        if (!parser.indented || parser.tip.type === 'paragraph' || parser.blank) return 0;
        parser.advanceOffset(4, true);
        parser.closeUnmatchedBlocks();
        const block = parser.addChild('code_block');
        block.fenced = false;
        return 2;
    },
];

function parseListMarker(parser, container) {
    if (parser.indent >= 4) return null;
    const rest = parser.line.slice(parser.nextNonspace);
    const data = {
        markerOffset: parser.indent, markerStart: parser.lineStart + parser.nextNonspace, prefixStart: parser.abs,
    };
    let match;
    if ((match = rest.match(reBulletMarker))) {
        data.ordered = false;
        data.bulletChar = match[0];
    } else if ((match = rest.match(reOrderedMarker)) && (container.type !== 'paragraph' || match[1] === '1')) {
        data.ordered = true;
        data.start = parseInt(match[1], 10);
        data.delimiter = match[2];
    } else {
        return null;
    }
    const next = parser.line[parser.nextNonspace + match[0].length];
    if (!(next === undefined || next === ' ' || next === TAB)) return null;
    if (container.type === 'paragraph' && !reNonSpace.test(parser.line.slice(parser.nextNonspace + match[0].length))) {
        return null; // (an empty item cannot interrupt a paragraph)
    }
    parser.advanceNextNonspace();
    parser.advanceOffset(match[0].length, true);
    data.markerEnd = parser.abs;
    const spacesStartColumn = parser.column, spacesStartOffset = parser.offset;
    do {
        parser.advanceOffset(1, true);
    } while (parser.column - spacesStartColumn < 5 && isSpaceOrTab(parser.line[parser.offset]));
    const blankItem = parser.line[parser.offset] === undefined;
    const spaces = parser.column - spacesStartColumn;
    if (spaces >= 5 || spaces < 1 || blankItem) {
        data.padding = match[0].length + 1;
        parser.column = spacesStartColumn;
        parser.offset = spacesStartOffset;
        if (isSpaceOrTab(parser.line[parser.offset])) parser.advanceOffset(1, true);
    } else {
        data.padding = match[0].length + spaces;
    }
    data.contentStart = parser.abs;
    return data;
}

function listsMatch(list, data) {
    return list.ordered === data.ordered && list.delimiter === data.delimiter && list.bulletChar === data.bulletChar;
}

// A link reference definition at `pos` of `text`: its end (at a line's end), and the definition added to `map` unless
// one of its label is there already; 0 if there is none.
function parseReference(text, pos, map) {
    const label = text.slice(pos).match(/^\[(?:[^\\\[\]]|\\.){0,999}\]/s);
    if (!label || label[0].length < 3 || !/\S/.test(label[0].slice(1, -1))) return 0;
    let i = pos + label[0].length;
    if (text[i] !== ':') return 0;
    i++;
    i = skipSpacesAndNewline(text, i);
    const destination = parseLinkDestination(text, i);
    if (!destination) return 0;
    i = destination.end;
    const beforeTitle = i;
    let j = skipSpacesAndNewline(text, i);
    let title = null;
    if (j !== beforeTitle) {
        const parsed = parseLinkTitle(text, j);
        if (parsed) {
            title = parsed.title;
            j = parsed.end;
        }
    }
    let end = atLineEnd(text, title !== null ? j : beforeTitle);
    if (end < 0 && title !== null) {
        title = null;
        end = atLineEnd(text, beforeTitle);
    }
    if (end < 0) return 0;
    const key = normalizeLabel(label[0].slice(1, -1));
    if (key && !map.has(key)) map.set(key, { destination: destination.destination, title });
    return end;
}

// Whether a run of exactly `n` backticks is in text[from, end).
function closingTicksAt(text, from, end, n) {
    for (let i = from; i < end; i++) {
        if (text[i] !== '`') continue;
        let run = 1;
        while (text[i + run] === '`') run++;
        if (run === n) return true;
        i += run - 1;
    }
    return false;
}

function skipSpacesAndNewline(text, i) {
    while (isSpaceOrTab(text[i])) i++;
    if (text[i] === '\n') {
        i++;
        while (isSpaceOrTab(text[i])) i++;
    }
    return i;
}

// Where the line ends if there is nothing but white space from `i` to its end; -1 if there is more.
function atLineEnd(text, i) {
    while (isSpaceOrTab(text[i])) i++;
    return i >= text.length || text[i] === '\n' ? i : -1;
}
