// <markdown-editor>: the editor as an element of the page, for pages that write HTML rather than script.
//
//   <markdown-editor name="body" theme="dark" style="height: 400px">
//     <script type="text/markdown">
//       # Hello
//
//       Some **bold** text.
//     </script>
//   </markdown-editor>
//
// Attributes: `src` (a Markdown file to load), `theme` ("light" or "dark"), `toolbar` ("false" for none, or the names of
// the items wanted, in order, separated by spaces or commas: "undo redo | strong em"), `readonly`,
// `name` (the field's name in a form: the element is a form field whose value is the Markdown), `image-base`,
// `paragraph-numbers` (each paragraph's number in a gutter), `paragraph-types` (each paragraph's type there),
// `outline` (headings that can be collapsed: marks beside them that fold what they head), `screen-reader` ("auto",
// "on" or "off": the editor's screen reader mode).
// Properties: `value`, `editor` (the MarkdownEditor). Events: 'input' and 'change' after each of the user's changes (as a
// <textarea>'s: not for a value a script sets, a form's reset or a `src` file), 'linkclick'.

import { MarkdownEditor } from './MarkdownEditor.js';

// The Markdown written inside the element, with the indentation its lines share taken off.
function initialMarkdown(element) {
    const source = element.querySelector('script[type="text/markdown"], template');
    let text = source ? (source instanceof HTMLTemplateElement ? templateText(source.content) : source.textContent)
        : element.textContent;
    text = text.replace(/^\s*\n/, '').replace(/\n\s*$/, '');
    const indents = text.split('\n').filter((line) => line.trim()).map((line) => /^[ \t]*/.exec(line)[0].length);
    const common = indents.length ? Math.min(...indents) : 0;
    if (!common) return text;
    return text.split('\n').map((line) => line.slice(Math.min(common, /^[ \t]*/.exec(line)[0].length))).join('\n');
}

// What a <template> holds, as the Markdown written in it: its text as text (a quote's ">" and an "&" as they are, where
// `innerHTML` gives "&gt;" and "&amp;"), and the elements the page's parser made of the HTML in it written as HTML.
function templateText(node) {
    let text = '';
    for (const child of node.childNodes) {
        if (child.nodeType === Node.TEXT_NODE) {
            text += child.data;
        } else if (child.nodeType === Node.COMMENT_NODE) {
            text += `<!--${child.data}-->`;
        } else if (child.nodeType === Node.ELEMENT_NODE) {
            const html = child.cloneNode(false).outerHTML; // (its start tag, and its end tag unless it is a void one)
            const end = `</${child.localName}>`;
            const closed = html.endsWith(end);
            const inner = child instanceof HTMLTemplateElement ? child.content : child;
            text += (closed ? html.slice(0, -end.length) : html) + templateText(inner) + (closed ? end : '');
        }
    }
    return text;
}

// The toolbar an attribute asks for: all of it (none given, or "true"), none ("false"), or the items it names.
function toolbarOf(attribute) {
    const text = (attribute ?? '').trim();
    if (!text || text === 'true') return true;
    if (text === 'false') return false;
    return text.split(/[\s,]+/).filter(Boolean);
}

export class MarkdownEditorElement extends HTMLElement {
    static formAssociated = true;
    static observedAttributes = [
        'readonly', 'theme', 'src', 'image-base', 'paragraph-numbers', 'paragraph-types', 'toolbar', 'outline',
        'screen-reader',
    ];

    constructor() {
        super();
        this._internals = this.attachInternals?.() ?? null;
    }

    connectedCallback() {
        if (this.editor) return;
        if (!this.style.display) this.style.display = 'block';
        const markdown = this.hasAttribute('value') ? this.getAttribute('value') : initialMarkdown(this);
        this._defaultValue = markdown; // (what a form's reset brings back)
        this.replaceChildren();
        this.editor = new MarkdownEditor(this, {
            markdown,
            theme: this.getAttribute('theme') ?? 'light',
            toolbar: toolbarOf(this.getAttribute('toolbar')),
            readOnly: this.hasAttribute('readonly'),
            imageBase: this.getAttribute('image-base') ?? undefined,
            showParagraphNumbers: this.hasAttribute('paragraph-numbers'),
            showParagraphTypes: this.hasAttribute('paragraph-types'),
            showOutline: this.hasAttribute('outline'),
            screenReader: this.getAttribute('screen-reader') ?? 'auto',
        });
        this.editor.addEventListener('change', () => {
            this._internals?.setFormValue(this.editor.value);
            // (as a <textarea>'s: the user's changes only, not a value a script sets, a form's reset or a `src` file;
            // the editor's own 'change' is raised for those too)
            if (this._settingValue) return;
            this.dispatchEvent(new Event('input', { bubbles: true }));
            this.dispatchEvent(new Event('change', { bubbles: true }));
        });
        this.editor.addEventListener('linkclick', (event) => {
            const relay = new CustomEvent('linkclick', { bubbles: true, cancelable: true, detail: event.detail });
            if (!this.dispatchEvent(relay)) event.preventDefault();
        });
        this._internals?.setFormValue(this.editor.value);
        if (this.hasAttribute('src')) this._load(this.getAttribute('src'));
    }

    attributeChangedCallback(name, _old, value) {
        if (!this.editor) return;
        if (name === 'readonly') this.editor.readOnly = value !== null;
        else if (name === 'theme') this.editor.theme = value ?? 'light';
        else if (name === 'src' && value) this._load(value);
        else if (name === 'image-base') this.editor.imageBase = value;
        else if (name === 'paragraph-numbers') this.editor.showParagraphNumbers = value !== null;
        else if (name === 'paragraph-types') this.editor.showParagraphTypes = value !== null;
        else if (name === 'toolbar') this.editor.toolbarItems = toolbarOf(value);
        else if (name === 'outline') this.editor.showOutline = value !== null;
        else if (name === 'screen-reader') this.editor.screenReader = value ?? 'auto';
    }

    async _load(url) {
        const response = await fetch(url);
        if (!response.ok) throw new Error(`markdown-editor: could not load ${url} (${response.status})`);
        if (!this.hasAttribute('image-base')) this.editor.imageBase = new URL(url, location.href).href;
        const markdown = await response.text();
        this._defaultValue = markdown; // (the document the element was given: a form's reset brings it back)
        this.value = markdown;
    }

    get value() {
        return this.editor ? this.editor.value : '';
    }

    set value(markdown) {
        if (!this.editor) {
            this.setAttribute('value', markdown);
            return;
        }
        this._settingValue = true;
        try {
            this.editor.value = markdown;
        } finally {
            this._settingValue = false;
        }
        this._internals?.setFormValue(this.editor.value);
    }

    get name() {
        return this.getAttribute('name');
    }

    get form() {
        return this._internals?.form ?? null;
    }

    focus() {
        this.editor?.focus();
    }

    // A form's reset: the Markdown the element was given (written in it, its `value` attribute, or its `src` document)
    // comes back, as a <textarea> goes back to the text written in it.
    formResetCallback() {
        this.value = this._defaultValue ?? '';
    }
}

if (typeof customElements !== 'undefined' && !customElements.get('markdown-editor')) {
    customElements.define('markdown-editor', MarkdownEditorElement);
}
