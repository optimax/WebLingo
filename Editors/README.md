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
next requested.

### A breakpoint's condition

A breakpoint may have a condition (**Edit Condition** on its marker), for scripts, pages and sites alike: it stops only
where the condition is `true`, `request.path == "/cart"` in a site's shared code, or `name.startsWith("A") && i > 0` in
a loop. A condition reads without running the program's code: names, literals, members, indexing, operators, and the
methods of strings, arrays, maps and sets that only read, `startsWith`, `contains`, `indexOf`, `has` and the like; a
property or a method written in WebLingo, an assignment or a call of a function cannot be in one. A condition that cannot
be one, or that reads a name not in scope on its line, leaves its breakpoint unverified, its marker saying why; one
that fails as it runs, or gives anything but a Boolean, stops there, the **Debug Console** saying why.

## Any debugger: the launch settings

`weblingo --debug-adapter` and `wasp --debug-adapter` speak the Debug Adapter Protocol on their standard input and output,
so that any debugger that speaks it debugs a script, a page or a site with them, VS Code's extension being one. A
debugger starts the adapter, then sends its `launch` request with these arguments, the protocol's own `noDebug` and the
adapter's; nothing else in a launch configuration, VS Code's `type`, `name` or `serverReadyAction`, reaches the adapter:

| Argument | What it is | Default |
|---|---|---|
| `program` | the script, a `.wlg`, for `weblingo`, or the page, a `.wasp`, for `wasp`; for `wasp`, left out to debug a site | none: a script's and a page's launch needs it |
| `cwd` | the folder the program runs in, from which a relative `program` and `root` are found | the program's own folder, or the site's root |
| `args` | the program's parameters, an array of `name=value` strings, as on the command line | none |
| `stopOnEntry` | `true` to stop at the program's first statement | `false` |
| `noDebug` | `true` to run without stopping | `false` |
| `root` | for `wasp`, the site's root, from which a path that starts with `/` is found, as `wasp run --root` gives it; without `program`, the site to serve | the page's folder |
| `port` | for `wasp`, without `program`: the port of this machine the site is served on, 0 for one the system chooses | 8080 |

Breakpoints, their conditions, stepping, the call stack and the variables are the protocol's own requests, and a
breakpoint may stand in any `.wlg` or `.wasp` file the program is made of.

`wasp --debug-adapter` also takes an `attach` request, which attaches it to a site already running on this machine,
under IIS, `wasp fastcgi` or `wasp serve`, where the site's `wasp.config` lets a debugger in, in development
(the WASP User Manual, section 15). Its arguments:

| Argument | What it is | Default |
|---|---|---|
| `port` | the port the site lets a debugger in on, on this machine, its `wasp.config`'s `"debug"`'s `"port"` | none: needed |
| `token` | the token its `"debug"` gives | none: needed |

VS Code's extension has the configuration "WASP: Attach to a running site" for it.

### Visual Studio, without an extension

Visual Studio's own Debug Adapter Host starts any adapter from a launch file whose `$adapter` and `$adapterArgs` name it,
the rest the arguments above:

```json
{
  "$adapter": "C:\\Program Files\\WebLingo\\wasp.exe",
  "$adapterArgs": "--debug-adapter",
  "request": "launch",
  "program": "C:\\Sites\\shop\\index.wasp",
  "root": "C:\\Sites\\shop",
  "args": ["name=Anna"]
}
```

Open the page in Visual Studio, set its breakpoints in the margin, and in the **Command Window** (**View**, **Other
Windows**) run `DebugAdapterHost.Launch /LaunchJson:"C:\Sites\shop\launch.json"`; or start Visual Studio with it,
`devenv /Command "DebugAdapterHost.Launch /LaunchJson:C:\Sites\shop\launch.json"`. The page stops at a breakpoint in
it, an include, a layout, a component or a module, with its call stack, **Locals**, stepping and **Continue**; its output
goes to the **Output** window, **Debug**. Visual Studio goes on at once from a stop on entry, so that `stopOnEntry` does
not stop there: set a breakpoint on the first statement instead. **Autos** stays empty, Visual Studio having no
language service for WebLingo. `DebugAdapterHost.Logging /On /OutputWindow` shows what Visual Studio and the adapter say
to each other.

### Visual Studio, with WebLingo's extension

`Editors/VisualStudio` is an extension of Visual Studio 2022 and 2026's, which debugs a script or a page in a folder
Visual Studio opens (**File**, **Open**, **Folder**) with F5, and `launch.vs.json`'s configurations of the type
`weblingo`. `build.ps1` builds it, `bin/WebLingo.VisualStudio.vsix`, with the C# compiler Visual Studio installs and no
package: `WebLingoLaunch.cs`, its launch targets, compiled for .NET Framework 4.8 against Visual Studio's own assemblies,
which it references and never ships; `WebLingo.pkgdef`, which registers `weblingo --debug-adapter` and
`wasp --debug-adapter` with Visual Studio's Debug Adapter Host as engines of its own, WebLingo and WASP;
`extension.vsixmanifest`; and `manifest.json` and `catalog.json`, which `build.ps1` writes from the manifest, as Visual
Studio's SDK does, Visual Studio's installer refusing a VSIX without them as "not a valid VSIX package". WebLingo's
setup for Windows offers it where it finds Visual Studio 2022 or 2026 without it, `Installers/Windows/package.ps1`
building it into each WebLingo zip as `VisualStudio/WebLingo.VisualStudio.vsix` where Visual Studio is installed, and
its uninstaller removes it where setup added it. Install it yourself by opening the `.vsix`, Visual Studio closed, or
from a command line, `VSIXInstaller.exe /quiet WebLingo.VisualStudio.vsix`, `VSIXInstaller.exe` in Visual Studio's
`Common7\IDE`.

In the folder, a `.wlg` or a `.wasp` file chosen as the startup item, by its context menu's **Set as Startup Item**, or
the file in front with **Current Document** chosen on the toolbar, is what F5 debugs, a script with `weblingo`'s adapter,
a page with `wasp`'s, each found on the PATH, as WebLingo's installer puts it there, else in `Program Files\WebLingo`.
A configuration of `launch.vs.json`, in the folder's `.vs`, by its `type`, `"weblingo"`, takes the launch settings above,
a relative path in them from the folder; Visual Studio lists a configuration only with the file its `project` names, so
that each names one, and one without `program` serves a site:

```json
{
  "version": "0.2.1",
  "configurations": [
    { "type": "weblingo", "name": "The shop", "project": "index.wasp", "program": "index.wasp" },
    { "type": "weblingo", "name": "The shop, served", "project": "index.wasp", "root": "${workspaceRoot}", "port": 8080 }
  ]
}
```

Tried with Visual Studio Community 2026 (18.12), installed by `VSIXInstaller.exe`: a script's configuration of
`launch.vs.json`, stopped on entry, at a breakpoint and with its locals; F5 on a page in front, at a breakpoint in its
loop, with its locals, its output in the Debug output; and a page's configuration with a relative `program`.
