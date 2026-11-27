// Where a keyboard move goes, as pure functions of a paragraph's text: the caret keys use them, so there is one
// definition of a character and of a word. (The web counterpart of the navigation helpers of VirtualTextView.Api.cs.)

const segmenter = typeof Intl !== 'undefined' && Intl.Segmenter
    ? new Intl.Segmenter(undefined, { granularity: 'grapheme' })
    : null;

/** The character that is a line break inside a paragraph (Shift+Enter): Unicode's LINE SEPARATOR, as in the control. */
export const SOFT_BREAK = '\u2028';

const isHigh = (code) => code >= 0xd800 && code <= 0xdbff;
const isLow = (code) => code >= 0xdc00 && code <= 0xdfff;

/** Where the grapheme cluster at `index` ends: a move by one character never leaves a letter without its accent. */
export function nextCharacterBoundary(text, index) {
    if (index >= text.length) return text.length;
    if (segmenter) {
        // (from a little before, so that the cluster the index is in is found whole)
        const from = Math.max(0, index - 16);
        for (const { index: at, segment } of segmenter.segment(text.slice(from, Math.min(text.length, index + 64)))) {
            const end = from + at + segment.length;
            if (end > index) return end;
        }
        return text.length;
    }
    return index + (isHigh(text.charCodeAt(index)) && isLow(text.charCodeAt(index + 1)) ? 2 : 1);
}

/** Where the grapheme cluster before `index` starts. */
export function previousCharacterBoundary(text, index) {
    if (index <= 0) return 0;
    if (segmenter) {
        const from = Math.max(0, index - 64);
        let previous = from;
        for (const { index: at } of segmenter.segment(text.slice(from, index))) previous = from + at;
        return previous;
    }
    return index - (index >= 2 && isLow(text.charCodeAt(index - 1)) && isHigh(text.charCodeAt(index - 2)) ? 2 : 1);
}

/** Where the character Backspace removes before `index` starts: a whole surrogate pair, but a combining accent alone. */
export function backspaceStart(text, index) {
    if (index <= 0) return 0;
    index = Math.min(index, text.length);
    return index >= 2 && isLow(text.charCodeAt(index - 1)) && isHigh(text.charCodeAt(index - 2)) ? index - 2 : index - 1;
}

/** Where the character Delete removes at `index` ends: a whole grapheme cluster. */
export const deleteEnd = (text, index) => nextCharacterBoundary(text, Math.max(0, index));

const WORD = /[\p{L}\p{N}_\p{M}]/u;
const SPACE = /\s/u;

function classOf(text, index) {
    const ch = text[index];
    if (SPACE.test(ch) || ch === SOFT_BREAK) return 0;
    return WORD.test(ch) ? 1 : 2;
}

/** Ctrl+Right: to the start of the next word (past the rest of this one and the spaces after it). */
export function nextWordBoundary(text, index) {
    let i = Math.min(index, text.length);
    if (i >= text.length) return text.length;
    const kind = classOf(text, i);
    if (kind !== 0) {
        while (i < text.length && classOf(text, i) === kind) i = nextCharacterBoundary(text, i);
    }
    while (i < text.length && classOf(text, i) === 0) i = nextCharacterBoundary(text, i);
    return i;
}

/** Ctrl+Left: to the start of this word, or of the one before when already at the start of one. */
export function previousWordBoundary(text, index) {
    let i = Math.min(index, text.length);
    while (i > 0 && classOf(text, previousCharacterBoundary(text, i)) === 0) i = previousCharacterBoundary(text, i);
    if (i === 0) return 0;
    const kind = classOf(text, previousCharacterBoundary(text, i));
    while (i > 0 && classOf(text, previousCharacterBoundary(text, i)) === kind) i = previousCharacterBoundary(text, i);
    return i;
}

/** The word a double-click selects: [start, end) around `index` (a run of spaces or of punctuation is a word too). */
export function wordAt(text, index) {
    if (text.length === 0) return [0, 0];
    let at = Math.min(index, text.length - 1);
    if (index === text.length || (index > 0 && classOf(text, at) === 0 && classOf(text, index - 1) !== 0)) at = index - 1;
    const kind = classOf(text, at);
    let start = at, end = at + 1;
    while (start > 0 && classOf(text, start - 1) === kind) start--;
    while (end < text.length && classOf(text, end) === kind) end++;
    return [start, end];
}

/** Whether a character is part of a word (letters, digits, '_'), as the formatting commands count one. */
export const isWordCharacter = (ch) => ch !== undefined && WORD.test(ch);
