// MarkdownEditor: the WYSIWYG Markdown editor as a component a page plugs in. It puts a VirtualTextView (and, if wanted,
// a toolbar) into an element of the page, shows the Markdown it is given as formatted text, and edits the Markdown as
// the user edits what they see. The page reads the Markdown back with `value` (or `getMarkdown()`), and is told of each
// change ('change').
//
//   const editor = new MarkdownEditor(document.querySelector('#editor'), { markdown: '# Hello', theme: 'dark' });
//   editor.addEventListener('change', () => save(editor.value));

import { VirtualTextView, GutterContent, refuseDrops } from './view/VirtualTextView.js';
import { MarkdownController } from './markdown/controller.js';
import { MarkdownStyleProvider, RawStyleProvider } from './markdown/styleProvider.js';
import { RawMode, rawToSource, rawFromSource } from './markdown/rawView.js';

const CSS = `
.mde { display: flex; flex-direction: column; box-sizing: border-box; height: 100%; min-height: 200px;
  border: 1px solid var(--mde-line); border-radius: 8px; overflow: hidden; background: var(--mde-bg); color: var(--mde-ink); }
.mde-toolbar { display: flex; flex-wrap: wrap; align-items: center; gap: 2px; padding: 4px 6px;
  border-bottom: 1px solid var(--mde-line); background: var(--mde-bar); }
.mde-toolbar button { all: unset; box-sizing: border-box; display: inline-flex; align-items: center; justify-content: center;
  min-width: 30px; height: 30px; padding: 0 6px; border-radius: 6px; cursor: pointer; color: var(--mde-ink);
  font: 600 13px system-ui, -apple-system, "Segoe UI", sans-serif; }
.mde-toolbar button:hover { background: var(--mde-hover); }
.mde-toolbar button:focus-visible { outline: 2px solid var(--mde-accent); outline-offset: -2px; }
.mde-toolbar button[aria-pressed="true"] { background: var(--mde-pressed); color: var(--mde-accent); }
.mde-toolbar button:disabled { opacity: 0.4; cursor: default; }
.mde-toolbar svg { width: 18px; height: 18px; fill: none; stroke: currentColor; stroke-width: 2; stroke-linecap: round;
  stroke-linejoin: round; }
.mde-toolbar .mde-zoom { min-width: 48px; font-weight: 500; font-variant-numeric: tabular-nums; }
.mde-toolbar .mde-sep { width: 1px; height: 20px; margin: 0 4px; background: var(--mde-line); }
.mde-toolbar select { font: 13px system-ui, sans-serif; height: 28px; border: 1px solid var(--mde-line); border-radius: 6px;
  background: var(--mde-bg); color: var(--mde-ink); padding: 0 4px; }
.mde-body { position: relative; flex: 1; min-height: 0; }
.mde-body > .vtv { position: absolute; inset: 0; min-height: 0; }
`;

let injected = false;
function injectStyles() {
    if (injected || typeof document === 'undefined') return;
    const style = document.createElement('style');
    style.textContent = CSS;
    document.head.append(style);
    injected = true;
}

const ICONS = {
    undo: '<path d="M9 14 4 9l5-5"/><path d="M4 9h10.5a5.5 5.5 0 0 1 0 11H11"/>',
    redo: '<path d="m15 14 5-5-5-5"/><path d="M20 9H9.5a5.5 5.5 0 0 0 0 11H13"/>',
    strong: '<path d="M7 5h6a3.5 3.5 0 0 1 0 7H7z"/><path d="M7 12h7a3.5 3.5 0 0 1 0 7H7z"/>',
    em: '<path d="M19 4h-9"/><path d="M14 20H5"/><path d="M15 4 9 20"/>',
    underline: '<path d="M6 4v6a6 6 0 0 0 12 0V4"/><path d="M4 21h16"/>',
    strike: '<path d="M16 4H9a3 3 0 0 0-2.83 4"/><path d="M14 12a4 4 0 0 1 0 8H6"/><path d="M4 12h16"/>',
    code: '<path d="m16 18 6-6-6-6"/><path d="m8 6-6 6 6 6"/>',
    quote: '<path d="M3 21c3 0 7-1 7-8V5c0-1.25-.76-2-2-2H4c-1.25 0-2 .75-2 1.97V11c0 1.25.75 2 2 2 1 0 1 0 1 1v1c0 1-1 2' +
        '-2 2s-1 .01-1 1.03V20c0 1 0 1 1 1z"/><path d="M15 21c3 0 7-1 7-8V5c0-1.25-.76-2-2-2h-4c-1.25 0-2 .75-2 1.97V11' +
        'c0 1.25.75 2 2 2h.75c0 2.25.25 4-2.75 4v3c0 1 0 1 1 1z"/>',
    bullet: '<path d="M9 6h11"/><path d="M9 12h11"/><path d="M9 18h11"/>' +
        '<circle cx="4" cy="6" r="1"/><circle cx="4" cy="12" r="1"/><circle cx="4" cy="18" r="1"/>',
    number: '<path d="M10 6h11"/><path d="M10 12h11"/><path d="M10 18h11"/>' +
        '<path d="M4 6h1v4"/><path d="M4 10h2"/><path d="M6 18H4c0-1 2-2 2-3s-1-1.5-2-1"/>',
    task: '<rect x="3" y="5" width="6" height="6" rx="1"/><path d="m3 17 2 2 4-4"/>' +
        '<path d="M13 6h8"/><path d="M13 12h8"/><path d="M13 18h8"/>',
    codeblock: '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="m10 9-3 3 3 3"/><path d="m14 15 3-3-3-3"/>',
    rule: '<path d="M3 12h18"/>',
    table: '<rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18"/><path d="M3 15h18"/>' +
        '<path d="M9 3v18"/><path d="M15 3v18"/>',
    link: '<path d="M10 13a5 5 0 0 0 7.54.54l3-3a5 5 0 0 0-7.07-7.07l-1.72 1.71"/>' +
        '<path d="M14 11a5 5 0 0 0-7.54-.54l-3 3a5 5 0 0 0 7.07 7.07l1.71-1.71"/>',
    image: '<rect x="3" y="3" width="18" height="18" rx="2"/><circle cx="9" cy="9" r="2"/>' +
        '<path d="m21 15-3.09-3.09a2 2 0 0 0-2.82 0L6 21"/>',
    marks: '<path d="M13 4v16"/><path d="M17 4v16"/><path d="M19 4H9.5a4.5 4.5 0 0 0 0 9H13"/>',
    numbers: '<path d="M4 9h16"/><path d="M4 15h16"/><path d="M10 3 8 21"/><path d="M16 3l-2 18"/>',
    types: '<path d="M4 6h9"/><path d="M4 12h6"/><path d="M4 18h9"/><path d="M16 9l3 3-3 3"/>',
    outline: '<path d="m4 6 3 3 3-3"/><path d="M13 7h7"/><path d="m5 14 3 3-3 3"/><path d="M13 17h7"/>',
    zoomout: '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/><path d="M8 11h6"/>',
    zoomin: '<circle cx="11" cy="11" r="7"/><path d="m21 21-4.3-4.3"/><path d="M8 11h6"/><path d="M11 8v6"/>',
    raw: '<path d="M14 3v4a1 1 0 0 0 1 1h4"/><path d="M17 21H7a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h7l5 5v11a2 2 0 0 1-2 2z"/>' +
        '<path d="m10 13-2 2 2 2"/><path d="m14 17 2-2-2-2"/>',
};

/** The toolbar's items, in order ('|' a separator): what `toolbar: true` shows. */
export const DEFAULT_TOOLBAR = ['undo', 'redo', '|', 'block', '|', 'strong', 'em', 'underline', 'strike', 'code', '|',
    'quote', 'bullet', 'number', 'task', 'codeblock', '|', 'link', 'image', 'table', 'rule', '|', 'marks', 'numbers',
    'types', 'raw', '|', 'zoomout', 'zoom', 'zoomin'];
// (the toolbar's 'outline' item, collapsible headings on or off, is not in it: a page offers it if it wants to)

// (what the raw Markdown view leaves on: the rest of the toolbar formats, which the source shown as it is has none of)
const RAW_ITEMS = new Set(['undo', 'redo', 'marks', 'numbers', 'types', 'outline', 'raw', 'zoomout', 'zoom', 'zoomin']);

// (the toolbar's switches of the view, which are on in a read-only document too)
const VIEW_ITEMS = new Set(['marks', 'numbers', 'types', 'outline', 'raw', 'zoomout', 'zoom', 'zoomin']);

// (the zoom buttons' step, in percent of the text's own size, and the view's limits: 25% to 500%)
const ZOOM_STEP = 10, MIN_ZOOM = 0.25, MAX_ZOOM = 5;

const LABELS = {
    undo: 'Undo (Ctrl+Z)', redo: 'Redo (Ctrl+Y)', strong: 'Bold (Ctrl+B)', em: 'Italic (Ctrl+I)', underline: 'Underline (Ctrl+U)',
    strike: 'Strikethrough (Ctrl+Shift+X)', code: 'Inline code (Ctrl+`)', quote: 'Quote (Ctrl+Alt+Q)',
    bullet: 'Bullet list (Ctrl+Shift+8)', number: 'Numbered list (Ctrl+Shift+7)', task: 'Task list (Ctrl+Shift+9)',
    codeblock: 'Code block (Ctrl+Alt+C)', link: 'Link (Ctrl+K)', image: 'Picture', table: 'Table',
    rule: 'Horizontal line (Ctrl+Alt+L)', marks: 'Paragraph marks', block: 'Paragraph or heading (Ctrl+0 to Ctrl+6)',
    raw: 'Markdown source (Ctrl+Shift+R)', numbers: 'Paragraph numbers', types: 'Paragraph types',
    outline: 'Outline marks (fold what a heading heads)', zoomout: 'Zoom out', zoomin: 'Zoom in',
    zoom: 'Zoom (click for 100%; Ctrl+wheel zooms too)',
};

const BLOCK_OPTIONS = [['paragraph', 'Paragraph'], ['h1', 'Heading 1'], ['h2', 'Heading 2'], ['h3', 'Heading 3'],
    ['h4', 'Heading 4'], ['h5', 'Heading 5'], ['h6', 'Heading 6']];

export class MarkdownEditor extends EventTarget {
    /**
     * @param {HTMLElement|string} target The element (or a selector of it) the editor goes into; it fills it.
     * @param {object} [options] { markdown, theme ('light' | 'dark' | a theme object), toolbar (true, false, or a list
     *   of DEFAULT_TOOLBAR's items and the page's own buttons: `toolbarItems`), readOnly, revealSyntax (the syntax
     *   at the caret shown; true by default), revealUnfocused (shown while the editor has no focus too; false by default), showFormattingMarks,
     *   imageBase (the URL relative pictures are loaded from), openLinks (Ctrl+click opens a link in a new tab; true
     *   by default), prompt (an async function (question, value) asking the user for a link's or a picture's
     *   address; window.prompt by default), rawMode (start in the raw Markdown view), showParagraphNumbers (each
     *   paragraph's number in a gutter left of the text), showParagraphTypes (each paragraph's type there),
     *   showOutline (headings that can be collapsed: the marks beside them there that fold what they head; false by
     *   default, the page's to offer), screenReader ('auto', the default, 'on' or 'off': `screenReader`) }
     */
    constructor(target, options = {}) {
        super();
        injectStyles();
        const host = typeof target === 'string' ? document.querySelector(target) : target;
        if (!host) throw new Error('MarkdownEditor: no element to put the editor in.');
        this.options = { toolbar: true, openLinks: true, ...options };
        this.host = host;
        this.root = document.createElement('div');
        this.root.className = 'mde';
        this.body = document.createElement('div');
        this.body.className = 'mde-body';
        this.root.append(this.body);
        // (a file dropped on the toolbar is not opened in place of the page either: the view refuses drops on itself)
        for (const [type, handler] of refuseDrops()) this.root.addEventListener(type, handler);
        host.append(this.root);

        this.styles = new MarkdownStyleProvider(this.options.theme ?? 'light');
        this.view = new VirtualTextView(this.body, {
            styleProvider: this.styles, readOnly: this.options.readOnly, acceptsTab: true,
            showFormattingMarks: this.options.showFormattingMarks, label: this.options.label ?? 'Markdown editor',
            leftGutterContent: (this.options.showParagraphNumbers ? GutterContent.ParagraphNumbers : GutterContent.None) |
                (this.options.showParagraphTypes ? GutterContent.StyleNames : GutterContent.None) |
                (this.options.showOutline === true ? GutterContent.Outline : GutterContent.None),
            padding: this.options.padding ?? [16, 24, 24, 24],
        });
        if (this.options.imageBase) this.imageBase = this.options.imageBase;
        this.controller = new MarkdownController(this.view, {
            markdown: this.options.markdown ?? '', revealSyntax: this.options.revealSyntax !== false,
            revealUnfocused: this.options.revealUnfocused === true,
        });
        this.raw = new RawMode(this.view, this.controller, new RawStyleProvider(this.styles));
        // (screen reader mode, 'auto': on while the main pointer is a fine one, a mouse's, and so as that changes)
        this._finePointer = typeof matchMedia === 'function' ? matchMedia('(pointer: fine)') : null;
        this._onPointerChange = () => this._applyScreenReader();
        this._finePointer?.addEventListener?.('change', this._onPointerChange);
        this.screenReader = this.options.screenReader ?? 'auto';
        this._rawChanges = 0; // (the edits made in the raw view, which `version` counts as well)
        this._applyThemeColors();
        this._buildToolbar();
        this._bindKeys();
        const changed = () => {
            this._refreshToolbar();
            this.dispatchEvent(new Event('change'));
            this.options.onChange?.(this);
        };
        const moved = () => {
            this._refreshToolbar();
            this.dispatchEvent(new Event('selectionchange'));
        };
        this.controller.addEventListener('change', changed);
        this.controller.addEventListener('selectionchange', moved);
        // (in the raw view the edits and the moves are the view's own)
        this.view.addEventListener('documentchange', () => {
            if (!this.raw.isOn || this.view.version === this.raw.session.loadedVersion) return;
            this._rawChanges++;
            changed();
        });
        this.view.addEventListener('selectionchange', () => {
            if (this.raw.isOn) moved();
        });
        // (the browser's own Undo and Redo, which the controller takes outside the raw view)
        this.view.addEventListener('undo', () => this.raw.isOn && this.undo());
        this.view.addEventListener('redo', () => this.raw.isOn && this.redo());
        this.view.addEventListener('hyperlinkclick', (event) => this._onLinkClick(event));
        // (Shift+Delete and Ctrl+Shift+V use the asynchronous clipboard, which a page may not be allowed: it is told)
        this.view.addEventListener('clipboardunavailable', () => this.dispatchEvent(new Event('clipboardunavailable')));
        if (this.options.rawMode) this.rawMode = true;
        this._refreshToolbar();
    }

    // ---- The document ------------------------------------------------------------------------------------------------

    /**
     * The Markdown being edited (in the line endings it was given in): in the raw view, the text it holds. Setting it
     * loads a new document (in the formatted view).
     */
    get value() {
        if (!this.raw.isOn) return this.controller.getMarkdown();
        const source = this.raw.source(), ending = this.controller.lineEnding;
        return ending === '\n' ? source : source.replace(/\n/g, ending);
    }

    set value(markdown) {
        const wasRaw = this.raw.isOn;
        this.raw.abandon();
        this.controller.load(String(markdown ?? ''));
        if (wasRaw) this._modeChanged();
    }

    getMarkdown() {
        return this.value;
    }

    setMarkdown(markdown) {
        this.value = markdown;
    }

    /** Counts every change of the document (the raw view's edits too): a saved document's version is a clean one. */
    get version() {
        return this.controller.version + this._rawChanges;
    }

    // ---- The raw Markdown view -------------------------------------------------------------------------------------

    /**
     * Whether the raw Markdown view is on: the document's source shown and edited as it is, in place of the formatted
     * text (MaxPad's View > Raw Markdown). Looking changes nothing; text changed there becomes the document when the view
     * is left, as one step of the history. The caret, the selection and the caret's line on the screen stay.
     */
    get rawMode() {
        return this.raw.isOn;
    }

    set rawMode(on) {
        if (!!on === this.raw.isOn) return;
        const caret = this.view.caret;
        const top = caret ? this.view.lineTopInView(caret) : null;
        if (on) this.raw.enter();
        else this.raw.exit();
        const now = this.view.caret;
        if (now) {
            if (top !== null) this.view.scrollToLineAt(now, top);
            else this.view.scrollToMiddle(now);
        }
        this._modeChanged();
    }

    toggleRawMode() {
        this.rawMode = !this.rawMode;
    }

    _modeChanged() {
        this._refreshToolbar();
        this.dispatchEvent(new Event('modechange'));
    }

    // Whether the commands that format the document can run: not in a read-only document, nor in the raw view.
    get _canFormat() {
        return !this.readOnly && !this.raw.isOn;
    }

    get readOnly() {
        return this.view.readOnly;
    }

    set readOnly(value) {
        this.view.readOnly = !!value;
        this.controller.refreshReveal(); // (a read-only document shows no syntax at the caret)
        this._refreshToolbar();
    }

    /**
     * 'light', 'dark' or a theme object (see styleProvider.js) over the light theme (over the dark one with `base:
     * 'dark'`): the default stylesheet's class `mde-dark`, and its custom properties. The getter gives its values.
     */
    set theme(theme) {
        this.options.theme = theme;
        this.styles.setTheme(theme);
        this.view.invalidateStyles();
        this._applyThemeColors();
    }

    get theme() {
        return this.styles.theme;
    }

    /** Where relative picture addresses are loaded from (a URL). */
    set imageBase(base) {
        this._imageBase = base;
        this.view.imageResolver = (src) => {
            try {
                return new URL(src, new URL(base, location.href)).href;
            } catch {
                return src;
            }
        };
    }

    get imageBase() {
        return this._imageBase;
    }

    /** Each paragraph's number in a gutter left of the text (MaxPad's View > Paragraph Numbers). Off by default. */
    get showParagraphNumbers() {
        return (this.view.leftGutterContent & GutterContent.ParagraphNumbers) !== 0;
    }

    set showParagraphNumbers(on) {
        this._showInGutter(GutterContent.ParagraphNumbers, on);
    }

    /**
     * Each paragraph's type in the gutter, left of its number when both are shown (MaxPad's View > Paragraph Types): the
     * name of its kind of block (Heading 2, Bullet, Code, Quote...); in the raw Markdown view, the type of the block
     * each line begins in the formatted view. Off by default.
     */
    get showParagraphTypes() {
        return (this.view.leftGutterContent & GutterContent.StyleNames) !== 0;
    }

    set showParagraphTypes(on) {
        this._showInGutter(GutterContent.StyleNames, on);
    }

    /**
     * Whether headings can be collapsed: the outline marks in the gutter (MaxPad's View > Outline Marks), beside each
     * heading with something under it, a triangle that collapses what it heads, down to the next heading of its level or
     * a higher one, and expands it again. Off by default: the page decides whether it offers this (2026-11-04, the user;
     * the toolbar's 'outline' item, which the default toolbar leaves out, or a button of its own). Turning it off
     * expands every collapsed heading, so that nothing stays folded with no mark to open it.
     */
    get showOutline() {
        return (this.view.leftGutterContent & GutterContent.Outline) !== 0;
    }

    set showOutline(on) {
        if (!on) this.view.expandAll();
        this._showInGutter(GutterContent.Outline, on);
    }

    /** Collapses every heading that has something under it (MaxPad's Collapse All); nothing while `showOutline` is off. */
    collapseAll() {
        if (this.showOutline) this.view.collapseAll();
    }

    /** Expands every collapsed heading: the whole document shows (MaxPad's Expand All). */
    expandAll() {
        this.view.expandAll();
    }

    _showInGutter(content, on) {
        const shown = this.view.leftGutterContent;
        this.view.leftGutterContent = on ? shown | content : shown & ~content;
        this._refreshToolbar();
    }

    /**
     * The number of the paragraph the caret is in, as the gutter numbers it (a table's row being one), in either view:
     * what a status bar says, to agree with the gutter. Null with no caret.
     */
    get paragraphNumber() {
        return this.view.caret ? this.view.getParagraphNumber(this.view.caret.paragraph) : null;
    }

    get showFormattingMarks() {
        return this.view.showFormattingMarks;
    }

    set showFormattingMarks(value) {
        this.view.showFormattingMarks = value;
        this._refreshToolbar();
    }

    /** The scale the document is shown at (1 is 100%; 0.25 to 5). 'zoomchange' after it changes. */
    get zoom() {
        return this.view.zoom;
    }

    set zoom(value) {
        const before = this.view.zoom;
        this.view.zoom = value;
        if (this.view.zoom === before) return;
        this._refreshToolbar();
        this.dispatchEvent(new Event('zoomchange'));
    }

    /** One step larger (10 percent more of the text's own size, as MaxPad's Zoom In, from the whole percent). */
    zoomIn() {
        this.zoom = (Math.round(this.zoom * 100) + ZOOM_STEP) / 100;
    }

    /** One step smaller (MaxPad's Zoom Out). */
    zoomOut() {
        this.zoom = (Math.round(this.zoom * 100) - ZOOM_STEP) / 100;
    }

    /** The text at its own size, 100% (MaxPad's Actual Size). */
    resetZoom() {
        this.zoom = 1;
    }

    /** The selection in the Markdown: { anchor, caret }, offsets in the source with "\n" line endings. */
    get selection() {
        if (!this.raw.isOn) return { ...this.controller.selection };
        const texts = this.raw.texts(), view = this.view;
        const at = (position) => rawToSource(position ?? { paragraph: 0, index: 0 }, texts);
        return { anchor: at(view.anchor ?? view.caret), caret: at(view.caret) };
    }

    /** Selects from `anchor` to `caret` (offsets in the source with "\n" line endings), and scrolls it into view. */
    select(anchor, caret = anchor) {
        if (!this.raw.isOn) return this.controller.select(anchor, caret);
        const texts = this.raw.texts();
        return this.view.select(rawFromSource(anchor, texts), rawFromSource(caret, texts));
    }

    focus() {
        this.view.focus();
    }

    /**
     * Screen reader mode: 'on', 'off', or 'auto' (the default), on where the main pointer is a mouse's and off on a touch
     * screen. On, the text area that has the focus holds the text round the caret, as it is shown, its own caret and
     * selection where the editor's are, so that a screen reader reads what the caret goes over and the lines round it
     * (the view's `screenReader`, which says whether it is on). Off on a touch screen because a phone's keyboard reads
     * that text too, and may correct a word of it, which the editor does not follow. Shift+Alt+F1 switches it.
     */
    get screenReader() {
        return this._screenReader;
    }

    set screenReader(value) {
        this._screenReader = value === 'on' || value === true ? 'on' : value === 'off' || value === false ? 'off' : 'auto';
        this._applyScreenReader();
    }

    _applyScreenReader() {
        const setting = this._screenReader;
        this.view.screenReader = setting === 'on' || (setting === 'auto' && (this._finePointer?.matches ?? true));
    }

    /** Takes the editor out of the page. */
    destroy() {
        this._finePointer?.removeEventListener?.('change', this._onPointerChange);
        this.view.destroy();
        this.root.remove();
    }

    // ---- Commands ----------------------------------------------------------------------------------------------------

    /**
     * Undoes a step: of the document's history, or in the raw view of its own (an edit at a time). Nothing in a read-only
     * document, which undo would change (the keys and the browser's Edit menu as well as the toolbar).
     */
    undo() {
        if (this.readOnly) return;
        if (this.raw.isOn) this.raw.undo();
        else this.controller.undo();
        this._refreshToolbar();
    }

    redo() {
        if (this.readOnly) return;
        if (this.raw.isOn) this.raw.redo();
        else this.controller.redo();
        this._refreshToolbar();
    }

    get canUndo() {
        return this.raw.isOn ? this.raw.canUndo : this.controller.canUndo;
    }

    get canRedo() {
        return this.raw.isOn ? this.raw.canRedo : this.controller.canRedo;
    }

    /** Toggles an inline format: 'strong', 'em', 'underline', 'strike', 'code', 'sub', 'sup'. */
    toggleInline(format) {
        if (this._canFormat) this.controller.toggleInline(format);
    }

    isInline(format) {
        return !this.raw.isOn && this.controller.isInline(format);
    }

    /** 'paragraph', 'h1' to 'h6', 'quote', 'bullet', 'number', 'task' or 'code' (a toggle, as the toolbar's buttons). */
    setBlockType(type) {
        if (this._canFormat) this.controller.setBlockType(type);
    }

    /** Headings of `level` (1 to 6), or paragraphs (0), of the paragraphs the selection touches: not a toggle. */
    setHeadingLevel(level) {
        if (this._canFormat) this.controller.setHeadingLevel(level);
    }

    /** One heading level deeper (`by` 1: a paragraph becomes Heading 1) or shallower (-1). */
    stepHeadingLevel(by) {
        if (this._canFormat) this.controller.stepHeadingLevel(by);
    }

    /** The type of the paragraph at the caret ('markdown' in the raw view). */
    get blockType() {
        return this.raw.isOn ? 'markdown' : this.controller.blockType;
    }

    insertTable(columns = 3, rows = 2) {
        if (this._canFormat) this.controller.insertTable(columns, rows);
    }

    insertRule() {
        if (this._canFormat) this.controller.insertRule();
    }

    insertCodeBlock(language = '') {
        if (this._canFormat) this.controller.insertCodeBlock(language);
    }

    /**
     * The selection made a link to `address` (with `title`, if given); in a link, that link's address changed instead,
     * its text and title kept, and an empty address takes the link away, leaving its text (MaxPad's Ctrl+K).
     */
    insertLink(address, title = '') {
        if (this._canFormat) this.controller.insertLink(address, title);
    }

    insertImage(address, description = '') {
        if (this._canFormat) this.controller.insertImage(address, description);
    }

    /**
     * Asks for a link's address (the page's `prompt`, or the browser's) and makes the selection a link to it; in a link,
     * asks with its address and changes it (an empty answer takes the link away). Cancelled, or answered with just
     * "https://", it does nothing.
     */
    async promptLink() {
        if (!this._canFormat) return;
        const current = this.controller.linkAtCaret();
        const answer = await this._ask('Link address:', current?.destination ?? 'https://');
        this.focus();
        if (answer === null || answer === undefined) return;
        const address = String(answer).trim();
        if (address !== 'https://') this.insertLink(address);
    }

    async promptImage() {
        if (!this._canFormat) return;
        const address = await this._ask('Picture address:', '');
        this.focus();
        if (address) this.insertImage(address.trim(), '');
    }

    async _ask(question, value) {
        if (this.options.prompt) return this.options.prompt(question, value);
        return window.prompt(question, value);
    }

    /** A command by name, as the toolbar runs it ('strong', 'h2', 'bullet', 'undo', 'table', 'link'...). */
    execute(name) {
        if (['strong', 'em', 'underline', 'strike', 'code', 'sub', 'sup'].includes(name)) return this.toggleInline(name);
        if (/^h[1-6]$/.test(name)) return this.setHeadingLevel(+name[1]);
        if (['paragraph', 'quote', 'bullet', 'number', 'task'].includes(name)) return this.setBlockType(name);
        switch (name) {
            case 'undo': return this.undo();
            case 'redo': return this.redo();
            case 'codeblock': return this.setBlockType('code');
            case 'rule': return this.insertRule();
            case 'table': return this.insertTable();
            case 'link': return this.promptLink();
            case 'image': return this.promptImage();
            case 'marks':
                this.showFormattingMarks = !this.showFormattingMarks;
                return undefined;
            case 'numbers':
                this.showParagraphNumbers = !this.showParagraphNumbers;
                return undefined;
            case 'types':
                this.showParagraphTypes = !this.showParagraphTypes;
                return undefined;
            case 'outline':
                this.showOutline = !this.showOutline;
                return undefined;
            case 'zoomin': return this.zoomIn();
            case 'zoomout': return this.zoomOut();
            case 'zoom': return this.resetZoom();
            case 'collapseAll': return this.collapseAll();
            case 'expandAll': return this.expandAll();
            case 'raw': return this.toggleRawMode();
            default:
                throw new Error(`MarkdownEditor: no command "${name}".`);
        }
    }

    // ---- Keys, toolbar, links ---------------------------------------------------------------------------------------

    // The keys: MaxPad's (a browser gives a page Ctrl+1 to Ctrl+6 before it switches tabs with them, and switches none
    // when the page takes them), with Ctrl+Alt+0 to 6 as well, as TinyPad's, for a browser that keeps Ctrl+digits;
    // and other keys where a browser keeps MaxPad's (Ctrl+Shift+T, Q, C, [ and ]).
    _bindKeys() {
        const keys = this.view.keymap;
        const run = (fn) => () => {
            fn();
            return true;
        };
        keys.set('Ctrl+Shift+R', run(() => this.toggleRawMode()));
        keys.set('Ctrl+Z', run(() => this.undo()));
        keys.set('Ctrl+Y', run(() => this.redo()));
        keys.set('Ctrl+Shift+Z', run(() => this.redo()));
        keys.set('Ctrl+B', run(() => this.toggleInline('strong')));
        keys.set('Ctrl+I', run(() => this.toggleInline('em')));
        keys.set('Ctrl+U', run(() => this.toggleInline('underline')));
        keys.set('Ctrl+Shift+X', run(() => this.toggleInline('strike')));
        keys.set('Ctrl+`', run(() => this.toggleInline('code')));
        keys.set('Ctrl+K', run(() => this.promptLink()));
        for (let level = 0; level <= 6; level++) {
            keys.set(`Ctrl+${level}`, run(() => this.setHeadingLevel(level)));
            keys.set(`Ctrl+Alt+${level}`, run(() => this.setHeadingLevel(level)));
        }
        keys.set('Ctrl+Shift+7', run(() => this.setBlockType('number')));
        keys.set('Ctrl+Shift+8', run(() => this.setBlockType('bullet')));
        keys.set('Ctrl+Shift+9', run(() => this.setBlockType('task')));
        keys.set('Ctrl+Alt+Q', run(() => this.setBlockType('quote')));
        keys.set('Ctrl+Alt+C', run(() => this.setBlockType('code')));
        keys.set('Ctrl+Alt+L', run(() => this.insertRule()));
        keys.set('Alt+F10', run(() => this.focusToolbar())); // (the toolbar's key in editors of the web)
        keys.set('Alt+Shift+F1', run(() => {
            this.screenReader = this.view.screenReader ? 'off' : 'on';
            this.view.announce(this.view.screenReader ? 'Screen reader mode on' : 'Screen reader mode off');
        }));
        // (the zoom has no keys, as MaxPad's: Ctrl and the mouse wheel zoom, about 2% a notch)
        this.view.scroller.addEventListener('wheel', (event) => {
            if (!event.ctrlKey) return;
            event.preventDefault();
            const notches = event.deltaY / (event.deltaMode === 1 ? 3 : event.deltaMode === 2 ? 0.3 : 100);
            this.zoom = this.zoom * Math.pow(0.98, notches);
        }, { passive: false });
        // (keys by where they are, whatever a layout types with them: the backquote, ` on most layouts and Dead on some;
        // and the = and - keys of Ctrl+= and Ctrl+-, MaxPad's heading level one deeper or shallower, with the number pad's)
        const byCode = {
            Backquote: () => this.toggleInline('code'),
            Equal: () => this.stepHeadingLevel(1), NumpadAdd: () => this.stepHeadingLevel(1),
            Minus: () => this.stepHeadingLevel(-1), NumpadSubtract: () => this.stepHeadingLevel(-1),
        };
        // (a key with no code -- a virtual keyboard's, a remote desktop's -- by what it types)
        const codeOf = { '`': 'Backquote', '=': 'Equal', '+': 'NumpadAdd', '-': 'Minus' };
        this.view.addEventListener('key', (event) => {
            const { event: key } = event.detail;
            const command = byCode[key.code || codeOf[key.key]];
            if (command && (key.ctrlKey || key.metaKey) && !key.altKey) {
                event.preventDefault();
                command();
            }
        });
    }

    /**
     * The toolbar's items, in order: DEFAULT_TOOLBAR's names (a page picks which it wants, in its order), '|' for a
     * separator, and the page's own buttons, each an object { name, title, icon (an SVG: a whole <svg> element, or what
     * goes inside a 24 x 24 one drawn as the editor's icons are, in strokes), label (text, for a button with no icon),
     * onClick(editor, event), pressed(editor) (true or false: a switch, shown pressed or not), enabled(editor) }. A click
     * on one of the page's buttons calls its `onClick` and raises 'toolbarclick' (`detail.name`, `detail.item`); the
     * caret stays in the editor. Setting it builds the toolbar again; an empty list (or false) shows none.
     */
    get toolbarItems() {
        return [...(this._toolbarItems ?? [])];
    }

    set toolbarItems(items) {
        this.options.toolbar = items;
        this.toolbar?.remove();
        this.toolbar = null;
        this.blockSelect = null;
        this._buildToolbar();
        this.refreshToolbar();
    }

    /** Shows again what the toolbar's buttons say (pressed, enabled): for a page whose own buttons' state changed. */
    refreshToolbar() {
        this._refreshToolbar();
    }

    _buildToolbar() {
        const toolbar = this.options.toolbar;
        const items = toolbar === true ? DEFAULT_TOOLBAR : Array.isArray(toolbar) && toolbar.length ? toolbar : null;
        this.buttons = new Map();
        this._customButtons = new Map();
        this._toolbarItems = items ? [...items] : [];
        if (!items) return;
        const bar = document.createElement('div');
        bar.className = 'mde-toolbar';
        bar.setAttribute('role', 'toolbar');
        bar.setAttribute('aria-label', 'Formatting');
        for (const item of items) {
            if (item && typeof item === 'object') {
                bar.append(this._customButton(item));
                continue;
            }
            if (item === '|') {
                const separator = document.createElement('span');
                separator.className = 'mde-sep';
                bar.append(separator);
                continue;
            }
            if (item === 'block') {
                const select = document.createElement('select');
                select.title = LABELS.block;
                select.setAttribute('aria-label', 'Paragraph type');
                for (const [value, label] of BLOCK_OPTIONS) select.append(new Option(label, value));
                select.addEventListener('change', () => {
                    this.setHeadingLevel(select.value === 'paragraph' ? 0 : +select.value[1]);
                    this.focus();
                });
                this.blockSelect = select;
                bar.append(select);
                continue;
            }
            const button = document.createElement('button');
            button.type = 'button';
            button.title = LABELS[item] ?? item;
            button.setAttribute('aria-label', LABELS[item] ?? item);
            button.innerHTML = ICONS[item] ? `<svg viewBox="0 0 24 24" aria-hidden="true">${ICONS[item]}</svg>` : item;
            if (item === 'zoom') button.className = 'mde-zoom'; // (its text is the zoom: _refreshToolbar)
            // (a click keeps the caret in the editor: the button does not take the focus)
            button.addEventListener('mousedown', (event) => event.preventDefault());
            button.addEventListener('click', () => {
                this.execute(item);
                if (item !== 'link' && item !== 'image') this.focus();
            });
            this.buttons.set(item, button);
            bar.append(button);
        }
        this.root.insertBefore(bar, this.body);
        this.toolbar = bar;
        // (one Tab stop: the arrow keys, Home and End go from control to control, Escape back to the text)
        bar.addEventListener('keydown', (event) => this._onToolbarKey(event));
        bar.addEventListener('focusin', (event) => this._rove(event.target));
        this._rove(null);
    }

    /** Gives the focus to the toolbar (Alt+F10 in the text): its control that was last in use, or its first. */
    focusToolbar() {
        if (!this.toolbar) return;
        this._rove(null);
        this._toolbarControls().find((control) => control.tabIndex === 0)?.focus();
    }

    // The toolbar's controls that can take the focus, in order.
    _toolbarControls() {
        return this.toolbar ? [...this.toolbar.querySelectorAll('button, select')].filter((c) => !c.disabled) : [];
    }

    // The toolbar is one Tab stop (WAI-ARIA's toolbar): `control` is the one with tabIndex 0, the others -1; null keeps
    // the one there is, or takes the first that can be used when it cannot.
    _rove(control) {
        if (!this.toolbar) return;
        const all = [...this.toolbar.querySelectorAll('button, select')];
        const usable = all.filter((c) => !c.disabled);
        let current = control && usable.includes(control) ? control : all.find((c) => c.tabIndex === 0);
        if (!current || current.disabled) current = usable[0] ?? null;
        for (const c of all) c.tabIndex = c === current ? 0 : -1;
    }

    _onToolbarKey(event) {
        if (event.altKey || event.ctrlKey || event.metaKey) return;
        const controls = this._toolbarControls();
        const at = controls.indexOf(document.activeElement);
        let next = null;
        switch (event.key) {
            case 'ArrowRight': next = controls[(at + 1) % controls.length]; break;
            case 'ArrowLeft': next = controls[(at - 1 + controls.length) % controls.length]; break;
            case 'Home': next = controls[0]; break;
            case 'End': next = controls[controls.length - 1]; break;
            case 'Escape':
                event.preventDefault();
                this.focus();
                return;
            default: return;
        }
        if (!next) return;
        event.preventDefault();
        this._rove(next);
        next.focus();
    }

    // A button of the page's own (`toolbarItems`): drawn as the editor's are, its click the page's.
    _customButton(item) {
        const button = document.createElement('button');
        button.type = 'button';
        const title = item.title ?? item.label ?? item.name ?? '';
        if (title) {
            button.title = title;
            button.setAttribute('aria-label', title);
        }
        const icon = typeof item.icon === 'string' ? item.icon.trim() : '';
        if (icon.startsWith('<svg')) button.innerHTML = icon;
        else if (icon) button.innerHTML = `<svg viewBox="0 0 24 24" aria-hidden="true">${icon}</svg>`;
        else button.textContent = item.label ?? item.name ?? '?';
        if (item.name) button.dataset.name = item.name;
        button.addEventListener('mousedown', (event) => event.preventDefault());
        button.addEventListener('click', (event) => {
            item.onClick?.(this, event);
            this.dispatchEvent(new CustomEvent('toolbarclick', { detail: { name: item.name ?? null, item } }));
            this._refreshToolbar();
            if (item.keepFocus !== false) this.focus();
        });
        this._customButtons.set(button, item);
        return button;
    }

    _refreshToolbar() {
        for (const [button, item] of this._customButtons ?? []) {
            const pressed = item.pressed?.(this);
            if (pressed === true || pressed === false) button.setAttribute('aria-pressed', String(pressed));
            if (item.enabled) button.disabled = !item.enabled(this);
        }
        if (!this.buttons?.size && !this.blockSelect) {
            this._rove(null);
            return;
        }
        const readOnly = this.readOnly, raw = this.raw.isOn;
        const type = this.blockType;
        for (const [item, button] of this.buttons) {
            let pressed = null;
            if (['strong', 'em', 'underline', 'strike', 'code'].includes(item)) pressed = this.isInline(item);
            else if (['quote', 'bullet', 'number', 'task'].includes(item)) pressed = type === item;
            else if (item === 'codeblock') pressed = type === 'code';
            else if (item === 'marks') pressed = this.showFormattingMarks;
            else if (item === 'raw') pressed = raw;
            else if (item === 'numbers') pressed = this.showParagraphNumbers;
            else if (item === 'types') pressed = this.showParagraphTypes;
            else if (item === 'outline') pressed = this.showOutline;
            else if (item === 'zoom') {
                const text = Math.round(this.zoom * 100) + '%';
                if (button.textContent !== text) button.textContent = text;
            } else if (item === 'zoomin') button.disabled = this.zoom >= MAX_ZOOM - 1e-9;
            else if (item === 'zoomout') button.disabled = this.zoom <= MIN_ZOOM + 1e-9;
            if (pressed !== null) button.setAttribute('aria-pressed', String(pressed));
            if (item === 'undo') button.disabled = readOnly || !this.canUndo;
            else if (item === 'redo') button.disabled = readOnly || !this.canRedo;
            else if (!VIEW_ITEMS.has(item)) button.disabled = readOnly || (raw && !RAW_ITEMS.has(item));
        }
        if (this.blockSelect) {
            const known = BLOCK_OPTIONS.some(([value]) => value === type);
            this.blockSelect.value = known ? type : 'paragraph';
            this.blockSelect.disabled = readOnly || raw;
        }
        this._rove(null); // (the toolbar's Tab stop stays on a control that can be used)
    }

    _applyThemeColors() {
        const t = this.styles.theme;
        const dark = isDark(t.background, this.root);
        const set = (name, value) => this.root.style.setProperty(name, value);
        set('--mde-bg', t.background);
        set('--mde-ink', t.color);
        set('--mde-line', dark ? '#3a3d42' : '#d0d7de');
        set('--mde-bar', dark ? '#25272b' : '#f6f8fa');
        set('--mde-hover', dark ? '#33363c' : '#e7eaee');
        set('--mde-pressed', dark ? '#2e3a50' : '#dbe7ff');
        set('--mde-accent', t.linkColor);
    }

    _onLinkClick(event) {
        const { link } = event.detail;
        const detail = { href: link.target, title: link.title ?? null };
        const relay = new CustomEvent('linkclick', { cancelable: true, detail });
        this.dispatchEvent(relay);
        if (!relay.defaultPrevented && this.options.openLinks && /^(https?:|mailto:)/i.test(link.target)) {
            window.open(link.target, '_blank', 'noopener');
        }
    }
}

// Whether a CSS color is dark (what the frame and the toolbar are drawn in: the theme's background), however it is
// written -- "#111", "rgb(...)", "hsl(...)", "oklch(...)", a name, a custom property of the page's -- as the browser
// computes it on `element` (on the page's root while the element is not in the page) and draws it (one pixel of a
// canvas). A color it cannot tell, or a transparent one, is light.
let pixel = null;
function isDark(color, element) {
    if (!color || typeof document === 'undefined') return false;
    const probe = document.createElement('span');
    probe.style.color = color;
    if (!probe.style.color) return false; // (not a color)
    probe.style.display = 'none';
    (element?.isConnected ? element : document.documentElement).append(probe);
    const computed = getComputedStyle(probe).color;
    probe.remove();
    pixel ??= Object.assign(document.createElement('canvas'), { width: 1, height: 1 })
        .getContext('2d', { willReadFrequently: true });
    if (!pixel) return false;
    pixel.clearRect(0, 0, 1, 1);
    pixel.fillStyle = computed;
    pixel.fillRect(0, 0, 1, 1);
    const [r, g, b, a] = pixel.getImageData(0, 0, 1, 1).data;
    return a > 0 && 0.299 * r + 0.587 * g + 0.114 * b < 128;
}
