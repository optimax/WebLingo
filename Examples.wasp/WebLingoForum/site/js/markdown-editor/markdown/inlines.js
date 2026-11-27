// The inlines of a block's text, by CommonMark's algorithm (the delimiter stack for emphasis, the bracket stack for links),
// with Markdig's extras that MaxPad uses: ==underline== (MaxPad's own reading of Markdig's "marked"), ~~strikethrough~~,
// ~subscript~ and ^superscript^. Every node is [start, end) of the text it was given, its syntax included:
//
//   text, entity (as text), code { contentStart, contentEnd }, escape (the backslash and its character), html,
//   autolink { contentStart, contentEnd, destination }, softbreak (the line ending), hardbreak (the backslash or the
//   spaces before the line ending, and the line ending), em / strong / underline / strike / sub / sup { delimiter,
//   children }, link / image { textStart, textEnd, destination, title, children }.

const TAGNAME = '[A-Za-z][A-Za-z0-9-]*';
const ATTRIBUTENAME = '[a-zA-Z_:][a-zA-Z0-9:._-]*';
const ATTRIBUTEVALUE = '(?:[^"\'=<>`\\x00-\\x20]+|\'[^\']*\'|"[^"]*")';
const ATTRIBUTE = '(?:\\s+' + ATTRIBUTENAME + '(?:\\s*=\\s*' + ATTRIBUTEVALUE + ')?)';
export const OPENTAG = '<' + TAGNAME + ATTRIBUTE + '*\\s*/?>';
export const CLOSETAG = '</' + TAGNAME + '\\s*[>]';

const reEscapable = /^[!"#$%&'()*+,./:;<=>?@[\\\]^_`{|}~-]/;
const rePunctuation = /^[!"#$%&'()*+,\-./:;<=>?@[\\\]^_`{|}~\p{P}\p{S}]/u;
const reWhitespace = /^\s/u;
const reText = /^[^\n`[\]\\!<&*_~^=]+/;
const reAutolink = /^<[A-Za-z][A-Za-z0-9.+-]{1,31}:[^<>\x00-\x20]*>/;
const DOMAIN_LABEL = '[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?';
const reEmailAutolink = new RegExp("^<[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@" + DOMAIN_LABEL + '(?:\\.' + DOMAIN_LABEL + ')*>');
const reHtmlTag = new RegExp('^(?:' + OPENTAG + '|' + CLOSETAG + '|<!-->|<!--->|<!--[\\s\\S]*?-->|[<][?][\\s\\S]*?[?][>]|' +
    '<![A-Za-z]+[^>]*>|<!\\[CDATA\\[[\\s\\S]*?\\]\\]>)', 'i');
const reEntity = /^&(?:#[xX][a-f0-9]{1,6}|#[0-9]{1,7}|[a-z][a-z0-9]{1,31});/i;

/** A label as two labels that match compare: case folded, its white space made single spaces. */
export function normalizeLabel(label) {
    return label.trim().replace(/[ \t\r\n]+/g, ' ').toLowerCase().toUpperCase();
}

const unescape = (text) => text.replace(/\\([!"#$%&'()*+,./:;<=>?@[\\\]^_`{|}~-])/g, '$1');

/** A link's destination at `i` (`<...>`, or text with balanced parentheses): `{ destination, end }`, or null. */
export function parseLinkDestination(text, i) {
    if (text[i] === '<') {
        for (let j = i + 1; j < text.length; j++) {
            const ch = text[j];
            if (ch === '\\' && reEscapable.test(text[j + 1] ?? '')) j++;
            else if (ch === '>') return { destination: unescape(text.slice(i + 1, j)), end: j + 1 };
            else if (ch === '<' || ch === '\n') return null;
        }
        return null;
    }
    let j = i, depth = 0;
    while (j < text.length) {
        const ch = text[j];
        if (ch === '\\' && reEscapable.test(text[j + 1] ?? '')) {
            j += 2;
            continue;
        }
        if (ch === '(') {
            if (++depth > 32) return null;
        } else if (ch === ')') {
            if (depth === 0) break;
            depth--;
        } else if (/[\s\x00-\x1f]/.test(ch)) {
            break;
        }
        j++;
    }
    if (depth !== 0 || (j === i && text[j] !== ')')) return null;
    return { destination: unescape(text.slice(i, j)), end: j };
}

/** A link's title at `i` ("...", '...' or (...)): `{ title, end }`, or null. */
export function parseLinkTitle(text, i) {
    const open = text[i];
    const close = open === '(' ? ')' : open;
    if (open !== '"' && open !== "'" && open !== '(') return null;
    for (let j = i + 1; j < text.length; j++) {
        const ch = text[j];
        if (ch === '\\' && reEscapable.test(text[j + 1] ?? '')) j++;
        else if (ch === close) return { title: unescape(text.slice(i + 1, j)), end: j + 1 };
        else if (open === '(' && ch === '(') return null;
    }
    return null;
}

// The length of a link label at `i` ("[...]", up to 999 characters, no unescaped bracket inside), or 0.
function parseLinkLabel(text, i) {
    if (text[i] !== '[') return 0;
    for (let j = i + 1; j < text.length && j - i <= 1000; j++) {
        const ch = text[j];
        if (ch === '\\' && reEscapable.test(text[j + 1] ?? '')) j++;
        else if (ch === '[') return 0;
        else if (ch === ']') return j - i + 1;
    }
    return 0;
}

class Node {
    constructor(type, start, end) {
        this.type = type;
        this.start = start;
        this.end = end;
        this.parent = null;
        this.prev = null;
        this.next = null;
        this.firstChild = null;
        this.lastChild = null;
    }

    appendChild(child) {
        child.unlink();
        child.parent = this;
        if (this.lastChild) {
            this.lastChild.next = child;
            child.prev = this.lastChild;
        } else {
            this.firstChild = child;
        }
        this.lastChild = child;
    }

    insertAfter(sibling) {
        sibling.unlink();
        sibling.next = this.next;
        if (sibling.next) sibling.next.prev = sibling;
        sibling.prev = this;
        this.next = sibling;
        sibling.parent = this.parent;
        if (this.parent && this.parent.lastChild === this) this.parent.lastChild = sibling;
    }

    unlink() {
        if (this.prev) this.prev.next = this.next;
        else if (this.parent) this.parent.firstChild = this.next;
        if (this.next) this.next.prev = this.prev;
        else if (this.parent) this.parent.lastChild = this.prev;
        this.parent = this.prev = this.next = null;
    }
}

/** The inlines of `text`, link reference definitions from `definitions` (a Map of normalized labels). */
export function parseInlines(text, definitions) {
    return new InlineParser(text, definitions ?? new Map()).parse();
}

class InlineParser {
    constructor(text, definitions) {
        this.text = text;
        this.definitions = definitions;
    }

    parse() {
        this.pos = 0;
        this.root = new Node('root', 0, this.text.length);
        this.delimiters = null;
        this.brackets = null;
        while (this.pos < this.text.length) this.parseInline();
        this.processEmphasis(null);
        return finish(this.root);
    }

    add(type, start, end) {
        const node = new Node(type, start, end);
        this.root.appendChild(node);
        return node;
    }

    text_(start, end) {
        return this.add('text', start, end);
    }

    parseInline() {
        const text = this.text, start = this.pos, ch = text[start];
        switch (ch) {
            case '\n':
                this.parseNewline();
                return;
            case '\\':
                if (text[start + 1] === '\n') {
                    this.add('hardbreak', start, start + 2);
                    this.pos += 2;
                } else if (reEscapable.test(text[start + 1] ?? '')) {
                    this.add('escape', start, start + 2);
                    this.pos += 2;
                } else {
                    this.text_(start, ++this.pos);
                }
                return;
            case '`':
                this.parseBackticks();
                return;
            case '*': case '_': case '~': case '^': case '=':
                this.parseDelimiters(ch);
                return;
            case '[':
                this.addBracket(this.text_(start, ++this.pos), false);
                return;
            case '!':
                if (text[start + 1] === '[') {
                    this.pos += 2;
                    this.addBracket(this.text_(start, this.pos), true);
                } else {
                    this.text_(start, ++this.pos);
                }
                return;
            case ']':
                this.parseCloseBracket();
                return;
            case '<':
                this.parseAngle();
                return;
            case '&': {
                const match = text.slice(start).match(reEntity);
                this.pos += match ? match[0].length : 1;
                this.text_(start, this.pos);
                return;
            }
            default: {
                const match = text.slice(start).match(reText);
                this.pos += match ? match[0].length : 1;
                this.text_(start, this.pos);
            }
        }
    }

    // A line ending: a hard break when two spaces or more end the line (they are the break's), else a soft one.
    parseNewline() {
        const at = this.pos;
        let spaces = 0;
        while (at - spaces - 1 >= 0 && this.text[at - spaces - 1] === ' ') spaces++;
        const last = this.root.lastChild;
        if (spaces >= 2 && last && last.type === 'text' && last.end === at && last.end - last.start >= spaces) {
            last.end -= spaces;
            if (last.end === last.start) last.unlink();
            this.add('hardbreak', at - spaces, at + 1);
        } else {
            this.add('softbreak', at, at + 1);
        }
        this.pos++;
    }

    parseBackticks() {
        const text = this.text, start = this.pos;
        let n = 0;
        while (text[start + n] === '`') n++;
        const afterOpen = start + n;
        for (let i = afterOpen; i < text.length; i++) {
            if (text[i] !== '`') continue;
            let run = 0;
            while (text[i + run] === '`') run++;
            if (run === n) {
                const node = this.add('code', start, i + run);
                node.contentStart = afterOpen;
                node.contentEnd = i;
                this.pos = i + run;
                return;
            }
            i += run - 1;
        }
        this.pos = afterOpen;
        this.text_(start, afterOpen);
    }

    parseAngle() {
        const rest = this.text.slice(this.pos);
        const start = this.pos;
        let match;
        if ((match = rest.match(reAutolink)) || (match = rest.match(reEmailAutolink))) {
            const node = this.add('autolink', start, start + match[0].length);
            node.contentStart = start + 1;
            node.contentEnd = node.end - 1;
            const address = match[0].slice(1, -1);
            node.destination = rest.match(reAutolink) ? address : 'mailto:' + address;
            this.pos = node.end;
        } else if ((match = rest.match(reHtmlTag))) {
            this.add('html', start, start + match[0].length);
            this.pos += match[0].length;
        } else {
            this.text_(start, ++this.pos);
        }
    }

    // ---- Emphasis ------------------------------------------------------------------------------------------------

    parseDelimiters(ch) {
        const text = this.text, start = this.pos;
        let n = 0;
        while (text[start + n] === ch) n++;
        this.pos += n;
        if (ch === '=' && n < 2) {
            this.text_(start, this.pos); // (one "=" is text)
            return;
        }
        const before = start === 0 ? '\n' : text[start - 1];
        const after = start + n < text.length ? text[start + n] : '\n';
        const afterSpace = reWhitespace.test(after), afterPunctuation = rePunctuation.test(after);
        const beforeSpace = reWhitespace.test(before), beforePunctuation = rePunctuation.test(before);
        const leftFlanking = !afterSpace && (!afterPunctuation || beforeSpace || beforePunctuation);
        const rightFlanking = !beforeSpace && (!beforePunctuation || afterSpace || afterPunctuation);
        let canOpen, canClose;
        if (ch === '_') {
            canOpen = leftFlanking && (!rightFlanking || beforePunctuation);
            canClose = rightFlanking && (!leftFlanking || afterPunctuation);
        } else {
            canOpen = leftFlanking;
            canClose = rightFlanking;
        }
        const node = this.add('delim', start, this.pos);
        const delimiter = {
            ch, count: n, original: n, node, canOpen, canClose, previous: this.delimiters, next: null,
        };
        if (this.delimiters) this.delimiters.next = delimiter;
        this.delimiters = delimiter;
    }

    removeDelimiter(delimiter) {
        if (delimiter.previous) delimiter.previous.next = delimiter.next;
        if (delimiter.next) delimiter.next.previous = delimiter.previous;
        else this.delimiters = delimiter.previous;
    }

    processEmphasis(stackBottom) {
        let closer = this.delimiters;
        while (closer && closer.previous !== stackBottom) closer = closer.previous;
        // (commonmark.js's openers bottom: for each kind of closer, the delimiter below which no opener was found for
        // one of that kind, so that no search is made twice -- else a paragraph of openers and closers that never meet
        // is searched in a time that grows with its square)
        const bottoms = new Map();
        while (closer) {
            if (!closer.canClose) {
                closer = closer.next;
                continue;
            }
            const kind = bottomKind(closer);
            const bottom = kind !== null && bottoms.has(kind) ? bottoms.get(kind) : stackBottom;
            let opener = closer.previous, found = false;
            while (opener && opener !== stackBottom && opener !== bottom) {
                if (opener.ch === closer.ch && opener.canOpen && matches(opener, closer)) {
                    found = true;
                    break;
                }
                opener = opener.previous;
            }
            const oldCloser = closer;
            if (found) {
                const use = usedOf(opener, closer);
                const emphasisStart = opener.node.end - use, emphasisEnd = closer.node.start + use;
                opener.count -= use;
                closer.count -= use;
                opener.node.end = emphasisStart;
                closer.node.start = emphasisEnd;
                const emphasis = new Node(typeOf(closer.ch, use), emphasisStart, emphasisEnd);
                emphasis.delimiter = use;
                let node = opener.node.next;
                while (node && node !== closer.node) {
                    const next = node.next;
                    emphasis.appendChild(node);
                    node = next;
                }
                opener.node.insertAfter(emphasis);
                if (opener.next !== closer) {
                    opener.next = closer;
                    closer.previous = opener;
                }
                if (opener.count === 0) {
                    opener.node.unlink();
                    this.removeDelimiter(opener);
                }
                if (closer.count === 0) {
                    closer.node.unlink();
                    const next = closer.next;
                    this.removeDelimiter(closer);
                    closer = next;
                }
            } else {
                if (kind !== null) bottoms.set(kind, oldCloser.previous);
                closer = closer.next;
                if (!oldCloser.canOpen) this.removeDelimiter(oldCloser);
            }
        }
        while (this.delimiters && this.delimiters !== stackBottom) this.removeDelimiter(this.delimiters);
    }

    // ---- Links ---------------------------------------------------------------------------------------------------

    addBracket(node, image) {
        if (this.brackets) this.brackets.bracketAfter = true;
        this.brackets = { node, image, active: true, previous: this.brackets, previousDelimiter: this.delimiters };
    }

    parseCloseBracket() {
        const text = this.text, startpos = this.pos;
        this.pos++;
        const opener = this.brackets;
        if (!opener) {
            this.text_(startpos, this.pos);
            return;
        }
        if (!opener.active) {
            this.text_(startpos, this.pos);
            this.brackets = opener.previous;
            return;
        }
        const savepos = this.pos;
        let matched = false, destination = null, title = null;
        if (text[this.pos] === '(') {
            let i = skipSpace(text, this.pos + 1);
            const parsed = parseLinkDestination(text, i);
            if (parsed) {
                i = parsed.end;
                const beforeTitle = i;
                i = skipSpace(text, i);
                if (i > beforeTitle) {
                    const titled = parseLinkTitle(text, i);
                    if (titled) {
                        title = titled.title;
                        i = skipSpace(text, titled.end);
                    }
                }
                if (text[i] === ')') {
                    destination = parsed.destination;
                    this.pos = i + 1;
                    matched = true;
                }
            }
        }
        if (!matched) {
            const beforeLabel = this.pos;
            const n = parseLinkLabel(text, beforeLabel);
            let label = null;
            if (n > 2) label = text.slice(beforeLabel + 1, beforeLabel + n - 1);
            else if (!opener.bracketAfter) label = text.slice(opener.node.end, startpos);
            if (label !== null) {
                const definition = this.definitions.get(normalizeLabel(label));
                if (definition) {
                    destination = definition.destination;
                    title = definition.title;
                    matched = true;
                    this.pos = n > 0 ? beforeLabel + n : savepos;
                }
            }
            if (!matched) this.pos = savepos;
        }
        if (!matched) {
            this.brackets = opener.previous;
            this.pos = startpos + 1;
            this.text_(startpos, this.pos);
            return;
        }
        const link = new Node(opener.image ? 'image' : 'link', opener.node.start, this.pos);
        link.textStart = opener.node.end;
        link.textEnd = startpos;
        link.destination = destination;
        link.title = title;
        let node = opener.node.next;
        while (node) {
            const next = node.next;
            link.appendChild(node);
            node = next;
        }
        this.root.appendChild(link);
        this.processEmphasis(opener.previousDelimiter);
        this.brackets = opener.previous;
        opener.node.unlink();
        if (!opener.image) {
            for (let earlier = this.brackets; earlier; earlier = earlier.previous) {
                if (!earlier.image) earlier.active = false; // (no link in a link)
            }
        }
    }
}

// Whether `opener` and `closer` make emphasis: the rule of three for * and _, two of each for == (and ~~ when either has
// one only makes ~subscript~).
function matches(opener, closer) {
    if (closer.ch === '*' || closer.ch === '_') {
        const odd = (closer.canOpen || opener.canClose) && closer.original % 3 !== 0 &&
            (opener.original + closer.original) % 3 === 0;
        return !odd;
    }
    if (closer.ch === '=') return opener.count >= 2 && closer.count >= 2;
    return true;
}

// The kind of a closer for the openers' bottom: closers of one kind are matched by the same openers, so that where none
// was found for one, none is for another. * and _ by the rule of three (whether the closer can open, and its length
// modulo 3); ~ and ^ by their character; == only while it has two left to use (an opener with fewer than two never has
// more again), null else: no bottom.
function bottomKind(closer) {
    switch (closer.ch) {
        case '*':
        case '_':
            return closer.ch + (closer.canOpen ? 3 : 0) + (closer.original % 3);
        case '=':
            return closer.count >= 2 ? '=' : null;
        default:
            return closer.ch;
    }
}

function usedOf(opener, closer) {
    switch (closer.ch) {
        case '^': return 1;
        case '=': return 2;
        default: return opener.count >= 2 && closer.count >= 2 ? 2 : 1;
    }
}

function typeOf(ch, used) {
    switch (ch) {
        case '~': return used === 2 ? 'strike' : 'sub';
        case '^': return 'sup';
        case '=': return 'underline';
        default: return used === 2 ? 'strong' : 'em';
    }
}

function skipSpace(text, i) {
    while (text[i] === ' ' || text[i] === '\t') i++;
    if (text[i] === '\n') {
        i++;
        while (text[i] === ' ' || text[i] === '\t') i++;
    }
    return i;
}

// The tree as plain objects: a delimiter or a bracket that made nothing is text, and neighbouring texts are one.
function finish(container) {
    const out = [];
    for (let node = container.firstChild; node; node = node.next) {
        if (node.end <= node.start && node.type !== 'link' && node.type !== 'image') continue;
        const type = node.type === 'delim' ? 'text' : node.type;
        const last = out[out.length - 1];
        if (type === 'text' && last && last.type === 'text' && last.end === node.start) {
            last.end = node.end;
            continue;
        }
        const plain = { type, start: node.start, end: node.end };
        for (const key of ['contentStart', 'contentEnd', 'textStart', 'textEnd', 'destination', 'title', 'delimiter']) {
            if (node[key] !== undefined) plain[key] = node[key];
        }
        if (node.firstChild || type === 'link' || type === 'image' || isEmphasis(type)) plain.children = finish(node);
        out.push(plain);
    }
    return out;
}

export const isEmphasis = (type) =>
    type === 'em' || type === 'strong' || type === 'underline' || type === 'strike' || type === 'sub' || type === 'sup';
