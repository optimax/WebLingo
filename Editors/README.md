# Editors

Syntax colouring for WebLingo's files in editors: `.wl`, WebLingo's scripts and modules, and `.wasp`, WASP's pages.

## Notepad++

`NotepadPlusPlus/` holds two user-defined languages, which Notepad++ applies by a file's extension:

| File | Language | Files |
|---|---|---|
| `WebLingo.udl.xml` | WebLingo | `.wl` |
| `WASP.udl.xml` | WASP | `.wasp` |

To install them, in Notepad++, choose **Language → User Defined Language → Define your language…**, then **Import…**,
once for each file; or copy both files into `%APPDATA%\Notepad++\userDefineLangs` and restart Notepad++. A file opened
afterwards is coloured by its extension; one already open, by choosing its language from the **Language** menu.

What is coloured:

- **In WebLingo:** its keywords, `true`, `false`, `null`, `Infinity` and `NaN`, the contextual words (`from`, `in`,
  `get`, `set`, `property`, `field`, `operator`, `constructor`, `destructor`), the built-in classes and functions, strings
  of every kind (`"…"`, `'…'`, `"""…"""` and `'''…'''`), numbers, comments, and operators; and a block between `{` and
  `}` can be folded.
- **In WASP:** the WebLingo code in a page's blocks, `<% … %>`, `<%= … %>` and `<%- … %>`, as above, `include` and
  `layout` among its keywords, and `renderComponent` and `renderBody` among its functions; and, outside the blocks,
  HTML's tags, their attributes' values, and HTML's comments, its text left as it is, so that a word of the text is never
  taken for a keyword.

Their limits, which come from what a user-defined language of Notepad++ can tell apart:

- **A template's comment, `<%# … %>`, is coloured as code**, not as a comment.
- **A page that begins with HTML** is seen as HTML from its first tag, if that tag is one that begins a page, an include
  or a component (`<!DOCTYPE`, `<html>`, `<header>`, `<footer>`, `<div>`, `<section>`, `<p>` and the like); a page that
  begins otherwise is coloured as code until its first block. A page that begins with a block, as most do, is not
  affected.
- **In a block, `<` written right before a letter** (`i<n`) begins what looks like a tag, up to the next `>`; written
  with spaces, as WebLingo is usually written (`i < n`), it does not.
- **The colours are for a light background.** With Notepad++'s dark mode, change them in **Language → User Defined
  Language → Define your language…**, each language's **Styler** buttons.

They were tried with Notepad++ 8.9.8, on Windows.
