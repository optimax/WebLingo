// A Markdown source read and projected, with the questions the commands ask of it: which line an offset is on, what the
// containers put at the start of a line, which lines are blank. Immutable: a command makes a new source, and a new state
// is read from it.

import { parseMarkdown, reparseMarkdown } from './parser.js';
import { project, reproject } from './projection.js';

export class MarkdownState {
    constructor(source, options = {}) {
        this.source = source;
        this.caret = options.caret ?? null; // (where the caret was when it was projected: a cell's padding typed shows)
        this.doc = options.doc ?? parseMarkdown(source);
        this.projection = options.projection ?? project(this.doc, { caret: options.caret });
        this._cover = this.projection.cover ?? null;
    }

    /**
     * The state of `source`, which is `old.source` with `change` made (`{ start, removed, inserted }`: where, and how many
     * characters went and came), read again only where the change can have made a difference (`reparseMarkdown`,
     * `reproject`), or whole when that cannot be told. `old` is not to be used afterwards: what was kept of it is the new
     * state's. (`MarkdownState.incremental = false` reads every source whole.)
     */
    static edited(old, source, change, options = {}) {
        if (MarkdownState.incremental && old && change) {
            const reread = reparseMarkdown(old.doc, source, change);
            if (reread) {
                const projection = reproject(old.projection, reread.doc, reread.window, { caret: options.caret });
                const state = new MarkdownState(source, { ...options, doc: reread.doc, projection });
                state.window = reread.window;
                return state;
            }
        }
        return new MarkdownState(source, options);
    }

    get lines() {
        return this.doc.lines;
    }

    get paragraphs() {
        return this.projection.paragraphs;
    }

    lineAt(offset) {
        const lines = this.doc.lines;
        let low = 0, high = lines.length - 1;
        while (low < high) {
            const middle = (low + high + 1) >> 1;
            if (lines[middle].start <= offset) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    lineStart(line) {
        return this.doc.lines[line].start;
    }

    lineEnd(line) {
        return this.doc.lines[line].end;
    }

    lineText(line) {
        return this.source.slice(this.lineStart(line), this.lineEnd(line));
    }

    marks(line) {
        return this.doc.lineMarks[line] ?? [];
    }

    // Where what the containers put at the start of a line ends.
    marksEnd(line) {
        const marks = this.marks(line);
        return marks.length ? marks[marks.length - 1].end : this.lineStart(line);
    }

    /** The leaf that a line is in (null for a blank line, or one with only a container's marks on it). */
    leafOn(line) {
        if (!this._cover) {
            this._cover = new Array(this.doc.lines.length).fill(null);
            for (const leaf of this.doc.leaves) {
                for (let at = leaf.firstLine; at <= leaf.lastLine; at++) this._cover[at] ??= leaf;
            }
        }
        return this._cover[line] ?? null;
    }

    /** Whether a line is blank: in no leaf, no list item starting on it, nothing but white space after its marks. */
    isBlankLine(line) {
        if (line < 0 || line >= this.doc.lines.length || this.leafOn(line)) return false;
        if (this.marks(line).some((mark) => mark.kind === 'item')) return false;
        return !/\S/.test(this.source.slice(this.marksEnd(line), this.lineEnd(line)));
    }

    /**
     * What a new line that goes on in the same containers as `line` starts with: a quote's marker as it is written, the
     * room of a list item's marker as spaces, an item's indentation as it is.
     */
    continuationPrefix(line, until = Infinity) {
        let prefix = '';
        for (const mark of this.marks(line)) {
            if (mark.start >= until) break;
            prefix += mark.kind === 'item' ? ' '.repeat(mark.end - mark.start) : this.source.slice(mark.start, mark.end);
        }
        return prefix;
    }

    /** What a blank line in the same containers as `line` is written with: the quotes' markers (">"), nothing else. */
    blankPrefix(line, until = Infinity) {
        return this.continuationPrefix(line, until).replace(/\s+$/, '');
    }

    /** Whether `line` is a blank line of the containers `prefix` (a blankPrefix) says. */
    isBlankLineOf(line, prefix) {
        return this.isBlankLine(line) && this.blankPrefix(line) === prefix;
    }

    paragraphIndexAt(offset) {
        return this.projection.paragraphAt(offset);
    }

    paragraphAt(offset) {
        return this.projection.paragraphs[this.projection.paragraphAt(offset)];
    }

    /** The source with [start, end) replaced by `text`. */
    replaced(start, end, text) {
        return this.source.slice(0, start) + text + this.source.slice(end);
    }
}

MarkdownState.incremental = true;

/** The end of line `line` of a source (lines joined by "\n"). */
export function lineEndOf(source, line) {
    let at = 0;
    for (let i = 0; i < line; i++) {
        const next = source.indexOf('\n', at);
        if (next < 0) return source.length;
        at = next + 1;
    }
    const end = source.indexOf('\n', at);
    return end < 0 ? source.length : end;
}
