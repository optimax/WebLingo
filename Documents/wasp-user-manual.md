# WASP User Manual

WASP, Web Application Server Pages, is how WebLingo builds websites. A WASP site is a folder of files, as a PHP site is:
pages (`.wasp`), which are HTML with WebLingo in them, modules (`.wl`), which hold the code the pages share, route
handlers (also `.wl`), which answer a path with code alone, and any other files, such as stylesheets and images, which
are sent as they are. WebLingo is installed once on a server, and every site uses it.

This manual shows how to write and serve such a site. It explains as much of the WebLingo language as pages need, but
it is not a reference to the language; the WebLingo Language Reference, `Documents/weblingo-language-reference.md`, is
that.

WASP is pre-alpha. Everything here works as described, and every example in this manual was run and gave the output
shown, but names and details may still change before a first release. [What is not there yet](#18-what-is-not-there-yet)
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
14. [Configuration: `WASP.config`](#14-configuration-waspconfig)
15. [While you work](#15-while-you-work)
16. [The `wasp` command line](#16-the-wasp-command-line)
17. [Deploying to IIS](#17-deploying-to-iis)
18. [What is not there yet](#18-what-is-not-there-yet)

---

## 1. Getting started

### What you need

To serve a site you need `wasp`, WASP's command line, and the .NET 10 runtime it runs on. Until WASP has an installer,
you build it from the repository, with the .NET 10 SDK: in the repository's `WebLingo` folder, either run it directly,

```bash
dotnet run --project src/WebLingo.Wasp.Cli -- serve --root ../Examples/example1
```

or publish it once, with `publish.ps1`, which puts `wasp`, `weblingo` and the standard modules into one folder,
`publish/WebLingo`, from which you run `wasp` (section 17 installs that folder on a server).

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
| `/api/teas` | `api/teas.wl`, a route handler ([section 10](#10-route-handlers)) |
| `/css/site.css` | `css/site.css`, sent as it is |

- A page, or a route handler, is found by its path **without** its extension: `/hello`, not `/hello.wasp`, which is a
  404.
- A folder's `index.wasp` (or `index.wl`) answers the folder's path, with or without its closing `/`.
- Any other file is sent as it is, with the content type its extension gives (`text/css` for `.css`, `image/png` for
  `.png`, and so on); a file whose extension the server does not know is a 404.
- Paths are compared exactly, their case included, on every operating system: `/Hello` does not find `hello.wasp`.
- `products.wasp` and `products.wl` in one folder are both `/products`, which is an error: the site does not load.

Some files are never sent, whatever their path:

- a page's source, and a module's, a `.wl` that exports no handler (`/lib/catalog.wl` is a 404);
- compiled WebLingo, `.wlbc` files;
- the site's configuration, `WASP.config`, and IIS's, `web.config`;
- every file and folder whose name starts with `.`, such as `.git`.

A typical site:

```text
mysite/
  index.wasp             /
  shop.wasp              /shop
  contact.wasp           /contact
  teas/index.wasp        /teas
  api/teas.wl            /api/teas, a route handler
  layouts/main.wasp      the layout the pages share
  includes/header.wasp   a piece of a page
  components/tea.wasp    a part a page renders, with values of its own
  lib/catalog.wl         a module of shared code
  css/site.css           sent as it is
  WASP.config            the site's configuration
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
| `<%= value %>` | writes a value, escaped for HTML ([section 4](#4-escaping)) |
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
  line break after it.

Everything else inside the blocks is the WebLingo language: variables (`var`, `const`), functions, classes, arrays
(`[1, 2]`), maps (`{ name: "Ann" }`), `if`, `while`, `for`, `foreach`, `try`, and the rest. An interpolated string,
`$"…"`, puts values in text, with a format for numbers when you want one: `$"€{price:N2}"` writes `€6.50` for 6.5.

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

WASP escapes for HTML only, for now: a value put into a URL, a `<script>` or a `<style>` is not escaped for those, and
a page must take care of it itself.

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

Code that pages share, such as classes, data and functions, goes into a module, a `.wl` file, whose declarations
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

A page imports names from it, `import { teas, cheapest } from "/lib/catalog.wl";`, or the module itself, `import
catalog from "/lib/catalog.wl";`, then `catalog.teas`. Here is `shop.wasp`, which uses the module, the layout of
[section 6](#6-layouts) and the component of [section 7](#7-components):

```html
<% import { teas, cheapest } from "/lib/catalog.wl"; %>
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

- An import's path that starts with `/` is from the site's root, any other from the importing file's folder; `.wl`
  may be left out.
- An import stands at a page's or a module's top level, not inside a block or a function; wherever it stands there, the
  module it imports runs before anything else the file does.
- A module runs once for each request that imports it, the first time it is imported; every later import in that
  request gets the same module. Nothing in a module lasts from one request to the next.
- Standard modules are imported by name: `import fs from "std:fs";` for files ([section 11](#11-waiting-await)).

---

## 9. The request and the response

A page that declares `param request;` and `param response;` is given the request it answers and the response it
makes, each an object whose members you read and set. A page declares only those it uses.

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

`/greet?name=Ann` gives `Hello, Ann!`, and `/greet`, `Hello, stranger!`.

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
  write($"{bytes} bytes");
}
```

Bytes, their members, and how to read them as text, are in the WebLingo Language Reference.

### The response

| Member | What it is |
|---|---|
| `response.status` | the status, 200 until set: `response.status = 404;` |
| `response.headers` | the response's headers: `response.headers["Content-Type"] = "text/plain";` |

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

A `.wl` file that exports a function named after an HTTP method answers its path with code alone, without a page's
text: an API, a redirect, a download. The function is given the request and the response, as a page's parameters are,
and what it writes, with `write`, is the response's body.

```javascript
import { teas } from "/lib/catalog.wl";

export function get(request, response) {
  response.headers["Content-Type"] = "application/json";
  var parts = [];
  foreach (var tea in teas) parts.push($"{{\"name\": \"{tea.name}\", \"price\": {tea.price}}}");
  write("[");
  var first = true;
  foreach (var part in parts) {
    if (!first) write(", ");
    write(part);
    first = false;
  }
  write("]");
}

export async function post(request, response) {
  var text = await request.text();
  response.status = 201;
  response.headers["Content-Type"] = "text/plain";
  write($"Received {text.length} characters.");
}
```

As `api/teas.wl`, `GET /api/teas` gives

```json
[{"name": "Sencha", "price": 6.5}, {"name": "Assam", "price": 4.25}]
```

and `POST /api/teas`, a 201 and `Received 5 characters.` for a body of `hello`.

- The functions are named in lower case: `get`, `head`, `post`, `put`, `delete`, `patch` and `options`. A function may
  be `async`, and await what it needs.
- `HEAD` calls `head`, or `get` when there is no `head`, its body not sent.
- Any other method is a 405, whose `Allow` header lists the methods the handler answers: `PUT /api/teas` is a 405 with
  `Allow: GET, HEAD, POST`.
- A `.wl` that exports none of those functions is a module, not a handler, and its path is a 404.
- A handler's `Content-Type` is `text/html; charset=utf-8` unless it sets another.
- The module runs anew for each request, its top level first, then the function: nothing in it lasts between requests.

WebLingo has no JSON module yet, so a handler builds JSON as text, as above. In an interpolated string, `{{` and `}}`
write `{` and `}`.

---

## 11. Waiting: `await`

A page may wait for something that takes time, such as a file, a component or a timer, with `await`, anywhere in its
blocks:

```html
<% var started = "started"; %>
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
Locally, `wasp serve` runs in development; under IIS, in production; `WASP.config` may name either
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
- **A client that takes nothing for 30 seconds** has its request cancelled, its response ended. `WASP.config`'s
  `"sendTimeout"` gives another time, in seconds.

---

## 14. Configuration: `WASP.config`

A site's configuration is a JSON file, `WASP.config`, in its root:

```json
{ "environment": "development", "sendTimeout": 60 }
```

| Setting | What it says | Default |
|---|---|---|
| `"environment"` | `"development"`, errors shown in their pages, or `"production"`, not shown | development under `wasp serve`, production under IIS |
| `"sendTimeout"` | how many seconds a page waits for a client that has taken nothing of its output, more than 0 | 30 |
| `"schemes"` | folders of native modules, each a name and a folder: `{ "native": "C:/WebLingo/native" }` | `std`, WebLingo's own |

A setting the file does not know, or a value it does not allow, stops the site from starting, with a message that says
which. `wasp serve` reads the file when it starts; after changing it, start `wasp serve`, or the IIS site, again.

Its name is `WASP.config`, written so: Linux tells `wasp.config` from it. A `weblingo.config` configures a folder of
scripts that `weblingo` runs, not a site, and `wasp` does not read it. `wasp run` and `wasp check` read the
`WASP.config` in the page's folder, for its `"schemes"`.

---

## 15. While you work

- **Changes are served at once.** `wasp serve` watches the site's folder. When files change, it waits until they have
  stopped changing for a quarter of a second, then loads the site anew; requests already running finish on the old
  version. A change that breaks the site's routes, such as a page and a route handler for one path, leaves the old
  version served, and `wasp serve` says why. A change that breaks a page makes that page a 500 until it is fixed.
- **Development shows errors.** Run locally, the site is in development unless `WASP.config` says otherwise
  ([section 12](#12-errors)).
- **Pages are compiled when first asked for**, and kept in memory; WASP never writes compiled files into a site's
  folder.
- **`wasp check`** reports a page's errors without running it, and `wasp run` runs it, which is quicker than a browser
  for a page that needs no request ([section 16](#16-the-wasp-command-line)).
- **Editors colour pages and modules**: the repository's `Editors/` folder has syntax colouring for `.wasp` and `.wl`
  files, and its `README.md` says how to install it.

---

## 16. The `wasp` command line

```text
wasp serve [--root <folder>] [--port <port>] [--scheme <name>=<folder> …]
wasp run [--root <folder>] [--scheme <name>=<folder> …] <page.wasp> [name=value …]
wasp check [--root <folder>] [--scheme <name>=<folder> …] <page.wasp>
wasp --version
```

- **`wasp serve`** serves the site whose root is `--root`, the current folder by default, on `--port`, 8080 by default,
  to the local machine only. Under IIS, IIS starts it ([section 17](#17-deploying-to-iis)).
- **`wasp run`** compiles a page and runs it, writing its output to the console. `--root` names the site's root, from
  which a path that starts with `/` is found; without it, the page's own folder is the root. The values after the page,
  `name=value`, are given to its parameters, each a string:

  ```html
  <% param name = "World"; %>
  <p>Hello, <%= name %>!</p>
  ```

  `wasp run cli.wasp` writes `<p>Hello, World!</p>`, and `wasp run cli.wasp name=Ann`, `<p>Hello, Ann!</p>`.
- **`wasp check`** compiles a page and reports its errors, without running it: `wasp check broken.wasp` writes
  `broken.wasp(1,8): error WL2001: 'nope' is not defined.`
- **`--scheme name=folder`** names a folder of native modules, over the configuration's.

An error is written as `file(line,column): error CODE: message`, to the error output. The exit code says how it went:

| Exit code | Meaning |
|---|---|
| 0 | it ran, or it compiled without errors |
| 1 | the page has compilation errors |
| 2 | the command line was wrong |
| 3 | the page failed while it ran |
| 4 | a file could not be read or written |

---

## 17. Deploying to IIS

WebLingo is installed **once on a server**, and every site uses it, as PHP is installed once for every PHP site: a site
holds only its own files, its pages beside its `web.config`.

Once for the server:

1. Install the **ASP.NET Core Hosting Bundle** for .NET 10, which brings the .NET runtime and IIS's ASP.NET Core
   Module.
2. On a Windows machine with the .NET 10 SDK, run the repository's `WebLingo/publish.ps1`, which publishes WebLingo
   into `WebLingo/publish/WebLingo`: `wasp.exe`, `weblingo.exe`, their libraries, and the standard modules, `std\`.
3. Copy that folder to the server as `C:\Program Files\WebLingo`. On the server itself, `publish.ps1 -Destination
   "C:\Program Files\WebLingo"`, from an elevated PowerShell, does both.

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
- **Under IIS, a site runs in production** unless its `WASP.config` says otherwise.

The repository's `Examples/example1` is such a site, and `Examples/README.md` walks through deploying it.

---

## 18. What is not there yet

WASP is pre-alpha. These are not there yet, and are planned or being designed:

- **Uploaded files**: forms posted as `multipart/form-data` cannot be read.
- **Escaping for URLs, scripts and styles**: `<%=` escapes for HTML only ([section 4](#4-escaping)).
- **JSON**: no module reads or writes it; a handler builds it as text ([section 10](#10-route-handlers)).
- **Sessions and cookies**: a page reads the `Cookie` header and sets `Set-Cookie` itself, with `response.headers.add`.
- **Hosts other than ASP.NET Core**: FastCGI and a standalone server are planned. WASP has been tried on Windows;
  Linux is built and tested, but no site has been served on it yet.
