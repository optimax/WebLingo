// VirtualTextView for the web: a rich text view of many independent paragraphs, each a list of runs, that lays out and
// draws only the paragraphs near the viewport however long the document is, and draws its own caret and selection. The
// counterpart of OptimaxTools.RichText.VirtualTextView (see Documents/virtual-text-view-control-tutorial.md in
// MarkdownTools): the same model and the same division of work.
//
// - The document is a list of paragraphs (see model.js); a position is { paragraph, index }.
// - The styles are the host's: a style provider says what a paragraph's and a run's style IDs look like (styles.js).
// - The undo history is the host's: the view tells its `editRecorder` of each edit the user makes, as a TextEdit, and
//   replays one given back to `applyEdit`.
// - The commands are the host's: the view handles typing, the caret and the selection, the mouse, Enter, Shift+Enter,
//   Backspace, Delete, Tab (with `acceptsTab`), select all, cut, copy and paste. Before it changes the document it raises
//   a cancelable 'editcommand' event: a host that keeps the document elsewhere (a Markdown source) does the edit itself,
//   gives the view the paragraphs that changed (`replaceParagraphs`) and calls preventDefault().
// - It scrolls itself: it fills its host element (which must have a height) and scrolls inside it.

import {
    COMPUTED_PROPERTIES, DEFAULT_DOCUMENT_STYLE, DEFAULT_LINK_STYLE, ResolvedParagraphStyle, characterCss, sides,
} from './styles.js';
import {
    SOFT_BREAK, TextEdit, cloneParagraph, comparePositions, formatAt, normalizeRuns, paragraphLength, paragraphText,
    plainParagraph, pos, samePosition, splitRuns, textBetween,
} from './model.js';
import {
    backspaceStart, deleteEnd, nextCharacterBoundary, nextWordBoundary, previousCharacterBoundary, previousWordBoundary,
    wordAt,
} from './text.js';

const BUFFER = 600; // (pixels laid out above and below the viewport)
const MIRROR_AROUND = 5, MIRROR_LIMIT = 200; // (screen reader mode: the paragraphs round the caret; a selection's most)
const ZWSP = '\u200B';
const BREAKS = /(\u2028)/; // (a run cut at its line breaks, each break kept as a piece of its own)

const CSS = `
.vtv { position: relative; overflow: hidden; min-height: 120px; outline: none; cursor: text; }
.vtv-scroller { position: absolute; inset: 0; overflow: auto; overflow-anchor: none; }
.vtv-page { position: relative; box-sizing: border-box; }
.vtv-content { position: absolute; left: 0; right: 0; }
.vtv-para { display: flow-root; box-sizing: border-box; }
.vtv-box { position: relative; white-space: pre-wrap; overflow-wrap: break-word; tab-size: 4; box-sizing: border-box;
  min-height: 0; }
.vtv-nowrap .vtv-box { white-space: pre; overflow-wrap: normal; }
.vtv-hidden { font-size: 0 !important; letter-spacing: 0 !important; }
.vtv-align-left { text-align: left; }
.vtv-align-center { text-align: center; }
.vtv-align-right { text-align: right; }
.vtv-bold { font-weight: 700; }
.vtv-italic { font-style: italic; }
.vtv-collapsed { line-height: 0 !important; }
.vtv-collapsed > * { font-size: 0 !important; }
.vtv-collapsed .vtv-marker { display: none; }
.vtv-marker { position: absolute; white-space: nowrap; user-select: none; pointer-events: none; }
.vtv-link { cursor: pointer; }
.vtv-image { max-width: 100%; vertical-align: baseline; }
.vtv-image-missing { display: inline-block; vertical-align: baseline; border: 1px dashed #b0b4ba; color: #6a737d;
  font: 12px system-ui, sans-serif; padding: 18px 12px; background: #f6f8fa; }
.vtv-marks .vtv-eol::before { content: '\\00B6'; color: #9aa4b2; font-weight: normal; font-style: normal; }
.vtv-marks .vtv-br::before { content: '\\21B5'; color: #9aa4b2; }
.vtv-table-wrap { display: flow-root; }
.vtv-table { border-collapse: collapse; }
.vtv-table td { vertical-align: top; padding: 0; }
.vtv-overlay, .vtv-underlay { position: absolute; inset: 0; pointer-events: none; }
.vtv-gutter { position: absolute; top: 0; bottom: 0; box-sizing: border-box; overflow: hidden; pointer-events: none;
  user-select: none; display: none; }
.vtv-gutter-label { position: absolute; white-space: nowrap; line-height: normal; }
.vtv-gutter-name { position: absolute; white-space: nowrap; line-height: normal; overflow: hidden; text-overflow: ellipsis;
  text-align: right; }
.vtv-base { display: inline-block; width: 0; height: 0; }
.vtv-outline-mark { position: absolute; pointer-events: auto; cursor: pointer; opacity: 0.75; }
.vtv-outline-mark:hover { opacity: 1; }
.vtv-outline-mark::after { content: ''; position: absolute; left: 50%; bottom: 3px; width: var(--vtv-mark);
  height: var(--vtv-mark); margin-left: calc(var(--vtv-mark) / -2); background: currentColor;
  clip-path: polygon(0 13%, 100% 13%, 50% 100%); }
.vtv-outline-mark.vtv-closed::after { clip-path: polygon(6.5% 0, 93.5% 50%, 6.5% 100%); }
.vtv-current-line { position: absolute; left: 0; right: 0; display: none; }
.vtv-selection, .vtv-highlight { position: absolute; }
.vtv-caret { position: absolute; width: 2px; margin-left: -1px; }
.vtv-caret.vtv-blink { animation: vtv-blink 1.06s steps(1) infinite; }
@keyframes vtv-blink { 50% { visibility: hidden; } }
.vtv-composition { position: absolute; white-space: pre; text-decoration: underline; background: inherit; }
.vtv-status { position: absolute; width: 1px; height: 1px; overflow: hidden; clip-path: inset(50%); white-space: nowrap; }
.vtv-input { position: absolute; width: 1px; height: 1em; padding: 0; border: 0; margin: 0; opacity: 0; resize: none;
  overflow: hidden; white-space: pre; pointer-events: none; font-size: 16px; caret-color: transparent; }
`;

let styleInjected = false;
function injectStyles() {
    if (styleInjected || typeof document === 'undefined') return;
    const style = document.createElement('style');
    style.textContent = CSS;
    document.head.append(style);
    styleInjected = true;
}

const isMac = typeof navigator !== 'undefined' && /Mac|iPhone|iPad/.test(navigator.platform ?? '');

/** The name of a key with its modifiers, as a keymap names it: "Ctrl+B", "Ctrl+Shift+Z", "Shift+Enter". */
export function keyChord(event) {
    let key = event.key;
    if (/^Key[A-Z]$/.test(event.code ?? '')) key = event.code.slice(3);
    else if (/^Digit\d$/.test(event.code ?? '')) key = event.code.slice(5);
    else if (key && key.length === 1) key = key.toUpperCase();
    if (key === ' ') key = 'Space';
    const parts = [];
    if (event.ctrlKey || (isMac && event.metaKey)) parts.push('Ctrl');
    if (event.altKey) parts.push('Alt');
    if (event.shiftKey) parts.push('Shift');
    parts.push(key);
    return parts.join('+');
}

/** What a gutter shows (the WPF control's GutterContent, flags: the numbers and the names together, the names outer). */
export const GutterContent = Object.freeze({ None: 0, ParagraphNumbers: 1, StyleNames: 2, Outline: 8 });

const GUTTER_PADDING = 6; // (inside the gutter, either side of its numbers)
const GUTTER_SPACING = 8; // (between the gutter and the text: the control's DefaultGutterSpacing)
const GUTTER_COLUMN_GAP = 10; // (between the names and the numbers: the control's ColumnGap)
const GUTTER_NAMES_WIDTH = 96; // (the names' column: the control's DefaultNamesColumnWidth; a longer name ends in ...)
const OUTLINE_WIDTH = 16; // (the outline marks' column, nearest the text: the control's OutlineColumnWidth)
const OUTLINE_GAP = 4; // (between it and the numbers or the names: OutlineGap)
const OUTLINE_MARK_SHARE = 0.8; // (a mark's side: four fifths of the height of the heading's small letters)
const MIN_OUTLINE_MARK = 5;

/** The text a run shows: a line break inside the paragraph is a line break, unless the run is hidden. */
const shownText = (text, hidden) => (text.includes(SOFT_BREAK) ? text.replaceAll(SOFT_BREAK, hidden ? ' ' : '\n') : text);

export class VirtualTextView extends EventTarget {
    /**
     * @param {HTMLElement} host The element the view fills (give it a height).
     * @param {object} [options] { styleProvider, editRecorder, readOnly, acceptsTab, padding, wordWrap,
     *   showFormattingMarks, paragraphs, clipboard, screenReader }
     */
    constructor(host, options = {}) {
        super();
        injectStyles();
        this.host = host;
        host.classList.add('vtv');
        this.scroller = el('div', 'vtv-scroller');
        this.page = el('div', 'vtv-page');
        this.content = el('div', 'vtv-content');
        this.overlay = el('div', 'vtv-overlay');
        this.underlay = el('div', 'vtv-underlay'); // (under the text: the band behind the caret's line)
        this.currentLine = el('div', 'vtv-current-line');
        this.underlay.append(this.currentLine);
        this.caretElement = el('div', 'vtv-caret');
        this.composition = el('div', 'vtv-composition');
        this.input = document.createElement('textarea');
        this.input.className = 'vtv-input';
        this.input.setAttribute('autocapitalize', 'off');
        this.input.setAttribute('autocomplete', 'off');
        this.input.setAttribute('spellcheck', 'false');
        this.input.setAttribute('aria-label', options.label ?? 'Text editor');
        this.input.tabIndex = host.tabIndex >= 0 ? host.tabIndex : 0;
        this.gutter = el('div', 'vtv-gutter'); // (left of the text: the paragraph numbers)
        this.page.append(this.underlay, this.gutter, this.content, this.overlay);
        this.overlay.append(this.caretElement, this.composition);
        this.page.append(this.input);
        this.status = el('div', 'vtv-status'); // (what is said to a screen reader: `announce`)
        this.status.setAttribute('role', 'status');
        this.host.append(this.status);
        this.scroller.append(this.page);
        host.append(this.scroller);

        this.paragraphs = [];
        this.heights = [];
        this.measured = [];
        this.tops = [0];
        this._topsValid = 0;
        this._elements = new Map();
        this._paragraphStyles = new Map();
        this._characterCss = new Map();
        this._styleVersion = 0;
        this._rendered = null;
        this._deferrals = 0;
        this._layoutPending = false;
        this._selectionPool = [];
        this._highlightPool = [];
        this._version = 0;
        this._focused = false;
        this._composing = false;
        this._typingFormat = null;
        this._highlights = [];
        this.caret = null;
        this.anchor = null;
        this.affinity = 'forward';
        this.preferredX = null;
        this.keymap = new Map();
        this.clipboard = options.clipboard ?? null;
        this.mac = isMac; // (a Mac's keys: Cmd with the arrows, Backspace and Delete)

        this._styleProvider = options.styleProvider ?? null;
        this.editRecorder = options.editRecorder ?? null;
        this._readOnly = !!options.readOnly;
        this.acceptsTab = !!options.acceptsTab;
        this._padding = sides(options.padding ?? [12, 16, 12, 16]);
        this._wordWrap = options.wordWrap !== false;
        this._showFormattingMarks = !!options.showFormattingMarks;
        this._leftGutterContent = options.leftGutterContent ?? GutterContent.None;
        this._screenReader = !!options.screenReader;
        this._gutterLabels = [];
        this._gutterNames = [];
        this._gutterMarks = [];
        this._paragraphLabels = options.paragraphLabels ?? null;
        this._collapsed = new Set(); // (the collapsed headings: their paragraph objects, which edits move)
        this._collapseStamp = 0;
        this._folded = null; // (which paragraphs are folded away, by index, when any heading is collapsed)
        this._foldedSet = new Set(); // (the paragraphs folded when the heights last followed the folds)
        this._zoom = 1;
        this._applyDocumentStyle();
        this._wireInput();
        this._resizeObserver = new ResizeObserver(() => this._onResize());
        this._resizeObserver.observe(this.host);
        this.setParagraphs(options.paragraphs ?? [plainParagraph('')]);
    }

    /** Takes the view out of its host and lets go of what it listens to. */
    destroy() {
        clearTimeout(this._menuTimer);
        clearTimeout(this._announceTimer);
        this._resizeObserver.disconnect();
        this._unwire?.();
        this.scroller.remove();
        this.host.classList.remove('vtv');
    }

    // ---- Properties ---------------------------------------------------------------------------------------------

    get styleProvider() {
        return this._styleProvider;
    }

    set styleProvider(provider) {
        this._styleProvider = provider;
        this.invalidateStyles();
    }

    /** A document the user may look at and not change: typing and the edit commands do nothing (Copy still works). */
    get readOnly() {
        return this._readOnly;
    }

    set readOnly(value) {
        this._readOnly = !!value;
        this.input.readOnly = this._readOnly;
        this._drawOverlay();
    }

    /**
     * What the gutter left of the text shows (`GutterContent`, flags): `ParagraphNumbers`, each paragraph's number beside
     * its first line, `StyleNames`, the name of its style (`getStyleName`), left of the numbers when both are shown, or
     * `None` (the default), when the gutter is not there and takes no room. A view setting, not the document's; how it
     * looks is the document style's `gutter`.
     */
    get leftGutterContent() {
        return this._leftGutterContent;
    }

    set leftGutterContent(content) {
        const value = content || GutterContent.None;
        if (value === this._leftGutterContent) return;
        this._leftGutterContent = value;
        this._applyDocumentStyle();
        this._invalidateHeights();
    }

    /**
     * What the gutter's names (`GutterContent.StyleNames`) say of a paragraph, by its index, where the host names
     * paragraphs itself rather than by their styles (the raw Markdown view names each line by the kind of block it is):
     * a function of the index, whose null answer shows the style's name as ever and an empty one nothing. Null (the
     * default) asks nothing. The control's ParagraphLabels; the gutter asks again each time it is drawn.
     */
    get paragraphLabels() {
        return this._paragraphLabels;
    }

    set paragraphLabels(labels) {
        this._paragraphLabels = labels ?? null;
        if (this._leftGutterContent & GutterContent.StyleNames) this._afterHostChange(false);
    }

    get padding() {
        return [...this._padding];
    }

    set padding(value) {
        this._padding = sides(value);
        this._applyDocumentStyle();
        this._invalidateHeights();
    }

    get wordWrap() {
        return this._wordWrap;
    }

    set wordWrap(value) {
        this._wordWrap = !!value;
        this.host.classList.toggle('vtv-nowrap', !this._wordWrap);
        this._invalidateHeights();
    }

    /** Marks the ends of paragraphs (¶) and the line breaks inside them (↵). */
    get showFormattingMarks() {
        return this._showFormattingMarks;
    }

    set showFormattingMarks(value) {
        this._showFormattingMarks = !!value;
        this.host.classList.toggle('vtv-marks', this._showFormattingMarks);
        this._invalidateHeights();
    }

    /** The scale everything is drawn at (1 is 100%): presentation only, the document is not changed. */
    get zoom() {
        return this._zoom;
    }

    set zoom(value) {
        const zoom = Math.min(5, Math.max(0.25, Number(value) || 1));
        if (zoom === this._zoom) return;
        this._zoom = zoom;
        this.invalidateStyles();
    }

    /** Counts every change of the document, so that a host can tell whether it changed since it last looked. */
    get version() {
        return this._version;
    }

    get paragraphCount() {
        return this.paragraphs.length;
    }

    get hasFocus() {
        return this._focused;
    }

    /** The selection, start before end; null when nothing is selected. */
    get selection() {
        if (!this.caret || !this.anchor || samePosition(this.caret, this.anchor)) return null;
        return comparePositions(this.anchor, this.caret) < 0
            ? { start: this.anchor, end: this.caret }
            : { start: this.caret, end: this.anchor };
    }

    get hasSelection() {
        return this.selection !== null;
    }

    get selectedText() {
        const selection = this.selection;
        return selection ? textBetween(this.paragraphs, selection.start, selection.end) : '';
    }

    /** The format text typed next is given, on top of what it takes from the text before it (a function of a run). */
    get typingFormat() {
        return this._typingFormat;
    }

    setTypingFormat(format) {
        this._typingFormat = format ?? null;
    }

    // ---- The document -------------------------------------------------------------------------------------------

    /** Loads a whole new document (paragraphs, or strings of text) and leaves it with no caret. */
    setParagraphs(paragraphs) {
        const list = [...paragraphs].map((p) => (typeof p === 'string' ? plainParagraph(p) : p));
        if (list.length === 0) list.push(plainParagraph(''));
        this.paragraphs = list;
        this._elements.clear();
        this._collapsed.clear();
        this._collapseStamp++;
        this._foldedSet = new Set();
        this.heights = list.map((_, i) => this._estimateHeight(i));
        this.measured = list.map(() => false);
        this._topsValid = 0;
        this.caret = this.anchor = null;
        this._typingFormat = null;
        this.scroller.scrollTop = 0;
        this.scroller.scrollLeft = 0;
        this._changed();
        this._layout();
        this._selectionChanged();
    }

    /** The whole text, paragraphs joined by "\n" (a line break inside a paragraph is U+2028). */
    getText() {
        return this.paragraphs.map(paragraphText).join('\n');
    }

    getParagraphText(index) {
        return paragraphText(this.paragraphs[index]);
    }

    /** A copy of a paragraph to read or keep. */
    getParagraph(index) {
        return cloneParagraph(this.paragraphs[index]);
    }

    getTextRange(start, end) {
        const [a, b] = comparePositions(start, end) <= 0 ? [start, end] : [end, start];
        return textBetween(this.paragraphs, this._clamp(a), this._clamp(b));
    }

    /**
     * Replaces `removeCount` paragraphs from `first` with `replacement` (the very objects go into the document), leaving
     * the caret and the selection where they were: before the paragraphs unchanged, after them moved by the difference,
     * inside them where `mapPosition` says. Not recorded for undo unless `record` is set. Only the paragraphs replaced
     * are measured again.
     */
    replaceParagraphs(first, removeCount, replacement, { mapPosition = null, scrollCaretIntoView = true, record = false } = {}) {
        if (first < 0 || removeCount < 0 || first + removeCount > this.paragraphs.length) {
            throw new RangeError('The paragraphs to replace are not all in the document.');
        }
        const inserted = [...replacement];
        const caretBefore = this.caret, anchorBefore = this.anchor;
        const removed = record ? this.paragraphs.slice(first, first + removeCount).map(cloneParagraph) : null;
        for (const paragraph of this.paragraphs.slice(first, first + removeCount)) this._elements.delete(paragraph);
        const collapsed = this._collapsedAmong(first, removeCount);
        this.paragraphs.splice(first, removeCount, ...inserted);
        if (collapsed.length || this._collapsed.size) this._handOnCollapsed(collapsed, first, removeCount, inserted);
        this.heights.splice(first, removeCount, ...inserted.map(() => 0));
        this.measured.splice(first, removeCount, ...inserted.map(() => false));
        for (let i = first; i < first + inserted.length; i++) this.heights[i] = this._estimateHeight(i);
        this._repairTableNeighbours(first, inserted.length);
        this._topsValid = Math.min(this._topsValid, first);
        if (this.paragraphs.length === 0) {
            this.paragraphs.push(plainParagraph(''));
            this.heights.push(this._estimateHeight(0));
            this.measured.push(false);
        }
        const moved = (position) => {
            if (!position) return null;
            if (position.paragraph < first) return position;
            if (position.paragraph >= first + removeCount) {
                return pos(position.paragraph + inserted.length - removeCount, position.index);
            }
            if (mapPosition) return this._clamp(mapPosition(position));
            const p = inserted.length === 0 ? first : first + Math.min(position.paragraph - first, inserted.length - 1);
            return this._clamp(pos(p, position.index));
        };
        const caretAfter = moved(caretBefore);
        this.caret = caretAfter;
        this.anchor = anchorBefore && caretBefore && samePosition(anchorBefore, caretBefore) ? caretAfter : moved(anchorBefore);
        if (record) {
            this.editRecorder?.recordEdit(new TextEdit({
                first, before: removed, after: inserted.map(cloneParagraph), caretBefore: caretBefore ?? pos(first, 0),
                caretAfter: caretAfter ?? pos(first, 0), anchorBefore, anchorAfter: this.anchor, kind: 'structure',
            }));
        }
        this._changed();
        this._afterHostChange(scrollCaretIntoView);
    }

    /**
     * Gives a paragraph other runs that say the same text (syntax shown or hidden, say), and its list marker and how many
     * characters of its text hang where the marker is drawn. Nothing is recorded, the caret does not move, and the
     * version does not change. False, and nothing done, if they are what the paragraph has.
     */
    restyleParagraph(index, runs, listMarker = this.paragraphs[index]?.listMarker ?? null, markerTextLength = 0) {
        const paragraph = this.paragraphs[index];
        if (!paragraph) throw new RangeError('No such paragraph.');
        const restyled = runs.map((run) => ({ ...run }));
        if (restyled.map((run) => run.text).join('') !== paragraphText(paragraph)) {
            throw new Error("The runs must say the paragraph's own text.");
        }
        if ((paragraph.listMarker ?? null) === (listMarker ?? null) && (paragraph.markerTextLength ?? 0) === markerTextLength &&
            sameRuns(paragraph.runs, restyled)) return false;
        paragraph.runs = restyled;
        paragraph.listMarker = listMarker ?? null;
        paragraph.markerTextLength = markerTextLength;
        paragraph._stamp = (paragraph._stamp ?? 0) + 1;
        this.measured[index] = false;
        this._afterHostChange(false);
        return true;
    }

    /** Holds back the laying out until `fn` is done: several changes are then one pass. */
    batch(fn) {
        this._deferrals++;
        try {
            return fn();
        } finally {
            if (--this._deferrals === 0 && this._layoutPending) {
                const scroll = this._scrollPending;
                this._layoutPending = this._scrollPending = false;
                this._afterHostChange(scroll);
            }
        }
    }

    /** The host's stylesheet changed: every style is asked for again, and the document laid out again. */
    invalidateStyles() {
        this._paragraphStyles.clear();
        this._characterCss.clear();
        this._linkLook = null;
        this._styleVersion++;
        this._elements.clear();
        this._applyDocumentStyle();
        this._invalidateHeights();
    }

    // ---- Caret and selection ------------------------------------------------------------------------------------

    /** Puts the caret at `position` (clamped), with no selection, and scrolls it into view. */
    setCaret(position, { scroll = true } = {}) {
        const clamped = this._clamp(position);
        this._revealPosition(clamped);
        this.caret = this.anchor = clamped;
        this.affinity = 'forward';
        this._caretMoved(scroll);
    }

    /** Selects from `anchor` to `active`, the caret at `active`, and scrolls it into view. */
    select(anchor, active, { scroll = true } = {}) {
        this.anchor = this._clamp(anchor);
        this.caret = this._clamp(active);
        this._revealPosition(this.anchor);
        this._revealPosition(this.caret);
        this._caretMoved(scroll);
    }

    selectAll() {
        const last = this.paragraphs.length - 1;
        this.select(pos(0, 0), pos(last, paragraphLength(this.paragraphs[last])));
    }

    focus() {
        this.input.focus({ preventScroll: true });
    }

    /** Scrolls the least distance that brings `position` into view. */
    scrollIntoView(position) {
        this._scrollToPosition(this._clamp(position), 'nearest');
    }

    /** Scrolls so that the line of `position` is in the middle of the view. */
    scrollToMiddle(position) {
        this._scrollToPosition(this._clamp(position), 'middle');
    }

    /** How far the top of the line of `position` is from the top of what can be seen; null when it is out of sight. */
    lineTopInView(position) {
        const rect = this.getCaretRect(position);
        if (!rect) return null;
        const top = rect.top - this.scroller.getBoundingClientRect().top;
        return top >= 0 && top + rect.height <= this.scroller.clientHeight ? top : null;
    }

    /**
     * Scrolls so that the top of the line of `position` is `top` from the top of what can be seen, as far as the document
     * reaches either way: after a switch of views, the caret's line where the eye left it (MaxPad's ScrollToLineAt). A
     * paragraph not laid out yet has an estimated height, which laying it out puts right, so it takes a few rounds.
     */
    scrollToLineAt(position, top) {
        const p = this._clamp(position);
        this._scrollToPosition(p, 'nearest');
        for (let round = 0; round < 6; round++) {
            const rect = this._caretRect(p);
            if (!rect) break;
            const most = Math.max(0, this.scroller.scrollHeight - this.scroller.clientHeight);
            const wanted = Math.max(0, Math.min(most, rect.top - top)); // (as far as the document's ends let it)
            if (Math.abs(this.scroller.scrollTop - wanted) < 0.5) break;
            this.scroller.scrollTop = wanted;
            this._layout();
        }
        this._drawOverlay();
    }

    /** The rectangle (client coordinates) a caret at `position` takes; null when it is not laid out. */
    getCaretRect(position) {
        const rect = this._caretRect(this._clamp(position));
        if (!rect) return null;
        const page = this.page.getBoundingClientRect();
        return { left: page.left + rect.left, top: page.top + rect.top, width: 1, height: rect.height };
    }

    /** The position under a point (client coordinates), as a click there would put the caret. */
    positionFromPoint(clientX, clientY) {
        const page = this.page.getBoundingClientRect();
        return this._hit(clientX - page.left, clientY - page.top)?.position ?? null;
    }

    // ---- Highlights ---------------------------------------------------------------------------------------------

    /** Shades ranges behind the text ({ start, end, color }: search matches, say). Null or empty clears them. */
    setHighlights(highlights) {
        const ordered = (h) => (comparePositions(h.start, h.end) <= 0 ? h : { ...h, start: h.end, end: h.start });
        this._highlights = (highlights ?? []).map(ordered).sort((a, b) => comparePositions(a.start, b.start));
        this._drawOverlay();
    }

    get highlights() {
        return this._highlights;
    }

    // ---- Formatting ---------------------------------------------------------------------------------------------

    /** The character style of the character before `position` (null for none). */
    getCharacterStyleIdAt(position) {
        const p = this._clamp(position);
        return formatAt(this.paragraphs[p.paragraph], p.index).styleId ?? null;
    }

    /** The link at `position`, { link, start, end }, or null. */
    getHyperlinkAt(position) {
        const p = this._clamp(position);
        const paragraph = this.paragraphs[p.paragraph];
        let at = 0;
        const runs = paragraph.runs;
        for (let r = 0; r < runs.length; r++) {
            const end = at + runs[r].text.length;
            if (runs[r].link && p.index >= at && p.index < end) {
                let start = at, stop = end;
                for (let b = r - 1; b >= 0 && sameLinkOf(runs[b], runs[r]); b--) start -= runs[b].text.length;
                for (let a = r + 1; a < runs.length && sameLinkOf(runs[a], runs[r]); a++) stop += runs[a].text.length;
                return { link: runs[r].link, start: pos(p.paragraph, start), end: pos(p.paragraph, stop) };
            }
            at = end;
        }
        return null;
    }

    /** Gives the text from `start` to `end` the character style `map` makes of each run's (one undo step). */
    mapCharacterStyles(start, end, map) {
        this._mapRuns(start, end, (run) => {
            const mapped = map(run.styleId ?? null);
            return mapped === (run.styleId ?? null) ? run : { ...run, styleId: mapped ?? undefined };
        });
    }

    setCharacterStyle(start, end, styleId) {
        this.mapCharacterStyles(start, end, () => styleId);
    }

    setHyperlink(start, end, link) {
        this._mapRuns(start, end, (run) => ({ ...run, link: link ?? undefined }));
    }

    /** Gives the paragraphs from `first` to `last` a style (one undo step). */
    setParagraphStyles(first, last, styleId) {
        const [a, b] = [Math.max(0, Math.min(first, last)), Math.min(this.paragraphs.length - 1, Math.max(first, last))];
        this._recordedChange(a, b, (paragraphs) => paragraphs.map((p) => ({ ...p, runs: p.runs, styleId })), 'style');
    }

    // ---- Editing (the host's calls) -----------------------------------------------------------------------------

    /** Types `text` at the caret, replacing the selection, as a paste would (line breaks start paragraphs). */
    insertText(text) {
        if (!this.caret || !text) return;
        this._insert(text.replace(/\r\n?/g, '\n'), 'paste');
    }

    /** Replaces from `start` to `end` as the user would by selecting it and typing `text` (the host is asked first). */
    typeOver(start, end, text) {
        if (this._readOnly) return;
        this.select(start, end, { scroll: false });
        if (text) this._command('insertText', text);
        else if (this.selection) this._command('delete');
    }

    /** Deletes the selection (one undo step). */
    deleteSelection() {
        const selection = this.selection;
        if (!selection) return;
        this._edit(selection.start, selection.end, '', null, 'delete');
    }

    /** Replays an edit (a recorded one, or its inverse to undo it). It is not recorded again. */
    applyEdit(edit) {
        const after = edit.after.map(cloneParagraph);
        for (const paragraph of this.paragraphs.slice(edit.first, edit.first + edit.before.length)) {
            this._elements.delete(paragraph);
        }
        this.paragraphs.splice(edit.first, edit.before.length, ...after);
        this.heights.splice(edit.first, edit.before.length, ...after.map(() => 0));
        this.measured.splice(edit.first, edit.before.length, ...after.map(() => false));
        for (let i = edit.first; i < edit.first + after.length; i++) this.heights[i] = this._estimateHeight(i);
        this._topsValid = Math.min(this._topsValid, edit.first);
        this.caret = this._clamp(edit.caretAfter);
        this.anchor = edit.anchorAfter ? this._clamp(edit.anchorAfter) : this.caret;
        this._changed();
        this._afterHostChange(true);
        this._selectionChanged();
    }

    /** Copies the selection onto the clipboard: true once it is there ('clipboardunavailable' if it cannot be). */
    async copy() {
        const text = this._copyText();
        return text !== null && this._writeClipboard(text);
    }

    /**
     * Cuts the selection: it is taken out once it is on the clipboard -- not when the clipboard cannot be written
     * ('clipboardunavailable'), nor when the selection changed meanwhile. True when it was cut.
     */
    async cut() {
        const selection = this.selection;
        if (!selection || this._readOnly) return false;
        const text = this._copyText();
        if (text === null || !(await this._writeClipboard(text))) return false;
        const now = this.selection;
        if (!now || !samePosition(now.start, selection.start) || !samePosition(now.end, selection.end)) return false;
        this._command('cut', text, { written: true });
        return true;
    }

    /** Pastes the clipboard's text at the caret ('clipboardunavailable', and nothing pasted, if it cannot be read). */
    async paste() {
        if (!this.caret || this._readOnly) return;
        let text;
        try {
            const clipboard = this.clipboard ?? navigator.clipboard;
            if (!clipboard?.readText) throw new Error('No clipboard to read.');
            text = await clipboard.readText();
        } catch {
            this.dispatchEvent(new Event('clipboardunavailable'));
            return;
        }
        this._command('paste', text);
    }

    // ---- The rules of a delete, for a host that does its own --------------------------------------------------

    static backspaceStart(text, index) {
        return backspaceStart(text, index);
    }

    static deleteEnd(text, index) {
        return deleteEnd(text, index);
    }

    static wordStart(text, index) {
        return previousWordBoundary(text, index);
    }

    static wordEnd(text, index) {
        return nextWordBoundary(text, index);
    }

    // ===============================================================================================================
    // Internals
    // ===============================================================================================================

    _clamp(position) {
        if (!position) return pos(0, 0);
        const p = Math.max(0, Math.min(position.paragraph ?? 0, this.paragraphs.length - 1));
        return pos(p, Math.max(0, Math.min(position.index ?? 0, paragraphLength(this.paragraphs[p]))));
    }

    _changed() {
        this._version++;
        if (this._documentChangeQueued) return;
        this._documentChangeQueued = true;
        queueMicrotask(() => {
            this._documentChangeQueued = false;
            this.dispatchEvent(new Event('documentchange'));
        });
    }

    _selectionChanged() {
        this._syncInput();
        if (this._selectionChangeQueued) return;
        this._selectionChangeQueued = true;
        queueMicrotask(() => {
            this._selectionChangeQueued = false;
            this.dispatchEvent(new Event('selectionchange'));
        });
    }

    _caretMoved(scroll) {
        this._typingFormat = null;
        if (scroll && this.caret) this._scrollToPosition(this.caret, 'nearest');
        this._drawOverlay();
        this.preferredX = this._caretRect(this.caret)?.left ?? this.preferredX;
        this._selectionChanged();
    }

    _afterHostChange(scroll) {
        if (this._deferrals > 0) {
            this._layoutPending = true;
            this._scrollPending ||= scroll;
            return;
        }
        this._layout();
        if (scroll && this.caret) this._scrollToPosition(this.caret, 'nearest');
        this._drawOverlay();
    }

    // ---- Styles ---------------------------------------------------------------------------------------------------

    _paragraphStyle(styleId) {
        const key = styleId ?? '';
        let resolved = this._paragraphStyles.get(key);
        if (!resolved) {
            const style = this._styleProvider?.getParagraphStyle?.(styleId ?? null);
            const className = typeof style?.className === 'string' ? style.className.trim() : '';
            // (a style of the host's stylesheet: what it makes of a box of the class, read off a probe; not kept while
            // the view is out of the page, where nothing is styled)
            const computed = className ? this._computedStyleOf(className) : null;
            resolved = new ResolvedParagraphStyle(style, this._zoom, computed);
            if (!className || this.host.isConnected) this._paragraphStyles.set(key, resolved);
        }
        return resolved;
    }

    // What the host's stylesheet makes of a paragraph's box of the class, where the paragraphs are (so that what the
    // content inherits counts).
    _computedStyleOf(className) {
        const outer = el('div', 'vtv-para');
        const box = el('div', 'vtv-box ' + className);
        outer.style.cssText = 'position:absolute;visibility:hidden;left:0;right:0;';
        outer.append(box);
        this.content.append(outer);
        const style = getComputedStyle(box);
        const values = {};
        for (const property of COMPUTED_PROPERTIES) values[property] = style[property];
        outer.remove();
        return values;
    }

    // The CSS and the classes of a run: the hyperlink style's, its character style's, its own bold and italic.
    _runLook(run) {
        let css = '', className = '';
        const look = (style) => ({ css: characterCss(style, this._zoom), className: style?.className?.trim() ?? '' });
        if (run.link) {
            const link = this._linkLook ??= look(this._styleProvider?.getHyperlinkStyle?.() ?? DEFAULT_LINK_STYLE);
            css += link.css;
            className += ' ' + link.className;
        }
        if (run.styleId) {
            let own = this._characterCss.get(run.styleId);
            if (own === undefined) {
                own = look(this._styleProvider?.getCharacterStyle?.(run.styleId));
                this._characterCss.set(run.styleId, own);
            }
            css += own.css;
            className += ' ' + own.className;
        }
        if (this._classStyles) {
            if (run.bold) className += ' vtv-bold';
            if (run.italic) className += ' vtv-italic';
        } else {
            if (run.bold) css += 'font-weight:700;';
            if (run.italic) css += 'font-style:italic;';
        }
        return { css, className: className.trim() };
    }

    _applyDocumentStyle() {
        const style = { ...DEFAULT_DOCUMENT_STYLE, ...(this._styleProvider?.getDocumentStyle?.() ?? {}) };
        this.documentStyle = style;
        // (a stylesheet's document: its classes on the host element, what its rules for the content are under, and its
        // custom properties there, with the zoom as one more, --vtv-zoom, for sizes the stylesheet scales by it)
        const classes = typeof style.className === 'string' ? style.className.split(/\s+/).filter(Boolean) : [];
        for (const name of this._documentClasses ?? []) if (!classes.includes(name)) this.host.classList.remove(name);
        this.host.classList.add(...classes);
        this._documentClasses = classes;
        this._classStyles = classes.length > 0; // (bold and italic runs then by class: the stylesheet's to restyle)
        const variables = style.variables ?? {};
        for (const name of this._documentVariables ?? []) if (!(name in variables)) this.host.style.removeProperty(name);
        for (const [name, value] of Object.entries(variables)) this.host.style.setProperty(name, value);
        this._documentVariables = Object.keys(variables);
        this.host.style.setProperty('--vtv-zoom', String(this._zoom));
        const [top, right, bottom, left] = this._padding.map((side) => side * this._zoom);
        // (the gutter: another term of the left inset, inside the padding, as the control's -- and the text's maximum
        // width is the text's, so the gutter widens the page rather than narrowing the text)
        const gutter = this._styleGutter(style.gutter, left);
        const room = gutter ? gutter + GUTTER_SPACING * this._zoom : 0;
        this.page.style.paddingLeft = left + room + 'px';
        this.page.style.paddingRight = right + 'px';
        this.content.style.left = left + room + 'px';
        this.content.style.right = right + 'px';
        this._padTop = top;
        this._padBottom = bottom;
        // (the page: as wide as the text may be and where it goes -- the document style's maxWidth and placement, or,
        // with a page class, the stylesheet's max-width and margin, which are given the room around the text, padding and
        // gutter, as --vtv-page-inset: 2026-11-03, TO-DO "Editor Document Alignment")
        const pageClasses = typeof style.pageClassName === 'string' ? style.pageClassName.split(/\s+/).filter(Boolean) : [];
        for (const name of this._pageClasses ?? []) if (!pageClasses.includes(name)) this.page.classList.remove(name);
        this.page.classList.add(...pageClasses);
        this._pageClasses = pageClasses;
        if (pageClasses.length) {
            this.page.style.maxWidth = '';
            this.page.style.margin = '';
            this.page.style.setProperty('--vtv-page-inset', left + right + room + 'px');
        } else {
            const maxWidth = Number.isFinite(style.maxWidth) ? style.maxWidth * this._zoom + left + right + room : null;
            this.page.style.maxWidth = maxWidth ? maxWidth + 'px' : '';
            this.page.style.margin = style.placement === 'center' ? '0 auto' : '0';
            this.page.style.removeProperty('--vtv-page-inset');
        }
        this.host.style.background = style.background;
        this.host.style.color = style.color;
        this.caretElement.style.background = style.caretColor;
        this.host.classList.toggle('vtv-nowrap', !this._wordWrap);
        this.host.classList.toggle('vtv-marks', this._showFormattingMarks);
    }

    // ---- The gutter -----------------------------------------------------------------------------------------------

    // Styles the gutter and gives its width (0 when it shows nothing), as the control's: its numbers' column is as wide
    // as the number of paragraphs has digits (two at least), its names' column GUTTER_NAMES_WIDTH, outside the numbers,
    // with a gap between the two and room either side; the outline marks' column, OUTLINE_WIDTH, is nearest the text.
    _styleGutter(look, left) {
        const gutter = this.gutter;
        this._gutterDigits = this._digitsOfCount();
        this._gutterBaseline = null; // (measured again when it is drawn: the font may have changed)
        const numbers = (this._leftGutterContent & GutterContent.ParagraphNumbers) !== 0;
        const names = (this._leftGutterContent & GutterContent.StyleNames) !== 0;
        const outline = (this._leftGutterContent & GutterContent.Outline) !== 0;
        if (!numbers && !names && !outline) {
            gutter.style.display = 'none';
            return 0;
        }
        const g = { ...DEFAULT_DOCUMENT_STYLE.gutter, ...(look ?? {}) };
        const size = g.fontSize * this._zoom;
        const font = `${g.bold ? 700 : 400} ${size}px ${g.fontFamily}`;
        const canvas = (VirtualTextView._measure ??= document.createElement('canvas')).getContext('2d');
        canvas.font = font;
        const border = g.borderColor ? 1 : 0;
        const numbersWidth = numbers ? this._gutterDigits * canvas.measureText('0').width : 0;
        const namesWidth = names ? GUTTER_NAMES_WIDTH * this._zoom : 0;
        const gap = numbers && names ? GUTTER_COLUMN_GAP * this._zoom : 0;
        const outlineWidth = outline ? OUTLINE_WIDTH * this._zoom : 0;
        const outlineTaken = outline ? outlineWidth + (numbers || names ? OUTLINE_GAP * this._zoom : 0) : 0;
        const width = Math.ceil(numbersWidth + gap + namesWidth + outlineTaken + 2 * GUTTER_PADDING * this._zoom + border);
        gutter.style.cssText = `display:block;left:${left}px;width:${width}px;font:${font};color:${g.color};` +
            (g.background ? `background:${g.background};` : '') +
            (border ? `border-right:1px solid ${g.borderColor};` : '');
        this._outlineInset = GUTTER_PADDING * this._zoom + border; // (from the gutter's right edge to the outline column)
        this._outlineWidth = outlineWidth;
        this._gutterInset = this._outlineInset + outlineTaken; // (to its numbers)
        this._namesInset = this._gutterInset + numbersWidth + gap; // (and to its names)
        this._namesWidth = namesWidth;
        this._namesColor = g.namesColor ?? g.color;
        return width;
    }

    /**
     * What the gutter's names say of paragraph `index`: the host's `paragraphLabels`, when it answers; else the style
     * provider's `getStyleLabel(styleId)`, when it has one; else the style's `name`, or its ID when it has none.
     */
    getStyleName(index) {
        const own = this._paragraphLabels?.(index);
        if (own !== null && own !== undefined) return String(own);
        const styleId = this.paragraphs[index]?.styleId ?? null;
        const label = this._styleProvider?.getStyleLabel?.(styleId);
        if (label) return label;
        return this._paragraphStyle(styleId).style.name || (styleId ?? '');
    }

    _digitsOfCount() {
        return Math.max(2, String(Math.max(1, this.paragraphs?.length ?? 1)).length);
    }

    /**
     * The number the gutter gives paragraph `index`: its place among the document's blocks, from 1, where a table's row
     * is one block (its cells all have its first cell's number), as the control's. What a status bar says of the caret's
     * paragraph, to agree with the gutter.
     */
    getParagraphNumber(index) {
        const i = Math.max(0, Math.min(index, this.paragraphs.length - 1));
        return i + 1 - this._laterCellsBefore(i);
    }

    // How many paragraphs up to `index` are a table row's cell other than its first (counted once per change of the
    // document: none in a document with no table).
    _laterCellsBefore(index) {
        if (this._cellCount?.version !== this._version) {
            let counts = null, count = 0;
            for (let p = 0; p < this.paragraphs.length; p++) {
                if (this.paragraphs[p].cell?.column > 0) {
                    counts ??= new Int32Array(this.paragraphs.length);
                    count++;
                }
                if (counts) counts[p] = count;
            }
            this._cellCount = { version: this._version, counts };
        }
        return this._cellCount.counts ? this._cellCount.counts[index] : 0;
    }

    // The numbers beside the paragraphs laid out, each on its paragraph's first line, baseline to baseline (so that one
    // beside a large heading lines up with the heading's text). A paragraph with nothing to see (a code fence out of
    // sight) has none, so that it writes over no neighbour's -- unless its box is drawn (a border, a background) and its
    // room, margins and all, is half as tall again as a number (a rule): its number then sits in the middle of the room.
    // (all the places are read first and the numbers written after, so that the page is laid out once, not once a number)
    // The names go on the same lines, beside the numbers (each trimmed to its column, an empty one not drawn); a heading
    // that can be collapsed has its outline mark, nearest the text, its foot on the heading's baseline.
    _drawGutter() {
        const labels = this._gutterLabels;
        const numbers = [];
        const showNumbers = (this._leftGutterContent & GutterContent.ParagraphNumbers) !== 0;
        const showNames = (this._leftGutterContent & GutterContent.StyleNames) !== 0;
        const showOutline = (this._leftGutterContent & GutterContent.Outline) !== 0;
        if (showNumbers || showNames || showOutline) {
            const page = this._pageRect().top;
            if (this._gutterBaseline === null) {
                const probe = el('div', 'vtv-gutter-label');
                probe.append('0', el('span', 'vtv-base'));
                this.gutter.append(probe);
                const box = probe.getBoundingClientRect();
                this._gutterBaseline = probe.lastChild.getBoundingClientRect().top - box.top;
                this._gutterLabelHeight = box.height;
                probe.remove();
            }
            for (const element of this.content.children) {
                for (const box of element._boxes ?? [element._box]) {
                    const paragraph = box._paragraph;
                    if (paragraph.cell?.column > 0) continue; // (a table row is one number, beside its first cell)
                    let top;
                    if (box.classList.contains('vtv-collapsed')) {
                        const style = this._paragraphStyle(paragraph.styleId);
                        const outer = (element._boxes ? box : element).getBoundingClientRect();
                        if (!style.drawn || outer.height < 1.5 * this._gutterLabelHeight) continue;
                        top = outer.top - page + (outer.height - this._gutterLabelHeight) / 2;
                    } else {
                        top = box._base.getBoundingClientRect().top - page - this._gutterBaseline;
                    }
                    numbers.push({ index: box._index, top });
                }
            }
        }
        const write = (pool, className, k, text, css) => {
            let label = pool[k];
            if (!label) {
                label = el('div', className);
                this.gutter.append(label);
                pool.push(label);
            }
            if (label.textContent !== text) label.textContent = text;
            label.style.cssText = css;
        };
        const names = this._gutterNames;
        let shownNumbers = 0, shownNames = 0;
        for (const { index, top } of numbers) {
            if (showNumbers) {
                write(labels, 'vtv-gutter-label', shownNumbers++, String(this.getParagraphNumber(index)),
                    `top:${top}px;right:${this._gutterInset}px;`);
            }
            const name = showNames ? this.getStyleName(index) : '';
            if (name) {
                write(names, 'vtv-gutter-name', shownNames++, name,
                    `top:${top}px;right:${this._namesInset}px;width:${this._namesWidth}px;color:${this._namesColor};`);
            }
        }
        for (let k = shownNumbers; k < labels.length; k++) labels[k].style.cssText = 'display:none';
        for (let k = shownNames; k < names.length; k++) names[k].style.cssText = 'display:none';
        const marks = this._gutterMarks;
        let shownMarks = 0;
        for (const { index, top } of showOutline ? numbers : []) {
            if (!this.isCollapsible(index)) continue;
            let mark = marks[shownMarks];
            if (!mark) {
                mark = el('div', 'vtv-outline-mark');
                // (a click turns it, and starts no selection; the caret stays where it is)
                mark.addEventListener('mousedown', (event) => {
                    event.preventDefault();
                    event.stopPropagation();
                });
                mark.addEventListener('click', (event) => {
                    event.stopPropagation();
                    this.setCollapsed(mark._index, !this.isCollapsed(mark._index));
                });
                this.gutter.append(mark);
                marks.push(mark);
            }
            shownMarks++;
            const collapsed = this.isCollapsed(index);
            const style = this._paragraphStyle(this.paragraphs[index].styleId);
            const side = Math.min(Math.max(OUTLINE_MARK_SHARE * 0.5 * style.fontSize, MIN_OUTLINE_MARK * this._zoom),
                this._outlineWidth);
            const baseline = top + this._gutterBaseline;
            mark._index = index;
            mark.classList.toggle('vtv-closed', collapsed);
            mark.title = collapsed ? 'Expand' : 'Collapse';
            mark.style.cssText = `top:${baseline - side - 3}px;height:${side + 6}px;right:${this._outlineInset}px;` +
                `width:${this._outlineWidth}px;--vtv-mark:${side}px;`;
        }
        for (let k = shownMarks; k < marks.length; k++) marks[k].style.cssText = 'display:none';
    }

    // ---- The outline -----------------------------------------------------------------------------------------------
    //
    // The WPF control's outline (VirtualTextView.Outline.cs; MaxPad's outline marks). A heading -- a paragraph whose style
    // has an `outlineLevel` -- heads the paragraphs after it, down to the next heading of its level or a higher one (a
    // smaller number): lower headings and what they head, tables and pictures, are its part. A collapsed heading's part
    // is folded away: it takes no room, is not drawn, and the caret steps over it; a caret put into it by anything but
    // the caret keys (the host, a click on a link) opens the headings that fold it. A collapsed heading is known by its
    // paragraph object, and one an edit replaces with a heading of the same level and text hands its state on.

    /** The outline level of paragraph `index`: its style's `outlineLevel` (1 and up for a heading), 0 for any other. */
    getOutlineLevel(index) {
        const paragraph = this.paragraphs[index];
        if (!paragraph || paragraph.cell) return 0;
        return Math.max(0, Math.floor(this._paragraphStyle(paragraph.styleId).style.outlineLevel ?? 0) || 0);
    }

    /** Whether paragraph `index` heads a part that can be collapsed: a heading with something under it. */
    isCollapsible(index) {
        if (index < 0 || index + 1 >= this.paragraphs.length) return false;
        const level = this.getOutlineLevel(index);
        if (level <= 0) return false;
        const next = this.getOutlineLevel(index + 1);
        return next <= 0 || next > level;
    }

    /** Whether the heading at `index` is collapsed. */
    isCollapsed(index) {
        return this._collapsed.has(this.paragraphs[index]);
    }

    /** Whether paragraph `index` is folded away: in the part of a collapsed heading (not the heading, which shows). */
    isFolded(index) {
        this._ensureFolds();
        return !!this._folded?.[index];
    }

    /**
     * Collapses the heading at `index` (folding its part away) or expands it; nothing for a paragraph that is no
     * collapsible heading. A caret or selection end in what is folded comes back to the end of the heading's text.
     */
    setCollapsed(index, collapsed) {
        const heading = this.paragraphs[index];
        if (!heading || (collapsed && !this.isCollapsible(index))) return;
        if (!!collapsed === this._collapsed.has(heading)) return;
        if (collapsed) this._collapsed.add(heading);
        else this._collapsed.delete(heading);
        this._collapseStamp++;
        if (collapsed) {
            const end = pos(index, paragraphLength(heading));
            const caretFolded = this.caret && this.isFolded(this.caret.paragraph);
            const anchorFolded = this.anchor && this.isFolded(this.anchor.paragraph);
            if (caretFolded) this.caret = end;
            if (anchorFolded || (caretFolded && !this.anchor)) this.anchor = end;
            if (caretFolded || anchorFolded) this._selectionChanged();
        }
        this._outlineChanged();
    }

    /** Collapses every heading that has something under it. */
    collapseAll() {
        let any = false;
        for (let i = 0; i < this.paragraphs.length; i++) {
            if (this.isCollapsible(i) && !this._collapsed.has(this.paragraphs[i])) {
                this._collapsed.add(this.paragraphs[i]);
                any = true;
            }
        }
        if (!any) return;
        this._collapseStamp++;
        if (this.caret && this.isFolded(this.caret.paragraph)) {
            const shown = this._shownFrom(this.caret.paragraph, -1);
            this.caret = this.anchor = pos(shown, paragraphLength(this.paragraphs[shown]));
            this._selectionChanged();
        }
        this._outlineChanged();
    }

    /** Expands every collapsed heading: the whole document shows. */
    expandAll() {
        if (this._collapsed.size === 0) return;
        this._collapsed.clear();
        this._collapseStamp++;
        this._outlineChanged();
    }

    _outlineChanged() {
        this._afterHostChange(false);
        this.dispatchEvent(new Event('outlinechange'));
    }

    // Works out which paragraphs are folded, if the document or the collapsed headings changed since it last did; a
    // collapsed heading no longer in the document is forgotten.
    _ensureFolds() {
        if (this._collapsed.size === 0) {
            this._folded = null;
            return;
        }
        const n = this.paragraphs.length;
        if (this._folded?.length === n && this._foldsKey === `${this._version}:${this._collapseStamp}`) return;
        const folded = new Uint8Array(n);
        const present = new Set();
        let foldLevel = 0; // (the level of the collapsed heading whose part this is; 0 outside any)
        for (let i = 0; i < n; i++) {
            const paragraph = this.paragraphs[i];
            const level = this.getOutlineLevel(i);
            if (foldLevel > 0) {
                if (level > 0 && level <= foldLevel) {
                    foldLevel = 0; // (a heading of the same level or a higher one ends the part)
                } else {
                    folded[i] = 1;
                    if (this._collapsed.has(paragraph)) present.add(paragraph); // (collapsed inside: it stays so)
                    continue;
                }
            }
            if (level > 0 && this._collapsed.has(paragraph)) {
                present.add(paragraph);
                foldLevel = level;
            }
        }
        if (present.size !== this._collapsed.size) this._collapsed = present;
        this._folded = folded;
        this._foldsKey = `${this._version}:${this._collapseStamp}`;
    }

    // The heights follow the folds: a folded paragraph's is 0 (it is measured as that), one that is folded no more is
    // estimated again and measured when it is laid out. Every other paragraph keeps its height.
    _refold() {
        this._ensureFolds();
        const folded = this._folded;
        if (!folded && this._foldedSet.size === 0) return;
        const now = new Set();
        for (let i = 0; i < this.paragraphs.length; i++) {
            const paragraph = this.paragraphs[i];
            if (folded?.[i]) {
                now.add(paragraph);
                if (this.heights[i] !== 0 || !this.measured[i]) {
                    this.heights[i] = 0;
                    this.measured[i] = true;
                    this._topsValid = Math.min(this._topsValid, i);
                }
            } else if (this._foldedSet.has(paragraph)) {
                this.measured[i] = false;
                this.heights[i] = this._estimateHeight(i);
                this._topsValid = Math.min(this._topsValid, i);
            }
        }
        this._foldedSet = now;
    }

    // The first paragraph from `index` on (back, for a negative direction) that is not folded away (-1 or the count if
    // none is).
    _shownFrom(index, direction) {
        this._ensureFolds();
        let i = index;
        while (i >= 0 && i < this.paragraphs.length && this._folded?.[i]) i += direction < 0 ? -1 : 1;
        return i;
    }

    // A position the host puts the caret at is never folded away: the headings that fold it are expanded.
    _revealPosition(position) {
        if (!position || this._collapsed.size === 0) return;
        let guard = this.paragraphs.length;
        while (this.isFolded(position.paragraph) && guard-- > 0) {
            for (let j = position.paragraph - 1; j >= 0; j--) {
                if (this._collapsed.has(this.paragraphs[j]) && !this._folded[j]) {
                    this._collapsed.delete(this.paragraphs[j]);
                    this._collapseStamp++;
                    break;
                }
            }
        }
    }

    // The collapsed headings among the paragraphs an edit is about to replace: where they are among them, their level
    // and their text.
    _collapsedAmong(first, count) {
        const found = [];
        if (this._collapsed.size === 0) return found;
        for (let k = 0; k < count; k++) {
            const paragraph = this.paragraphs[first + k];
            if (this._collapsed.has(paragraph)) {
                found.push({ offset: k, level: this.getOutlineLevel(first + k), text: paragraphText(paragraph) });
            }
        }
        return found;
    }

    // A collapsed heading replaced stays collapsed: the paragraph of the replacement with its level and text nearest
    // where it was, or the one in its place if the replacement is as long and that is a heading (it was edited).
    _handOnCollapsed(collapsed, first, removeCount, inserted) {
        for (const heading of collapsed) {
            let to = null;
            for (let j = 0; j < inserted.length; j++) {
                if (this.getOutlineLevel(first + j) === heading.level && paragraphText(inserted[j]) === heading.text &&
                    (to === null || Math.abs(j - heading.offset) < Math.abs(to - heading.offset))) to = j;
            }
            if (to === null && inserted.length === removeCount && this.getOutlineLevel(first + heading.offset) > 0) {
                to = heading.offset;
            }
            if (to !== null) this._collapsed.add(inserted[to]);
        }
        this._collapseStamp++;
    }

    // ---- Layout ---------------------------------------------------------------------------------------------------

    _invalidateHeights() {
        this._widthCache = 0;
        this.measured = this.paragraphs.map(() => false);
        this.heights = this.paragraphs.map((_, i) => this._estimateHeight(i));
        this._topsValid = 0;
        this._afterHostChange(false);
    }

    _onResize() {
        const width = this.scroller.clientWidth;
        if (width === this._lastWidth && this.scroller.clientHeight === this._lastHeight) return;
        const widthChanged = width !== this._lastWidth;
        this._lastWidth = width;
        this._lastHeight = this.scroller.clientHeight;
        if (widthChanged) {
            this._widthCache = 0;
            this.measured = this.paragraphs.map(() => false);
        }
        this._layout();
        this._drawOverlay();
    }

    // The width the text is laid out to (kept until the size or the styles change: estimates ask for it often).
    get _contentWidth() {
        this._widthCache ||= this.content.clientWidth || Math.max(100, (this.scroller.clientWidth || 600) - 32);
        return this._widthCache;
    }

    _estimateHeight(i) {
        const paragraph = this.paragraphs[i];
        if (paragraph.cell) {
            if (i > 0 && this.paragraphs[i - 1].cell?.table === paragraph.cell.table) return 0;
            let rows = 1;
            for (let j = i + 1; j < this.paragraphs.length && this.paragraphs[j].cell?.table === paragraph.cell.table; j++) {
                if (this.paragraphs[j].cell.row !== this.paragraphs[j - 1].cell.row) rows++;
            }
            const style = this._paragraphStyle(paragraph.styleId);
            return rows * (style.lineHeight + style.vertical) + 24;
        }
        const style = this._paragraphStyle(paragraph.styleId);
        let visible = 0, breaks = 0, images = 0;
        for (const run of paragraph.runs) {
            if (run.image) images++;
            if (run.hidden) continue;
            visible += run.text.length;
            for (const ch of run.text) if (ch === SOFT_BREAK) breaks++;
        }
        if (visible === 0 && paragraphLength(paragraph) > 0 && !images) return style.vertical;
        const width = Math.max(40, this._contentWidth - style.horizontal);
        const perLine = Math.max(1, width / (style.fontSize * 0.52));
        const lines = this._wordWrap ? Math.max(1, Math.ceil(visible / perLine)) + breaks : 1 + breaks;
        return lines * style.lineHeight + style.vertical + images * 120;
    }

    _ensureTops() {
        const n = this.paragraphs.length;
        if (this.tops.length !== n + 1) {
            const tops = new Array(n + 1).fill(0);
            for (let i = 0; i < Math.min(this._topsValid, n); i++) tops[i] = this.tops[i];
            this.tops = tops;
        }
        let i = Math.min(this._topsValid, n);
        if (i === 0) this.tops[0] = 0;
        for (; i < n; i++) this.tops[i + 1] = this.tops[i] + this.heights[i];
        this._topsValid = n;
    }

    get _totalHeight() {
        this._ensureTops();
        return this.tops[this.paragraphs.length];
    }

    // The paragraph at content height `y` (clamped to the document).
    _indexAtY(y) {
        this._ensureTops();
        const n = this.paragraphs.length;
        if (y <= 0) return 0;
        let low = 0, high = n - 1;
        while (low < high) {
            const middle = (low + high + 1) >> 1;
            if (this.tops[middle] <= y) low = middle;
            else high = middle - 1;
        }
        return low;
    }

    _tableStart(i) {
        const table = this.paragraphs[i]?.cell?.table;
        if (!table) return i;
        while (i > 0 && this.paragraphs[i - 1].cell?.table === table) i--;
        return i;
    }

    _tableEnd(i) {
        const table = this.paragraphs[i]?.cell?.table;
        if (!table) return i;
        while (i + 1 < this.paragraphs.length && this.paragraphs[i + 1].cell?.table === table) i++;
        return i;
    }

    // After paragraphs were replaced, a table's estimated height belongs to its first cell.
    _repairTableNeighbours(first, count) {
        const from = Math.max(0, first - 1), to = Math.min(this.paragraphs.length - 1, first + count);
        for (let i = from; i <= to; i++) {
            if (!this.paragraphs[i]?.cell) continue;
            const start = this._tableStart(i);
            for (let j = start; j <= this._tableEnd(i); j++) {
                this.measured[j] = false;
                this.heights[j] = j === start ? this._estimateHeight(j) : 0;
            }
            this._topsValid = Math.min(this._topsValid, start);
            i = this._tableEnd(i);
        }
    }

    _layout() {
        if (this._deferrals > 0) {
            this._layoutPending = true;
            return;
        }
        if (!this.host.isConnected) return;
        this._refold(); // (the outline: what is folded away takes no room)
        if (this._leftGutterContent & GutterContent.ParagraphNumbers && this._digitsOfCount() !== this._gutterDigits) {
            // (one more digit to the numbers: the gutter is wider, the text narrower, every height to measure again)
            this._applyDocumentStyle();
            this._widthCache = 0;
            this.measured = this.paragraphs.map(() => false);
        }
        const scroller = this.scroller;
        for (let pass = 0; pass < 4; pass++) {
            this._ensureTops();
            const viewTop = scroller.scrollTop - this._padTop;
            const viewHeight = scroller.clientHeight || 400;
            const first = this._tableStart(this._indexAtY(viewTop - BUFFER));
            const last = this._tableEnd(this._indexAtY(viewTop + viewHeight + BUFFER));
            const anchor = this._indexAtY(Math.max(0, viewTop));
            const anchorOffset = viewTop - this.tops[anchor];
            this._render(first, last);
            const changed = this._measure(first, last);
            this._ensureTops();
            this.page.style.height = this._padTop + this._totalHeight + this._padBottom + 'px';
            this.content.style.top = this._padTop + this.tops[first] + 'px';
            if (!changed) break;
            if (viewTop > 0) {
                const wanted = this.tops[anchor] + anchorOffset + this._padTop;
                if (Math.abs(wanted - scroller.scrollTop) > 0.5) scroller.scrollTop = wanted;
            }
        }
        this._drawGutter();
    }

    _render(first, last) {
        const children = [];
        const keep = new Set();
        for (let i = first; i <= last; i++) {
            const paragraph = this.paragraphs[i];
            if (this._folded?.[i]) {
                if (paragraph.cell) i = this._tableEnd(i); // (a table is folded whole)
                continue;
            }
            if (paragraph.cell) {
                const end = this._tableEnd(i);
                children.push(this._tableElement(i, end, keep));
                i = end;
                continue;
            }
            children.push(this._paragraphElement(i, keep));
        }
        for (const key of this._elements.keys()) if (!keep.has(key)) this._elements.delete(key);
        const current = this.content.children;
        let same = current.length === children.length;
        for (let k = 0; same && k < children.length; k++) same = current[k] === children[k];
        if (!same) this.content.replaceChildren(...children);
        this._rendered = { first, last };
    }

    _paragraphElement(i, keep) {
        const paragraph = this.paragraphs[i];
        keep.add(paragraph);
        let element = this._elements.get(paragraph);
        if (!element || element._stamp !== paragraph._stamp || element._styleVersion !== this._styleVersion) {
            element = this._buildParagraph(paragraph);
            this._elements.set(paragraph, element);
            this.measured[i] = false;
        }
        element._index = i;
        element._box._index = i;
        return element;
    }

    _buildParagraph(paragraph) {
        const style = this._paragraphStyle(paragraph.styleId);
        const outer = el('div', 'vtv-para');
        if (style.outerCss) outer.style.cssText = style.outerCss;
        const box = el('div', style.className ? 'vtv-box ' + style.className : 'vtv-box');
        if (style.boxCss) box.style.cssText = style.boxCss;
        this._fillBox(box, paragraph, style);
        outer.append(box);
        outer._box = box;
        outer._stamp = paragraph._stamp;
        outer._styleVersion = this._styleVersion;
        return outer;
    }

    _fillBox(box, paragraph, style) {
        const length = paragraphLength(paragraph);
        const hasImage = paragraph.runs.some((run) => run.image);
        const collapsed = length > 0 && !hasImage && paragraph.runs.every((run) => run.hidden || run.text.length === 0);
        if (collapsed) box.classList.add('vtv-collapsed');
        if (style.hidden) box.classList.add('vtv-collapsed');
        box._runs = [];
        box._paragraph = paragraph;
        if (paragraph.listMarker && !collapsed) {
            const marker = el('span', 'vtv-marker');
            marker.textContent = paragraph.listMarker;
            marker.style.right = `calc(100% - ${style.padding[3] + style.border[3]}px + 0.45em)`;
            marker.style.top = style.padding[0] + 'px';
            box.append(marker);
        }
        // (no room, no text: it sits on the first line's baseline, which is where the gutter's number goes)
        box._base = el('span', 'vtv-base');
        box.append(box._base);
        let at = 0;
        for (const run of paragraph.runs) {
            const { css, className } = this._runLook(run);
            const pieces = run.hidden || !run.text.includes(SOFT_BREAK) ? [run.text] : run.text.split(BREAKS);
            let offset = at;
            for (const piece of pieces) {
                if (piece.length === 0) continue;
                const span = el('span', className ? 'vtv-run ' + className : 'vtv-run');
                if (run.hidden) {
                    span.classList.add('vtv-hidden');
                    span.setAttribute('aria-hidden', 'true'); // (a screen reader reads the text as it is shown)
                }
                if (piece === SOFT_BREAK) span.classList.add('vtv-br');
                if (run.link) {
                    span.classList.add('vtv-link');
                    span.title = run.link.title ? `${run.link.title}\n${run.link.target}` : run.link.target;
                }
                if (css) span.style.cssText = css;
                span.textContent = shownText(piece, run.hidden);
                span._start = offset;
                span._length = piece.length;
                span._run = run;
                box.append(span);
                box._runs.push(span);
                offset += piece.length;
            }
            if (run.image) box.append(this._imageElement(run, at + run.text.length));
            at += run.text.length;
        }
        const eol = el('span', 'vtv-eol');
        eol.textContent = ZWSP;
        eol._start = length;
        box.append(eol);
        box._eol = eol;
        box._hung = !(paragraph.markerTextLength > 0);
    }

    _imageElement(run, after) {
        const { src, alt, title } = run.image;
        const image = document.createElement('img');
        image.className = 'vtv-image';
        image.alt = alt ?? '';
        if (title) image.title = title;
        image._after = after;
        image.draggable = false;
        image.addEventListener('load', () => this._remeasureSoon());
        image.addEventListener('error', () => {
            const missing = el('span', 'vtv-image-missing');
            missing.textContent = alt || src || 'picture';
            missing.title = src;
            missing._after = after;
            image.replaceWith(missing);
            this._remeasureSoon();
        });
        image.src = this._resolveImage ? this._resolveImage(src) : src;
        return image;
    }

    /** A function from a picture's address to the URL it is loaded from (relative addresses, say). */
    set imageResolver(fn) {
        this._resolveImage = fn;
        this._elements.clear();
        this._afterHostChange(false);
    }

    _remeasureSoon() {
        if (this._remeasureQueued) return;
        this._remeasureQueued = true;
        requestAnimationFrame(() => {
            this._remeasureQueued = false;
            if (this._rendered) for (let i = this._rendered.first; i <= this._rendered.last; i++) this.measured[i] = false;
            this._layout();
            this._drawOverlay();
        });
    }

    _tableElement(first, last, keep) {
        const table = this.paragraphs[first].cell.table;
        let wrap = this._elements.get(table);
        const cells = this.paragraphs.slice(first, last + 1);
        const stale = !wrap || wrap._styleVersion !== this._styleVersion || wrap._cells.length !== cells.length ||
            wrap._cells.some((cell, k) => cell !== cells[k] || cell._stamp !== wrap._stamps[k]);
        keep.add(table);
        if (stale) {
            wrap = el('div', 'vtv-para vtv-table-wrap');
            const tableStyle = this._styleProvider?.getTableStyle?.(table) ?? {};
            // (a table of the host's stylesheet has its class, its margins in it; the wrapper holds them in)
            const element = el('table', tableStyle.className ? 'vtv-table ' + tableStyle.className : 'vtv-table');
            if (!tableStyle.className) {
                const margin = sides(tableStyle.margin ?? [8, 0, 8, 0]).map((side) => side * this._zoom + 'px');
                wrap.style.cssText = `padding:${margin.join(' ')};`;
            }
            if (tableStyle.css) Object.assign(element.style, tableStyle.css);
            let row = null, rowIndex = -1;
            wrap._boxes = [];
            for (const paragraph of cells) {
                if (paragraph.cell.row !== rowIndex) {
                    row = el('tr', '');
                    element.append(row);
                    rowIndex = paragraph.cell.row;
                }
                const td = el('td', tableStyle.cellClassName ?? '');
                if (tableStyle.cellCss) td.style.cssText = tableStyle.cellCss;
                const style = this._paragraphStyle(paragraph.styleId);
                const box = el('div', style.className ? 'vtv-box ' + style.className : 'vtv-box');
                if (style.boxCss) box.style.cssText = style.boxCss;
                const align = paragraph.cell.align;
                if (align === 'left' || align === 'center' || align === 'right') box.classList.add('vtv-align-' + align);
                this._fillBox(box, paragraph, style);
                td.append(box);
                row.append(td);
                wrap._boxes.push(box);
            }
            wrap.append(element);
            wrap._cells = cells;
            wrap._stamps = cells.map((cell) => cell._stamp);
            wrap._styleVersion = this._styleVersion;
            this._elements.set(table, wrap);
            for (let i = first; i <= last; i++) this.measured[i] = false;
        }
        wrap._index = first;
        wrap._last = last;
        wrap._boxes.forEach((box, k) => (box._index = first + k));
        return wrap;
    }

    _measure(first, last) {
        let changed = false;
        for (const element of this.content.children) {
            const i = element._index;
            if (element._boxes) {
                for (const box of element._boxes) this._hang(box);
            } else {
                this._hang(element._box);
            }
            if (this.measured[i]) continue;
            const height = element.getBoundingClientRect().height;
            const count = element._boxes ? element._last - i + 1 : 1;
            for (let k = 0; k < count; k++) {
                const value = k === 0 ? height : 0;
                if (Math.abs(this.heights[i + k] - value) > 0.5) {
                    this.heights[i + k] = value;
                    this._topsValid = Math.min(this._topsValid, i + k);
                    changed = true;
                }
                this.measured[i + k] = true;
            }
        }
        return changed;
    }

    // The characters of a list item's marker shown as text hang left of the text, where the drawn marker goes.
    _hang(box) {
        if (!box || box._hung) return;
        box._hung = true;
        const paragraph = box._paragraph;
        const count = paragraph.markerTextLength ?? 0;
        if (count <= 0) return;
        const rects = this._rangeRectsInBox(box, 0, count);
        const width = rects.reduce((sum, rect) => sum + (rect.right - rect.left), 0);
        const style = this._paragraphStyle(paragraph.styleId);
        const room = style.margin[3] + style.padding[3] + style.border[3];
        const indent = style.firstLineIndent - Math.min(width, room);
        box.style.textIndent = indent + 'px';
    }

    _boxOf(i) {
        for (const element of this.content.children) {
            if (element._boxes) {
                if (i >= element._index && i <= element._last) return element._boxes[i - element._index];
            } else if (element._index === i) {
                return element._box;
            }
        }
        return null;
    }

    _isRendered(i) {
        return !!this._rendered && i >= this._rendered.first && i <= this._rendered.last;
    }

    // Makes paragraph `i` laid out (scrolling it into view if it is far), so that its geometry can be read.
    _ensureRendered(i) {
        if (this._boxOf(i)) return;
        this._ensureTops();
        const top = this.tops[i] + this._padTop;
        const scroller = this.scroller;
        if (top < scroller.scrollTop - BUFFER || top > scroller.scrollTop + scroller.clientHeight + BUFFER) {
            scroller.scrollTop = Math.max(0, top - scroller.clientHeight / 2);
        }
        this._layout();
    }

    _scrollToPosition(position, mode) {
        this._ensureRendered(position.paragraph);
        let rect = this._caretRect(position);
        const scroller = this.scroller;
        if (!rect) {
            this._ensureTops();
            const top = this.tops[position.paragraph] + this._padTop;
            scroller.scrollTop = Math.max(0, top - scroller.clientHeight / 2);
            this._layout();
            rect = this._caretRect(position);
            if (!rect) return;
        }
        const view = scroller.clientHeight;
        let target = scroller.scrollTop;
        if (mode === 'middle') target = rect.top - view / 2 + rect.height / 2;
        else if (rect.top < scroller.scrollTop) target = rect.top - 4;
        else if (rect.top + rect.height > scroller.scrollTop + view) target = rect.top + rect.height - view + 4;
        if (Math.abs(target - scroller.scrollTop) > 0.5) {
            scroller.scrollTop = Math.max(0, target);
            this._layout();
        }
        const left = rect.left + this.page.offsetLeft;
        if (left < scroller.scrollLeft) scroller.scrollLeft = Math.max(0, left - 20);
        else if (left > scroller.scrollLeft + scroller.clientWidth - 4) scroller.scrollLeft = left - scroller.clientWidth + 20;
    }

    // ---- Geometry (in the page's coordinates) ----------------------------------------------------------------------

    // The span of a box that holds character `index` (start <= index < start + length), or null.
    _spanAt(box, index) {
        const runs = box._runs;
        let low = 0, high = runs.length - 1;
        while (low <= high) {
            const middle = (low + high) >> 1;
            const span = runs[middle];
            if (index < span._start) high = middle - 1;
            else if (index >= span._start + span._length) low = middle + 1;
            else return span;
        }
        return null;
    }

    _pageRect() {
        return this.page.getBoundingClientRect();
    }

    _charRects(span, from, to) {
        const node = span.firstChild;
        if (!node) return [];
        const range = document.createRange();
        range.setStart(node, from - span._start);
        range.setEnd(node, to - span._start);
        return [...range.getClientRects()];
    }

    // The rectangle of visible character `index` of a box, in client coordinates; null if it is hidden or missing.
    _charRect(box, index, last = false) {
        const span = this._spanAt(box, index);
        if (!span || span._run.hidden) return null;
        const rects = this._charRects(span, index, index + 1).filter((rect) => rect.height > 0);
        if (!rects.length) return null;
        return last ? rects[rects.length - 1] : rects[0];
    }

    _isHiddenAt(box, index) {
        const span = this._spanAt(box, index);
        return !span || span._run.hidden;
    }

    // Where the caret at `position` is drawn: { left, top, height } in the page's coordinates; null if not laid out.
    _caretRect(position) {
        if (!position) return null;
        const box = this._boxOf(position.paragraph);
        if (!box) return null;
        const page = this._pageRect();
        const text = paragraphText(this.paragraphs[position.paragraph]);
        const index = Math.min(position.index, text.length);
        const at = (rect, x) => ({ left: x - page.left, top: rect.top - page.top, height: rect.height });
        // the visible characters either side of the caret, past any hidden text
        let before = index - 1;
        while (before >= 0 && this._isHiddenAt(box, before)) before--;
        let after = index;
        while (after < text.length && this._isHiddenAt(box, after)) after++;
        const beforeIsBreak = before >= 0 && text[before] === SOFT_BREAK;
        const afterIsBreak = after < text.length && text[after] === SOFT_BREAK;
        const fromBefore = () => {
            if (before < 0 || beforeIsBreak) return null;
            const rect = this._charRect(box, before, true);
            return rect ? at(rect, rect.right) : null;
        };
        const fromAfter = () => {
            if (after >= text.length || afterIsBreak) return null;
            const rect = this._charRect(box, after);
            return rect ? at(rect, rect.left) : null;
        };
        const result = this.affinity === 'backward' ? fromBefore() ?? fromAfter() : fromAfter() ?? fromBefore();
        if (result) return result;
        if (afterIsBreak && !beforeIsBreak && before < 0) {
            const rect = this._charRect(box, after);
            if (rect) return at(rect, rect.left);
        }
        if (afterIsBreak && beforeIsBreak) {
            const rect = this._charRect(box, after);
            if (rect) return at(rect, rect.left);
        }
        // nothing visible on the caret's side of a break, or an empty or wholly hidden paragraph: the end of the box
        const eol = box._eol.getClientRects();
        const style = this._paragraphStyle(this.paragraphs[position.paragraph].styleId);
        if (eol.length && eol[eol.length - 1].height > 0) {
            const rect = eol[eol.length - 1];
            return at(rect, rect.left);
        }
        const boxRect = box.getBoundingClientRect();
        return { left: boxRect.left - page.left + style.padding[3] + style.border[3], top: boxRect.top - page.top,
            height: Math.max(2, Math.min(style.lineHeight, boxRect.height || style.lineHeight)) };
    }

    // The client rectangles of characters [from, to) of a box that can be seen.
    _rangeRectsInBox(box, from, to) {
        const out = [];
        for (const span of box._runs) {
            const start = span._start, end = start + span._length;
            if (end <= from || start >= to || span._run.hidden) continue;
            for (const rect of this._charRects(span, Math.max(from, start), Math.min(to, end))) {
                if (rect.width > 0 || rect.height > 0) out.push(rect);
            }
        }
        return out;
    }

    // The box under a point (page coordinates): the one containing it, or the nearest.
    _boxAtPoint(x, y) {
        const page = this._pageRect();
        const cx = x + page.left, cy = y + page.top;
        let best = null, bestDistance = Infinity;
        for (const element of this.content.children) {
            const boxes = element._boxes ?? [element._box];
            const outer = element.getBoundingClientRect();
            const vertical = cy < outer.top ? outer.top - cy : cy > outer.bottom ? cy - outer.bottom : 0;
            if (vertical > bestDistance) continue;
            for (const box of boxes) {
                const rect = box.getBoundingClientRect();
                const dy = cy < rect.top ? rect.top - cy : cy > rect.bottom ? cy - rect.bottom : 0;
                const dx = cx < rect.left ? rect.left - cx : cx > rect.right ? cx - rect.right : 0;
                const distance = (element._boxes ? dy : Math.min(dy, vertical)) * 1000 + dx;
                if (distance < bestDistance) {
                    bestDistance = distance;
                    best = box;
                }
            }
        }
        return best;
    }

    // The position under a point (page coordinates): { position, affinity, link }.
    _hit(x, y, box = this._boxAtPoint(x, y)) {
        if (!box) return null;
        const i = box._index;
        const paragraph = this.paragraphs[i];
        const page = this._pageRect();
        const cx = x + page.left, cy = y + page.top;
        // the characters that can be seen, by line
        const chars = [];
        for (const span of box._runs) {
            if (span._run.hidden) continue;
            const node = span.firstChild;
            if (!node) continue;
            const range = document.createRange();
            for (let k = 0; k < span._length; k++) {
                range.setStart(node, k);
                range.setEnd(node, k + 1);
                const rects = range.getClientRects();
                if (!rects.length) continue;
                const rect = rects[rects.length - 1];
                if (rect.height <= 0) continue;
                chars.push({ index: span._start + k, rect, run: span._run, isBreak: span.classList.contains('vtv-br') });
            }
        }
        const images = [...box.querySelectorAll('.vtv-image, .vtv-image-missing')];
        if (chars.length === 0 && images.length === 0) {
            let index = 0;
            const text = paragraphText(paragraph);
            if (text.length && paragraph.runs.every((run) => run.hidden)) index = 0;
            else index = text.length;
            return { position: pos(i, index), affinity: 'forward', link: null };
        }
        for (const image of images) {
            const rect = image.getBoundingClientRect();
            chars.push({ index: image._after, rect: { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom,
                height: rect.height }, run: null, image: true });
        }
        // the line nearest the point
        let line = null, lineDistance = Infinity;
        for (const c of chars) {
            const dy = cy < c.rect.top ? c.rect.top - cy : cy > c.rect.bottom ? cy - c.rect.bottom : 0;
            if (dy < lineDistance) {
                lineDistance = dy;
                line = c;
            }
        }
        const onLine = chars.filter((c) => c.rect.top < line.rect.bottom - 1 && c.rect.bottom > line.rect.top + 1);
        onLine.sort((a, b) => a.rect.left - b.rect.left);
        let best = onLine[0], side = 'before';
        for (const c of onLine) {
            if (cx >= c.rect.left) {
                best = c;
                side = cx > (c.rect.left + c.rect.right) / 2 ? 'after' : 'before';
            }
        }
        if (best.image) {
            const index = side === 'after' ? best.index : best.index - 1;
            return { position: pos(i, Math.max(0, index)), affinity: 'forward', link: null };
        }
        let index = side === 'after' ? best.index + 1 : best.index;
        let affinity = 'forward';
        if (side === 'after') {
            if (best.isBreak) index = best.index; // (the end of a line ended by a break is before the break)
            else if (best === onLine[onLine.length - 1]) affinity = 'backward'; // (the end of a wrapped line)
        }
        const link = best.run?.link && cx >= best.rect.left && cx <= best.rect.right ? best.run.link : null;
        return { position: pos(i, index), affinity, link };
    }

    // ---- Drawing the caret, the selection and the highlights ------------------------------------------------------

    _drawOverlay() {
        if (!this._rendered) return;
        const page = this._pageRect();
        const toPage = (rects, color, pool, className) => {
            let used = 0;
            for (const rect of rects) {
                let div = pool[used];
                if (!div) {
                    div = el('div', className);
                    this.overlay.insertBefore(div, this.caretElement);
                    pool.push(div);
                }
                div.style.cssText = `left:${rect.left - page.left}px;top:${rect.top - page.top}px;` +
                    `width:${Math.max(0, rect.right - rect.left)}px;height:${rect.bottom - rect.top}px;background:${color};`;
                used++;
            }
            for (let k = used; k < pool.length; k++) pool[k].style.cssText = 'display:none';
        };
        toPage(this._rectsOfRanges(this._highlights.map((h) => ({ start: h.start, end: h.end }))),
            this.documentStyle.highlightColor, this._highlightPool, 'vtv-highlight');
        const selection = this.selection;
        toPage(selection ? this._rectsOfRanges([selection], true) : [], this.documentStyle.selectionColor,
            this._selectionPool, 'vtv-selection');
        // (the band behind the caret's line, where the document's style has one: the raw Markdown view's)
        const band = this.documentStyle.currentLineColor;
        const line = band && this.caret ? this._caretRect(this.caret) : null;
        this.currentLine.style.cssText = line
            ? `display:block;top:${line.top}px;height:${line.height}px;background:${band};`
            : '';
        const showCaret = this.caret && this._focused && (!this._readOnly || selection);
        const rect = showCaret ? line ?? this._caretRect(this.caret) : null;
        if (rect && !this._composing) {
            this.caretElement.style.display = '';
            this.caretElement.style.left = rect.left + 'px';
            this.caretElement.style.top = rect.top + 'px';
            this.caretElement.style.height = rect.height + 'px';
            this.caretElement.classList.remove('vtv-blink');
            void this.caretElement.offsetWidth; // (the blink starts again at a move)
            this.caretElement.classList.add('vtv-blink');
            this.input.style.left = rect.left + 'px';
            this.input.style.top = rect.top + 'px';
        } else {
            this.caretElement.style.display = 'none';
        }
    }

    // The client rectangles of ranges of the document, one per line of each paragraph laid out; with `ends`, a little
    // more at the end of each paragraph the range goes past.
    _rectsOfRanges(ranges, ends = false) {
        const out = [];
        if (!this._rendered) return out;
        const { first, last } = this._rendered;
        for (const range of ranges) {
            const from = Math.max(first, range.start.paragraph), to = Math.min(last, range.end.paragraph);
            for (let i = from; i <= to; i++) {
                const box = this._boxOf(i);
                if (!box) continue;
                const length = paragraphLength(this.paragraphs[i]);
                const start = i === range.start.paragraph ? range.start.index : 0;
                const end = i === range.end.paragraph ? range.end.index : length;
                const rects = mergeLines(this._rangeRectsInBox(box, start, end));
                if (ends && i < range.end.paragraph) {
                    const eol = box._eol.getClientRects();
                    const style = this._paragraphStyle(this.paragraphs[i].styleId);
                    if (eol.length) {
                        const rect = eol[eol.length - 1];
                        const lastRect = rects[rects.length - 1];
                        const sameLine = lastRect && Math.abs(lastRect.top - rect.top) < rect.height;
                        const top = sameLine ? lastRect.top : rect.top;
                        const bottom = sameLine ? lastRect.bottom : rect.bottom;
                        rects.push({ left: rect.left, right: rect.left + style.fontSize * 0.4, top, bottom });
                    }
                }
                out.push(...rects);
            }
        }
        return out;
    }

    // ---- Input --------------------------------------------------------------------------------------------------

    _wireInput() {
        const input = this.input;
        const on = (target, type, handler, options) => {
            target.addEventListener(type, handler, options);
            return () => target.removeEventListener(type, handler, options);
        };
        const offs = [
            on(input, 'keydown', (e) => this._onKeyDown(e)),
            on(input, 'beforeinput', (e) => this._onBeforeInput(e)),
            on(input, 'input', () => this._onInput()),
            on(input, 'compositionstart', () => this._onCompositionStart()),
            on(input, 'compositionupdate', (e) => this._onCompositionUpdate(e)),
            on(input, 'compositionend', (e) => this._onCompositionEnd(e)),
            on(input, 'copy', (e) => this._onCopy(e, false)),
            on(input, 'cut', (e) => this._onCopy(e, true)),
            on(input, 'paste', (e) => this._onPaste(e)),
            on(input, 'focus', () => {
                this._focused = true;
                this._drawOverlay();
                this.dispatchEvent(new Event('focus'));
            }),
            on(input, 'blur', () => {
                this._focused = false;
                this._drawOverlay();
                this.dispatchEvent(new Event('blur'));
            }),
            on(this.scroller, 'mousedown', (e) => this._onMouseDown(e)),
            on(this.scroller, 'contextmenu', (e) => this._onContextMenu(e)),
            on(this.scroller, 'scroll', () => this._onScroll(), { passive: true }),
            on(this.host, 'mousedown', (e) => {
                if (e.target === this.host) {
                    e.preventDefault();
                    this.focus();
                }
            }),
            ...refuseDrops().map(([type, handler]) => on(this.host, type, handler)),
        ];
        this._unwire = () => offs.forEach((off) => off());
    }

    _onScroll() {
        if (this._scrollQueued) return;
        this._scrollQueued = true;
        requestAnimationFrame(() => {
            this._scrollQueued = false;
            this._layout();
            this._drawOverlay();
        });
    }

    // What the hidden input holds: with a selection, something selected, so that the browser offers Copy and Cut; in
    // screen reader mode, the text round the caret (`_mirror`).
    _syncInput() {
        if (this._composing) return;
        if (this._screenReader && this.caret) return this._mirror();
        const value = this.selection ? '\u00A0' : '';
        if (this.input.value !== value) this.input.value = value;
        if (value) this.input.select();
    }

    /**
     * Screen reader mode: the text area that has the focus holds the text round the caret as it is shown -- the
     * paragraphs from MIRROR_AROUND before the caret's (or the selection's first) to as many after its last, each a line
     * of it, the syntax that is hidden left out, a line break inside a paragraph a line break -- with its own caret and
     * selection where the view's are, kept so at every move and edit (`_syncInput`): a screen reader reads the text area,
     * so it reads what the caret goes over, and the lines round it. Off by default; a host turns it on where a screen
     * reader may be (MarkdownEditor's `screenReader`). A phone's keyboard reads that text too, and may correct a word of
     * it, which the view does not follow (it types at its own caret): hence not on everywhere.
     */
    get screenReader() {
        return this._screenReader;
    }

    set screenReader(on) {
        this._screenReader = !!on;
        if (!this._composing) this.input.value = '';
        this._syncInput();
    }

    /** Says `text` to a screen reader (a polite live region of the view's), what a change of a mode is, say. */
    announce(text) {
        this.status.textContent = ''; // (emptied, then filled a moment later: the same words said again are said)
        clearTimeout(this._announceTimer);
        this._announceTimer = setTimeout(() => (this.status.textContent = text), 50);
    }

    _mirror() {
        const input = this.input;
        const caret = this.caret, anchor = this.anchor ?? caret;
        let first = Math.min(caret.paragraph, anchor.paragraph), last = Math.max(caret.paragraph, anchor.paragraph);
        const whole = last - first <= MIRROR_LIMIT; // (a selection too long to hold: the caret's paragraph, all selected)
        if (!whole) first = last = caret.paragraph;
        first = Math.max(0, first - MIRROR_AROUND);
        last = Math.min(this.paragraphs.length - 1, last + MIRROR_AROUND);
        let text = '';
        const starts = [];
        for (let p = first; p <= last; p++) {
            if (p > first) text += '\n';
            starts.push(text.length);
            for (const run of this.paragraphs[p].runs) if (!run.hidden) text += run.text.replaceAll(SOFT_BREAK, '\n');
        }
        // (a position of the view as an offset in that text: in hidden syntax, where it would be)
        const offsetOf = (position) => {
            const p = Math.max(first, Math.min(last, position.paragraph));
            let at = 0, offset = starts[p - first];
            for (const run of this.paragraphs[p].runs) {
                const length = run.text.length;
                if (position.index <= at + length) return offset + (run.hidden ? 0 : position.index - at);
                at += length;
                if (!run.hidden) offset += length;
            }
            return offset;
        };
        if (input.value !== text) input.value = text;
        let a = offsetOf(anchor), c = offsetOf(caret);
        if (!whole) [a, c] = [starts[caret.paragraph - first], offsetOf(pos(caret.paragraph, Infinity))];
        input.setSelectionRange(Math.min(a, c), Math.max(a, c), c < a ? 'backward' : 'forward');
    }

    _onKeyDown(e) {
        if (this._composing || e.isComposing || e.keyCode === 229) return;
        // (Escape with nothing selected, then Tab or Shift+Tab: the focus leaves the view, as the browser moves it, where
        // Tab is else the view's)
        const escaped = this._escaped;
        this._escaped = false;
        const chord = keyChord(e);
        const keyEvent = new CustomEvent('key', { cancelable: true, detail: { chord, event: e } });
        this.dispatchEvent(keyEvent);
        const bound = this.keymap.get(chord);
        if (keyEvent.defaultPrevented || (bound && bound(e) !== false)) {
            e.preventDefault();
            return;
        }
        const shift = e.shiftKey;
        const word = e.ctrlKey || e.altKey;
        // (a Mac's Cmd with the arrows: the ends of the line and of the document; with Backspace and Delete, what is
        // before the caret on its line, or after it)
        const mac = this.mac && e.metaKey && !e.ctrlKey;
        const handled = () => e.preventDefault();
        switch (e.key) {
            case 'ArrowLeft':
                handled();
                if (mac) return this._move((p) => this._lineEdge(p, -1), shift, 0);
                return this._move((p) => (word ? this._wordMove(p, -1) : this._charMove(p, -1)), shift, -1);
            case 'ArrowRight':
                handled();
                if (mac) return this._move((p) => this._lineEdge(p, 1), shift, 0, 'backward');
                return this._move((p) => (word ? this._wordMove(p, 1) : this._charMove(p, 1)), shift, 1);
            case 'ArrowUp':
                handled();
                if (mac) return this._move(() => pos(0, 0), shift, 0);
                return this._vertical(-1, shift);
            case 'ArrowDown':
                handled();
                if (mac) return this._move(() => this._documentEnd(), shift, 0);
                return this._vertical(1, shift);
            case 'Home':
                handled();
                return this._move((p) => (e.ctrlKey || e.metaKey ? pos(0, 0) : this._lineEdge(p, -1)), shift, 0);
            case 'End':
                handled();
                return this._move((p) => (e.ctrlKey || e.metaKey ? this._documentEnd() : this._lineEdge(p, 1)), shift, 0,
                    e.ctrlKey || e.metaKey ? 'forward' : 'backward');
            case 'PageUp':
                handled();
                return this._page(-1, shift);
            case 'PageDown':
                handled();
                return this._page(1, shift);
            case 'Enter':
                handled();
                return this._command(shift ? 'softBreak' : 'enter');
            case 'Backspace':
                handled();
                if (mac) return this._deleteToLineEdge(-1);
                return this._command(e.ctrlKey || e.altKey ? 'deleteWordBack' : 'backspace');
            case 'Delete':
                handled();
                if (mac) return this._deleteToLineEdge(1);
                if (shift && !e.ctrlKey && this.selection) return this.cut();
                return this._command(e.ctrlKey || e.altKey ? 'deleteWordForward' : 'delete');
            case 'Tab':
                if (e.ctrlKey || e.altKey || e.metaKey || escaped) return;
                if (this._tabInTable(shift)) return handled();
                if (!this.acceptsTab) return;
                handled();
                return this._command(shift ? 'shiftTab' : 'tab');
            case 'Escape':
                if (this.selection) {
                    handled();
                    this.setCaret(this.caret);
                } else {
                    this._escaped = true;
                }
                return;
            default:
                break;
        }
        if (chord === 'Ctrl+A') {
            handled();
            this.selectAll();
        } else if (chord === 'Ctrl+Shift+V') {
            handled();
            this.paste();
        }
    }

    _onBeforeInput(e) {
        if (e.isComposing || e.inputType === 'insertCompositionText') return;
        const type = e.inputType;
        const commands = {
            insertLineBreak: 'softBreak', insertParagraph: 'enter', deleteContentBackward: 'backspace',
            deleteContentForward: 'delete', deleteWordBackward: 'deleteWordBack', deleteWordForward: 'deleteWordForward',
            deleteContent: 'delete', // (the Delete of the menu a right-click opens)
        };
        if (type === 'insertText' || type === 'insertReplacementText') {
            e.preventDefault();
            const text = e.data ?? e.dataTransfer?.getData('text/plain') ?? '';
            if (text) this._command('insertText', text.replace(/\r\n?/g, '\n'));
        } else if (commands[type]) {
            e.preventDefault();
            this._command(commands[type]);
        } else if (type === 'historyUndo' || type === 'historyRedo') {
            e.preventDefault();
            this.dispatchEvent(new Event(type === 'historyUndo' ? 'undo' : 'redo'));
        } else if (type === 'insertFromPaste' || type === 'deleteByCut') {
            // (handled by the clipboard events)
        } else if (type === 'insertFromDrop') {
            e.preventDefault(); // (nothing is dropped into the text yet: `refuseDrops`)
        } else {
            e.preventDefault();
        }
    }

    _onInput() {
        if (!this._composing) this._syncInput();
    }

    _onCompositionStart() {
        if (this._readOnly) return;
        this._composing = true;
        const rect = this._caretRect(this.caret);
        if (rect) {
            const style = this.caret ? this._paragraphStyle(this.paragraphs[this.caret.paragraph].styleId) : null;
            this.composition.style.cssText = `display:block;left:${rect.left}px;top:${rect.top}px;` +
                (style ? style.fontCss : '');
            this.composition.textContent = '';
        }
        this.caretElement.style.display = 'none';
    }

    _onCompositionUpdate(e) {
        this.composition.textContent = e.data ?? '';
    }

    _onCompositionEnd(e) {
        this._composing = false;
        this.composition.style.display = 'none';
        this.composition.textContent = '';
        this.input.value = '';
        if (e.data) this._command('insertText', e.data);
        else this._drawOverlay();
        this._syncInput(); // (what the text area holds again: in screen reader mode, the text round the caret)
    }

    _copyText() {
        const selection = this.selection;
        if (!selection) return null;
        const event = new CustomEvent('editcommand', {
            cancelable: true,
            detail: { command: 'copy', text: null, start: selection.start, end: selection.end, hasSelection: true, result: null },
        });
        this.dispatchEvent(event);
        if (event.defaultPrevented && typeof event.detail.result === 'string') return event.detail.result;
        return this.selectedText.replaceAll(SOFT_BREAK, '\n');
    }

    // The text onto the clipboard (the view's `clipboard`, or the browser's asynchronous one): true once it is there.
    // A clipboard there is none of, or that refuses (a page not allowed to write it), raises 'clipboardunavailable'.
    async _writeClipboard(text) {
        try {
            const clipboard = this.clipboard ?? navigator.clipboard;
            if (!clipboard?.writeText) throw new Error('No clipboard to write.');
            await clipboard.writeText(text);
            return true;
        } catch {
            this.dispatchEvent(new Event('clipboardunavailable'));
            return false;
        }
    }

    _onCopy(e, cut) {
        e.preventDefault();
        if (cut && this._readOnly) return;
        const text = this._copyText();
        if (text === null) return;
        e.clipboardData?.setData('text/plain', text);
        if (this.clipboard) this._writeClipboard(text);
        if (cut) this._command('cut', text, { written: true });
    }

    _onPaste(e) {
        e.preventDefault();
        if (this._readOnly || !this.caret) return;
        const text = e.clipboardData?.getData('text/plain') ?? null;
        this._command('paste', text);
    }

    // ---- Mouse ----------------------------------------------------------------------------------------------------

    // Whether a point (client coordinates) is on the scroller's scroll bars.
    _onScrollBar(e) {
        const rect = this.scroller.getBoundingClientRect();
        return e.clientX - rect.left >= this.scroller.clientWidth || e.clientY - rect.top >= this.scroller.clientHeight;
    }

    _onMouseDown(e) {
        this._escaped = false;
        if (e.button === 2 && !this._onScrollBar(e)) {
            e.preventDefault(); // (the focus stays in the text area: the menu that follows is its, `_onContextMenu`)
            this.focus();
            return;
        }
        if (e.button !== 0) return;
        if (this._onScrollBar(e)) return;
        e.preventDefault();
        this.focus();
        const page = this._pageRect();
        const hit = this._hit(e.clientX - page.left, e.clientY - page.top);
        if (!hit) return;
        if (hit.link && (e.ctrlKey || e.metaKey)) {
            const detail = { link: hit.link, position: hit.position, event: e };
            this.dispatchEvent(new CustomEvent('hyperlinkclick', { detail }));
            return;
        }
        const clicks = e.detail;
        const runClick = new CustomEvent('runclick', { cancelable: true, detail: { position: hit.position, event: e } });
        this.dispatchEvent(runClick);
        if (runClick.defaultPrevented) return;
        let anchorRange = null; // (a word or a paragraph selected by a double or triple click: the drag extends it)
        if (clicks === 2 || clicks >= 3) {
            const p = hit.position.paragraph;
            const text = paragraphText(this.paragraphs[p]);
            const [start, end] = clicks === 2 ? wordAt(text, hit.position.index) : [0, text.length];
            anchorRange = { start: pos(p, start), end: pos(p, end) };
            this.affinity = 'forward';
            this.select(anchorRange.start, anchorRange.end, { scroll: false });
        } else if (e.shiftKey && this.caret) {
            this.affinity = hit.affinity;
            this.select(this.anchor ?? this.caret, hit.position, { scroll: false });
        } else {
            this.caret = this.anchor = this._clamp(hit.position);
            this.affinity = hit.affinity;
            this._caretMoved(false);
        }
        let lastY = e.clientY, lastX = e.clientX;
        const extendTo = (clientX, clientY) => {
            const pageNow = this._pageRect();
            const point = this._hit(clientX - pageNow.left, clientY - pageNow.top);
            if (!point) return;
            this.affinity = point.affinity;
            if (anchorRange) {
                // (a word or a paragraph selected by the clicks stays selected whole, whichever way the drag goes)
                const before = comparePositions(point.position, anchorRange.start) < 0;
                const beyond = comparePositions(point.position, anchorRange.end) > 0;
                if (before) this.select(anchorRange.end, point.position, { scroll: false });
                else this.select(anchorRange.start, beyond ? point.position : anchorRange.end, { scroll: false });
            } else {
                this.select(this.anchor, point.position, { scroll: false });
            }
        };
        const move = (event) => {
            lastX = event.clientX;
            lastY = event.clientY;
            extendTo(lastX, lastY);
        };
        const timer = setInterval(() => {
            const rect = this.scroller.getBoundingClientRect();
            const step = lastY < rect.top ? lastY - rect.top : lastY > rect.bottom ? lastY - rect.bottom : 0;
            if (step) {
                this.scroller.scrollTop += step;
                this._layout();
                extendTo(lastX, Math.min(Math.max(lastY, rect.top + 2), rect.bottom - 2));
            }
        }, 50);
        const up = () => {
            clearInterval(timer);
            window.removeEventListener('mousemove', move);
            window.removeEventListener('mouseup', up);
            this.preferredX = this._caretRect(this.caret)?.left ?? null;
        };
        window.addEventListener('mousemove', move);
        window.addEventListener('mouseup', up);
    }

    // A right-click: the browser's menu is made the text area's -- its Cut, Copy and Paste the view's, through the
    // clipboard events -- by putting the text area under the pointer for a moment, holding the selection as `_syncInput`
    // keeps it (a selected character while something is selected, so that Cut and Copy are offered). A click outside the
    // selection first puts the caret there, as a click does; one inside it keeps it. The menu works on the text area
    // that has the focus wherever it is, so it goes back to the caret soon after (`_restoreInput`).
    _onContextMenu(e) {
        if (this._onScrollBar(e)) return;
        const page = this._pageRect();
        const hit = this._hit(e.clientX - page.left, e.clientY - page.top);
        const selection = this.selection;
        const inside = selection && hit && comparePositions(selection.start, hit.position) <= 0 &&
            comparePositions(hit.position, selection.end) <= 0;
        if (hit && !inside) {
            this.caret = this.anchor = this._clamp(hit.position);
            this.affinity = hit.affinity;
            this._caretMoved(false);
        }
        this.focus();
        this._syncInput();
        const input = this.input.style;
        input.left = `${e.clientX - page.left - 8}px`;
        input.top = `${e.clientY - page.top - 8}px`;
        input.width = input.height = '16px';
        input.pointerEvents = 'auto';
        input.zIndex = '10';
        clearTimeout(this._menuTimer);
        this._menuTimer = setTimeout(() => this._restoreInput(), 100);
    }

    _restoreInput() {
        const input = this.input.style;
        input.width = input.height = input.pointerEvents = input.zIndex = '';
        this._drawOverlay();
    }

    // A Mac's Cmd+Backspace (`direction` -1) and Cmd+Delete (1): what is between the caret and the start or the end of its
    // line (a wrapped line's own) is selected and deleted, one command as a selection's Backspace or Delete is; at the
    // line's edge already, or with a selection, a Backspace or a Delete.
    _deleteToLineEdge(direction) {
        if (!this.caret) return;
        const command = direction < 0 ? 'backspace' : 'delete';
        if (this.selection) return this._command(command);
        const edge = this._clamp(this._lineEdge(this.caret, direction));
        if (!samePosition(edge, this.caret)) this.select(this.caret, edge, { scroll: false });
        this._command(command);
    }

    // ---- Caret moves --------------------------------------------------------------------------------------------

    _move(target, extend, direction, affinity = 'forward') {
        if (!this.caret) return;
        const selection = this.selection;
        let next;
        if (!extend && selection && direction !== 0) next = direction < 0 ? selection.start : selection.end;
        else next = this._clamp(target(this.caret));
        this.affinity = affinity;
        this.caret = next;
        if (!extend) this.anchor = next;
        this._caretMoved(true);
    }

    _charMove(p, direction) {
        const text = paragraphText(this.paragraphs[p.paragraph]);
        if (direction < 0) {
            if (p.index > 0) return pos(p.paragraph, previousCharacterBoundary(text, p.index));
            return this._acrossParagraphs(p, -1);
        }
        if (p.index < text.length) return pos(p.paragraph, nextCharacterBoundary(text, p.index));
        return this._acrossParagraphs(p, 1);
    }

    _wordMove(p, direction) {
        const text = paragraphText(this.paragraphs[p.paragraph]);
        if (direction > 0) {
            if (p.index >= text.length) return this._acrossParagraphs(p, 1);
            return pos(p.paragraph, nextWordBoundary(text, p.index));
        }
        if (p.index <= 0) return this._acrossParagraphs(p, -1);
        return pos(p.paragraph, previousWordBoundary(text, p.index));
    }

    // From a paragraph's edge to the next one's start, or to the previous one's end: over what is folded away.
    _acrossParagraphs(p, direction) {
        const next = this._shownFrom(p.paragraph + direction, direction);
        if (next < 0 || next >= this.paragraphs.length) return p;
        return direction < 0 ? pos(next, paragraphLength(this.paragraphs[next])) : pos(next, 0);
    }

    _documentEnd() {
        const last = Math.max(0, this._shownFrom(this.paragraphs.length - 1, -1));
        return pos(last, paragraphLength(this.paragraphs[last]));
    }

    // Home and End: the start or the end of the line the caret is on (a wrapped line's own, not the paragraph's).
    _lineEdge(p, direction) {
        this._ensureRendered(p.paragraph);
        const rect = this._caretRect(p);
        const box = this._boxOf(p.paragraph);
        if (!rect || !box) return pos(p.paragraph, direction < 0 ? 0 : paragraphLength(this.paragraphs[p.paragraph]));
        const y = rect.top + rect.height / 2;
        const hit = this._hit(direction < 0 ? -1e6 : 1e6, y, box);
        return hit ? hit.position : p;
    }

    _vertical(direction, extend) {
        if (!this.caret) return;
        const selection = this.selection;
        const from = !extend && selection ? (direction < 0 ? selection.start : selection.end) : this.caret;
        this._ensureRendered(from.paragraph);
        const rect = this._caretRect(from);
        const box = this._boxOf(from.paragraph);
        if (!rect || !box) return;
        const x = this.preferredX ?? rect.left;
        const page = this._pageRect();
        const boxRect = box.getBoundingClientRect();
        const lineTop = rect.top, lineBottom = rect.top + rect.height;
        let target = null;
        // another line of the same paragraph?
        const y = direction < 0 ? lineTop - 3 : lineBottom + 3;
        if (y > boxRect.top - page.top + 1 && y < boxRect.bottom - page.top - 1) {
            const hit = this._hit(x, y, box);
            if (hit && hit.position.paragraph === from.paragraph && !samePosition(hit.position, from)) {
                const hitRect = this._caretRect(hit.position);
                if (!hitRect || Math.abs(hitRect.top - rect.top) > 2) target = hit;
            }
        }
        if (!target) {
            let next = this._shownFrom(from.paragraph + direction, direction); // (over what is folded away)
            const cell = this.paragraphs[from.paragraph].cell;
            if (cell) {
                // (in a table: the cell above or below, in the same column; out of the table at its edge)
                const columns = cell.table.columns;
                const sameColumn = from.paragraph + direction * columns;
                next = this.paragraphs[sameColumn]?.cell?.table === cell.table ? sameColumn
                    : direction < 0 ? this._tableStart(from.paragraph) - 1 : this._tableEnd(from.paragraph) + 1;
            } else if (this.paragraphs[next]?.cell) {
                // (into a table from below: its last row's first cell; from above: its first cell)
                const end = this._tableEnd(next);
                next = direction < 0 ? end - this.paragraphs[end].cell.column : this._tableStart(next);
            }
            if (next < 0) {
                target = { position: pos(0, 0), affinity: 'forward' };
            } else if (next >= this.paragraphs.length) {
                target = { position: this._documentEnd(), affinity: 'forward' };
            } else {
                this._ensureRendered(next);
                const nextCell = this.paragraphs[next].cell;
                if (nextCell && !cell) {
                    // (into a table: the cell of its row that is under the caret)
                    const rowStart = next - nextCell.column;
                    for (let k = 0; k < nextCell.table.columns; k++) {
                        const candidate = this._boxOf(rowStart + k)?.getBoundingClientRect();
                        if (candidate && x + page.left >= candidate.left && x + page.left <= candidate.right) next = rowStart + k;
                    }
                }
                const nextBox = this._boxOf(next);
                if (nextBox) {
                    const nextRect = nextBox.getBoundingClientRect();
                    const ny = direction < 0 ? nextRect.bottom - page.top - 2 : nextRect.top - page.top + 2;
                    target = this._hit(x, ny, nextBox);
                }
            }
        }
        if (!target) return;
        const keepX = this.preferredX ?? rect.left;
        this.affinity = target.affinity;
        this.caret = target.position;
        if (!extend) this.anchor = target.position;
        this._typingFormat = null;
        this._scrollToPosition(this.caret, 'nearest');
        this._drawOverlay();
        this.preferredX = keepX;
        this._selectionChanged();
    }

    _page(direction, extend) {
        if (!this.caret) return;
        const rect = this._caretRect(this.caret);
        const scroller = this.scroller;
        const before = scroller.scrollTop;
        scroller.scrollTop += direction * scroller.clientHeight * 0.9;
        this._layout();
        if (Math.abs(scroller.scrollTop - before) < 1) {
            this._move(() => (direction < 0 ? pos(0, 0) : this._documentEnd()), extend, 0);
            return;
        }
        const y = (rect ? rect.top + rect.height / 2 : before) + (scroller.scrollTop - before);
        const hit = this._hit(this.preferredX ?? rect?.left ?? 0, y);
        if (!hit) return;
        const keepX = this.preferredX;
        this.caret = hit.position;
        if (!extend) this.anchor = hit.position;
        this._drawOverlay();
        this.preferredX = keepX;
        this._selectionChanged();
    }

    // Tab in a table moves between cells; in its last cell it is an edit the host may take (a row added).
    _tabInTable(back) {
        if (!this.caret) return false;
        const cell = this.paragraphs[this.caret.paragraph].cell;
        if (!cell) return false;
        const next = this.caret.paragraph + (back ? -1 : 1);
        if (this.paragraphs[next]?.cell?.table === cell.table) {
            this.select(pos(next, 0), pos(next, paragraphLength(this.paragraphs[next])));
            return true;
        }
        if (!back) this._command('tab');
        return true;
    }

    // ---- Edit commands --------------------------------------------------------------------------------------------

    // Raises 'editcommand' (cancelable): a host that does the command itself calls preventDefault(); else the view does it.
    _command(command, text = null, extra = {}) {
        if (!this.caret) return;
        if (this._readOnly) return;
        const selection = this.selection ?? { start: this.caret, end: this.caret };
        const event = new CustomEvent('editcommand', {
            cancelable: true,
            detail: { command, text, start: selection.start, end: selection.end, hasSelection: !!this.selection, ...extra },
        });
        this.dispatchEvent(event);
        if (event.defaultPrevented) return;
        switch (command) {
            case 'insertText':
                return this._insert(text, 'typing');
            case 'paste':
                if (text) this._insert(text.replace(/\r\n?/g, '\n'), 'paste');
                return;
            case 'enter':
                return this._enter();
            case 'softBreak':
                return this._insert(SOFT_BREAK, 'typing');
            case 'backspace':
            case 'deleteWordBack':
                return this._backspace(command === 'deleteWordBack');
            case 'delete':
            case 'deleteWordForward':
                return this._delete(command === 'deleteWordForward');
            case 'tab':
                if (!this.paragraphs[this.caret.paragraph].cell) this._insert('\t', 'typing');
                return;
            case 'shiftTab': {
                const { paragraph, index } = this.caret;
                if (!this.selection && index > 0 && paragraphText(this.paragraphs[paragraph])[index - 1] === '\t') {
                    this._edit(pos(paragraph, index - 1), this.caret, '', null, 'delete');
                }
                return;
            }
            case 'cut':
                return this.deleteSelection();
            default:
        }
    }

    _insert(text, kind) {
        const selection = this.selection ?? { start: this.caret, end: this.caret };
        const paragraph = this.paragraphs[selection.start.paragraph];
        let format = formatAt(paragraph, selection.start.index);
        if (this._typingFormat) format = this._typingFormat({ ...format, text: '' });
        const keep = this._typingFormat;
        this._edit(selection.start, selection.end, text, format, kind);
        this._typingFormat = keep;
    }

    _enter() {
        const selection = this.selection ?? { start: this.caret, end: this.caret };
        const paragraph = this.paragraphs[selection.start.paragraph];
        const atEnd = selection.end.index === paragraphLength(this.paragraphs[selection.end.paragraph]);
        const style = this._styleProvider?.getParagraphStyle?.(paragraph.styleId ?? null);
        const next = atEnd && style && style.nextStyleId !== undefined ? style.nextStyleId : undefined;
        this._edit(selection.start, selection.end, '\n', formatAt(paragraph, selection.start.index), 'enter', next);
    }

    _backspace(word) {
        const selection = this.selection;
        if (selection) return this._edit(selection.start, selection.end, '', null, 'delete');
        const { paragraph, index } = this.caret;
        const text = paragraphText(this.paragraphs[paragraph]);
        if (index === 0) {
            if (paragraph === 0) return;
            if (this.paragraphs[paragraph].cell || this.paragraphs[paragraph - 1].cell) return;
            const end = pos(paragraph - 1, paragraphLength(this.paragraphs[paragraph - 1]));
            return this._edit(end, this.caret, '', null, 'delete');
        }
        const start = word ? previousWordBoundary(text, index) : backspaceStart(text, index);
        this._edit(pos(paragraph, start), this.caret, '', null, 'delete');
    }

    _delete(word) {
        const selection = this.selection;
        if (selection) return this._edit(selection.start, selection.end, '', null, 'delete');
        const { paragraph, index } = this.caret;
        const text = paragraphText(this.paragraphs[paragraph]);
        if (index >= text.length) {
            if (paragraph + 1 >= this.paragraphs.length) return;
            if (this.paragraphs[paragraph].cell || this.paragraphs[paragraph + 1].cell) return;
            return this._edit(this.caret, pos(paragraph + 1, 0), '', null, 'delete');
        }
        const end = word ? nextWordBoundary(text, index) : deleteEnd(text, index);
        this._edit(this.caret, pos(paragraph, end), '', null, 'delete');
    }

    // The view's own edit: [start, end) replaced with `text` ("\n" between paragraphs), in `format`; one TextEdit.
    _edit(start, end, text, format, kind, nextStyleId) {
        const first = start.paragraph, last = end.paragraph;
        // (no edit reaches across a table's cells, nor makes a cell two)
        if (this.paragraphs[first].cell && (first !== last || text.includes('\n'))) return;
        const caretBefore = this.caret, anchorBefore = this.anchor;
        const before = this.paragraphs.slice(first, last + 1).map(cloneParagraph);
        const removedText = textBetween(this.paragraphs, start, end);
        const head = splitRuns(this.paragraphs[first].runs, start.index)[0];
        const tail = splitRuns(this.paragraphs[last].runs, end.index)[1];
        const lines = text.split('\n');
        const base = this.paragraphs[first];
        const run = (line) => ({ ...(format ?? formatAt(base, start.index)), text: line });
        const after = [];
        if (lines.length === 1) {
            after.push({ ...base, runs: normalizeRuns([...head, run(lines[0]), ...tail]), _stamp: undefined });
        } else {
            after.push({ ...base, runs: normalizeRuns([...head, run(lines[0])]), _stamp: undefined });
            for (let k = 1; k < lines.length - 1; k++) {
                after.push({ ...base, listMarker: null, runs: normalizeRuns([run(lines[k])]), _stamp: undefined });
            }
            const lastStyle = nextStyleId !== undefined ? nextStyleId : this.paragraphs[last].styleId;
            after.push({ ...this.paragraphs[last], styleId: lastStyle, listMarker: null,
                runs: normalizeRuns([run(lines[lines.length - 1]), ...tail]), _stamp: undefined });
        }
        for (const paragraph of after) delete paragraph._stamp;
        for (const paragraph of this.paragraphs.slice(first, last + 1)) this._elements.delete(paragraph);
        this.paragraphs.splice(first, last - first + 1, ...after);
        this.heights.splice(first, last - first + 1, ...after.map(() => 0));
        this.measured.splice(first, last - first + 1, ...after.map(() => false));
        for (let i = first; i < first + after.length; i++) this.heights[i] = this._estimateHeight(i);
        this._topsValid = Math.min(this._topsValid, first);
        const caretAfter = lines.length === 1
            ? pos(first, start.index + lines[0].length)
            : pos(first + lines.length - 1, lines[lines.length - 1].length);
        this.caret = this.anchor = caretAfter;
        this.affinity = 'forward';
        this.editRecorder?.recordEdit(new TextEdit({
            first, before, after: after.map(cloneParagraph), caretBefore, caretAfter,
            anchorBefore: anchorBefore && !samePosition(anchorBefore, caretBefore) ? anchorBefore : null, anchorAfter: null,
            kind, removedText, insertedText: text,
        }));
        this._changed();
        this._afterHostChange(true);
        this.preferredX = this._caretRect(this.caret)?.left ?? null;
        this._selectionChanged();
    }

    // Formatting the view does itself: each run from `start` to `end` made what `map` makes of it; one TextEdit.
    _mapRuns(start, end, map) {
        const [a, b] = [this._clamp(start), this._clamp(end)].sort(comparePositions);
        if (samePosition(a, b)) return;
        this._recordedChange(a.paragraph, b.paragraph, (paragraphs) => paragraphs.map((paragraph, k) => {
            const p = a.paragraph + k;
            const from = p === a.paragraph ? a.index : 0;
            const to = p === b.paragraph ? b.index : paragraphLength(paragraph);
            const [left, rest] = splitRuns(paragraph.runs, from);
            const [middle, right] = splitRuns(rest, to - from);
            return { ...paragraph, runs: normalizeRuns([...left, ...middle.map(map), ...right]) };
        }), 'format');
    }

    _recordedChange(first, last, change, kind) {
        const before = this.paragraphs.slice(first, last + 1).map(cloneParagraph);
        const after = change(this.paragraphs.slice(first, last + 1)).map((paragraph) => {
            const copy = { ...paragraph };
            delete copy._stamp;
            return copy;
        });
        for (const paragraph of this.paragraphs.slice(first, last + 1)) this._elements.delete(paragraph);
        this.paragraphs.splice(first, last - first + 1, ...after);
        for (let i = first; i <= last; i++) {
            this.measured[i] = false;
            this.heights[i] = this._estimateHeight(i);
        }
        this._topsValid = Math.min(this._topsValid, first);
        this.editRecorder?.recordEdit(new TextEdit({
            first, before, after: after.map(cloneParagraph), caretBefore: this.caret ?? pos(first, 0),
            caretAfter: this.caret ?? pos(first, 0), anchorBefore: this.anchor, anchorAfter: this.anchor, kind,
        }));
        this._changed();
        this._afterHostChange(false);
    }
}

/**
 * The listeners ([type, handler]) that keep a drop on `element` from doing what the browser would do with it -- open a
 * dropped file in place of the page, and the work in it lost -- as long as the view takes no drops itself: a drag over
 * it is shown as one that cannot be dropped. A page that takes drops on it sets `dataTransfer.dropEffect` in its own
 * `dragover` listener (on the element, added after the view, or on an element round it), and is then given the drop.
 */
export function refuseDrops() {
    return [
        ['dragover', (event) => {
            event.preventDefault();
            if (event.dataTransfer) event.dataTransfer.dropEffect = 'none';
        }],
        ['drop', (event) => event.preventDefault()],
    ];
}

function el(tag, className) {
    const element = document.createElement(tag);
    if (className) element.className = className;
    return element;
}

function sameRuns(a, b) {
    if (a.length !== b.length) return false;
    for (let k = 0; k < a.length; k++) {
        const x = a[k], y = b[k];
        if (x.text !== y.text || !!x.bold !== !!y.bold || !!x.italic !== !!y.italic || !!x.hidden !== !!y.hidden ||
            (x.styleId ?? null) !== (y.styleId ?? null) || (x.link?.target ?? null) !== (y.link?.target ?? null) ||
            (x.image?.src ?? null) !== (y.image?.src ?? null)) return false;
    }
    return true;
}

function sameLinkOf(a, b) {
    return !!a.link && !!b.link && a.link.target === b.link.target && (a.link.title ?? null) === (b.link.title ?? null);
}

// Rectangles on one line made one (a selection drawn over several runs is one band, not overlapping ones).
function mergeLines(rects) {
    const lines = [];
    for (const rect of rects) {
        const line = lines.find((l) => Math.min(l.bottom, rect.bottom) - Math.max(l.top, rect.top) >
            0.5 * Math.min(l.bottom - l.top, rect.bottom - rect.top));
        if (line) {
            line.left = Math.min(line.left, rect.left);
            line.right = Math.max(line.right, rect.right);
            line.top = Math.min(line.top, rect.top);
            line.bottom = Math.max(line.bottom, rect.bottom);
        } else {
            lines.push({ left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom });
        }
    }
    return lines;
}
