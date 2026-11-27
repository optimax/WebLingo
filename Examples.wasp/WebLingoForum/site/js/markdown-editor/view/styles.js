// What the view needs of a host's stylesheet (the web counterpart of IStyleProvider, ParagraphStyle and CharacterStyle):
// a style provider answers for each style ID, the view asks once per ID and keeps the answer until the host calls
// `invalidateStyles()`. The styles are plain objects; a property left out has its default (a paragraph style) or no
// opinion (a character style, which overrides only what it sets).
//
// ParagraphStyle: { name, fontFamily, fontSize (px), bold, italic, color, background, underline, strikethrough,
//   lineHeight (a multiple of the font size), textAlign, margin, padding, borderWidth, borderColor, borderStyle,
//   borderRadius (each of the four a number or [top, right, bottom, left]), firstLineIndent, nextStyleId, hidden,
//   css (more CSS for the paragraph's box, as { property: value }) } -- or { name, className, nextStyleId, hidden }:
//   the look is then the host's stylesheet's, for the class, and the rest is not used.
// CharacterStyle: { fontFamily, fontSize, bold, italic, color, background, underline, strikethrough,
//   script ('sub' | 'super'), hidden, css, className (classes of the host's stylesheet, beside the rest) }
// DocumentStyle: { className (classes the view's host element gets: what the stylesheet's rules for the content are
//   under), variables (CSS custom properties set on it, { '--name': value }), pageClassName (classes of the page, the
//   text's column: its max-width and margin are then the stylesheet's, which --vtv-page-inset, the room around the text,
//   helps with), maxWidth, placement ('left' | 'center'; both without a pageClassName),
//   background, color, selectionColor, caretColor,
//   highlightColor, currentLineColor (a band behind the caret's line; none by default), gutter: { fontFamily,
//   fontSize, bold, color (the numbers'), namesColor (the style names'; the numbers' color by default), background,
//   borderColor (a line between the gutter and the text) } }

export const DEFAULT_PARAGRAPH_STYLE = Object.freeze({
    name: '', fontFamily: 'system-ui, -apple-system, "Segoe UI", Roboto, sans-serif', fontSize: 16, bold: false,
    italic: false, color: '#1f2328', background: null, underline: false, strikethrough: false, lineHeight: 1.45,
    textAlign: 'left', margin: 0, padding: 0, borderWidth: 0, borderColor: 'transparent', borderStyle: 'solid',
    borderRadius: 0, firstLineIndent: 0, nextStyleId: undefined, hidden: false, css: null,
});

export const DEFAULT_LINK_STYLE = Object.freeze({ color: '#0563c1', underline: true });

export const DEFAULT_DOCUMENT_STYLE = Object.freeze({
    maxWidth: Infinity, placement: 'left', background: '#ffffff', color: '#1f2328',
    selectionColor: 'rgba(51, 144, 255, 0.28)', caretColor: '#000000', highlightColor: 'rgba(255, 213, 0, 0.45)',
    currentLineColor: null,
    gutter: Object.freeze({
        fontFamily: 'system-ui, -apple-system, "Segoe UI", sans-serif', fontSize: 12, bold: false, color: '#808080',
        namesColor: null, background: null, borderColor: null,
    }),
});

/** Four sides of a thickness given as a number or [top, right, bottom, left] (CSS's order). */
export function sides(value) {
    if (Array.isArray(value)) {
        const valid = value.map((side) => (Number.isFinite(side) ? Math.max(0, side) : 0));
        const [top = 0, right = top, bottom = top, left = right] = valid;
        return [top, right, bottom, left];
    }
    const side = Number.isFinite(value) ? Math.max(0, value) : 0;
    return [side, side, side, side];
}

const px = (values) => values.map((value) => `${value}px`).join(' ');
const cssName = (property) => property.replace(/[A-Z]/g, (letter) => '-' + letter.toLowerCase());

function extraCss(css) {
    if (!css) return '';
    return Object.entries(css).map(([property, value]) => `${cssName(property)}:${value};`).join('');
}

function decorations(underline, strikethrough) {
    const lines = [];
    if (underline) lines.push('underline');
    if (strikethrough) lines.push('line-through');
    return lines.length ? lines.join(' ') : 'none';
}

/**
 * A paragraph style resolved to what the view draws: the CSS of the outer element (the margin, as padding, so that
 * margins add and never collapse) and of the box (font, colors, border, padding), and the numbers layout estimates with.
 *
 * A style with a `className` is the host's stylesheet's: the box gets the class and no CSS of its own, and the numbers
 * are read from the box as the browser styles it (`computed`, a CSSStyleDeclaration's values, read by the view off a
 * probe of the class; its margins are the box's own, which the outer element, a block formatting context, holds in).
 */
export class ResolvedParagraphStyle {
    constructor(style, zoom = 1, computed = null) {
        const s = { ...DEFAULT_PARAGRAPH_STYLE, ...(style ?? {}) };
        this.style = s;
        this.className = typeof s.className === 'string' ? s.className.trim() : '';
        if (computed) {
            this._fromComputed(computed);
            return;
        }
        const fontSize = s.fontSize * zoom;
        const margin = sides(s.margin).map((side) => side * zoom);
        const padding = sides(s.padding).map((side) => side * zoom);
        const border = sides(s.borderWidth).map((side) => side * zoom);
        this.fontSize = fontSize;
        this.lineHeight = Math.max(1, fontSize * s.lineHeight);
        this.margin = margin;
        this.padding = padding;
        this.border = border;
        this.vertical = margin[0] + margin[2] + padding[0] + padding[2] + border[0] + border[2];
        this.horizontal = margin[1] + margin[3] + padding[1] + padding[3] + border[1] + border[3];
        this.hidden = !!s.hidden;
        this.outerCss = `padding:${px(margin)};`;
        this.boxCss =
            `font-family:${s.fontFamily};font-size:${fontSize}px;line-height:${s.lineHeight};` +
            `font-weight:${s.bold ? 700 : 400};font-style:${s.italic ? 'italic' : 'normal'};color:${s.color};` +
            `text-decoration:${decorations(s.underline, s.strikethrough)};text-align:${s.textAlign};` +
            `padding:${px(padding)};border-width:${px(border)};border-style:${s.borderStyle};` +
            `border-color:${s.borderColor};` +
            (s.borderRadius ? `border-radius:${px(sides(s.borderRadius).map((r) => r * zoom))};` : '') +
            (s.background ? `background:${s.background};` : '') +
            (s.firstLineIndent ? `text-indent:${s.firstLineIndent * zoom}px;` : '') +
            extraCss(s.css);
        this.firstLineIndent = (s.firstLineIndent || 0) * zoom;
        this.drawn = border.some((side) => side > 0) || !!s.background; // (a box that can be seen with nothing in it)
        this.fontCss = `font-family:${s.fontFamily};font-size:${fontSize}px;line-height:${s.lineHeight};` +
            `font-weight:${s.bold ? 700 : 400};font-style:${s.italic ? 'italic' : 'normal'};color:${s.color};`;
    }

    _fromComputed(c) {
        const n = (value) => parseFloat(value) || 0;
        const fontSize = n(c.fontSize);
        this.fontSize = fontSize;
        this.lineHeight = c.lineHeight === 'normal' || !c.lineHeight ? fontSize * 1.2 : Math.max(1, n(c.lineHeight));
        this.margin = [n(c.marginTop), n(c.marginRight), n(c.marginBottom), n(c.marginLeft)];
        this.padding = [n(c.paddingTop), n(c.paddingRight), n(c.paddingBottom), n(c.paddingLeft)];
        this.border = [n(c.borderTopWidth), n(c.borderRightWidth), n(c.borderBottomWidth), n(c.borderLeftWidth)];
        const [margin, padding, border] = [this.margin, this.padding, this.border];
        this.vertical = margin[0] + margin[2] + padding[0] + padding[2] + border[0] + border[2];
        this.horizontal = margin[1] + margin[3] + padding[1] + padding[3] + border[1] + border[3];
        this.hidden = !!this.style.hidden;
        this.outerCss = '';
        this.boxCss = '';
        this.firstLineIndent = n(c.textIndent);
        const clear = !c.backgroundColor || /^(transparent|rgba\(0, 0, 0, 0\))$/.test(c.backgroundColor);
        this.drawn = this.border.some((side) => side > 0) || !clear || (c.backgroundImage ?? 'none') !== 'none';
        this.fontCss = `font-family:${c.fontFamily};font-size:${c.fontSize};line-height:${c.lineHeight};` +
            `font-weight:${c.fontWeight};font-style:${c.fontStyle};color:${c.color};`;
    }
}

/** What `ResolvedParagraphStyle` reads of a box the host's stylesheet styles (copied: a computed style is live). */
export const COMPUTED_PROPERTIES = ['fontSize', 'lineHeight', 'marginTop', 'marginRight', 'marginBottom', 'marginLeft',
    'paddingTop', 'paddingRight', 'paddingBottom', 'paddingLeft', 'borderTopWidth', 'borderRightWidth',
    'borderBottomWidth', 'borderLeftWidth', 'textIndent', 'backgroundColor', 'backgroundImage', 'fontFamily',
    'fontWeight', 'fontStyle', 'color'];

/** A character style resolved to the CSS of a run (only what the style has an opinion of). */
export function characterCss(style, zoom = 1) {
    if (!style) return '';
    let css = '';
    if (style.fontFamily) css += `font-family:${style.fontFamily};`;
    if (Number.isFinite(style.fontSize)) css += `font-size:${style.fontSize * zoom}px;`;
    if (style.bold !== undefined && style.bold !== null) css += `font-weight:${style.bold ? 700 : 400};`;
    if (style.italic !== undefined && style.italic !== null) css += `font-style:${style.italic ? 'italic' : 'normal'};`;
    if (style.color) css += `color:${style.color};`;
    if (style.background) css += `background:${style.background};`;
    if (style.underline !== undefined || style.strikethrough !== undefined) {
        css += `text-decoration:${decorations(style.underline, style.strikethrough)};`;
    }
    if (style.script === 'sub' || style.script === 'super') css += `vertical-align:${style.script};font-size:0.75em;`;
    css += extraCss(style.css);
    return css;
}
