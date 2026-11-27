// Undo and redo of the source (the web counterpart of MaxPad.Editing's EditHistory): unlimited, and of the Markdown, so
// that it is the same whatever shows it. A step is one replacement of the source -- { start, removed, inserted } -- and
// the caret and the other end of the selection before and after it. Typing a word is one step (a new word, or a caret
// that moved, starts another), and so is a run of Backspace or of Delete; what is typed over a selection goes on in the
// step that took the selection.

const isSpace = (ch) => ch === ' ' || ch === '\t' || ch === '\n';

export class EditHistory {
    constructor() {
        this.undoSteps = [];
        this.redoSteps = [];
    }

    get canUndo() {
        return this.undoSteps.length > 0;
    }

    get canRedo() {
        return this.redoSteps.length > 0;
    }

    clear() {
        this.undoSteps = [];
        this.redoSteps = [];
    }

    /** Keeps a step (joining it to the last one when it goes on with it). */
    record(step) {
        this.redoSteps = [];
        const last = this.undoSteps[this.undoSteps.length - 1];
        if (last && !last.sealed && this.merge(last, step)) return;
        this.undoSteps.push({ ...step });
    }

    /** The next step is a step of its own, whatever it is (after an undo, a click, a command of the page's). */
    seal() {
        const last = this.undoSteps[this.undoSteps.length - 1];
        if (last) last.sealed = true;
    }

    merge(last, step) {
        if (last.kind !== step.kind || last.after.caret !== step.before.caret || step.before.anchor !== step.before.caret) {
            return false;
        }
        if (step.kind === 'typing' && step.removed === '' && step.start === last.start + last.inserted.length) {
            const previous = last.inserted[last.inserted.length - 1];
            if (previous === '\n' || (isSpace(previous) && !isSpace(step.inserted[0]))) return false; // (a new word)
            last.inserted += step.inserted;
            last.after = step.after;
            return true;
        }
        if (step.kind === 'backspace' && step.inserted === '' && last.inserted === '' &&
            step.start + step.removed.length === last.start) {
            last.start = step.start;
            last.removed = step.removed + last.removed;
            last.after = step.after;
            return true;
        }
        if (step.kind === 'delete' && step.inserted === '' && last.inserted === '' && step.start === last.start) {
            last.removed += step.removed;
            last.after = step.after;
            return true;
        }
        return false;
    }

    /** The step to undo (moved to redo), or null. What is typed after it is a step of its own. */
    undo() {
        const step = this.undoSteps.pop();
        if (!step) return null;
        step.sealed = true;
        this.redoSteps.push(step);
        this.seal();
        return step;
    }

    /** The step to redo (moved back to undo), or null. */
    redo() {
        const step = this.redoSteps.pop();
        if (!step) return null;
        this.undoSteps.push(step);
        this.seal();
        return step;
    }
}

/**
 * The one replacement that makes `after` of `before`, as near `hint` as the text allows (a letter typed next to the same
 * letter is told as typed where the caret was).
 */
export function difference(before, after, hint = 0) {
    const limit = Math.min(before.length, after.length);
    let prefix = 0;
    while (prefix < limit && before.charCodeAt(prefix) === after.charCodeAt(prefix)) prefix++;
    prefix = Math.min(prefix, Math.max(0, hint));
    let suffix = 0;
    const room = Math.min(before.length, after.length) - prefix;
    const same = (k) => before.charCodeAt(before.length - 1 - k) === after.charCodeAt(after.length - 1 - k);
    while (suffix < room && same(suffix)) suffix++;
    return {
        start: prefix,
        removed: before.slice(prefix, before.length - suffix),
        inserted: after.slice(prefix, after.length - suffix),
    };
}
