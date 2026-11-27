// The controller between the Markdown source, its projection and the view (the web counterpart of MaxPad.Editing's
// MarkdownViewController; MarkdownTools/Documents/maxpad-editing.md):
//
//   key or clipboard --> VirtualTextView --> 'editcommand' --> controller --> a new source (commands.js)
//   view <-- replaceParagraphs <-- the paragraphs that changed <-- the source read and projected again
//
// - The source is the authority. The view's text is a projection of it; the view's own editing never runs, because the
//   controller handles every command (including the ones it decides do nothing).
// - One primitive: every command ends in `apply`: the source is replaced, read again, the view given just the paragraphs
//   that changed, the caret put where the command leaves it, and the change recorded in the history.
// - The caret is a place in the source. The view maps a position to the source through the projection; a place the view
//   has none for (after a line break that ends the text) is remembered while the view's caret is where it was put.
// - The syntax of the paragraph the caret is in is shown, and hidden again when the caret leaves it (`restyleParagraph`:
//   the same text, so nothing is edited or recorded); the caret on either fence of a block of code shows both.

import { MarkdownState } from './state.js';
import { viewRunsOf } from './projection.js';
import { EditHistory, difference } from './history.js';
import * as commands from './commands.js';
import { toggleInline, isInline } from './formatting.js';
import * as blocks from './blocks.js';

/** The line ending a text uses ("\r\n", "\r" or "\n"; "\n" when it has none). */
export function lineEndingOf(text) {
    const match = /\r\n|\r|\n/.exec(text);
    return match ? match[0] : '\n';
}

export class MarkdownController extends EventTarget {
    constructor(view, options = {}) {
        super();
        this.view = view;
        this.options = { revealSyntax: true, ...options };
        this.history = new EditHistory();
        this.version = 0;
        this.selection = { anchor: 0, caret: 0 };
        this._placed = null; // (the view's selection as the controller last put it)
        this._revealed = []; // ({ paragraph object, index }: the view paragraphs whose syntax is shown)
        this.suspended = false; // (while the view shows something else: the raw Markdown view)
        const listen = (type, handler) => view.addEventListener(type, (event) => {
            if (!this.suspended) handler(event);
        });
        listen('editcommand', (event) => this._onEditCommand(event));
        listen('selectionchange', () => this._onViewSelection());
        listen('runclick', (event) => this._onRunClick(event));
        // (the browser's Edit menu: not in a read-only document, which it would change)
        listen('undo', () => view.readOnly || this.undo());
        listen('redo', () => view.readOnly || this.redo());
        // (on a page, an editor without the focus shows no syntax: it reads as the formatted text it is)
        for (const type of ['focus', 'blur']) listen(type, () => view.batch(() => this._reveal()));
        this.load(options.markdown ?? '');
    }

    /**
     * The view is another's for a while (the raw Markdown view): the controller does nothing with it -- its edit commands
     * are the view's own -- until `resume`.
     */
    suspend() {
        this.history.seal();
        this.suspended = true;
        this._revealed = [];
        this._placed = null;
    }

    /**
     * The view is the controller's again: it shows `source` (the document as it was, or the text the view made of it,
     * which is then one step of the history, undone back to the document before `suspend`), the caret and the selection
     * at `caret` and `anchor` in it.
     */
    resume(source, caret, anchor = caret) {
        this.suspended = false;
        const changed = source !== this.state.source;
        if (changed) {
            const change = difference(this.state.source, source, Math.min(caret, anchor));
            this.history.record({
                ...change, kind: 'raw', before: { ...this.selection }, after: { caret, anchor },
            });
            this.history.seal();
            this.state = new MarkdownState(source, { caret });
        }
        const clamp = (offset) => Math.max(0, Math.min(this.state.source.length, offset));
        this.selection = { anchor: clamp(anchor), caret: clamp(caret) };
        this._revealed = [];
        this.view.setParagraphs(this.state.paragraphs.map((p) => this._viewParagraph(p)));
        this._showSelection(true);
        if (changed) this._changed();
    }

    // ---- The document ------------------------------------------------------------------------------------------------

    /** Loads a new document (no history); the caret at its start. */
    load(markdown) {
        this.lineEnding = lineEndingOf(markdown);
        const source = markdown.replace(/\r\n?/g, '\n');
        this.state = new MarkdownState(source, { caret: 0 });
        this.history.clear();
        this._revealed = [];
        this.view.setParagraphs(this.state.paragraphs.map((p) => this._viewParagraph(p)));
        this.selection = { anchor: 0, caret: 0 };
        this._showSelection(false);
        this._changed();
    }

    /** The document's Markdown, in its own line endings. */
    getMarkdown() {
        const source = this.state.source;
        return this.lineEnding === '\n' ? source : source.replace(/\n/g, this.lineEnding);
    }

    /** An edit of the host's own: [start, end) of the source (line endings "\n") replaced, one undo step. */
    replace(start, end, text, caret = start + text.length) {
        const source = this.state.source.slice(0, start) + text.replace(/\r\n?/g, '\n') + this.state.source.slice(end);
        this.history.seal();
        this.apply({ source, caret, anchor: caret }, 'host');
        this.history.seal();
    }

    /** Selects [anchor, caret) of the source (offsets in the source with "\n" line endings), the caret at `caret`. */
    select(anchor, caret = anchor) {
        const length = this.state.source.length;
        const clamp = (offset) => Math.max(0, Math.min(length, offset));
        this.history.seal();
        this._select(clamp(anchor), clamp(caret));
    }

    get canUndo() {
        return this.history.canUndo;
    }

    get canRedo() {
        return this.history.canRedo;
    }

    undo() {
        const step = this.history.undo();
        if (!step) return;
        const source = this.state.source;
        if (source.slice(step.start, step.start + step.inserted.length) !== step.inserted) {
            throw new Error('The source is not what the step to undo was made on.');
        }
        const before = source.slice(0, step.start) + step.removed + source.slice(step.start + step.inserted.length);
        this._setSource(before, step.before.caret, step.before.anchor,
            { start: step.start, removed: step.inserted.length, inserted: step.removed.length });
    }

    redo() {
        const step = this.history.redo();
        if (!step) return;
        const source = this.state.source;
        if (source.slice(step.start, step.start + step.removed.length) !== step.removed) {
            throw new Error('The source is not what the step to redo was made on.');
        }
        const after = source.slice(0, step.start) + step.inserted + source.slice(step.start + step.removed.length);
        this._setSource(after, step.after.caret, step.after.anchor,
            { start: step.start, removed: step.removed.length, inserted: step.inserted.length });
    }

    // ---- Commands a page calls -------------------------------------------------------------------------------------

    /** Toggles an inline format: 'strong', 'em', 'underline', 'strike', 'code', 'sub', 'sup'. */
    toggleInline(format) {
        this._syncFromView();
        this._run(() => toggleInline(this.state, this.selection, format), 'format');
    }

    /** Whether the selection (or the caret) is in an inline format. */
    isInline(format) {
        this._syncFromView();
        return isInline(this.state, this.selection, format);
    }

    /** Makes the paragraphs of the selection a type ('paragraph', 'h1'...'h6', 'quote', 'bullet', 'number', 'task', 'code'). */
    setBlockType(type) {
        this._syncFromView();
        this._run(() => blocks.setBlockType(this.state, this.selection, type), 'block');
    }

    /** Makes the paragraphs of the selection headings of `level` (1 to 6), or paragraphs (0); not a toggle. */
    setHeadingLevel(level) {
        this._run(() => blocks.setHeadingLevel(this.state, this.selection, level), 'block');
    }

    /** One heading level deeper (`by` 1: a paragraph becomes Heading 1) or shallower (-1), for each paragraph. */
    stepHeadingLevel(by) {
        this._run(() => blocks.stepHeadingLevel(this.state, this.selection, by), 'block');
    }

    /** The type of the paragraph at the caret. */
    get blockType() {
        this._syncFromView();
        return blocks.blockTypeAt(this.state, this.selection.caret);
    }

    insertRule() {
        this._run(() => blocks.insertRule(this.state, this.selection), 'block');
    }

    insertTable(columns = 3, rows = 2) {
        this._run(() => blocks.insertTable(this.state, this.selection, columns, rows), 'block');
    }

    insertCodeBlock(language = '') {
        this._run(() => blocks.insertCodeBlock(this.state, this.selection, language), 'block');
    }

    /**
     * The selection made a link to `address`, or the address of the link the caret is in changed (an empty one takes
     * that link away, leaving its text): blocks.insertLink.
     */
    insertLink(address, title = '') {
        if (address === null || address === undefined) return;
        this._run(() => blocks.insertLink(this.state, this.selection, String(address), title), 'block');
    }

    insertImage(address, description = '') {
        if (address) this._run(() => blocks.insertImage(this.state, this.selection, address, description), 'block');
    }

    /**
     * The link the caret (or the whole of the selection) is inside of, the one `insertLink` changes: { destination,
     * title, start, end } (source offsets), or null.
     */
    linkAtCaret() {
        this._syncFromView();
        const { anchor, caret } = this.selection;
        const found = blocks.linkAt(this.state, Math.min(anchor, caret), Math.max(anchor, caret));
        if (!found) return null;
        return { destination: found.destination, title: found.title ?? null, start: found.start, end: found.end };
    }

    // ---- The one primitive ---------------------------------------------------------------------------------------------

    // A command of the page's: worked out on the caret as it is now (`command` is a function of the state), one step.
    _run(command, kind) {
        this._syncFromView();
        const result = command();
        if (!result) return;
        this.history.seal();
        this.apply(result, kind);
        this.history.seal();
    }

    /** A command's result made the document: `{ source, caret, anchor }`, recorded as one step of `kind`. */
    apply(result, kind = 'edit') {
        if (!result) return;
        const before = this.state.source;
        const caret = result.caret ?? this.selection.caret;
        const anchor = result.anchor ?? (result.caret === null || result.caret === undefined ? this.selection.anchor : caret);
        if (result.source === before) {
            this._select(anchor, caret);
            return;
        }
        const hint = Math.min(this.selection.anchor, this.selection.caret, caret);
        const change = difference(before, result.source, hint);
        this.history.record({
            ...change, kind, before: { caret: this.selection.caret, anchor: this.selection.anchor }, after: { caret, anchor },
        });
        this._setSource(result.source, caret, anchor,
            { start: change.start, removed: change.removed.length, inserted: change.inserted.length });
    }

    // The source changed (`change`: where, and how many characters went and came; read again only where it can have
    // made a difference, MarkdownState.edited), and the view is given the paragraphs that differ.
    _setSource(source, caret, anchor = caret, change = null) {
        const old = this.state;
        caret = Math.max(0, Math.min(caret, source.length));
        anchor = Math.max(0, Math.min(anchor, source.length));
        this.state = MarkdownState.edited(old, source, change, { caret });
        const update = changedParagraphs(old.paragraphs, this.state.paragraphs);
        this.selection = { anchor, caret };
        this.view.batch(() => {
            this._unrevealAll(update);
            if (update) {
                const inserted = this.state.paragraphs.slice(update.first, update.first + update.insertCount)
                    .map((p) => this._viewParagraph(p));
                this.view.replaceParagraphs(update.first, update.removeCount, inserted, { scrollCaretIntoView: true });
            }
            this._showSelection(true);
        });
        this._changed();
    }

    _select(anchor, caret) {
        this.selection = { anchor, caret };
        this.view.batch(() => {
            this._unrevealAll(null);
            this._showSelection(true);
        });
    }

    _changed() {
        this.version++;
        this.dispatchEvent(new Event('change'));
    }

    // ---- The view --------------------------------------------------------------------------------------------------

    _viewParagraph(p, caret = null) {
        const { runs, listMarker, markerTextLength } = viewRunsOf(p, caret, this.state.doc, this.options);
        const paragraph = { runs, styleId: p.styleId, listMarker, markerTextLength };
        if (p.cell) {
            const { table, row, column, align, header } = p.cell;
            paragraph.cell = { table, row, column, align, header };
        }
        return paragraph;
    }

    // Puts the view's selection where the source selection is, and shows the syntax at the caret.
    _showSelection(scroll) {
        const projection = this.state.projection;
        const caret = projection.toView(this.selection.caret);
        const anchor = projection.toView(this.selection.anchor);
        this._placed = { caret, anchor };
        this._reveal();
        if (this.selection.anchor === this.selection.caret) this.view.setCaret(caret, { scroll });
        else this.view.select(anchor, caret, { scroll });
        this._placed = { caret: this.view.caret, anchor: this.view.anchor };
        this.dispatchEvent(new Event('selectionchange'));
    }

    _onViewSelection() {
        if (this._syncFromView()) this.dispatchEvent(new Event('selectionchange'));
    }

    // The source selection made what the view's is, if the user moved it (the view tells of a move a little later, and a
    // command must not act on the caret before it): true if it changed.
    _syncFromView() {
        const view = this.view;
        if (!view.caret || this.suspended) return false; // (suspended, the view's caret is in another's text)
        const placed = this._placed;
        if (placed && samePosition(placed.caret, view.caret) && samePosition(placed.anchor, view.anchor)) return false;
        const projection = this.state.projection;
        this.selection = { anchor: projection.toSource(view.anchor), caret: projection.toSource(view.caret) };
        this._placed = { caret: view.caret, anchor: view.anchor };
        this.history.seal();
        view.batch(() => this._reveal());
        return true;
    }

    /** Shows or hides the syntax at the caret again, after what decides it changed (the view made read-only). */
    refreshReveal() {
        if (!this.suspended) this.view.batch(() => this._reveal());
    }

    // The paragraphs whose syntax is shown at the caret: its own, and the other fence of a block of code. (None in a
    // read-only document, which is read as the formatted text it is: the user, 2026-11-03.)
    _reveal() {
        const caret = this.selection.caret;
        const paragraphs = this.state.paragraphs;
        const wanted = [];
        const focused = this.view.hasFocus || this.options.revealUnfocused === true;
        if (this.options.revealSyntax !== false && focused && !this.view.readOnly) {
            const index = this.state.projection.paragraphAt(caret);
            const p = paragraphs[index];
            if (p && caret >= p.start && caret <= p.end) {
                wanted.push({ index, caret });
                if (p.kind === 'fence' || p.kind === 'fence-close') {
                    const partner = this._fencePartner(index);
                    if (partner !== null) wanted.push({ index: partner, caret: paragraphs[partner].start });
                }
            }
        }
        // (what is shown and is no longer wanted is hidden again)
        const keep = [];
        for (const shown of this._revealed) {
            const index = this._indexOfViewParagraph(shown);
            if (index < 0) continue;
            if (wanted.some((w) => w.index === index)) {
                keep.push({ paragraph: shown.paragraph, index });
                continue;
            }
            const p = paragraphs[index];
            const { runs, listMarker, markerTextLength } = viewRunsOf(p, null, this.state.doc, this.options);
            this.view.restyleParagraph(index, runs, listMarker, markerTextLength);
        }
        this._revealed = keep;
        for (const { index, caret: at } of wanted) {
            const { runs, listMarker, markerTextLength } = viewRunsOf(paragraphs[index], at, this.state.doc, this.options);
            this.view.restyleParagraph(index, runs, listMarker, markerTextLength);
            if (!this._revealed.some((shown) => shown.index === index)) {
                this._revealed.push({ paragraph: this.view.paragraphs[index], index });
            } else {
                this._revealed.find((shown) => shown.index === index).paragraph = this.view.paragraphs[index];
            }
        }
    }

    _fencePartner(index) {
        const paragraphs = this.state.paragraphs;
        const p = paragraphs[index];
        const step = p.kind === 'fence' ? 1 : -1;
        const other = p.kind === 'fence' ? 'fence-close' : 'fence';
        for (let k = index + step; k >= 0 && k < paragraphs.length && paragraphs[k].block === p.block; k += step) {
            if (paragraphs[k].kind === other) return k;
        }
        return null;
    }

    _indexOfViewParagraph(shown) {
        const paragraphs = this.view.paragraphs;
        if (paragraphs[shown.index] === shown.paragraph) return shown.index;
        return paragraphs.indexOf(shown.paragraph);
    }

    // Before the paragraphs are replaced: what is shown outside them is hidden again; what is inside them goes with them.
    _unrevealAll(update) {
        const paragraphs = this.state.paragraphs;
        const keep = [];
        for (const shown of this._revealed) {
            const index = this._indexOfViewParagraph(shown);
            if (index < 0) continue;
            if (update && index >= update.first && index < update.first + update.removeCount) continue;
            // (its index in the new projection: moved by the difference, if it is after the replaced paragraphs)
            const after = update && index >= update.first + update.removeCount;
            const p = paragraphs[after ? index + update.insertCount - update.removeCount : index];
            if (!p) continue;
            keep.push({ paragraph: shown.paragraph, index });
        }
        for (const shown of keep) {
            const newIndex = update && shown.index >= update.first + update.removeCount
                ? shown.index + update.insertCount - update.removeCount : shown.index;
            const p = paragraphs[newIndex];
            const { runs, listMarker, markerTextLength } = viewRunsOf(p, null, this.state.doc, this.options);
            if (runs.map((run) => run.text).join('') === shown.paragraph.runs.map((run) => run.text).join('')) {
                this.view.restyleParagraph(shown.index, runs, listMarker, markerTextLength);
            }
        }
        this._revealed = [];
    }

    // ---- Edit commands from the view -------------------------------------------------------------------------------

    _onEditCommand(event) {
        event.preventDefault(); // (the view's own editing never runs: the source is the authority)
        this._syncFromView();
        const { command, text } = event.detail;
        const state = this.state, selection = this.selection;
        const collapsed = selection.anchor === selection.caret;
        switch (command) {
            case 'insertText':
                return this.apply(commands.insertText(state, selection, text), 'typing');
            case 'paste':
                if (text === null || text === undefined) {
                    this.dispatchEvent(new CustomEvent('pasteother', { detail: event.detail }));
                    return;
                }
                this.history.seal();
                this.apply(commands.insertText(state, selection, text), 'paste');
                return this.history.seal();
            case 'enter':
                this.history.seal();
                this.apply(commands.enter(state, selection), 'enter');
                return this.history.seal();
            case 'softBreak':
                this.history.seal();
                this.apply(commands.softBreak(state, selection), 'enter');
                return this.history.seal();
            case 'backspace':
                return this.apply(commands.backspace(state, selection), collapsed ? 'backspace' : 'delete-selection');
            case 'deleteWordBack':
                return this.apply(commands.backspace(state, selection, true), 'backspace');
            case 'delete':
                return this.apply(commands.deleteForward(state, selection), collapsed ? 'delete' : 'delete-selection');
            case 'deleteWordForward':
                return this.apply(commands.deleteForward(state, selection, true), 'delete');
            case 'tab':
            case 'shiftTab':
                this.history.seal();
                this.apply(commands.tab(state, selection, command === 'shiftTab'), 'tab');
                return this.history.seal();
            case 'cut':
                this.history.seal();
                this.apply(commands.deleteSelection(state, selection), 'cut');
                return this.history.seal();
            case 'copy':
                event.detail.result = commands.copiedText(state, selection);
                return undefined;
            default:
                return undefined;
        }
    }

    // A click on a task's box checks or unchecks it (the caret stays where it was).
    _onRunClick(event) {
        const { position } = event.detail;
        const p = this.state.paragraphs[position.paragraph];
        if (!p?.task) return;
        const index = p.start + position.index;
        if (index < p.task.start || index > p.task.start + 3) return;
        event.preventDefault();
        if (this.view.readOnly) return;
        const result = commands.toggleTask(this.state, p.task.start);
        if (!result) return;
        this.history.seal();
        this.apply({ source: result.source, caret: this.selection.caret, anchor: this.selection.anchor }, 'task');
        this.history.seal();
    }
}

const samePosition = (a, b) => !!a && !!b && a.paragraph === b.paragraph && a.index === b.index;

// Which paragraphs of a projection differ from those of the one before: { first, removeCount, insertCount }, or null when
// none do. A table is replaced whole, so that its cells are of one table.
function changedParagraphs(before, after) {
    let first = 0;
    const limit = Math.min(before.length, after.length);
    // (a paragraph kept from the projection before is the very object: no key to compare)
    const same = (x, y) => x === y || x.key === y.key;
    while (first < limit && same(before[first], after[first])) first++;
    if (first === before.length && first === after.length) return null;
    let tail = 0;
    while (tail < limit - first && same(before[before.length - 1 - tail], after[after.length - 1 - tail])) tail++;
    let removeEnd = before.length - tail, insertEnd = after.length - tail;
    // (whole tables, both sides: a paragraph at `at` that is a cell of the same table as the one before it)
    const inTable = (list, at) => at > 0 && at < list.length && !!cellOf(list[at]) && cellOf(list[at - 1]) === cellOf(list[at]);
    while (first > 0 && (inTable(before, first) || inTable(after, first))) first--;
    while (inTable(before, removeEnd) || inTable(after, insertEnd)) {
        removeEnd++;
        insertEnd++;
    }
    return { first, removeCount: removeEnd - first, insertCount: insertEnd - first };
}

const cellOf = (p) => p?.cell?.table ?? null;
