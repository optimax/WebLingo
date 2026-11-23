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
shown, but names and details may still change before a first release. [What is not there yet](#19-what-is-not-there-yet)
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
19. [What is not there yet](#19-what-is-not-there-yet)

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
changing the machine's policy, where Windows's default runs no script.

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
- every file and folder whose name starts with `.`, such as `.git`.

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
| `await request.text()` | the request's body, as text |
| `await request.form()` | a form's fields, as a map |
| `await request.read()` | the body's next piece, as bytes, or `null` at its end |

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
  is how a browser posts an ordinary form; a name given twice has its last value. A body of another type is an error.

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
and give the same; read in pieces, with `read()`, it cannot then be read whole too. Files uploaded through a form
(`multipart/form-data`) cannot be read yet.

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

### The response

| Member | What it is |
|---|---|
| `response.status` | the status, 200 until set: `response.status = 404;` |
| `response.headers` | the response's headers: `response.headers["Content-Type"] = "text/plain";` |
| `response.write(value)` | writes a value's text to the response's body, as `write` writes it: `response.write("done");` |
| `response.writeLn(value)` | the same, then a line break |

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

A page run by `wasp run` has no request: there, a page's parameters are values given on the command line
([section 16](#16-the-wasp-command-line)), and a page that declares `request` fails.

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
| WL3xxx | the program fails as it runs | WL3005, an index out of range; WL3006, a member a value does not have; WL3007, a key a map does not have; WL3004, a request cancelled |
| WL5xxx | templates | a file that cannot be included; WL5003, an include that includes itself |

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
| `"databases"` | the site's databases, each a name its scripts open it by and its connection string: `{ "fish": "sqlite:../database/fish.db" }` | none |
| `"files"` | the folders the site's pages and handlers may read, and read and write, through `std:fs`: `{ "read": ["../data"], "write": ["../uploads"] }` | none |
| `"com"` | the COM objects the site's pages and handlers may make, on Windows, through `windows:com`: `{ "allow": ["ADODB.Connection"] }`, `"*"` any; any other a `ComPermissionError`, WL3018 | none |
| `"env"` | what the site's pages and handlers may read of the machine's environment, through `std:env`: `{ "machineName": true, "variables": ["PATH", "APP_*"] }` | nothing |

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

Its name is `wasp.config`, in lower case: Linux tells `WASP.config` from it. A `weblingo.config` configures a folder of
scripts that `weblingo` runs, not a site, and `wasp` does not read it. `wasp run` and `wasp check` read the
`wasp.config` in the page's folder for its `"schemes"`. For `run` and `check`, the VM comes from that file unless
`--root` is given, in which case it comes from the site's root configuration.

---

## 15. While you work

- **Changes are served at once.** `wasp serve` watches the site's folder. When files change, it waits until they have
  stopped changing for a quarter of a second, then loads the site anew; requests already running finish on the old
  version. A change that breaks the site's routes, such as a page and a route handler for one path, leaves the old
  version served, and `wasp serve` says why. A change that breaks a page makes that page a 500 until it is fixed.
- **Development shows errors.** Run locally, the site is in development unless `wasp.config` says otherwise
  ([section 12](#12-errors)).
- **Pages are compiled when first asked for**, and kept in memory; WASP never writes compiled files into a site's
  folder.
- **`wasp check`** reports a page's errors without running it, and `wasp run` runs it, which is quicker than a browser
  for a page that needs no request ([section 16](#16-the-wasp-command-line)).
- **Editors colour pages and modules**: the repository's `Editors/` folder has syntax colouring for `.wasp` and `.wlg`
  files, and its `README.md` says how to install it.

---

## 16. The `wasp` command line

```text
wasp serve [--root <folder>] [--port <port>] [--scheme <name>=<folder> …]
wasp fastcgi [--root <folder>] [--port <port> | --socket <path> | --stdin] [--scheme <name>=<folder> …]
wasp run [--root <folder>] [--scheme <name>=<folder> …] <page.wasp> [name=value …]
wasp check [--root <folder>] [--scheme <name>=<folder> …] <page.wasp>
wasp --version
```

- **`wasp serve`** serves the site whose root is `--root`, the current folder by default, on `--port`, 8080 by default,
  to the local machine only. Under IIS, IIS starts it ([section 17](#17-deploying-to-iis)).
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

- **What `wasp` writes**, its errors when it cannot start included, and pages that fail after their output was sent,
  goes to IIS's log of it when `stdoutLogEnabled` is `true`: to `C:\inetpub\logs\WebLingo`, outside the site, in files
  whose names start with the site's. Create that folder first, and give the site's application pool
  (`IIS AppPool\<pool>`) write access to it. Keep the log outside the site: in it, it would be served, and every line
  written to it would reload the site.
- **Updating WebLingo** updates every site at once. A running `wasp.exe` keeps its files in use, so stop the sites'
  application pools, or IIS (`iisreset /stop`), before copying the new version over the old, and start them again
  after.
- **Under IIS, a site runs in production** unless its `wasp.config` says otherwise.

The repository's `Examples.wasp/example1` is such a site, and `Examples.wasp/README.md` walks through deploying it.

---

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
and `wasp` reloads it when its files change, as `wasp serve` does. Pass the web server every request, the site's other
files' included: `wasp` serves those too, and never serves the site's modules or its configuration, which a web server
serving the folder itself would.

**These configurations have been tried on Windows**, with nginx 1.30.6 and Apache httpd 2.4.71, Apache Lounge's build,
with mod_fcgid 2.3.10, each as written here but for its paths, which had no spaces; not yet on Linux, nor the systemd
service below.

### nginx

With `wasp fastcgi --root /var/www/mysite --port 9000` running:

```nginx
server {
    listen 80;
    server_name example.com;

    location / {
        include fastcgi_params;
        fastcgi_pass 127.0.0.1:9000;
    }
}
```

`fastcgi_params`, which comes with nginx, gives `wasp` the request's method, path, query and headers. On a Unix socket,
`fastcgi_pass unix:/run/wasp/mysite.sock;`. On Linux, a systemd service keeps `wasp` running, for example
`/etc/systemd/system/wasp-mysite.service`, with WebLingo installed in `/opt/weblingo`:

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

## 19. What is not there yet

These are not there yet, and are planned or being designed:

- **Uploaded files**: forms posted as `multipart/form-data` cannot be read.
- **Sessions and cookies**: a page reads the `Cookie` header and sets `Set-Cookie` itself, with `response.headers.add`.
- **A standalone server**, without IIS, nginx or Apache, is planned. WASP has been tried on Windows, through IIS's
  ASP.NET Core Module, and through FastCGI behind nginx and Apache httpd; Linux is built and tested, but no site has
  been served on it yet.
