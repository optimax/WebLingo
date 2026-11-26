# WASP User Manual

WASP, Web Application Server Pages, is how WebLingo builds websites. A WASP site is a folder of files, as a PHP site is:
pages (`.wasp`), which are HTML with WebLingo in them, modules (`.wlg`), which hold the code the pages share, route
handlers (also `.wlg`), which answer a path with code alone, and any other files, such as stylesheets and images, which
are sent as they are. WebLingo is installed once on a server, and every site uses it.

This manual shows how to write and serve such a site. It explains as much of the WebLingo language as pages need, but
it is not a reference to the language; the WebLingo Language Reference, `Documents/weblingo-language-reference.md`, is
that, and the WebLingo Standard Library Reference, `Documents/weblingo-standard-library-reference.md`, lists what the
built-in classes and the standard modules have.

Everything here works as described, and every example in this manual was run and gave the output
shown, but names and details may still change before a first release. [What is not there yet](#20-what-is-not-there-yet)
lists what is missing.

## Contents

1. [Getting started](#1-getting-started)
2. [A site's files and their paths](#2-a-sites-files-and-their-paths)
3. [Writing pages](#3-writing-pages)
4. [Escaping](#4-escaping)
5. [Includes](#5-includes)
6. [Layouts](#6-layouts)
7. [Components](#7-components)
8. [Modules](#8-modules)
9. [The request and the response](#9-the-request-and-the-response)
10. [Route handlers](#10-route-handlers)
11. [Waiting: `await`](#11-waiting-await)
12. [Errors](#12-errors)
13. [How a page's output is sent](#13-how-a-pages-output-is-sent)
14. [Configuration: `wasp.config`](#14-configuration-waspconfig)
15. [While you work](#15-while-you-work)
16. [The `wasp` command line](#16-the-wasp-command-line)
17. [Deploying to IIS](#17-deploying-to-iis)
18. [Serving through nginx or Apache: FastCGI](#18-serving-through-nginx-or-apache-fastcgi)
19. [Sites sharing a server](#19-sites-sharing-a-server)
20. [What is not there yet](#20-what-is-not-there-yet)

---

## 1. Getting started

### What you need

To serve a site you need `wasp`, WASP's command line, and the .NET 10 runtime it runs on. Until WASP has an installer,
you build it from the repository, with the .NET 10 SDK: either run it directly, from the repository's root, the folder
that holds `Examples.wasp` and `WebLingo`,

```bash
dotnet run --project WebLingo/src/WebLingo.Wasp.Cli -- serve --root Examples.wasp/example1
```

or publish it once, from the solution's folder, `WebLingo`, the one that holds `WebLingo.slnx`,

```powershell
powershell -ExecutionPolicy Bypass -File publish.ps1
```

which puts `wasp`, `weblingo` and the standard modules into one folder, `WebLingo/publish/WebLingo`, from which you run
`wasp` (section 17 installs that folder on a server). `-ExecutionPolicy Bypass` lets this one script run, without
changing the machine's policy, where Windows's default runs no script. On Linux, WebLingo's installers, in the
repository's `Installers/Linux`, install `wasp`, `weblingo` and the standard modules, with .NET's runtime, in
`/opt/weblingo`, as their `readme.md` says.

### Your first page

Make a folder for the site, say `mysite`, and in it a file `hello.wasp`:

```html
<!doctype html>
<% var name = "World"; %>
<h1>Hello, <%= name %>!</h1>
<p>2 + 3 = <%= 2 + 3 %></p>
```

Serve the folder:

```bash
wasp serve --root mysite
```

and open <http://localhost:8080/hello>. The browser gets:

```html
<!doctype html>
<h1>Hello, World!</h1>
<p>2 + 3 = 5</p>
```

The page's text is sent as it stands; `<% … %>` runs WebLingo, here declaring a variable; and `<%= … %>` writes a
value. The line that holds only `<% var name = "World"; %>` leaves no blank line behind.

`wasp serve` serves on port 8080 unless given another, `--port 5000`, and only to the machine it runs on. Ctrl+C stops
it. While it runs, it notices when you change, add or remove a file, and serves the new version at once
([section 15](#15-while-you-work)).

### Running a page without a server

`wasp run` runs a page and writes what it writes to the console, which is handy for checking a page:

```bash
wasp run mysite/hello.wasp
```

---

## 2. A site's files and their paths

A site is a folder, its *root*, and everything in it. A request's path finds the file it is for:

| Path | File |
|---|---|
| `/hello` | `hello.wasp` |
| `/` | `index.wasp` |
| `/teas` and `/teas/` | `teas/index.wasp` |
| `/teas/green` | `teas/green.wasp` |
| `/api/teas` | `api/teas.wlg`, a route handler ([section 10](#10-route-handlers)) |
| `/css/site.css` | `css/site.css`, sent as it is |

- A page, or a route handler, is found by its path **without** its extension: `/hello`, not `/hello.wasp`, which is a
  404.
- A folder's `index.wasp` (or `index.wlg`) answers the folder's path, with or without its closing `/`.
- Any other file is sent as it is, with the content type its extension gives (`text/css` for `.css`, `image/png` for
  `.png`, and so on); a file whose extension the server does not know is a 404.
- Paths are compared exactly, their case included, on every operating system: `/Hello` does not find `hello.wasp`.
- `products.wasp` and `products.wlg` in one folder are both `/products`, which is an error: the site does not load.

Some files are never sent, whatever their path:

- a page's source, and a module's, a `.wlg` that exports no handler (`/lib/catalog.wlg` is a 404);
- compiled WebLingo (`.wlb` files, and earlier builds' `.wlbr`, `.wlbs` and `.wlbc`);
- the site's configuration, `wasp.config`, and IIS's, `web.config`;
- a page or a route handler whose name holds `\`, `:` or a control character, which a file's name on Windows cannot have, and whose includes and imports could not be found;
- every file and folder whose name starts with `.`, such as `.git`;
- a database, SQL, a backup or an editor's copy, a log, an environment's or a configuration's settings, or a key, by its
  extension, whatever its case: `.db`, `.db3`, `.sqlite`, `.sqlite3`, `.mdb`, `.accdb`, `.mdf`, `.ldf`, `.sql`, `.bak`, `.backup`, `.old`, `.orig`, `.swp`, `.tmp`, `.log`, `.env`, `.config`, `.ini`, `.pfx`, `.p12`, `.pem` and `.key`, unless `wasp.config`'s `"serve"` allows its extension,
  `"serve": { "allow": [".pfx"] }` (section 14), the server then sending it as any other file, where it knows its type.
  Keep such files outside the site all the same: this only stops a copy left in it being sent.

A typical site:

```text
mysite/
  index.wasp             /
  shop.wasp              /shop
  contact.wasp           /contact
  teas/index.wasp        /teas
  api/teas.wlg            /api/teas, a route handler
  layouts/main.wasp      the layout the pages share
  includes/header.wasp   a piece of a page
  components/tea.wasp    a part a page renders, with values of its own
  lib/catalog.wlg         a module of shared code
  css/site.css           sent as it is
  wasp.config            the site's configuration
  web.config             IIS's, when the site runs under IIS
```

`layouts/main.wasp` is also `/layouts/main`, as every page is; a page that only makes sense inside another, as a layout
does, fails or writes something odd when asked for alone. A site that minds can keep such pages in a folder whose name
starts with `.`, which is never served, and include them from there.

---

## 3. Writing pages

A page is text, which is sent as it stands, and blocks of WebLingo:

| Block | What it does |
|---|---|
| `<% statements %>` | runs WebLingo statements |
| `<%= value %>` | writes a value, escaped for where it stands in the page's HTML ([section 4](#4-escaping)) |
| `<%- value %>` | writes a value as it stands, not escaped |
| `<%# anything %>` | nothing: a comment |

A statement may run across several blocks, the text between them written each time it is reached, so that loops and
conditions shape the page:

```html
<%# A comment: it writes nothing. %>
<% var teas = ["Sencha", "Assam", "Darjeeling"]; %>
<ul>
<% foreach (var tea in teas) { %>
  <li><%= tea %></li>
<% } %>
</ul>
<% if (teas.length > 2) { %>
<p>Plenty of tea.</p>
<% } else { %>
<p>Not much tea.</p>
<% } %>
```

writes:

```html
<ul>
  <li>Sencha</li>
  <li>Assam</li>
  <li>Darjeeling</li>
</ul>
<p>Plenty of tea.</p>
```

A few rules make pages come out as you would expect:

- **A line that holds only a `<% … %>` or `<%# … %>` block, and spaces, writes nothing**, not even its line break, so
  that statements leave no blank lines behind.
- **`<%= null %>` and `<%- null %>` write nothing**, so that a value that may be missing is simply blank.
- **`%>` inside a string or a comment does not end a block**: `<% var end = "%>"; %>` is one block. A `//` comment runs
  to the end of its line, so a block whose last line is a `//` comment must close on the next line.
- **`write(value)` and `writeLn(value)`**, inside a block, write a value as `<%-` does, not escaped, `writeLn` with a
  line break after it, `null` as `null`. A page has them without an import, its own, as its layout and its components
  have theirs; a module the page imports has neither, and writes the response with `response.write`
  ([section 10](#10-route-handlers)). A site has no console, so that `std:console`, which a script that `weblingo` runs
  imports to write, cannot be imported here.

Everything else inside the blocks is the WebLingo language: variables (`var`, `const`), functions, classes, arrays
(`[1, 2]`), maps (`{ name: "Anna" }`), `if`, `while`, `for`, `foreach`, `try`, and the rest. An interpolated string,
`$"…"`, puts values in text, with a format for numbers when you want one: `$"€{price:N2}"` writes `€6.50` for 6.5.

`DateTime.now()` is the server's date and time, its class `std:datetime`'s, which a page imports to name it. `<%= %>`
writes a date as WebLingo shows any date, `DateTime:2026-11-16T14:30:00-06:00`, so give it a format for people to read:

```html
<% import { DateTime } from "std:datetime"; %>
<p>Served on <%= DateTime.now().format("d MMMM yyyy 'at' HH:mm") %>.</p>
```

The WebLingo Language Reference has dates' members, their formats, and how to read one from text.

---

## 4. Escaping

`<%= value %>` writes the text a value has, the text `write` would write, with the five characters that matter in
HTML replaced, the same in text and in an attribute's value:

| Character | Written as |
|---|---|
| `&` | `&amp;` |
| `<` | `&lt;` |
| `>` | `&gt;` |
| `"` | `&quot;` |
| `'` | `&#39;` |

```html
<% var motto = "Tea & patience <always>"; %>
<p><%= motto %></p>
<p><%- "<em>raw HTML</em>" %></p>
<p>[<%= null %>]</p>
```

writes:

```html
<p>Tea &amp; patience &lt;always&gt;</p>
<p><em>raw HTML</em></p>
<p>[]</p>
```

Use `<%=` for anything that comes from a visitor or a database, and `<%-` only for HTML you made yourself. An instance
of a class of yours is written as its `toString()` gives it, and escaped the same way.

### Where a value stands

WASP reads the page's HTML as it compiles it, and escapes each `<%= value %>` for where it stands, as Go's
`html/template` does:

| Where | How the value is escaped |
|---|---|
| HTML's text, a quoted attribute's value, a `<title>`, a `<textarea>`, a comment | the five characters above |
| an unquoted attribute's value, `title=<%= x %>` | also spaces, `=` and `` ` ``; an empty value is `zWebLingoz` |
| where an attribute's name stands, `<input <%= x %>>` | only a plain attribute's name, in lower case |
| a URL's beginning, `href="<%= x %>"` | a scheme other than `http`, `https` and `mailto` refused, the rest percent-encoded as a URL needs |
| a URL's path, `href="/teas/<%= x %>"` | percent-encoded as a URL needs |
| a URL's query or fragment, `href="/?q=<%= x %>"` | percent-encoded whole |
| a script, `<script>var x = <%= x %>;</script>`, `onclick="f(<%= x %>)"` | written as JSON, which the script reads as a value |
| a script's string, `"…<%= x %>…"` | JavaScript's escapes |
| a style, `color: <%= x %>` | refused if it could change what the style means |
| a style's string, `"…<%= x %>…"` | CSS's escapes |
| a script's or a style's comment | nothing |

A value that cannot stand where it is safely is written `zWebLingoz`, so that you see it in the page:

```html
<% var name = "Tea & \"Biscuits\""; var link = "javascript:alert(1)"; var query = "green tea"; %>
<p title="<%= name %>"><%= name %></p>
<a href="<%= link %>">a visitor's link</a>
<a href="/search?q=<%= query %>">search</a>
<script>var shop = <%= { name: name, open: true } %>; var greeting = "Hello, <%= name %>";</script>
<div style="color: <%= "red; background: url(x)" %>"></div>
```

writes:

```html
<p title="Tea &amp; &quot;Biscuits&quot;">Tea &amp; &quot;Biscuits&quot;</p>
<a href="#zWebLingoz">a visitor's link</a>
<a href="/search?q=green%20tea">search</a>
<script>var shop = {"name":"Tea \u0026 \"Biscuits\"","open":true}; var greeting = "Hello, Tea \u0026 \u0022Biscuits\u0022";</script>
<div style="color: zWebLingoz"></div>
```

In a script, `<%= null %>` writes `null`; elsewhere it writes nothing. Quote your attributes: an unquoted value is
easy to get wrong. A `<script>` whose `type` is not JavaScript's or JSON's, a template's, holds HTML's text.

WASP follows the page's code between its blocks, so that it knows where each value stands however the page runs: a
block of code, `{ … }`, must end where the HTML stood when it began; the branches of an `if` and its `else`s must end
where each other's do; a statement without braces, `<% if (x) %>…`, must not leave the HTML elsewhere; a component is
rendered, and a layout's body written, in HTML's text; and a page ends there. A page that breaks one of these, or
whose HTML browsers read in more than one way, such as a quote in an unquoted attribute's value, is error WL5008, when
it is compiled:

```text
page.wasp(6,1): error WL5008: An 'if' without an 'else' and its branches end in different places in the page's HTML, HTML's text and a URL's path.
```

`<%-` is taken to leave the HTML where it was, as `write` is.

### What WASP cannot tell

Where WASP cannot tell what an attribute holds, such as a library's own attributes (`hx-get`, `x-data`), escape the
value yourself with the class `Html`, which a page imports, `<% import { Html } from "std:html"; %>`, whose text is then
escaped again for where it stands, as any string is:

| Member | What it gives |
|---|---|
| `Html.escape(x)` | `x`'s text with the five characters as entities, for HTML you build in code and write with `<%-` |
| `Html.url(x)` | `x` as a URL: a scheme that could run a script refused, the rest percent-encoded as a URL needs |
| `Html.urlPart(x)` | `x` percent-encoded whole, for a URL's query, a fragment, or a part you join in code |
| `Html.js(x)` | `x` as JSON a script reads as a value |
| `Html.jsString(x)` | `x`'s text with JavaScript's escapes, for inside a script's string |
| `Html.css(x)` | `x`'s text, or `zWebLingoz` if it could change what a style means |

```html
<% import { Html } from "std:html"; %>
<div hx-get="<%= Html.url(link) %>"></div>
```

writes `<div hx-get="#zWebLingoz"></div>`. Any WebLingo program may import it, not only a page (the Standard Library
Reference, "In WASP's pages"); a page's own escaping needs no import.

---

## 5. Includes

`<% include "/includes/header.wasp"; %>` puts another template's text where the statement stands, as if it had been
written there: the included template sees the variables of the place it is included in, and may change them.

```html
<header><a href="/">The Tea House</a> | <%= title %></header>
```

- A path that starts with `/` is from the site's root; any other is from the including file's folder,
  `"../includes/header.wasp"`.
- The path is a string as written, not an expression: an include is resolved, and compiled, with the page.
- An include may include others; a template that includes itself, directly or through others, is an error (WL5003).
- A path outside the site's root (`"../../secret.wasp"`) is an error: nothing outside the root can be included.

Use an include for a piece of a page that shares its variables, such as a header that shows the page's title; use a
component ([section 7](#7-components)) for a part with values of its own.

---

## 6. Layouts

A layout is a template that wraps a page: its `<html>`, its `<head>` and its header and footer. A page names its layout,
and the values the layout takes, in a statement of its own:

```html
<% layout "/layouts/main.wasp", { title: "Shop" }; %>
```

The layout declares the values it takes with `param`, one statement each, and calls `renderBody()` where the page's
own text goes:

```html
<% param title; %>
<!doctype html>
<html>
<head><title><%= title %> - The Tea House</title></head>
<body>
<% include "/includes/header.wasp"; %>
<main>
<% renderBody(); %>
</main>
</body>
</html>
```

- A page has at most one layout. The rest of the page is its body, which runs where the layout calls `renderBody()`.
- A layout's values are checked: a value the layout does not declare, or one it declares without a default that the
  page does not give, is an error when the page compiles (`'extra' is not a parameter of 'layouts/main.wasp'`, WL5006).
- A layout may have a layout of its own, and so on; a layout that is its own layout, directly or through others, is an
  error.

The page `shop.wasp` in [section 8](#8-modules) uses this layout.

---

## 7. Components

A component is a template that a page renders with values of its own, as many times as it likes: a card, a row, a
form. It declares its parameters with `param`, a default making one optional:

```html
<% param name; param price; param featured = false; %>
<div class="tea"><b><%= name %></b> <%= $"€{price:N2}" %><% if (featured) { %> (featured)<% } %></div>
```

A page renders it with `renderComponent(path, values)`, and awaits it:

```html
<% await renderComponent("/components/tea.wasp", { name: "Sencha", price: 6.5 }); %>
```

- `renderComponent` gives a task, which the page awaits, so that the component's text comes where the page asked for
  it ([section 11](#11-waiting-await)).
- Its path is any string, worked out as the page runs, from the root for one that starts with `/`, from the rendering
  file's folder otherwise. The component is found and compiled the first time it is rendered, then kept.
- A value the component does not declare, or a missing one without a default, is an error; a component that cannot be
  found is a runtime error, which a `try` can catch.
- A component sees only its parameters, not the page's variables. That is what makes it reusable.

---

## 8. Modules

Code that pages share, such as classes, data and functions, goes into a module, a `.wlg` file, whose declarations
marked `export` a page imports:

```javascript
export class Tea {
  constructor(name, price) { this.name = name; this.price = price; }
}

export const teas = [new Tea("Sencha", 6.5), new Tea("Assam", 4.25)];

export function cheapest() {
  var best = teas[0];
  foreach (var tea in teas) { if (tea.price < best.price) best = tea; }
  return best;
}
```

A page imports names from it, `import { teas, cheapest } from "/lib/catalog.wlg";`, or the module itself, `import
catalog from "/lib/catalog.wlg";`, then `catalog.teas`. Here is `shop.wasp`, which uses the module, the layout of
[section 6](#6-layouts) and the component of [section 7](#7-components):

```html
<% import { teas, cheapest } from "/lib/catalog.wlg"; %>
<% layout "/layouts/main.wasp", { title: "Shop" }; %>
<% foreach (var tea in teas) { %>
<% await renderComponent("/components/tea.wasp", { name: tea.name, price: tea.price, featured: tea == cheapest() }); %>
<% } %>
```

`/shop` gives:

```html
<!doctype html>
<html>
<head><title>Shop - The Tea House</title></head>
<body>
<header><a href="/">The Tea House</a> | Shop</header>
<main>
<div class="tea"><b>Sencha</b> €6.50</div>
<div class="tea"><b>Assam</b> €4.25 (featured)</div>
</main>
</body>
</html>
```

- An import's path that starts with `/` is from the site's root, any other from the importing file's folder; `.wlg`
  may be left out.
- An import stands at a page's or a module's top level, not inside a block or a function; wherever it stands there, the
  module it imports runs before anything else the file does.
- A module runs once for each request that imports it, the first time it is imported; every later import in that
  request gets the same module. Nothing in a module lasts from one request to the next.
- Standard modules are imported by name: `import fs from "std:fs";` for files ([section 11](#11-waiting-await)).

---

## 9. The request and the response

A page that declares `param request;` and `param response;` is given the request it answers and the response it
makes, each an object whose members you read and set. A page declares only those it uses. A layout and a component
may declare them too, and get the same request and response without the page giving them; an include sees the
page's, or its layout's, as it sees any of their variables.

### The request

| Member | What it is |
|---|---|
| `request.method` | `"GET"`, `"POST"`, and so on, as the client sent it |
| `request.path` | the path, from `/`, decoded, without the query: `/greet` |
| `request.query` | the query's names and values, decoded, as a map: `request.query["name"]` |
| `request.headers` | the request's headers: `request.headers["Accept"]` |
| `request.scheme` | `"http"` or `"https"` |
| `request.host` | the host the client asked for, with its port if it gave one: `example.com:8443` |
| `request.clientAddress` | the client's address, `203.0.113.7` or `2001:db8::7`, without its port; behind a proxy the site trusts, the one the proxy names (section 14, `"proxies"`) |
| `await request.text()` | the request's body, as text |
| `await request.form()` | a form's fields, as a map |
| `await request.files()` | a form's uploaded files, each input's name an array of `UploadedFile`s |
| `await request.read()` | the body's next piece, as bytes, or `null` at its end |
| `request.session` | the browser's session, which the site keeps between its requests (below, "Sessions") |

```html
<% param request; %>
<% var name = request.query?["name"]; %>
<% if (name == null) name = "stranger"; %>
<p>Hello, <%= name %>! You asked for <%= request.path %> with <%= request.method %>.</p>
<p>Your browser accepts: <%= request.headers?["Accept"] %></p>
```

`/greet?name=Anna` gives `Hello, Anna!`, and `/greet`, `Hello, stranger!`.

- A query's name that is not there is an error when read with `[…]`, as a map's missing key is; `?[…]` gives `null`
  instead. `request.query.has("name")` says whether it is there.
- A name given twice in the query, `?tag=a&tag=b`, has its last value, as in PHP.
- A header's name is matched without regard to case: `request.headers["accept"]` is `Accept`. A header given several
  times is read as its values joined by `", "`. A missing header is an error with `[…]`, and `null` with `?[…]`.
- A request's headers cannot be changed.

### The request's body

A form posted to a page, or anything else a client sends, is its body, which a page reads as it arrives:

- `await request.text()` gives the whole body as text, in the character set its `Content-Type` names, UTF-8 if it
  names none.
- `await request.form()` gives a form's fields as a map, for a form posted as `application/x-www-form-urlencoded`, which
  is how a browser posts an ordinary form, or as `multipart/form-data`, how it posts one with files; a name given twice
  has its last value. A body of another type is an error.

```html
<% param request; %>
<% if (request.method == "POST") { %>
<% var form = await request.form(); %>
<p>Thank you, <%= form["name"] %>.</p>
<% } else { %>
<form method="post"><input name="name"><button>Send</button></form>
<% } %>
```

- `await request.read()` gives the body's next piece as it arrives, as bytes, and `null` at its end, for a body too large to hold
  whole, such as an upload a page stores as it comes.

The body is read only when a page asks for it. Read whole, it is kept, so that `text()` and `form()` may each be called again,
and give the same; read in pieces, with `read()`, it cannot then be read whole too.

```javascript
export async function post(request, response) {
  var bytes = 0;
  var piece = await request.read();
  while (piece != null) {
    bytes = bytes + piece.length;
    piece = await request.read();
  }
  response.write($"{bytes} bytes");
}
```

Bytes, their members, and how to read them as text, are in the WebLingo Language Reference.

### Uploaded files

A form with a file input is posted as `multipart/form-data`, its `enctype`. `await request.files()` gives its files, a
map of each file input's name to an array of `UploadedFile`s, an array even of one, so that an input that takes several,
`<input type="file" name="photos" multiple>`, is read alike; and `await request.form()` its other fields, as for any form.
A file input left empty uploads no file.

| Member | What it gives |
|---|---|
| `file.name` | its input's name |
| `file.fileName` | its file's name, as the browser gave it: never use it as a path |
| `file.type` | its `Content-Type`, as the browser gave it, `application/octet-stream` if none |
| `file.size` | its size, in bytes |
| `await file.bytes()` | its content, as bytes |
| `await file.text()` | its content, as text, in UTF-8 |
| `await file.save(path)` | writes it at `path`, replacing a file there, within the folders `wasp.config`'s `"files"` lets the site write |

```html
<% param request; %>
<% if (request.method == "POST") { %>
<% var photo = (await request.files())["photo"][0]; %>
<% await photo.save("../uploads/latest-photo"); %>
<p>Thank you: <%= photo.fileName %>, <%= photo.size %> bytes.</p>
<% } else { %>
<form method="post" enctype="multipart/form-data"><input type="file" name="photo"><button>Send</button></form>
<% } %>
```

with `wasp.config`'s `{ "files": { "write": ["../uploads"] } }`. A page that keeps a file names it itself: a file's name is
whatever its sender chose, `../../web.config` among them.

A form is read as it arrives, once, `form()` and `files()` reading the same body; read so, it cannot then be read with
`text()` or `read()`, nor one read with `read()` as a form. A file is kept in memory up to 64 KB, the rest of it in a
temporary file of the server's, removed when its request ends. The largest body a site takes is 30,000,000 bytes, a
larger one refused with 413 before its page runs where its `Content-Length` says so, and an error where the page reads it
otherwise; `wasp.config` changes both (section 14), and under IIS the site's `web.config` too (section 17).

### The response

| Member | What it is |
|---|---|
| `response.status` | the status, 200 until set: `response.status = 404;` |
| `response.headers` | the response's headers: `response.headers["Content-Type"] = "text/plain";` |
| `response.write(value)` | writes a value's text to the response's body, as `write` writes it: `response.write("done");`; bytes as they are |
| `response.writeLn(value)` | the same, then a line break |
| `await response.sendFile(path, type, options)` | sends a file as the response ([Sending a file](#sending-a-file)) |

```html
<% param response; %>
<% response.status = 404; %>
<% response.headers["Cache-Control"] = "no-store"; %>
<h1>No such tea</h1>
```

- A response's headers have `has(name)`, `add(name, value)`, which gives a header one more value (as `Set-Cookie` needs),
  and `remove(name)`, besides reading and setting by `[…]`.
- A page's `Content-Type` is `text/html; charset=utf-8` unless it sets another.
- A status that is not an integer from 100 to 999, a header's name or value that HTTP does not allow, or a value that is
  not a string, is an error where it is set.
- Status and headers can be set until the page's output starts being sent, after its first 64 KB
  ([section 13](#13-how-a-pages-output-is-sent)); after that, setting them is an error. Set them before writing much.

### Sending a file

`await response.sendFile(path)` sends a file as the response, a page's or a route handler's, its type by its extension,
or the type given second; `{ download: "name" }`, third, has the browser save it under that name rather than show it,
and `{ download: true }` under its own. The file must be in a folder `wasp.config`'s `"files"` lets the site's scripts
read ([section 14](#14-configuration-waspconfig)), its path from the site's root, so that a site keeps such files
outside its folder, where nothing serves them by itself, and its own code decides who may have them:

```json
{ "files": { "read": ["../reports"] } }
```

```javascript
// report.wlg, answering /report
export async function get(request, response) {
    response.headers["Cache-Control"] = "private";
    await response.sendFile("../reports/2026.pdf", null, { download: "Report 2026.pdf" });
}
```

```text
HTTP/1.1 200 OK
Content-Length: 18342
Content-Type: application/pdf
Date: Thu, 26 Nov 2026 03:06:33 GMT
Server: Kestrel
Accept-Ranges: bytes
Cache-Control: private
ETag: "47a6-8df4b6a3f875faa"
Last-Modified: Thu, 26 Nov 2026 03:06:21 GMT
X-Content-Type-Options: nosniff
Referrer-Policy: strict-origin-when-cross-origin
Content-Security-Policy: frame-ancestors 'self'
X-Frame-Options: SAMEORIGIN
Content-Disposition: attachment; filename="Report 2026.pdf"
```

- The response keeps its status and its headers, the site's security headers among them. The file comes with its
  `Content-Length`, `Last-Modified` and `ETag`, so that a browser that has it already is answered 304, with no body, and
  one that asks for part of it, as a video player or a resumed download does, 206, with that part. A site's own files,
  its stylesheets and pictures, are sent the same way.
- What the page wrote before `sendFile`, and writes after it, is not sent. Sending a file once the page's output has
  started being sent, after its first 64 KB, is an error.
- A path the site may not read is a `FilePermissionError`; a file that is not there, or a folder, an `IOError`.
- A type given is a media type, `"application/pdf"` or `"text/csv; charset=utf-8"`. Without one, the file's type is the
  response's `Content-Type`, where the page set one, else the one its extension gives, else `application/octet-stream`.
- A name to save the file under that is not ASCII is sent as RFC 6266 has it, `filename*=UTF-8''…`, for every browser
  of today.
- `response.write(bytes)` writes bytes a script made as they are, in order with the text around them:
  `response.headers["Content-Type"] = "image/png"; response.write(picture);`.

### Cookies

| Member | What it is |
|---|---|
| `request.cookies` | the request's cookies, a map of their names to their values: `request.cookies["theme"]` |
| `response.setCookie(name, value, options)` | sets a cookie, its value a string; `options`, a map, may be left out |
| `response.deleteCookie(name, options)` | deletes a cookie; `options` may name its `path` and `domain`, as it was set |

```html
<% param request; param response; %>
<% var visits = request.cookies.has("visits") ? Integer.parse(request.cookies["visits"]) + 1 : 1; %>
<% response.setCookie("visits", visits.toString(), { maxAge: 60 * 60 * 24 * 365 }); %>
<p>Visits: <%= visits %></p>
```

The first request gives `Visits: 1`, its response's header `Set-Cookie: visits=1; Path=/; Max-Age=31536000; HttpOnly;
SameSite=Lax`, and the browser's next, `Visits: 2`.

- A cookie is secure by default: `HttpOnly`, so that a page's scripts in the browser cannot read it, `SameSite=Lax`,
  `Path=/`, and `Secure` when the request came over HTTPS. Each option changes one: `httpOnly` and `secure`, Booleans;
  `sameSite`, `"lax"`, `"strict"` or `"none"`, the last only for a secure cookie, as browsers ask; `path`, from `/`;
  `domain`; `maxAge`, in seconds; and `expires`, a `DateTime`. Without `maxAge` or `expires`, a cookie lasts until the
  browser closes. An option that is not one is an error that names the one you may have meant.
- A value may hold any text: what a cookie cannot hold as it is, a space, `;`, `"` or a letter outside ASCII, is sent
  as `%xx`, and `request.cookies` gives it back as it was set. A cookie's name is letters, digits and
  `!#$%&'*+-.^_`|~`.
- A cookie set twice in one response, of the same name, path and domain, is sent once, as it was set last; so is a
  cookie deleted after it was set.
- A cookie sent twice by the browser, of one name and different paths, is read as the first it sent, the one of the
  longest path.
- A cookie, as any header, can be set until the page's output starts being sent; after that, setting one is an error.

### Sessions

A session keeps what a site must remember of a browser between its requests, a user who has signed in or what is in a
cart, on the server; the browser keeps only the session's id, in a cookie.

| Member | What it is |
|---|---|
| `request.session[name]` | a value the session keeps; set, `request.session["user"] = "ann";`, it is kept for the browser's next requests |
| `request.session.has(name)` | whether the session keeps a value of that name |
| `request.session.remove(name)` | removes a value, giving whether it was there |
| `request.session.release()` | gives the session up before the request ends, so that the browser's other requests need not wait for it |
| `request.session.renew()` | gives the session a new id, its values kept, as a user signs in |
| `request.session.end()` | ends the session, as a user signs out |

```html
<% param request; %>
<% var visits = (request.session?["visits"] ?? 0) + 1; %>
<% request.session["visits"] = visits; %>
<p>Visits: <%= visits %></p>
```

The first request gives `Visits: 1`, its response's header `Set-Cookie: WASPSESSIONID=3da01f93ffffb71921519e9538ff061b;
Path=/; HttpOnly; SameSite=Lax`, the id another for each session, and the browser's next, `Visits: 2`.

- A session is made, and its cookie sent, once a page sets a value in it: a request that sets none makes none. Its
  cookie, `WASPSESSIONID`, holds a random id of 128 bits; it is `HttpOnly` and `SameSite=Lax`, `Secure` over HTTPS, and
  lasts until the browser closes. A session ends once no request has used it for 20 minutes.
- A session keeps what JSON holds: `null`, Booleans, integers, numbers, strings, and arrays and maps of them, a map's
  keys strings. Anything else, a decimal, a date, a set, an instance of a class, is an error where it is set: keep a
  decimal's or a date's text instead.
- A value read from a session is a copy, as classic ASP's `Session` gives an array: a change to an array or a map read
  from it is kept only once it is set again.

  ```javascript
  var cart = request.session?["cart"] ?? [];
  cart.push(item);
  request.session["cart"] = cart;
  ```

- A name the session does not keep is an error with `[…]`, as a map's missing key is, and `null` with `?[…]`.
- A session is made, renewed and ended with its cookie, which, as any header, can be set until the page's output starts
  being sent: set what a page keeps before writing much.
- Sites served under one host name, at different ports, share their cookies, the session's among them, as browsers keep
  cookies by host: each site then replaces the other's session. Give each site a name of its own.

**One request at a time.** A browser's requests of one session run one at a time: one that comes while another has the
session waits for it, as at an `await`, its server running other requests meanwhile, then reads what the other wrote. A
page that only reads its session says so, so that it does not wait:

```html
<% session readonly; %>
<% param request; %>
<p>Signed in as <%= request.session?["user"] ?? "nobody" %>.</p>
```

A route handler says so by an export, `export const session = "readonly";`. Such a request reads the values last
written, and changing them is an error that names where it declared it. Only the page or the handler that answers the
request declares it, once, at its top level: its includes, its layout and its components read the session as it says,
and declaring it there is WL5009. A request that has done with its session before it ends may give it up,
`request.session.release()`, its values written then, so that the browser's other requests run; changing it after is
an error. A request waits for its session for 30 seconds at most, then runs without it, and reading `request.session` is
then a `SessionLockError`, WL3024, which a `catch` may catch.

**Signing in and out.** Give the session a new id as a user signs in, so that an id someone saw before does not become
a signed-in user's, and end it as the user signs out:

```javascript
// api/sign-in.wlg
export async function post(request, response) {
  var form = await request.form();
  // … the user's password checked …
  request.session.renew();
  request.session["user"] = form["user"];
  response.status = 303;
  response.headers["Location"] = "/";
}
```

```javascript
// api/sign-out.wlg
export function post(request, response) {
  request.session.end();
  response.status = 303;
  response.headers["Location"] = "/";
}
```

`renew()` keeps the session's values, and does nothing where there is no session yet; `end()` removes the session, and
deletes its cookie.

**Where sessions are kept.** In the server's memory, by default: they last while `wasp` runs, kept as the site reloads
for a change to its files, and lost as it stops, as when IIS recycles the site's application pool; and they are one
process's. A site whose sessions must outlast its process, or that is served by several processes at once, as Apache's
`mod_fcgid` may start them, or by several servers, keeps them in one of its databases instead (below). `wasp.config`'s
`"sessions"` says where, how long a session lasts, and how long a request waits for one
([section 14](#14-configuration-waspconfig)).

### Sessions in a database

A site keeps its sessions in a table of a database its `wasp.config` names, which every process serving the site
shares:

```json
{
  "databases": { "main": "sqlite:../data/site.db" },
  "sessions": { "store": "database", "database": "main" }
}
```

The table, `wasp_sessions` unless `"sessions"` names another, `"table": "sessions"`, is made by the site's
administrator, once, before the site uses it: `wasp sessions create`, run in the site's folder, or with `--root`, makes it
in the database `"sessions"` names, or `--database` names, as an account allowed to make tables, which the site's own
need not be, reaches the database, Windows's own with SQL Server's integrated security; `--sql` prints the SQL instead,
for the administrator to run:

```text
> wasp sessions create --root site
WASP made the table of sessions 'wasp_sessions' in the database 'main', its schema 1.
```

Run again, it says that the table is there, and leaves it as it is. The SQL, for each database:

```sql
-- SQLite
CREATE TABLE wasp_sessions (id TEXT NOT NULL PRIMARY KEY, data TEXT NOT NULL, used INTEGER NOT NULL, locked_until INTEGER NULL);
CREATE INDEX wasp_sessions_used ON wasp_sessions (used);
INSERT INTO wasp_sessions (id, data, used, locked_until) VALUES ('schema', '{"schema": 1}', 9223372036854775807, NULL);

-- SQL Server
CREATE TABLE wasp_sessions (id VARCHAR(32) NOT NULL PRIMARY KEY, data NVARCHAR(MAX) NOT NULL, used BIGINT NOT NULL, locked_until BIGINT NULL);
CREATE INDEX wasp_sessions_used ON wasp_sessions (used);
INSERT INTO wasp_sessions (id, data, used, locked_until) VALUES ('schema', '{"schema": 1}', 9223372036854775807, NULL);

-- MySQL
CREATE TABLE wasp_sessions (id VARCHAR(32) NOT NULL PRIMARY KEY, data LONGTEXT NOT NULL, used BIGINT NOT NULL, locked_until BIGINT NULL) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin;
CREATE INDEX wasp_sessions_used ON wasp_sessions (used);
INSERT INTO wasp_sessions (id, data, used, locked_until) VALUES ('schema', '{"schema": 1}', 9223372036854775807, NULL);

-- PostgreSQL
CREATE TABLE wasp_sessions (id VARCHAR(32) NOT NULL PRIMARY KEY, data TEXT NOT NULL, used BIGINT NOT NULL, locked_until BIGINT NULL);
CREATE INDEX wasp_sessions_used ON wasp_sessions (used);
INSERT INTO wasp_sessions (id, data, used, locked_until) VALUES ('schema', '{"schema": 1}', 9223372036854775807, NULL);
```

- A site whose table is missing fails where a page uses a session, a `DatabaseError`, WL3015, its message naming the
  command that makes the table: `The session cannot be made: the database 'main' has no table of sessions
  'wasp_sessions', or cannot read it (…): make it with 'wasp sessions create --database main'.` For development, or a
  SQLite file of the site's own, `"create": true` has the site make its table where it is missing.
- A row holds a session: its id, its values, as JSON, when a request last used it, in milliseconds since 1970, UTC, and,
  while a request has its lock, when the lock ends unless that request's process renews it, which it does every 20
  seconds, so that a lock a process that stopped held ends within a minute. A request whose session another has reads
  its row again every 100 ms. The servers' clocks should agree, as each reads the times the others write.
- The row `schema` gives the table's schema: a later WASP, whose schema is another, says so, with the command that
  upgrades the table, and never changes the table itself.
- A session that has ended is removed by a request that comes after it, once a minute at most.
- WASP's tests try each database's SQL, a session kept and locked, on SQLite, SQL Server, MySQL 8.4 and PostgreSQL 19.

A page run by `wasp run` has no request: there, a page's parameters are values given on the command line
([section 16](#16-the-wasp-command-line)), and a page that declares `request` fails.

### Requests from other sites

A page that another site's form posts to may be made to act for a user signed in here, the browser sending this site's
cookies with the form: a cross-site request forgery. WASP refuses such a request: a POST, PUT, PATCH or DELETE that
another site sent is a 403 Forbidden before its page runs. Browsers say where a request comes from, by its
`Sec-Fetch-Site` header, and an older browser by its `Origin`; a request with neither, from a program such as `curl`,
is taken, as a browser that sends cookies sends one of them. GET, HEAD and OPTIONS are never refused, so change
things on a POST, never on a GET.

A page that other sites are meant to post to, an API's, says so, once, at its top level, as a session is declared read
only: `<% crossorigin allowed; %>`, and a route handler by an export, `export const crossorigin = "allowed";`. Only the
page or the handler that answers the request declares it: its includes, its layout and its components cannot, WL5010.

A page may check a token besides, its own check, called where it calls it. `request.csrfToken` is the request's token,
which the browser keeps in a cookie, `WASPCSRF`, set the first time a page reads it; a form sends it back in a hidden
field, `csrfToken`, or a script's request in a header, `X-CSRF-Token`; and `await request.checkCsrf()` says whether
the request sent the token its cookie holds:

```html
<% param request; %>
<% if (request.method == "POST") { %>
<p><%= await request.checkCsrf() ? "Saved." : "This form has expired: send it again." %></p>
<% } %>
<form method="post">
  <input type="hidden" name="csrfToken" value="<%= request.csrfToken %>">
  <button>Save</button>
</form>
```

Its first GET sets the cookie, `Set-Cookie: WASPCSRF=db83381894436e88eea1fc0f97a66980; Path=/; HttpOnly; SameSite=Lax`,
and writes the token into the form; the form posted back writes `<p>Saved.</p>`, and one posted with another token
`<p>This form has expired: send it again.</p>`. Posted from another site, the page does not run: under `wasp serve`,
in development, its 403 says why, `A POST from another site was refused, as a cross-site request forgery may be: its
Origin, 'https://attacker.example', is not this site's, 'localhost:8080'; …`, and in production only `403 Forbidden`.
`request.checkCsrf(token)` checks a token the page read itself, `await request.checkCsrf(form["t"])`.

### Security headers

Every response a site gives, a page's, a route handler's, a file's and an error's, has four headers WASP gives it:

```text
X-Content-Type-Options: nosniff
Referrer-Policy: strict-origin-when-cross-origin
Content-Security-Policy: frame-ancestors 'self'
X-Frame-Options: SAMEORIGIN
```

`nosniff` keeps a browser from taking a file for what it is not, an uploaded text file for a script; the referrer policy
sends another site only this site's address, never a page's path and query; and `frame-ancestors 'self'`, with
`X-Frame-Options` for browsers that do not know it, lets only this site's own pages show its pages in a frame, against a
page of another site that hides one of this site's under its own buttons (clickjacking).

A page changes them for itself, through `response.headers`, which has them before it runs:
`response.headers["Referrer-Policy"] = "no-referrer";`, `response.headers.remove("X-Frame-Options");`. A page's own
`Content-Security-Policy` replaces WASP's, so that a page with a policy of its own writes `frame-ancestors` in it where it
wants it. A page that fails has an error page, which has the site's four again.

A site changes them for all its responses in `wasp.config`, `"headers"`, each optional:

```json
{
  "headers": {
    "nosniff": true,
    "referrerPolicy": "strict-origin-when-cross-origin",
    "frameAncestors": ["self", "https://partner.example"]
  }
}
```

`"nosniff": false` leaves out `X-Content-Type-Options`; `"referrerPolicy"` is one of the policies browsers know,
`"no-referrer"`, `"no-referrer-when-downgrade"`, `"origin"`, `"origin-when-cross-origin"`, `"same-origin"`,
`"strict-origin"`, `"strict-origin-when-cross-origin"` or `"unsafe-url"`, or `null` for none; and `"frameAncestors"` lists
the sites whose pages may show the site's in a frame, `"self"` for its own, a scheme, a host, or both, as
`Content-Security-Policy`'s `frame-ancestors` names them, `["none"]` for none at all, or `null` for no limit. With the
settings above, a response says `Content-Security-Policy: frame-ancestors 'self' https://partner.example`, and no
`X-Frame-Options`, which can only name the site itself, `SAMEORIGIN` for `["self"]`, or no site, `DENY` for `["none"]`.

A site whose web server adds a `Content-Security-Policy` of its own, IIS's `customHeaders` in its `web.config` or nginx's
`add_header`, says `"frameAncestors": null`, or writes `frame-ancestors` in its own: WASP's is sent beside it otherwise,
and a browser keeps every policy it is sent. `Strict-Transport-Security`, and a full `Content-Security-Policy`, are each
site's own, which a page sets, or its web server.

---

## 10. Route handlers

A `.wlg` file that exports a function named after an HTTP method answers its path with code alone, without a page's
text: an API, a redirect, a download. The function is given the request and the response, as a page's parameters are,
and what it writes, with `response.write` and `response.writeLn`, is the response's body, as classic ASP's
`Response.Write` writes it; a handler has no `write` of its own, as a page has, and no console.

```javascript
import { Json } from "std:json";
import { teas } from "/lib/catalog.wlg";

export function get(request, response) {
  response.headers["Content-Type"] = "application/json";
  response.write(Json.stringify(teas));
}

export async function post(request, response) {
  var text = await request.text();
  response.status = 201;
  response.headers["Content-Type"] = "text/plain";
  response.write($"Received {text.length} characters.");
}
```

As `api/teas.wlg`, `GET /api/teas` gives

```json
[{"name":"Sencha","price":6.5},{"name":"Assam","price":4.25}]
```

and `POST /api/teas`, a 201 and `Received 5 characters.` for a body of `hello`.

- The functions are named in lower case: `get`, `head`, `post`, `put`, `delete`, `patch` and `options`. A function may
  be `async`, and await what it needs.
- `HEAD` calls `head`, or `get` when there is no `head`, its body not sent.
- Any other method is a 405, whose `Allow` header lists the methods the handler answers: `PUT /api/teas` is a 405 with
  `Allow: GET, HEAD, POST`.
- A `.wlg` that exports none of those functions is a module, not a handler, and its path is a 404.
- A handler's `Content-Type` is `text/html; charset=utf-8` unless it sets another.
- The module runs anew for each request, its top level first, then the function: nothing in it lasts between requests.
- A handler that only reads its session says so, `export const session = "readonly";` ([section 9](#sessions)).

`Json.stringify` writes a value as JSON, an instance as its fields, and `Json.parse(await request.text())` reads a
JSON body, `Json` imported from `std:json` (the Standard Library Reference, "`Json` and `std:json`").

---

## 11. Waiting: `await`

A page may wait for something that takes time, such as a file, a component or a timer, with `await`, anywhere in its
blocks:

```html
<% import { delay } from "std:system"; var started = "started"; %>
<p><%= started %></p>
<% await delay(100); %>
<p>a tenth of a second later</p>
```

- `await` waits for a task, a value that completes later: `renderComponent(…)` gives one, as do `request.text()`,
  `request.form()`, `delay(milliseconds)`, and the asynchronous functions of the standard modules.
- While a page waits, its server runs other requests: a page that waits holds nothing but its own memory.
- A function that waits must be declared `async`, `async function load() { … }`, and its callers await it in turn. A
  page's own blocks may always `await`.
- Several tasks can be awaited together: `import tasks from "std:tasks";` then `await tasks.all([a, b])`.

The standard module `std:fs` reads and writes files: `import fs from "std:fs";`, then `await fs.readText(path)`,
`fs.writeText(path, text)`, `fs.exists(path)`, `fs.list(path)` and others, each asynchronous, with a synchronous form
whose name ends in `Sync`, `fs.readTextSync(path)`. A relative path is from the server process's current folder, not
the page's, so a page should build an absolute one.

The standard modules `std:http` and `std:net` reach other servers: `import http from "std:http";`, then
`await http.get(url)`, `http.post(url, body, headers)` and `http.request(method, url, body, headers)`, each giving
the response's `"status"`, `"headers"` and `"body"`, and TCP connections, `Socket.connect(host, port)`. A site
reaches only the hosts its `wasp.config` allows ([section 14](#14-configuration-waspconfig)).

The standard module `std:db` reaches databases: `import { Database } from "std:db";`, then
`using (var db = await Database.open("fish")) { … }`, its `await db.query(sql, { name: value })` the rows, an array
of maps, `await db.execute(sql, values)` the number of rows changed, and `await db.transaction(async () => { … })`,
which commits what its function does, or rolls it back where it throws. A site names its databases in its
`wasp.config` ([section 14](#14-configuration-waspconfig)); `Examples.wasp/example2` is a fish store whose pages read and
change one.

---

## 12. Errors

### Errors in a page's code

A page that does not compile, or that fails while it runs, is a 500. In **development**, the 500's page shows each error,
the file, line and column it is in, its code and its message:

```html
<p>Before the error.</p>
<% var teas = ["Sencha"]; %>
<p><%= teas[3] %></p>
```

gives, in development:

```text
500 Internal Server Error
fails.wasp(3,8): error WL3005: The index 3 is outside 0 to 0.
```

In **production**, the 500's page says only `500 Internal Server Error`, so that visitors never see the site's code.
Locally, `wasp serve` runs in development; under IIS, in production; `wasp.config`'s `"mode"` may name either
([section 14](#14-configuration-waspconfig)).

Nothing the page wrote before the error is sent: a 500 has only its own page, and none of the headers the page set.
That holds while the page's output is under 64 KB; after that, see [section 13](#13-how-a-pages-output-is-sent).

### Catching an error

A runtime error can be caught, as an exception is, with `try` and `catch`, which is given an `Error` with the error's
`message` and `code`:

```html
<% try { %>
<% var tea = ["Sencha", "Assam"][5]; %>
<p><%= tea %></p>
<% } catch (e) { %>
<p>Something went wrong: <%= e.message %> (<%= e.code %>)</p>
<% } %>
```

writes `<p>Something went wrong: The index 5 is outside 0 to 1. (WL3005)</p>`. What a page wrote inside the `try`
before the error stays written, so work out values before writing them.

### The codes

Each error has a code, `WL` and four digits, whose first digit says what kind it is:

| Codes | Kind | Examples |
|---|---|---|
| WL1xxx | the text does not parse | a missing `}` |
| WL2xxx | the code does not make sense | WL2001, a name that is not defined; WL2016, a parameter not given |
| WL3xxx | the program fails as it runs | WL3005, an index out of range; WL3006, a member a value does not have; WL3007, a key a map does not have; WL3003, a request past one of its limits; WL3004, a request cancelled |
| WL5xxx | templates | a file that cannot be included; WL5003, an include that includes itself |

### A request's limits

A request is stopped once it passes one of its limits, so that a page that loops for ever, or keeps growing a string,
does not keep its server busy:

- **its time:** 60 seconds from when the server began it, its waits for a database, a client or a lock included, as
  IIS's request timeout counts them;
- **its instructions:** 1,000,000,000, some seconds of computing, a waiting request running none;
- **its memory:** 128 MB of strings, its values all counted as strings are, by their size.

A request stopped so is a **503 Service Unavailable**, its error WL3003, shown in its page in development only and written
to the server's log; a page cannot catch it. `wasp.config`'s `"limits"` gives others ([section 14](#14-configuration-waspconfig)):

```json
{ "limits": { "requestTimeout": 120, "requestInstructions": 5000000000, "requestMemory": 512 } }
```

`"requestTimeout": null` gives a site's requests no time limit at all, for pages that are meant to stay open; their
instructions and their memory are still limited, and a reload still ends them, as below. A page that is meant to stay
open, where the site's others are not, says so itself, `<% timeout none; %>`, at the top level of its own text, as it
declares `session readonly;`, and a route handler by an export, `export const timeout = "none";`; its includes, its
layout and its components cannot, WL5011. Under IIS and behind nginx, the web server's own timeout still applies to such a
page, its `web.config`'s `requestTimeout` and nginx's `fastcgi_read_timeout`, which a page meant to stay open raises
there too. A limit of time is at most
4,294,967 seconds, about 49 days.

**When the site changes.** A request already running when the site's files change, and it loads them anew
([section 15](#15-while-you-work)), finishes on the site as it was, but only for so long: once the **drain** has passed
since the change, 60 seconds or the site's `"requestTimeout"`, whichever is longer, it is stopped as a request past its
time is, a 503, WL3003, the log saying that a reload ended it, so that the old version of the site is gone within the
drain. By default, then, a change stops no request its time limit would not. `"limits"`' `"drain"` gives another, in
seconds, which stops even a request with no time limit:

```json
{ "limits": { "requestTimeout": null, "drain": 30 } }
```

A request stopped at a breakpoint is not stopped so while it is: the time it waits for you does not count.

**What a client may send.** The server refuses a request whose line is longer than 8,192 bytes, a 414, or whose headers
are more than 32,768 bytes, or 100 headers, a 431; one whose line and headers take more than 30 seconds to arrive, a 408;
and a body that comes slower than 240 bytes a second once its first 5 seconds of waiting are over; and it closes a kept
connection that has waited 130 seconds for its next request. `"limits"` gives others, in bytes and in seconds:

```json
{ "limits": { "requestLine": 16384, "requestHeaders": 65536, "requestArrival": 10, "keepAlive": 60 } }
```

Each applies to `wasp serve` whichever server it runs, ASP.NET Core's under IIS too; behind nginx or Apache, through
FastCGI, the web server's own limits apply.

A request stopped by its time stops at once where it computes, and where it waits, once its wait gives way, a wait for
a session another request holds once that wait ends. While its server runs other requests on the same worker, a page
that computes without waiting holds that worker until it is stopped. Under the debugger, its time is not counted, a
request stopped at a breakpoint waiting for you. Under IIS, its ASP.NET Core Module gives a request two minutes too,
its `web.config`'s `requestTimeout`, which a site whose `"requestTimeout"` is longer raises with it.

### The log

A page or a handler writes to its site's log with `std:log`, at one of four levels, `log.debug(text)`,
`log.info(text)`, `log.warn(text)` and `log.error(text)`, each a string:

```html
<% param request; %>
<% import log from "std:log"; %>
<% var name = request.query?["name"] ?? "nobody"; %>
<% log.info("Greeted " + name); %>
<p>Hello, <%= name %>.</p>
```

Each line is its time, in UTC, its level, its request, as the debugger names it, its method, its path and its number,
and its text; asked for as `/greet?name=Anna`, then as `/greet`:

```text
2026-11-25T08:24:43.520Z INFO GET /greet #3 Greeted Anna
2026-11-25T08:24:43.595Z INFO GET /greet #4 Greeted nobody
```

A request is numbered when it first writes to the log, so that every line of one request has its number. WASP writes
there too: a request's errors, at `error`, whatever the mode, those a 500 or a 503 shows in development and hides in
production, `ERROR GET /greet #1 greet.wasp(2,4): error WL1001: …`; a request refused as another site's
([section 9](#requests-from-other-sites)) at `warn`; and its reloads at `info`. A runtime error nothing caught is
followed by the calls it was raised in, the innermost first, each a line of its own, in the file it is in, a page's
top level named so; a page, `cart.wasp`, that adds up prices with a module's `total`, given an item without one:

```text
2026-11-25T08:46:34.592Z ERROR GET /cart #1 lib/prices.wlg(2,12): error WL3007: The map has no key "price".
2026-11-25T08:46:34.594Z ERROR GET /cart #1   at priceOf in lib/prices.wlg(2,12)
2026-11-25T08:46:34.594Z ERROR GET /cart #1   at total in lib/prices.wlg(7,40)
2026-11-25T08:46:34.594Z ERROR GET /cart #1   at the top level of cart.wasp(2,15)
```

An asynchronous function resumed after an `await` begins its calls again: an error raised after one names the calls
from that function on. A text is kept to one line, a line break
in it written `\n`, so that no value a client sent makes a line of its own.

Where the log goes: under `wasp serve`, the terminal; under FastCGI, the standard error, which a service manager's
journal keeps, and Apache's error log under `mod_fcgid`; under IIS, its `stdout` log, which is for finding why a site
does not start, not for a site in production. There, name a file, `wasp.config`'s `"log"`
([section 14](#14-configuration-waspconfig)):

```json
{ "log": { "file": "C:/inetpub/logs/WebLingo/mysite.log", "level": "info" } }
```

WASP writes a file a day, the day's date in its name, `mysite-2026-11-25.log`, the folder made where it is not there.
The file is outside the site, or the site does not start: in it, the file would be served, and the site reloaded at each
line. WASP writes it itself, as the host, so that the site's pages need no permission to write it and cannot change
it; its process's account, an application pool's under IIS, writes the folder. `"level"` is the lowest level written,
`info` unless it says otherwise, a line below it left out.

---

## 13. How a page's output is sent

A page's output is kept until it reaches **64 KB**, then sent as the page writes it:

- **Most pages are smaller**, and are sent whole once they have run, so that a page that fails is still a clean 500,
  and its status and headers can be set anywhere in it.
- **A longer page starts being sent once it reaches 64 KB.** From then on, its status and headers are fixed, and if it
  fails, its response ends where it was: the client sees the page unfinished, the error is written to the server's log
  and, in development, at the end of what was sent.
- **A client that reads slowly makes the page wait** for it, as at an `await`, its server running other requests
  meanwhile, so that a slow client never fills the server's memory.
- **A client that takes nothing for 30 seconds** has its request cancelled, its response ended. `wasp.config`'s
  `"sendTimeout"` gives another time, in seconds.

---

## 14. Configuration: `wasp.config`

A site's configuration is a JSON file, `wasp.config`, in its root:

```json
{ "mode": "development", "sendTimeout": 60 }
```

| Setting | What it says | Default |
|---|---|---|
| `"mode"` | `"development"`, errors shown in their pages, or `"production"`, not shown | development under `wasp serve`, production under IIS |
| `"sendTimeout"` | how many seconds a page waits for a client that has taken nothing of its output, more than 0 | 30 |
| `"schemes"` | folders of native modules, each a name and a folder: `{ "native": "C:/WebLingo/native" }` | `std`, WebLingo's own |
| `"network"` | the hosts the site's pages and handlers may reach, through `std:http` and `std:net`: `{ "allow": ["api.example.com"] }` | none |
| `"databases"` | the site's databases, each a name its scripts open it by and its connection string, or where that is kept: `{ "fish": "sqlite:../database/fish.db" }` | none |
| `"files"` | the folders the site's pages and handlers may read, and read and write, through `std:fs`: `{ "read": ["../data"], "write": ["../uploads"] }` | none |
| `"com"` | the COM objects the site's pages and handlers may make, on Windows, through `windows:com`: `{ "allow": ["ADODB.Connection"] }`, `"*"` any; any other a `ComPermissionError`, WL3018 | none |
| `"env"` | what the site's pages and handlers may read of the machine's environment, through `std:env`: `{ "machineName": true, "variables": ["PATH", "APP_*"] }` | nothing |
| `"maxBodySize"` | the largest body a request may have, in bytes, a larger one a 413 (section 9, "Uploaded files") | 30,000,000 |
| `"uploads"` | what a form's files may be: `{ "inMemory": 65536, "maxSize": 20000000, "folder": "../tmp" }`, how many bytes of a file are kept in memory, the rest in a temporary file, the most its files may take in all, and the folder their temporary files are made in, outside the site | 64 KB, `"maxBodySize"`, the system's temporary folder |
| `"sessions"` | where the site keeps its sessions, and how long: `{ "store": "database", "database": "main", "table": "wasp_sessions", "create": false, "timeout": 20, "lockTimeout": 30 }`, the server's memory, `"memory"`, or a table of a database `"databases"` names, `"database"`, the table made where it is missing where `"create"` is `true`; the minutes a session lasts once no request uses it, and the seconds a request waits for a session another has (section 9, "Sessions" and "Sessions in a database") | the server's memory, `wasp_sessions`, `false`, 20 minutes, 30 seconds |
| `"headers"` | the headers every response has, each optional: `{ "nosniff": true, "referrerPolicy": "strict-origin-when-cross-origin", "frameAncestors": ["self"] }`, `X-Content-Type-Options: nosniff`, the `Referrer-Policy`, or `null` for none, and the sites whose pages may show the site's in a frame, `"self"`, `"none"`, a scheme or a host, or `null` for any (section 9, "Security headers") | all four headers, as shown |
| `"proxies"` | the proxies in front of the site that it trusts, each an address or a range, `["127.0.0.1", "10.0.0.0/8"]`: a request from one of them takes its client, `request.clientAddress`, and its scheme, `request.scheme`, from its `Forwarded`, or its `X-Forwarded-For` and `X-Forwarded-Proto`, the client the nearest address that is not a trusted proxy's; under `wasp serve`, on either server; behind nginx or Apache, through FastCGI, the web server gives the client | none: forwarding headers are not believed |
| `"serve"` | the extensions of files WASP does not send by default that the site sends: `{ "allow": [".pfx"] }`, each with its dot, of those section 2 lists | none |
| `"log"` | the site's log, each optional: `{ "file": "C:/inetpub/logs/WebLingo/mysite.log", "level": "info" }`, a file a day, outside the site, from this file's folder, and the lowest level written, `"debug"`, `"info"`, `"warn"` or `"error"` (section 12, "The log") | the host's own log, `"info"` |
| `"limits"` | a request's limits, each optional: `{ "requestTimeout": 60, "requestInstructions": 1000000000, "requestMemory": 128, "drain": 60, "requestLine": 8192, "requestHeaders": 32768, "requestArrival": 30, "keepAlive": 130 }`, the seconds it may take, its waits included, more than 0, or `null` for no time limit, the instructions it may run, the megabytes of strings it may hold, 1 to 1,048,576, the seconds it may go on running on the site as it was before its files changed, more than 0, the bytes of its line and of its headers, 1 to 1,048,576, the seconds its line and headers may take to arrive, and the seconds a kept connection may wait for its next request, each more than 0; seconds at most 4,294,967; one passed a 503, WL3003, or the client's request refused, a 414, a 431 or a 408 (section 12, "A request's limits"); beside them, a script's workers' limits under `weblingo`, `"workers"`, `"workerTimeout"`, `"workerInstructions"` and `"workerMemory"`, which a site's pages, running no workers, do not use (the Standard Library Reference, `std:workers`) | 60 seconds, 1,000,000,000 instructions, 128 MB, a drain of 60 seconds or `"requestTimeout"`, whichever is longer, 8,192 and 32,768 bytes, 30 and 130 seconds |
| `"mail"` | the mail servers the site's pages and handlers send through, with `std:mail`, each a name and its settings: `{ "default": { "host": "smtp.example.com", "port": 587, "user": "…", "password": { "env": "SMTP_PASSWORD" } } }` | none |
| `"base"` | the folder of the site this one lies over, from this file's folder: `"C:/WebLingoForum/site"`, its files taken where this site has none of its own, its `wasp.config`'s settings where this one gives none ([A site over a base](#a-site-over-a-base)) | none |

A setting the file does not know, or a value it does not allow, stops the site from starting, with a message that says
which. `wasp serve` and `wasp fastcgi` read the site's configuration when they start. Restart the host after changing it.

A site reaches other servers only as its `"network"` allows, each host named as an address names it, a name or an
address, `api.example.com` or `127.0.0.1`, at any of its ports, its letters' case aside:

```json
{ "network": { "allow": ["api.example.com", "localhost"] } }
```

A request to any other host is a `NetworkPermissionError`, WL3017, where the script makes it,
`'example.com' is not a host this application may reach.`, which a `catch` may catch; without the setting, a site
reaches no host. A script that `weblingo` runs reaches any.

A site reaches files only in the folders its `"files"` lists: those in `"read"` to read, list and look for, those in
`"write"` to read and write as well. A folder in the list is from the configuration's folder when it is relative, and
a path in a script from the site's root, its folder, under `wasp serve` and IIS alike:

```json
{ "files": { "read": ["../data"], "write": ["../uploads"] } }
```

```javascript
var teas = await fs.readText("../data/teas.json");        // read, as ../data is listed
await fs.writeText("../uploads/order.txt", order);         // written, as ../uploads is listed to be written
await fs.writeText("../data/teas.json", "[]");             // a FilePermissionError: ../data is listed to be read only
```

A path is checked by where it really leads: `..`, a link and a junction followed, its letters' case aside on Windows;
and a Windows device's name, `NUL` or `COM1`, is never reached. A path the site may not reach is a `FilePermissionError`,
WL3016, where the script tries it, which a `catch` may catch: `'../data/teas.json' cannot be written: it is not in a
folder this site may write.`; without the setting, a site reaches no file. A SQLite database a script names by its
own connection string, `Database.open("sqlite:../uploads/orders.db")`, is checked as a file is, to be written unless
it is opened read-only, `Mode=ReadOnly`; one `"databases"` names is the site's own, and is not. Keep the folders outside
the site's folder, as a database is, since `wasp` serves a file in it to whoever asks for it. A script that `weblingo`
runs reaches any file, a relative path from the current folder.

A database's connection string is its provider's name, a colon, and what the provider reads: SQLite's, a file's
path, from the configuration's folder when it is relative. Keep the database outside the site's folder,
`../database/fish.db`, since `wasp` would serve a file in it to whoever asked for it, and give the site's application
pool write access to the folder it is in, where SQLite writes its journal. SQL Server's is SqlClient's own connection
string: `{ "todo": "sqlserver:Server=localhost;Database=Todo;Integrated Security=true;TrustServerCertificate=true" }`.
With `Integrated Security`, the site reaches the database as the account it runs as: under IIS, its application pool's.
`TrustServerCertificate` accepts the certificate a server made for itself when it was installed; a server the site
reaches over a network should have one its clients trust instead. WebLingo reaches SQL Server by TCP/IP, which the
server must have enabled, as SQL Server's Developer and Express editions do not when they are installed.

For MySQL or PostgreSQL, add the connection to `"databases"` in `wasp.config` and pass its name to `Database.open`.
Use a MySqlConnector string with the `mysql:` prefix or an Npgsql string with the `postgresql:` prefix.

A site's pages and handlers have no console: `std:console`, whose `writeLn`, `readLine` and the others a script that
`weblingo` runs writes and reads its console with, cannot be imported, WL2015; a page writes its response, and reads
what its request brings.

A site reads the machine's environment, through `std:env`, only as its `"env"` allows: the machine's name with
`"machineName": true`, its processor's architecture, the running `wasp`'s and the operating system,
`Environment.architecture`, `processArchitecture` and `os`, with `"platform": true`, and the environment variables
`"variables"` lists, a name ending in `*` allowing those it
begins, `"*"` any, their names compared as the platform compares them, ignoring case on Windows:

```json
{ "env": { "machineName": true, "variables": ["APP_*"] } }
```

```html
<% import { Environment } from "std:env"; %>
<p>Served by <%= Environment.machineName %>, release <%= Environment.variable("APP_RELEASE") %></p>
```

Anything else reads as `null`, as if it were not there, so that a page cannot tell what it may not read from what is
not set; without the setting, a site reads none of it. A site sets no variable: `Environment.setVariable` is an
`EnvironmentPermissionError`, WL3019, which a `catch` may catch. Under IIS, a site's variables are those its
`web.config` gives `wasp` (`<environmentVariables>`), beside the server's own, and may hold secrets: list only those
its pages need. A script that `weblingo` runs reads all of it, and sets variables, for its own process.

A site sends mail with `std:mail` through the servers its `"mail"` names, which need no `"network"` of their own;
a page that names a server of its own, `{ host: "…" }`, reaches only a host `"network"` allows. A send that names
no server uses `"default"`, or the one there is:

```json
{ "mail": { "default": { "host": "smtp.example.com", "port": 587, "user": "shop@example.com",
                          "password": { "file": "../secrets/smtp.txt" } } } }
```

```html
<% import mail from "std:mail";
   await mail.send({ from: "shop@example.com", to: "ann@example.com", subject: "Your order", text: "On its way." }); %>
<p>We have sent you a confirmation.</p>
```

A password, as a database's connection string, need not be written in `wasp.config`: `{ "env": "SMTP_PASSWORD" }`
reads it from an environment variable, which IIS gives `wasp` from the site's `web.config`, and `{ "file":
"../secrets/smtp.txt" }` from a file outside the site, from `wasp.config`'s folder; one inside the site, whose files
may be served, stops it from starting. Either is read each time it is used, so that a password changed there is
used at once, and no page sees it, nor any error, the development error page included. The Standard Library
Reference, section 31, describes the module, its messages and its servers' settings.

Its name is `wasp.config`, in lower case: Linux tells `WASP.config` from it. A `weblingo.config` configures a folder of
scripts that `weblingo` runs, not a site, and `wasp` does not read it. `wasp run` and `wasp check` read the
`wasp.config` in the page's folder for its `"schemes"`. For `run` and `check`, the VM comes from that file unless
`--root` is given, in which case it comes from the site's root configuration.

---

### A site over a base

One installed site may serve many clients, each with its own look, texts, code and settings, kept in a folder of the
client's own: the client's folder is the site's root, with its own `web.config` and `wasp.config`, which names the site
it lies over, its base:

```text
C:\WebLingoForum\site\        the forum, installed once: its pages, layouts, components, modules, stylesheets, wasp.config
C:\Clients\acme\site\        Acme's: web.config, wasp.config, and only the files it changes
    wasp.config                { "base": "C:/WebLingoForum/site", "databases": { "forum": "sqlite:../data/forum.db" } }
    layouts\main.wasp          Acme's own layout, which every page of the forum's is laid out by
    css\forum.css              Acme's own stylesheet
```

- A file is the client's where its folder has one, and the base's otherwise, at the same path: a page, an include, a
  layout, a component, a module, a stylesheet or a picture. The site's routes are both folders' pages and files.
- A setting of the client's `wasp.config` replaces the base's of that name whole, `"databases"` all its databases; a
  setting the client's does not give is the base's, a path in it from the base's folder. Give a client its own
  database, its own `"files"` and its own `"mail"`, so that it shares nothing of the base's.
- `wasp serve` and `wasp fastcgi` watch both folders: a change to either reloads the site.
- A script's paths, through `std:fs`, are from the client's folder; the base's files are only where `"files"` lists
  their folder.
- A base names no base of its own, and neither folder is within the other.
- `wasp run` and `wasp check` read a page, and what it includes, from its own folder, without its base.

## 15. While you work

- **Changes are served at once.** `wasp serve` watches the site's folder. When files change, it waits until they have
  stopped changing for a quarter of a second, then loads the site anew; requests already running finish on the old
  version, or are stopped once its drain has passed, 60 seconds by default ([section 12](#a-requests-limits)). A change
  that breaks the site's routes, such as a page and a route handler for one path, leaves the old
  version served, and `wasp serve` says why. A change that breaks a page makes that page a 500 until it is fixed.
- **Development shows errors.** Run locally, the site is in development unless `wasp.config` says otherwise
  ([section 12](#12-errors)).
- **Pages are compiled when first asked for**, and kept in memory; WASP never writes compiled files into a site's
  folder.
- **`wasp check`** reports a page's errors without running it, and `wasp run` runs it, which is quicker than a browser
  for a page that needs no request ([section 16](#16-the-wasp-command-line)).
- **Editors colour pages and modules**: the repository's `Editors/` folder has syntax colouring for `.wasp` and `.wlg`
  files, and its `README.md` says how to install it.
- **VS Code debugs a page, and a site**: with WebLingo's extension, a breakpoint in a page, an include, a layout, a
  component or a module it imports stops the page there, run as `wasp run` runs it, its parameters from the launch, with
  the call stack and the variables to look at, and stepping. A site is debugged as `wasp serve` serves it: a request a
  browser sends stops at a breakpoint, alone, each request a thread of the debugger's, `GET /cart #12`, while the site
  serves the others; the debugger's pause stops every request until it goes on. A breakpoint's condition stops only
  the requests it names, `request.path == "/cart"`, read without running the page's code. VS Code starts `wasp
  --debug-adapter` for either; the `Editors/` folder's `README.md` says how to set it up, and how any other debugger
  that speaks the Debug Adapter Protocol starts it, Visual Studio's own Debug Adapter Host among them, with no extension.

---

## 16. The `wasp` command line

```text
wasp serve [--root <folder>] [--port <port>] [--host aspnetcore|standalone] [--scheme <name>=<folder> …]
wasp fastcgi [--root <folder>] [--port <port> | --socket <path> | --stdin] [--scheme <name>=<folder> …]
wasp run [--root <folder>] [--scheme <name>=<folder> …] <page.wasp> [name=value …]
wasp check [--root <folder>] [--scheme <name>=<folder> …] <page.wasp>
wasp sessions create [--root <folder>] [--database <name>] [--table <name>] [--sql]
wasp --version
```

- **`wasp serve`** serves the site whose root is `--root`, the current folder by default, on `--port`, 8080 by default,
  to the local machine only. Under IIS, IIS starts it ([section 17](#17-deploying-to-iis)). `--host standalone` serves
  it with WebLingo's own HTTP server in place of ASP.NET Core's, HTTP/1.1, for a site behind a reverse proxy that gives
  it TLS and HTTP/2; it is being proved, and ASP.NET Core's stays the default, and IIS's, until it has been.
- **`wasp fastcgi`** serves the site whose root is `--root` to a web server other than IIS, nginx or Apache, through
  FastCGI, on `--port`, 9000 by default, on a Unix socket, `--socket`, or on the socket or pipe a web server that starts
  it gives it as its standard input, `--stdin` ([section 18](#18-serving-through-nginx-or-apache-fastcgi)).
- **`wasp run`** compiles a page and runs it, writing its output to the console. `--root` names the site's root, from
  which a path that starts with `/` is found; without it, the page's own folder is the root. The values after the page,
  `name=value`, are given to its parameters, each a string:

  ```html
  <% param name = "World"; %>
  <p>Hello, <%= name %>!</p>
  ```

  `wasp run cli.wasp` writes `<p>Hello, World!</p>`, and `wasp run cli.wasp name=Anna`, `<p>Hello, Anna!</p>`.
  `wasp run --timeout 300 cli.wasp` sets the regular expression match limit for this run to 300 milliseconds.
  The default is one second.
- **`wasp check`** compiles a page and reports its errors, without running it: `wasp check broken.wasp` writes
  `broken.wasp(1,8): ERROR WL2001: 'nope' is not defined.`
- **`wasp sessions create`** makes the table a site keeps its sessions in, in the database its `wasp.config`'s
  `"sessions"` names, or `--database` names, the table `wasp_sessions` unless `"sessions"` or `--table` names another;
  `--sql` prints its SQL instead ([section 9](#sessions-in-a-database)).
- **`--scheme name=folder`** names a folder of native modules, over the configuration's.

Command-line diagnostics go to standard error. An error starts with the file, line, column and `ERROR CODE`.
If there is a WebLingo exception class, it follows the code in parentheses, as in `ERROR WL3005 (IndexError)`.
Terminals that support bold display `ERROR` in bold. The exit code says how it went.

| Exit code | Meaning |
|---|---|
| 0 | it ran, or it compiled without errors |
| 1 | the page has compilation errors |
| 2 | the command line was wrong, which the standard error says before how `wasp` is used |
| 3 | the page failed while it ran |
| 4 | a file could not be read or written |

---

## 17. Deploying to IIS

WebLingo is installed **once on a server**, and every site uses it, as PHP is installed once for every PHP site: a site
holds only its own files, its pages beside its `web.config`.

Once for the server:

1. Turn on IIS (Windows Features' **Internet Information Services**, or, on Windows Server, the **Web Server (IIS)**
   role), then install the **ASP.NET Core Hosting Bundle** for .NET 10, which brings the .NET and ASP.NET Core
   runtimes and IIS's ASP.NET Core Module. Install the bundle after IIS: one installed before it does not add the
   module to IIS until it is run again and repaired.
2. On a Windows machine with the .NET 10 SDK and the repository, publish WebLingo from the solution's folder,
   `WebLingo`, with `powershell -ExecutionPolicy Bypass -File publish.ps1`, which publishes it into
   `WebLingo/publish/WebLingo`: `wasp.exe`, `weblingo.exe`, their libraries, and the standard modules, `std\`.
3. Copy that folder to the server as `C:\Program Files\WebLingo`. On the server itself, when it has the SDK and the
   repository, `powershell -ExecutionPolicy Bypass -File publish.ps1 -Destination "C:\Program Files\WebLingo"`, from
   an elevated prompt, does both.

For each site:

4. Copy the site's folder to the server, with a `web.config` beside its pages:

   ```xml
   <?xml version="1.0" encoding="utf-8"?>
   <configuration>
     <location path="." inheritInChildApplications="false">
       <system.webServer>
         <handlers>
           <add name="aspNetCore" path="*" verb="*" modules="AspNetCoreModuleV2" resourceType="Unspecified" />
         </handlers>
         <aspNetCore processPath="C:\Program Files\WebLingo\wasp.exe" arguments="serve" hostingModel="outofprocess"
                     stdoutLogEnabled="false" stdoutLogFile="C:\inetpub\logs\WebLingo\mysite" />
       </system.webServer>
     </location>
   </configuration>
   ```

5. In IIS, add a website whose physical path is that folder, in an application pool whose .NET CLR version is
   **No Managed Code**.

IIS then gives every request to the ASP.NET Core Module, which starts `wasp.exe serve` in the site's folder and passes
the requests to it. If WebLingo is installed elsewhere, change `processPath` to match.

- **What `wasp` writes**, its errors when it cannot start included, and the site's log, unless `wasp.config` names a
  file for it ([section 12](#the-log)), goes to IIS's log of it when `stdoutLogEnabled` is `true`: to
  `C:\inetpub\logs\WebLingo`, outside the site, in files whose names start with the site's. Create that folder first,
  and give the site's application pool (`IIS AppPool\<pool>`) write access to it. Keep the log outside the site: in
  it, it would be served, and every line written to it would reload the site. IIS's log of it is for finding why a site
  does not start: a site in production names its own, `"log": { "file": "C:/inetpub/logs/WebLingo/mysite.log" }`.
- **Updating WebLingo** updates every site at once. A running `wasp.exe` keeps its files in use, so stop the sites'
  application pools, or IIS (`iisreset /stop`), before copying the new version over the old, and start them again
  after.
- **Under IIS, a site runs in production** unless its `wasp.config` says otherwise.
- **A body larger than 30,000,000 bytes** is refused by IIS's request filtering before `wasp` sees it, whatever its
  `wasp.config`'s `"maxBodySize"` says: a site that takes larger uploads raises IIS's limit too, in its `web.config`'s
  `<system.webServer>`, `<security><requestFiltering><requestLimits maxAllowedContentLength="50000000" />`
  `</requestFiltering></security>`.

The repository's `Examples.wasp/example1` is such a site, and `Examples.wasp/README.md` walks through deploying it.

---

WebLingo Forum, `Examples.wasp/WebLingoForum`, a whole site with its database, its members' files and a client's
forum over it, is deployed so in its README's "Deploying it", under IIS and behind nginx and Apache.

## 18. Serving through nginx or Apache: FastCGI

A web server other than IIS, nginx or Apache httpd, runs a WASP site through FastCGI, as it runs PHP's: `wasp fastcgi`
serves the site to the web server, which passes it every request.

```text
wasp fastcgi [--root <folder>] [--port <port> | --socket <path> | --stdin] [--scheme <name>=<folder> …]
```

It runs in one of two ways:

- **As PHP-FPM runs, kept running.** `wasp fastcgi --root /var/www/mysite --port 9000` listens on the local machine's
  port, 9000 by default, PHP-FPM's, and writes `WASP serving FastCGI on 127.0.0.1:9000 (Production); Ctrl+C stops it.`;
  `--socket /run/wasp/mysite.sock` listens on a Unix socket instead, made at that path and removed when it stops. The
  server's service manager starts it and keeps it running: systemd on Linux, or whatever starts services on Windows.
  Each site has its own `wasp fastcgi`, on a port or a socket of its own.
- **As Apache's mod_fcgid starts PHP, on demand.** The web server starts `wasp fastcgi` itself, giving it a socket, or
  on Windows a named pipe, as its standard input. `wasp` notices one and serves it, with no `--port` or `--socket`;
  `--stdin` says so outright. What it writes then goes to its error output, which the web server logs.

Either way, a site runs in production unless its `wasp.config` says otherwise ([section 14](#14-configuration-waspconfig)),
and `wasp` reloads it when its files change, as `wasp serve` does: a page a request reads while it is replaced is served
whole, its old version or its new. Replace a site's files by copying them beside the site and moving them in, or with a tool
that does, such as `robocopy`: on Windows, a move over a file a request is reading is refused for that moment, and the tool
tries again. Pass the web server every request, the site's other
files' included: `wasp` serves those too, and never serves the site's modules or its configuration, which a web server
serving the folder itself would.

**These configurations have been tried on Windows**, with nginx 1.30.6 and Apache httpd 2.4.71, Apache Lounge's build,
with mod_fcgid 2.3.10, each as written here but for its paths, which had no spaces; not yet on Linux, nor the systemd
service below.

### nginx

With `wasp fastcgi --root /var/www/mysite --port 9000` running:

```nginx
upstream wasp {
    server 127.0.0.1:9000;
    keepalive 16;
}

server {
    listen 80;
    server_name example.com;

    location / {
        include fastcgi_params;
        fastcgi_keep_conn on;
        fastcgi_pass wasp;
    }
}
```

`fastcgi_params`, which comes with nginx, gives `wasp` the request's method, path, query and headers. `fastcgi_keep_conn`
and the upstream's `keepalive` keep nginx's connections to `wasp` from one request to the next, so that a request need not
open one of its own: on Windows, nginx served six times as many requests a second so, the clients 16 at once, as with a
connection for each request, `fastcgi_pass 127.0.0.1:9000;` alone, which works too. On a Unix socket,
`fastcgi_pass unix:/run/wasp/mysite.sock;`. On Linux, a systemd service keeps `wasp` running, for example
`/etc/systemd/system/wasp-mysite.service`, with WebLingo installed in `/opt/weblingo`, where its installers for Linux
install it:

```ini
[Unit]
Description=WASP site mysite (FastCGI)
After=network.target

[Service]
ExecStart=/opt/weblingo/wasp fastcgi --root /var/www/mysite --port 9000
User=www-data
Restart=always

[Install]
WantedBy=multi-user.target
```

### Apache httpd, to a running `wasp`

Through `mod_proxy_fcgi`, as Apache reaches PHP-FPM, with `wasp fastcgi` running as for nginx:

```apache
LoadModule proxy_module modules/mod_proxy.so
LoadModule proxy_fcgi_module modules/mod_proxy_fcgi.so

<VirtualHost *:80>
    ServerName example.com
    ProxyPass "/" "fcgi://127.0.0.1:9000/"
</VirtualHost>
```

### Apache httpd, starting `wasp` itself

Through `mod_fcgid`, which starts `wasp fastcgi` when the site is first requested, and more of them as requests come,
each serving one request at a time, as it starts PHP:

```apache
LoadModule fcgid_module modules/mod_fcgid.so

<VirtualHost *:80>
    ServerName example.com
    DocumentRoot "C:/Sites/mysite"
    <Directory "C:/Sites/mysite">
        SetHandler fcgid-script
        Options +ExecCGI
        FcgidWrapper "\"C:/Program Files/WebLingo/wasp.exe\" fastcgi --root C:/Sites/mysite" virtual
        Require all granted
    </Directory>
</VirtualHost>
```

mod_fcgid starts `wasp` in a folder of its own choosing, so `--root` names the site's. Each `wasp` it starts loads the
site anew, so the first request to each is slower than the rest.

---

## 19. Sites sharing a server

A server may serve many sites, each its own folder and its `wasp.config`, as it serves many PHP sites. Each `wasp`
serves one site: under IIS, the ASP.NET Core Module starts a `wasp.exe` for each; under FastCGI, each `wasp fastcgi`
serves the site its `--root` names. What keeps one site from another is in two places, and only one of them is a wall.

**What a site's configuration limits.** WASP checks what a site's pages and handlers reach through WebLingo's standard
modules against its `wasp.config` ([section 14](#14-configuration-waspconfig)): the folders `std:fs` reads and writes,
`"files"`; the hosts `std:http` and `std:net` reach, `"network"`; the COM objects `windows:com` makes, `"com"`; what
`std:env` reads, `"env"`; and its databases. A page that tries for more gets an error, a `FilePermissionError` or
another of its kind, and nothing reaches the machine. These checks are WASP's own, made in the site's process: a
page's WebLingo code reaches nothing past them, a page with a mistake in it or one a visitor's input leads astray
included, but they do not keep what runs as the process itself.

**What they do not limit.** Whatever runs as the process's own code reaches what its account may, beyond any of
`wasp.config`'s lists:

- **Native modules.** A module a site's schemes name, `"schemes": { "native": "…" }`, is .NET code loaded into the
  site's process and trusted as WebLingo's own standard modules are: it reaches every file, host and program the
  account may.
- **COM objects** `"com"` allows reach the machine as the account does: `WScript.Shell` runs programs, and
  `Scripting.FileSystemObject` reads and writes any file the account may, whatever `"files"` says.
- **A database** reached with the account's own credentials, SQL Server's `Integrated Security`, is reached as the
  account, which may reach other sites' databases too.
- **Its `wasp.config` itself.** Whoever writes a site's configuration decides what its pages reach, its native modules
  and its COM objects included: it is trusted as the site's code is.

So the wall between sites is the operating system's: each site's process, under an account of its own, which reaches
its own folders and nothing else.

- **Under IIS**, give each site an application pool of its own. Each pool's `wasp.exe` runs as the pool's identity,
  `IIS AppPool\<pool>`: give it read access to its site's folder and write access only to the folders its site writes,
  its log's and its uploads' among them, and nothing of another site's. Sites in one pool run as one account, and so
  reach each other's files through anything the account may do.
- **Under FastCGI**, run each site's `wasp fastcgi` as a user of its own: a service of its own for each, with its own
  user (systemd's `User=`), or, under `mod_fcgid`, a virtual host for each site whose `FcgidWrapper` starts `wasp` as
  that site's user, through Apache's `suexec` on Linux.
- **Each site's temporary files** in a folder of its own, `"uploads": { "folder": "…" }`, where the system's temporary
  folder is shared.
- **WebLingo itself**, installed once, is shared by every site, read only: no site's account may write its folder, or
  a site could change what every other site runs.

**Sites you do not trust**, other people's, as a host's customers' are, each run in a process of its own, under an
account of its own, as above, with no schemes but WebLingo's own `std`, no `"com"`, and the account allowed nothing
but its own site's folders: WASP's checks keep the site's pages to its configuration, and the operating system keeps
the site to its account.

---

## 20. What is not there yet

These are not there yet, and are planned or being designed:

- **Debugging a site under IIS or FastCGI**: a site is debugged only as the debugger serves it
  ([section 15](#15-while-you-work)).
- **A standalone server**, without IIS, nginx or Apache, is planned. WASP has been tried on Windows, through IIS's
  ASP.NET Core Module, and through FastCGI behind nginx and Apache httpd; Linux is built and tested, but no site has
  been served on it yet.
