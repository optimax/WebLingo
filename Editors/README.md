# Editors

Syntax colouring for WebLingo's files in editors: `.wlg`, WebLingo's scripts and modules, and `.wasp`, WASP's pages.

## Notepad++

`NotepadPlusPlus/` holds two user-defined languages, which Notepad++ applies by a file's extension:

| File | Language | Files |
|---|---|---|
| `WebLingo.udl.xml` | WebLingo | `.wlg` |
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

## VS Code

`VSCode/` is an extension of VS Code, in source, which gives it two languages, WebLingo, for `.wlg` files, and WASP, for
`.wasp` files, each with its TextMate grammar (`weblingo.tmLanguage.json` and `wasp.tmLanguage.json`) and its language
configuration (comments, brackets, and the pairs VS Code closes as you type).

On Windows, use **Install WebLingo.bat** in the extracted WebLingo ZIP and choose to enable debugging in VS Code.
For an existing WebLingo installation made from that ZIP, you can also choose **Extensions → … → Install from VSIX…**
and select `VSCode\WebLingo.vsix` in WebLingo's installation folder.

To work on the extension from source, in VS Code, open the Command Palette (**Ctrl+Shift+P**), run **Developer: Install Extension from
Location…**, and choose the `VSCode` folder. VS Code uses the folder where it is, so keep it there; a change to it, a
`git pull` for one, is used once VS Code reloads its window. To remove it, uninstall **WebLingo** in the Extensions view.

What is coloured:

- **In WebLingo:** its keywords and declarations, the names a declaration gives (a class's, a function's, a property's),
  `true`, `false`, `null`, `Infinity` and `NaN`, the built-in classes and functions, calls, strings of every kind, their
  escapes and their interpolations (`$"…{x}…"`, `$"""…{x}…"""` and `$$"""…{{x}}…"""`), numbers, comments, and
  operators, each with the scopes VS Code's themes know, so that a theme colours WebLingo as it colours JavaScript.
- **In WASP:** a page's HTML, by VS Code's own grammar for HTML, its scripts and styles included; and the WebLingo code
  in its blocks, `<% … %>`, `<%= … %>` and `<%- … %>`, wherever they are, in a tag, in an attribute's value or in a
  script too, `include` and `layout` among its keywords; and a template's comment, `<%# … %>`, as a comment. A `%>` in
  a string or a comment of a block does not end the block, as it does not for WASP.

In a `.wasp` file, **Toggle Block Comment** (**Shift+Alt+A**) writes a template's comment, `<%# … %>`, and typing `<%`
adds its `%>`. Emmet's abbreviations for HTML work in pages once `"emmet.includeLanguages": { "wasp": "html" }` is in
VS Code's settings.

Its colouring was tried with VS Code 1.146, on Windows.

### Debug a WebLingo script

On Windows with the .NET 10 SDK installed, double-click [build.bat](../build.bat) in the repository root and approve
the Windows administrator prompt. It publishes WebLingo and WASP together and updates the local installation.
With `C:\Program Files\WebLingo` on PATH, the extension's default debugger setting uses the installed executable.

Use a build of `weblingo` that includes the debug adapter. Put it on PATH or set **WebLingo: Debugger Path**
(`weblingo.debuggerPath`) in VS Code's user settings to the executable's full path. A Release build from this
repository puts the executable in `WebLingo/src/WebLingo.Cli/bin/Release/net10.0/`.

In a trusted workspace, open a local `.wlg` file, click the gutter next to a statement to set a breakpoint,
and press **F5**. Choose **WebLingo** if VS Code asks for a debugger. Use the debug toolbar to step or continue.
The **Variables** and **Call Stack** views show the current state, and the **Debug Console** shows script output.

Use a `.vscode/launch.json` configuration to pass script parameters or choose a working directory.

```json
{
  "version": "0.2.0",
  "configurations": [
    {
      "type": "weblingo",
      "request": "launch",
      "name": "Debug WebLingo Script",
      "program": "${file}",
      "cwd": "${workspaceFolder}",
      "args": ["name=Anna"],
      "stopOnEntry": true
    }
  ]
}
```

With no `cwd`, the debugger uses the script's folder. Breakpoints are verified when the source is compiled.
The debugger shows the active frames of asynchronous code and previews the first 32 entries of a collection.
Debug Console input does not supply standard input to the script.

### Debug a WASP page

The extension debugs a `.wasp` page as `wasp run` runs it, with `wasp --debug-adapter`: put `wasp` on PATH, as
`build.bat` and the installer do, or set **WebLingo: Wasp Debugger Path** (`weblingo.waspDebuggerPath`) to the
executable's full path. A Release build puts it in `WebLingo/src/WebLingo.Wasp.Cli/bin/Release/net10.0/`.

Open the page and press **F5**, choosing **WASP** if VS Code asks for a debugger. Breakpoints may stand in the page and
in the files it is made of: its includes, its layout, its components and the `.wlg` modules it imports, each stopping
at its own line; a line of the page's text, which writes it, is a statement too. Without a `launch.json`, the page's
site, from which a path that starts with `/` is found, is the nearest folder above it in the workspace that holds a
`wasp.config` or a `web.config`, else the page's own folder. A launch configuration names it, and gives the page its
parameters:

```json
{
  "type": "wasp",
  "request": "launch",
  "name": "Debug WASP Page",
  "program": "${workspaceFolder}/index.wasp",
  "root": "${workspaceFolder}",
  "args": ["name=Anna"],
  "stopOnEntry": false
}
```

The page runs with no request, as under `wasp run`; its output goes to the **Debug Console**.

### Debug a WASP site

A launch configuration without `program` serves the site at `root`, as `wasp serve` does, on `port` of this machine,
8080 unless it says otherwise, 0 for a port the system chooses; **Add Configuration…** offers it as **WASP: Debug a
site**. The **Debug Console** says where it serves, `WASP serving http://localhost:8080/ (development), under the
debugger.`, and VS Code's `serverReadyAction` can open the browser there:

```json
{
  "type": "wasp",
  "request": "launch",
  "name": "Debug WASP Site",
  "root": "${workspaceFolder}",
  "port": 8080,
  "serverReadyAction": { "pattern": "WASP serving (https?://\\S+)", "uriFormat": "%s", "action": "openExternally" }
}
```

Each request for a page or a handler is a thread in the **Call Stack** view, named by its method, its path and its
number, `GET /cart #12`. A request that reaches a breakpoint stops alone: the site goes on serving the others, and the
browser waits for its response until the request goes on. Stepping and **Continue** act on the request selected. The
debugger's **Pause** stops every request at its next statement, those that come after it too, until **Continue**. A
page changed while the site is served is reloaded, as under `wasp serve`, and its breakpoints are verified once it is
next requested. Breakpoints' conditions are not supported yet.
