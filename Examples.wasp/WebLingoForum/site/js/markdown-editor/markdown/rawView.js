// The raw Markdown view: the document's source, shown and edited in the same view as the formatted text (the web
// counterpart of MaxPad.Projection's RawView and MaxPad's RawModeService; the user's model of 2026-09-22 in MarkdownTools):
//
// - Looking changes nothing. A view left as it was gives the document back exactly, with its history.
// - Text that was changed becomes the document when the view is left, as one step of the document's history: one undo
//   brings the document back as it was before the view began.
// - While the view is on it has a history of its own, its text edits undone one at a time, which ends with the view.
// - The paragraphs are the formatted view's: a paragraph of the raw view starts where a paragraph of the formatted view
//   starts its line, and holds the lines up to the next one (the blank line that separates two blocks too), its line
//   endings drawn as breaks inside it and the last as its end. So a place in the source is the same place in either view.
//
// The source has "\n" line endings (the controller's), and a break of the view is one character, as "\n" is: a position
// of the raw view is the start of its paragraph plus its index, and the source the view's text says is its paragraphs
// joined by "\n", each break a "\n".

import { SOFT_BREAK } from '../view/text.js';
import { MarkdownState } from './state.js';

/** Where each paragraph of the raw view starts: the start of the line each projected paragraph begins on (the first 0). */
export function rawStarts(state) {
    const starts = [0];
    for (const paragraph of state.paragraphs) {
        const line = state.lineStart(state.lineAt(paragraph.start));
        if (line > starts[starts.length - 1]) starts.push(line);
    }
    return starts;
}

/**
 * The text of each paragraph of the raw view: the source from its start to the next one's, without the line ending that
 * ends it (its end is that), the line endings inside it made breaks. The last keeps everything to the end of the text.
 */
export function rawTexts(source, starts) {
    return starts.map((start, i) => {
        const last = i + 1 >= starts.length;
        let end = last ? source.length : starts[i + 1];
        if (!last && end > start && source[end - 1] === '\n') end--;
        return source.slice(start, end).split('\n').join(SOFT_BREAK);
    });
}

/** Where a place in the source is in the raw view. */
export function toRaw(offset, starts, texts) {
    let paragraph = 0;
    while (paragraph + 1 < starts.length && starts[paragraph + 1] <= offset) paragraph++;
    const index = Math.max(0, Math.min(offset - starts[paragraph], texts[paragraph].length));
    return { paragraph, index };
}

/** The source the raw view's paragraphs say: joined by "\n", every break, and any line ending pasted in, a "\n". */
export function sourceOfRaw(texts) {
    return texts.map((text) => text.split(SOFT_BREAK).join('\n').replace(/\r\n?/g, '\n')).join('\n');
}

/**
 * The type of each paragraph of the raw view, as the gutter's names show it (MaxPad's RawView.Kinds): the name
 * (`nameOf(styleId)`) of the block of the formatted view its first line is in -- of a table's row, its first cell's --
 * and '' for a line in no block (the blank line between two). A table's delimiter row on a line of its own is its header's.
 */
export function rawTypes(texts, nameOf) {
    const lines = texts.map((text) => text.split(SOFT_BREAK).join('\n').replace(/\r\n?/g, '\n'));
    const state = new MarkdownState(lines.join('\n'));
    const blocks = state.paragraphs;
    const types = new Array(texts.length);
    let start = 0, next = 0;
    for (let i = 0; i < texts.length; i++) {
        const line = state.lineAt(start);
        while (next < blocks.length && blocks[next].lastLine < line) next++;
        const block = blocks[next];
        if (block && block.firstLine <= line) types[i] = nameOf(block.styleId);
        else if (block?.cell && blocks[next - 1]?.cell?.table === block.cell.table) {
            types[i] = nameOf(blocks[next - 1].styleId); // (a delimiter row on its own: its header's)
        } else {
            types[i] = '';
        }
        start += lines[i].length + 1;
    }
    return types;
}

/** Where a place in the source the raw view's paragraphs say is in the view (the way back of `rawToSource`). */
export function rawFromSource(offset, texts) {
    let at = Math.max(0, offset);
    for (let paragraph = 0; paragraph < texts.length; paragraph++) {
        if (at <= texts[paragraph].length || paragraph === texts.length - 1) {
            return { paragraph, index: Math.min(at, texts[paragraph].length) };
        }
        at -= texts[paragraph].length + 1;
    }
    return { paragraph: 0, index: 0 };
}

/** Where a position of the raw view is in the source its paragraphs say. */
export function rawToSource(position, texts) {
    let offset = 0;
    for (let i = 0; i < position.paragraph && i < texts.length; i++) offset += texts[i].length + 1;
    return offset + Math.max(0, Math.min(position.index, texts[position.paragraph]?.length ?? 0));
}

/**
 * The history of the raw view: its edits (TextEdits from the view), undone and redone one at a time. It is not the
 * document's: it ends with the view, whose changes are then one step of the document's history.
 */
export class RawHistory {
    constructor() {
        this.done = [];
        this.undone = [];
    }

    get canUndo() {
        return this.done.length > 0;
    }

    get canRedo() {
        return this.undone.length > 0;
    }

    recordEdit(edit) {
        this.done.push(edit);
        this.undone = [];
    }

    undo(view) {
        const edit = this.done.pop();
        if (!edit) return false;
        this.undone.push(edit);
        view.applyEdit(edit.inverse());
        return true;
    }

    redo(view) {
        const edit = this.undone.pop();
        if (!edit) return false;
        this.done.push(edit);
        view.applyEdit(edit);
        return true;
    }
}

/**
 * The raw view on a MarkdownController's view. `enter()` puts the source in the view, the controller standing aside;
 * `exit()` gives the view back to the controller, the text it holds made the document if it was changed.
 */
export class RawMode {
    constructor(view, controller, styles) {
        this.view = view;
        this.controller = controller;
        this.styles = styles; // (the raw view's look: a RawStyleProvider)
        this.session = null;
    }

    get isOn() {
        return this.session !== null;
    }

    get canUndo() {
        return !!this.session?.history.canUndo;
    }

    get canRedo() {
        return !!this.session?.history.canRedo;
    }

    undo() {
        return !!this.session && this.session.history.undo(this.view);
    }

    redo() {
        return !!this.session && this.session.history.redo(this.view);
    }

    /** The text of the view, as it is now. */
    texts() {
        return Array.from({ length: this.view.paragraphCount }, (_, i) => this.view.getParagraphText(i));
    }

    /** The source the view says now (line endings "\n"). */
    source() {
        return sourceOfRaw(this.texts());
    }

    /** Whether the view's text is still what it was given. */
    get changed() {
        if (!this.session) return false;
        const texts = this.texts();
        return texts.length !== this.session.texts.length || texts.some((text, i) => text !== this.session.texts[i]);
    }

    // The source in the view, cut where the formatted view's paragraphs begin, the caret and the selection where they were.
    enter() {
        if (this.session) return;
        const controller = this.controller, view = this.view;
        const state = controller.state;
        const { anchor, caret } = controller.selection;
        const starts = rawStarts(state);
        const texts = rawTexts(state.source, starts);
        const history = new RawHistory();
        controller.suspend();
        this.session = { texts, history, styles: view.styleProvider };
        view.batch(() => {
            view.styleProvider = this.styles;
            view.editRecorder = history;
            view.setParagraphs(texts.map((text) => ({ runs: [{ text }], styleId: 'raw' })));
            if (anchor === caret) view.setCaret(toRaw(caret, starts, texts), { scroll: false });
            else view.select(toRaw(anchor, starts, texts), toRaw(caret, starts, texts), { scroll: false });
            view.paragraphLabels = (index) => this.typeOf(index);
        });
        this.session.loadedVersion = view.version;
    }

    /**
     * The type of the raw view's paragraph `index`, as the formatted view names its block (`rawTypes`): read again from
     * the view's text when it has changed since, and only when asked (the gutter asks only when it shows the names).
     */
    typeOf(index) {
        const session = this.session;
        if (!session) return null;
        if (session.typesVersion !== this.view.version) {
            const styles = session.styles;
            session.types = rawTypes(this.texts(), (styleId) => styles?.getParagraphStyle?.(styleId)?.name ?? styleId);
            session.typesVersion = this.view.version;
        }
        return session.types[index] ?? null;
    }

    // The view given back to the controller: the text it holds is the document if it was changed (one step of its
    // history), and the caret and the selection are where they were in the text.
    exit() {
        const session = this.session;
        if (!session) return;
        const view = this.view;
        const texts = this.texts();
        const caret = rawToSource(view.caret ?? { paragraph: 0, index: 0 }, texts);
        const anchor = rawToSource(view.anchor ?? view.caret ?? { paragraph: 0, index: 0 }, texts);
        const source = this.changed ? sourceOfRaw(texts) : this.controller.state.source;
        this.session = null;
        view.batch(() => {
            view.paragraphLabels = null;
            view.editRecorder = null;
            view.styleProvider = session.styles;
            this.controller.resume(source, caret, anchor);
        });
    }

    /** The view's text made the document while staying in the view (what a page that saves wants first). */
    commit() {
        if (!this.session) return;
        this.exit();
        this.enter();
    }

    /** Another document is coming: the view's text is not wanted, and nothing of it becomes the document. */
    abandon() {
        const session = this.session;
        if (!session) return;
        this.session = null;
        this.view.paragraphLabels = null;
        this.view.editRecorder = null;
        this.view.styleProvider = session.styles;
        this.controller.resume(this.controller.state.source, 0, 0);
    }
}
