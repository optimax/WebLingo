// The document of a VirtualTextView: a list of paragraphs, each a list of runs (a stretch of text and its formatting) and
// a style ID. A place in the document is { paragraph, index }: the index of a paragraph, and of a character in its text.
// (The web counterparts of VirtualParagraph, VirtualRun, DocumentPosition and TextEdit.)
//
// A run: { text, bold, italic, hidden, styleId, link: { target, title }, image: { src, alt, title } }. A run with an image
//   draws it after its text. Only `text` is required.
// A paragraph: { runs, styleId, listMarker, markerTextLength, cell: { table, row, column, align, header } }. A table's
//   cells are consecutive paragraphs, row by row, whose `cell.table` is the same object ({ columns }).

import { SOFT_BREAK } from './text.js';

export { SOFT_BREAK };

/** A position: the paragraph's index and the character's. */
export const pos = (paragraph, index) => ({ paragraph, index });

export const comparePositions = (a, b) => a.paragraph - b.paragraph || a.index - b.index;
export const samePosition = (a, b) => !!a && !!b && a.paragraph === b.paragraph && a.index === b.index;

/** A paragraph of plain text. */
export function plainParagraph(text, styleId = null) {
    return { runs: [{ text }], styleId };
}

export function paragraphText(paragraph) {
    let text = '';
    for (const run of paragraph.runs) text += run.text;
    return text;
}

export function paragraphLength(paragraph) {
    let length = 0;
    for (const run of paragraph.runs) length += run.text.length;
    return length;
}

export function cloneRun(run) {
    return { ...run };
}

/** A copy of a paragraph to keep: changing it changes nothing in the document (its table is shared, by reference). */
export function cloneParagraph(paragraph) {
    const copy = { ...paragraph, runs: paragraph.runs.map(cloneRun) };
    if (paragraph.cell) copy.cell = { ...paragraph.cell };
    delete copy._stamp;
    return copy;
}

const sameLink = (a, b) => a === b || (!!a && !!b && a.target === b.target && (a.title ?? null) === (b.title ?? null));

/** Whether two runs look the same (and so can be one). A run with a picture is never one with another. */
export function sameFormat(a, b) {
    return !a.image && !b.image && !!a.bold === !!b.bold && !!a.italic === !!b.italic && !!a.hidden === !!b.hidden &&
        (a.styleId ?? null) === (b.styleId ?? null) && sameLink(a.link, b.link);
}

/** Runs with neighbours that look the same made one, and empty ones dropped (but one is kept, to remember a format). */
export function normalizeRuns(runs) {
    const out = [];
    for (const run of runs) {
        if (run.text.length === 0 && !run.image) continue;
        const last = out[out.length - 1];
        if (last && sameFormat(last, run)) last.text += run.text;
        else out.push({ ...run });
    }
    if (out.length === 0) out.push({ ...(runs[0] ?? {}), text: '', image: undefined });
    return out;
}

/** The runs before and after `index` (a run that the index is inside of is cut in two). */
export function splitRuns(runs, index) {
    const left = [], right = [];
    let at = 0;
    for (const run of runs) {
        const end = at + run.text.length;
        if (end <= index) left.push({ ...run });
        else if (at >= index) right.push({ ...run });
        else {
            left.push({ ...run, text: run.text.slice(0, index - at), image: undefined });
            right.push({ ...run, text: run.text.slice(index - at) });
        }
        at = end;
    }
    return [left, right];
}

/**
 * The format text typed at `index` takes: that of the character before it (hidden text passed by), or at a paragraph's
 * start that of the first run. Typing at the end of a link does not make the link longer.
 */
export function formatAt(paragraph, index) {
    let at = 0, found = null, foundEnd = 0;
    for (const run of paragraph.runs) {
        if (index > 0 && at >= index) break;
        if (run.text.length && !run.hidden && (index > 0 || !found)) {
            found = run;
            foundEnd = at + run.text.length;
        }
        if (index === 0 && found) break;
        at += run.text.length;
    }
    const { text, image, ...format } = found ?? paragraph.runs[0] ?? {};
    if (format.link && foundEnd <= index) delete format.link;
    delete format.hidden;
    return format;
}

/** The text of the paragraphs from `start` to `end`, paragraphs joined by "\n". */
export function textBetween(paragraphs, start, end) {
    if (start.paragraph === end.paragraph) return paragraphText(paragraphs[start.paragraph]).slice(start.index, end.index);
    let text = paragraphText(paragraphs[start.paragraph]).slice(start.index);
    for (let p = start.paragraph + 1; p < end.paragraph; p++) text += '\n' + paragraphText(paragraphs[p]);
    return text + '\n' + paragraphText(paragraphs[end.paragraph]).slice(0, end.index);
}

/**
 * An edit as the view tells a host's recorder of it and replays it (`applyEdit`): the paragraphs from `first` that were
 * there (`before`, copies) and the ones that are there now (`after`), with the caret and the selection's other end
 * before and after. The text removed and inserted are there for a host that wants them. `inverse()` undoes it.
 */
export class TextEdit {
    constructor({ first, before, after, caretBefore, caretAfter, anchorBefore = null, anchorAfter = null, kind = 'edit',
        removedText = '', insertedText = '' }) {
        Object.assign(this, { first, before, after, caretBefore, caretAfter, anchorBefore, anchorAfter, kind, removedText,
            insertedText });
    }

    inverse() {
        return new TextEdit({
            first: this.first, before: this.after, after: this.before, caretBefore: this.caretAfter,
            caretAfter: this.caretBefore, anchorBefore: this.anchorAfter, anchorAfter: this.anchorBefore,
            kind: this.kind, removedText: this.insertedText, insertedText: this.removedText,
        });
    }
}
