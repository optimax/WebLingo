// The look of a Markdown document: what each paragraph and character style ID of the projection stands for. The web
// counterpart of MaxPad.Projection's MarkdownStyleProvider. The look itself is a stylesheet (stylesheet.js): each style
// is classes of it (`mde-content-...`), which a page's own stylesheet may restyle (2026-11-03, the user: the styles are
// to be "applied consistently through a stylesheet that the host supplies").
//
// A theme is 'light', 'dark', or an object of values over one of them ({ base: 'dark', ... } over the dark one):
// { fontFamily, fontSize, codeFontFamily, color, background, headingColor, linkColor, syntaxColor, quietColor,
// htmlColor, codeBackground, codeFrameColor, inlineCodeBackground, quoteBarColor, ruleColor, tableBorderColor,
// tableHeaderBackground, selectionColor, caretColor, highlightColor, currentLineColor, documentWidth, centered,
// listIndent, paragraphSpacing, headingLines, paragraphNumberColor, paragraphTypeColor, gutterFontSize }. The dark
// one is the stylesheet's class `mde-dark`; the values a page gives are the stylesheet's custom properties
// (`--mde-heading-color`, ...) on the content (the width and the centering too: `--mde-document-width`,
// `--mde-document-margin`, the page's), except for the gutter's font size, which the view is given.

import { parseStyleId } from './projection.js';
import { injectMarkdownStylesheet, STYLE_DEPTH_LIMIT } from './stylesheet.js';

export const LIGHT_THEME = Object.freeze({
    fontFamily: '"Segoe UI", system-ui, -apple-system, Roboto, "Helvetica Neue", sans-serif',
    fontSize: 16,
    codeFontFamily: 'Consolas, "Cascadia Mono", Menlo, "DejaVu Sans Mono", monospace',
    color: '#1f2328',
    background: '#ffffff',
    headingColor: '#203287',
    linkColor: '#0066cc',
    syntaxColor: '#808080',
    quietColor: '#6a737d',
    htmlColor: '#8250df',
    codeBackground: '#f5f6f8',
    codeFrameColor: '#dddddd',
    inlineCodeBackground: '#f0f1f3',
    quoteBarColor: '#c8cdd4',
    ruleColor: '#2032aa',
    tableBorderColor: '#d9dce1',
    tableHeaderBackground: '#f0f2f5',
    selectionColor: 'rgba(51, 144, 255, 0.28)',
    caretColor: '#000000',
    highlightColor: 'rgba(255, 213, 0, 0.45)',
    currentLineColor: '#fffbdd',
    documentWidth: 720,
    centered: true,
    listIndent: 28,
    paragraphSpacing: 8,
    headingLines: [3, 2, 1, 0.5, 0, 0],
    paragraphNumberColor: '#ab2899',
    paragraphTypeColor: '#137a10',
    gutterFontSize: 12,
});

export const DARK_THEME = Object.freeze({
    ...LIGHT_THEME,
    color: '#e6e6e6',
    background: '#1e1f22',
    headingColor: '#8fb4ff',
    linkColor: '#6cb6ff',
    syntaxColor: '#8b8f96',
    quietColor: '#a0a6ad',
    htmlColor: '#c49bff',
    codeBackground: '#2a2c30',
    codeFrameColor: '#3d4046',
    inlineCodeBackground: '#33363b',
    quoteBarColor: '#4b5059',
    ruleColor: '#8fb4ff',
    tableBorderColor: '#454950',
    tableHeaderBackground: '#2c2f34',
    selectionColor: 'rgba(90, 160, 255, 0.35)',
    caretColor: '#ffffff',
    highlightColor: 'rgba(255, 200, 0, 0.35)',
    currentLineColor: '#2b2d24',
    paragraphNumberColor: '#d68bd0',
    paragraphTypeColor: '#6fbf6a',
});

// The theme's values that are the stylesheet's custom properties as they are (colors and fonts): --mde-<name>.
const PROPERTY_KEYS = new Set(['fontFamily', 'codeFontFamily', 'color', 'background', 'headingColor', 'linkColor',
    'syntaxColor', 'quietColor', 'htmlColor', 'codeBackground', 'codeFrameColor', 'inlineCodeBackground',
    'quoteBarColor', 'ruleColor', 'tableBorderColor', 'tableHeaderBackground', 'selectionColor', 'caretColor',
    'highlightColor', 'currentLineColor', 'paragraphNumberColor', 'paragraphTypeColor']);

// Each kind of paragraph: its name (the gutter shows it) and its classes.
const KINDS = {
    p: ['Paragraph', 'mde-content-p'],
    blank: ['Empty paragraph', 'mde-content-blank'],
    bullet: ['Bullet', 'mde-content-bullet'],
    list: ['List', 'mde-content-numbered'],
    check: ['Check', 'mde-content-task'],
    image: ['Image', 'mde-content-image'],
    hr: ['Rule', 'mde-content-rule'],
    code: ['Code', 'mde-content-code-block'],
    'code-indented': ['Code', 'mde-content-code-block mde-content-code-indented'],
    'code-fence': ['Code fence', 'mde-content-code-fence'],
    'code-fence-close': ['Code fence', 'mde-content-code-fence mde-content-code-fence-close'],
    html: ['HTML', 'mde-content-html-block'],
    definition: ['Definition', 'mde-content-definition'],
    other: ['Other', 'mde-content-other'],
    'front-matter': ['Front matter', 'mde-content-front-matter'],
    'front-matter-fence': ['Front matter fence', 'mde-content-front-matter mde-content-front-matter-fence'],
    'table-header': ['Table header', 'mde-content-table-header'],
    'table-cell': ['Table row', 'mde-content-table-cell'],
};

// Each name of a character style ID (its names joined by '+'): its class.
const RUNS = {
    code: 'mde-content-code', underline: 'mde-content-underline', strike: 'mde-content-strike', sub: 'mde-content-sub',
    sup: 'mde-content-sup', html: 'mde-content-html', syntax: 'mde-content-syntax',
    'rule-syntax': 'mde-content-rule-syntax', concealed: 'mde-content-concealed', task: 'mde-content-task-box',
    'task-done': 'mde-content-task-box mde-content-task-done',
};

export class MarkdownStyleProvider {
    constructor(theme = 'light') {
        injectMarkdownStylesheet();
        this.setTheme(theme);
    }

    /**
     * The theme: 'light', 'dark', or an object of values over the light one (over the dark one with `base: 'dark'`).
     * `theme` is then the whole of it, its values (what the editor's frame is drawn in).
     */
    setTheme(theme) {
        const given = theme && typeof theme === 'object' ? theme : {};
        this.dark = theme === 'dark' || given.base === 'dark';
        this.overrides = Object.fromEntries(Object.entries(given).filter(([key, value]) => key !== 'base' && value != null));
        this.theme = { ...(this.dark ? DARK_THEME : LIGHT_THEME), ...this.overrides };
    }

    getDocumentStyle() {
        const t = this.theme;
        return {
            className: this.dark ? 'mde-content mde-dark' : 'mde-content',
            pageClassName: 'mde-content-page', // (its width and where it goes are the stylesheet's)
            variables: themeVariables(this.overrides),
            maxWidth: t.documentWidth > 0 ? t.documentWidth : Infinity, placement: t.centered ? 'center' : 'left',
            background: 'var(--mde-background)', color: 'var(--mde-color)', selectionColor: 'var(--mde-selection-color)',
            caretColor: 'var(--mde-caret-color)', highlightColor: 'var(--mde-highlight-color)',
            gutter: {
                fontFamily: t.fontFamily, fontSize: t.gutterFontSize, color: 'var(--mde-paragraph-number-color)',
                namesColor: 'var(--mde-paragraph-type-color)',
            },
        };
    }

    getHyperlinkStyle() {
        return { className: 'mde-content-link' };
    }

    getTableStyle() {
        return { className: 'mde-content-table', cellClassName: 'mde-content-td' };
    }

    /**
     * A paragraph's style: its name, and its classes -- `mde-content-para`, its kind's (`mde-content-h2`,
     * `mde-content-bullet`...), and in quotes and lists `mde-content-in-quote` and `mde-content-q<depth>`,
     * `mde-content-in-list` and `mde-content-l<depth>`.
     */
    getParagraphStyle(styleId) {
        const { kind, quotes, lists } = parseStyleId(styleId ?? 'p');
        const heading = /^h([1-6])$/.exec(kind);
        let [name, className] = KINDS[kind] ?? KINDS.p;
        if (heading) [name, className] = ['Heading ' + heading[1], `mde-content-heading mde-content-${kind}`];
        if (kind === 'p') name = plainName(quotes, lists);
        const classes = ['mde-content-para', className];
        if (quotes > 0) classes.push('mde-content-in-quote', `mde-content-q${Math.min(quotes, STYLE_DEPTH_LIMIT)}`);
        if (lists > 0) classes.push('mde-content-in-list', `mde-content-l${Math.min(lists, STYLE_DEPTH_LIMIT)}`);
        const style = { name, className: classes.join(' ') };
        if (heading) {
            style.nextStyleId = 'p';
            style.outlineLevel = +heading[1]; // (a heading heads what follows it: the view's outline folds it)
        }
        return style;
    }

    getCharacterStyle(styleId) {
        const classes = styleId.split('+').map((name) => RUNS[name]).filter(Boolean);
        return { className: classes.join(' ') };
    }
}

/**
 * The look of the raw Markdown view (MaxPad's RawStyleProvider): the code font, no margins, nothing hidden -- what the file
 * holds is what is drawn -- and a light band behind the caret's line (`--mde-current-line-color`). The width and the
 * centering are the formatted view's (`document`, a MarkdownStyleProvider), so that switching views does not move the
 * text sideways. Its paragraphs are `mde-content-raw`.
 */
export class RawStyleProvider {
    constructor(document) {
        this.document = document;
    }

    get theme() {
        return this.document.theme;
    }

    getDocumentStyle() {
        return { ...this.document.getDocumentStyle(), currentLineColor: 'var(--mde-current-line-color)' };
    }

    getParagraphStyle() {
        return { name: 'Markdown', className: 'mde-content-raw' };
    }

    getCharacterStyle() {
        return {};
    }
}

// The custom properties of the values a page gave its theme: the colors and the fonts as they are, the sizes in px
// (the base size as it is, the others scaled by the zoom as the stylesheet's em are).
function themeVariables(values) {
    const variables = {};
    for (const [key, value] of Object.entries(values)) {
        if (PROPERTY_KEYS.has(key)) variables['--mde-' + kebab(key)] = String(value);
        else if (key === 'fontSize') variables['--mde-font-size'] = `${value}px`;
        else if (key === 'listIndent' || key === 'paragraphSpacing') {
            variables['--mde-' + kebab(key)] = `calc(${value}px * var(--mde-zoom))`;
        } else if (key === 'documentWidth') {
            variables['--mde-document-width'] = value > 0 ? `calc(${value}px * var(--mde-zoom))` : 'none';
        } else if (key === 'centered') {
            variables['--mde-document-margin'] = value ? '0 auto' : '0';
        } else if (key === 'headingLines' && Array.isArray(value)) {
            value.forEach((width, i) => (variables[`--mde-h${i + 1}-line`] = `calc(${width}px * var(--mde-zoom))`));
        }
    }
    return variables;
}

const kebab = (name) => name.replace(/[A-Z]/g, (letter) => '-' + letter.toLowerCase());

// What a plain paragraph is called (the gutter's names show it): by what it is in, as MaxPad's. (One directly in a list
// item is a Bullet, a List or a Check; a "Quote item" is one in a quote and a list both.)
function plainName(quotes, lists) {
    if (quotes > 0) return lists > 0 ? 'Quote item' : 'Quote';
    return lists > 0 ? 'List item' : 'Paragraph';
}
