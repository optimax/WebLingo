// The default stylesheet of the Markdown content: MaxPad's look (its colors and proportions), as the classes the style
// provider gives the paragraphs and the runs (`mde-content-...`), in the cascade layer `mde`. A page's own stylesheet,
// outside any layer, wins over every rule of it whatever their specificity: a page restyles only what it wants
// (2026-11-03, the user: "Host only restyles what it wants").
//
// - The view's host element is `.mde-content` (with `.mde-dark` in the dark theme): the theme's values are custom
//   properties there (`--mde-heading-color`, ...), which a page may set too.
// - Sizes, margins and paddings are in em, so that the zoom, which scales the content's font size (`--mde-em`, the
//   base size times `--mde-zoom`), scales them all (the user: "use em"). A page's rule in px does not zoom.
// - The indent of lists and quotes is the same for every kind of paragraph in them, so it is in the content's em
//   (`--mde-em`), not the paragraph's own: `--mde-quotes` and `--mde-lists`, set by the depth classes, count the levels,
//   and a kind with room of its own on the left gives it as `--mde-pad-left`, which the indent is added to.

const svg = (body) => 'url("data:image/svg+xml,' +
    encodeURIComponent(`<svg xmlns='http://www.w3.org/2000/svg' viewBox='0 0 16 16'>${body}</svg>`) + '")';
const BOX = "<rect x='1.5' y='1.5' width='13' height='13' rx='2.5' fill='none' stroke='black' stroke-width='1.4'/>";
const TICK = "<path d='M3.5 8.5l3 3 6-7' fill='none' stroke='black' stroke-width='2'/>";

const MAX_DEPTH = 9; // (depth classes for the levels of quotes and of lists: deeper ones look as the ninth)

const depths = (letter, property) => Array.from({ length: MAX_DEPTH },
    (_, i) => `.mde-content-${letter}${i + 1} { ${property}: ${i + 1}; }`).join('\n');

export const MARKDOWN_CSS = `
@layer mde {
.mde-content {
  --mde-font-family: "Segoe UI", system-ui, -apple-system, Roboto, "Helvetica Neue", sans-serif;
  --mde-font-size: 16px;
  --mde-code-font-family: Consolas, "Cascadia Mono", Menlo, "DejaVu Sans Mono", monospace;
  --mde-color: #1f2328;
  --mde-background: #ffffff;
  --mde-heading-color: #203287;
  --mde-link-color: #0066cc;
  --mde-syntax-color: #808080;
  --mde-quiet-color: #6a737d;
  --mde-html-color: #8250df;
  --mde-code-background: #f5f6f8;
  --mde-code-frame-color: #dddddd;
  --mde-inline-code-background: #f0f1f3;
  --mde-quote-bar-color: #c8cdd4;
  --mde-rule-color: #2032aa;
  --mde-table-border-color: #d9dce1;
  --mde-table-header-background: #f0f2f5;
  --mde-selection-color: rgba(51, 144, 255, 0.28);
  --mde-caret-color: #000000;
  --mde-highlight-color: rgba(255, 213, 0, 0.45);
  --mde-current-line-color: #fffbdd;
  --mde-paragraph-number-color: #ab2899;
  --mde-paragraph-type-color: #137a10;
  --mde-zoom: var(--vtv-zoom, 1);
  --mde-em: calc(var(--mde-font-size) * var(--mde-zoom));
  --mde-quote-step: calc(0.875 * var(--mde-em));
  --mde-quote-bar: calc(0.1875 * var(--mde-em));
  --mde-list-indent: calc(1.75 * var(--mde-em));
  --mde-document-width: calc(45 * var(--mde-em)); /* (720 px at 16 px and 100%) */
  font-family: var(--mde-font-family);
  font-size: var(--mde-em);
  color: var(--mde-color);
}
.mde-content.mde-dark {
  --mde-color: #e6e6e6;
  --mde-background: #1e1f22;
  --mde-heading-color: #8fb4ff;
  --mde-link-color: #6cb6ff;
  --mde-syntax-color: #8b8f96;
  --mde-quiet-color: #a0a6ad;
  --mde-html-color: #c49bff;
  --mde-code-background: #2a2c30;
  --mde-code-frame-color: #3d4046;
  --mde-inline-code-background: #33363b;
  --mde-quote-bar-color: #4b5059;
  --mde-rule-color: #8fb4ff;
  --mde-table-border-color: #454950;
  --mde-table-header-background: #2c2f34;
  --mde-selection-color: rgba(90, 160, 255, 0.35);
  --mde-caret-color: #ffffff;
  --mde-highlight-color: rgba(255, 200, 0, 0.35);
  --mde-current-line-color: #2b2d24;
  --mde-paragraph-number-color: #d68bd0;
  --mde-paragraph-type-color: #6fbf6a;
}

/* The page: the text's column, as wide as the document may be (the room around the text, padding and gutter, added:
   --vtv-page-inset) and centered. A page's own rule moves it: .mde-content-page { margin: 0; } puts it at the left. */
.mde-content-page { max-width: calc(var(--mde-document-width) + var(--vtv-page-inset, 0px));
  margin: var(--mde-document-margin, 0 auto); }

/* Paragraphs: every one is .mde-content-para, and of its kind; in quotes and lists, of their depth too. */
.mde-content-para {
  font-family: var(--mde-font-family); font-size: 1em; line-height: 1.45; color: var(--mde-color);
  margin: 0 0 0.375em 0;
  padding-left: calc(var(--mde-pad-left, 0px) + var(--mde-quotes, 0) * var(--mde-quote-step)
    + var(--mde-lists, 0) * var(--mde-list-indent));
}
.mde-content-p, .mde-content-blank { margin-bottom: var(--mde-paragraph-spacing, 0.5em); }
.mde-content-image { text-align: center; margin: 0.625em 1.25em; }
.mde-content-rule {
  font-size: 0.125em; line-height: 1; margin: 6em 0; border-bottom: 1em solid var(--mde-rule-color);
}
.mde-content-heading { font-weight: 700; color: var(--mde-heading-color); line-height: 1.25; border-bottom-style: solid;
  border-bottom-color: var(--mde-heading-color); }
.mde-content-h1 { font-size: 2em; margin: 0.5625em 0 0.25em; padding-bottom: 0.09375em;
  border-bottom-width: var(--mde-h1-line, 0.09375em); }
.mde-content-h2 { font-size: 1.6875em; margin: 0.666667em 0 0.296296em; padding-bottom: 0.111111em;
  border-bottom-width: var(--mde-h2-line, 0.074074em); }
.mde-content-h3 { font-size: 1.4375em; margin: 0.608696em 0 0.347826em; padding-bottom: 0.130435em;
  border-bottom-width: var(--mde-h3-line, 0.043478em); }
.mde-content-h4 { font-size: 1.25em; margin: 0.7em 0 0.4em; padding-bottom: 0.15em;
  border-bottom-width: var(--mde-h4-line, 0.025em); }
.mde-content-h5 { font-size: 1.125em; margin: 0.777778em 0 0.444444em; border-bottom-width: var(--mde-h5-line, 0); }
.mde-content-h6 { font-size: 1.0625em; margin: 0.823529em 0 0.470588em; border-bottom-width: var(--mde-h6-line, 0); }
.mde-content-code-block, .mde-content-code-fence, .mde-content-html-block, .mde-content-definition,
.mde-content-other, .mde-content-front-matter {
  font-family: var(--mde-code-font-family); font-size: 0.875em; line-height: 1.45; margin: 0;
  background-color: var(--mde-code-background); --mde-pad-left: 0.857143em; padding-right: 0.857143em;
}
.mde-content-code-block { border: 0 solid var(--mde-code-frame-color); border-left-width: 1px; border-right-width: 1px; }
.mde-content-code-indented { border-width: 0; }
.mde-content-code-fence { color: var(--mde-quiet-color); border: 1px solid var(--mde-code-frame-color);
  border-bottom-width: 0; padding-top: 0.428571em; margin-top: 0.285714em; border-radius: 0.285714em 0.285714em 0 0; }
.mde-content-code-fence-close { border-top-width: 0; border-bottom-width: 1px; padding-top: 0; padding-bottom: 0.428571em;
  margin-top: 0; margin-bottom: 0.714286em; border-radius: 0 0 0.285714em 0.285714em; }
.mde-content-html-block { color: var(--mde-html-color); }
.mde-content-definition, .mde-content-other, .mde-content-front-matter { color: var(--mde-quiet-color); }
.mde-content-table-header, .mde-content-table-cell { margin: 0; padding-top: 0.25em; padding-right: 0.5em;
  padding-bottom: 0.25em; --mde-pad-left: 0.5em; }
.mde-content-table-header { font-weight: 700; background-color: var(--mde-table-header-background); }
.mde-content-in-quote { background-image: repeating-linear-gradient(to right, var(--mde-quote-bar-color) 0
  var(--mde-quote-bar), transparent var(--mde-quote-bar) var(--mde-quote-step));
  background-size: calc(var(--mde-quotes, 0) * var(--mde-quote-step)) 100%; background-repeat: no-repeat;
  background-position: left top; }
.mde-content-in-quote:not(.mde-content-code-block, .mde-content-code-fence, .mde-content-html-block,
  .mde-content-front-matter) { color: var(--mde-quiet-color); }
${depths('q', '--mde-quotes')}
${depths('l', '--mde-lists')}
.mde-content-raw { font-family: var(--mde-code-font-family); font-size: 0.94em; line-height: 1.5; margin: 0;
  color: var(--mde-color); }

/* Tables: the table and its cells' frames (the cells' text is a paragraph of its own, above). */
.mde-content-table { margin: 0.625em 0 0.875em; min-width: 30%; }
.mde-content-td { border: 1px solid var(--mde-table-border-color); }

/* Runs: the hyperlink's look first, so that a run's own style is over it. */
.mde-content-link { color: var(--mde-link-color); text-decoration-line: underline; }
.mde-content-code { font-family: var(--mde-code-font-family); background-color: var(--mde-inline-code-background);
  font-size: 0.92em; border-radius: 0.2em; padding: 0 0.125em; }
.mde-content-underline { text-decoration-line: underline; }
.mde-content-strike { text-decoration-line: line-through; }
.mde-content-underline.mde-content-strike { text-decoration-line: underline line-through; }
.mde-content-sub, .mde-content-sup { font-size: 0.75em; }
.mde-content-sub { vertical-align: sub; }
.mde-content-sup { vertical-align: super; }
.mde-content-html { color: var(--mde-html-color); font-family: var(--mde-code-font-family); }
.mde-content-syntax { color: var(--mde-syntax-color); font-weight: 400; font-style: normal; text-decoration-line: none; }
.mde-content-rule-syntax { color: var(--mde-syntax-color); font-size: var(--mde-em); }
.mde-content-concealed { color: transparent; }
.mde-content-task-box { font-family: var(--mde-code-font-family); color: transparent; cursor: pointer;
  background-color: var(--mde-quiet-color); -webkit-mask: ${svg(BOX)} center / 1.05em no-repeat;
  mask: ${svg(BOX)} center / 1.05em no-repeat; }
.mde-content-task-done { background-color: var(--mde-link-color); -webkit-mask-image: ${svg(BOX + TICK)};
  mask-image: ${svg(BOX + TICK)}; }
}
`;

export const STYLE_DEPTH_LIMIT = MAX_DEPTH;

let injected = false;

/** Puts the default stylesheet into the page, once, first in the head (a page's own stylesheets come after it). */
export function injectMarkdownStylesheet() {
    if (injected || typeof document === 'undefined') return;
    const style = document.createElement('style');
    style.dataset.mde = 'default';
    style.textContent = MARKDOWN_CSS;
    document.head.prepend(style);
    injected = true;
}
