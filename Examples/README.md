# Examples

Each example is a site's own files, as a server runs them: its pages, its modules, its other files and its
`web.config`, side by side, as a PHP site's are; example2's site is its `site` folder, its database in a folder
beside it. WebLingo itself is installed once on the server, for every site. The WASP User Manual,
`Documents/wasp-user-manual.md`, explains what the examples use.

## example1: The Lingo Tea House, a website built with WASP

A small website, an example of WASP, Web Application Server Pages, WebLingo's server pages: four pages that share a
layout, a header and a footer, a component that shows a tea, and a module that holds the tea house's catalog.

```text
example1/               the site, and wasp's root
  web.config            IIS's configuration: the ASP.NET Core Module runs WebLingo's wasp
  index.wasp            /          the home page, with this week's teas
  teas/index.wasp       /teas      every tea, by kind
  brewing.wasp          /brewing   a table of water temperatures and steeping times
  about.wasp            /about     a page of text, and an escaped value
  layouts/main.wasp     the layout every page has: its title, its header and its footer
  includes/             the header, with the current page marked, and the footer
  components/           tea-card.wasp, which a page renders for each tea, with its parameters
  lib/teas.wl           the catalog: a class, the teas, and the functions the pages call
  css/site.css          a file served as it is
```

### What it shows

- **Pages and routes.** A page is found by its path without `.wasp`: `/brewing` is `brewing.wasp`, and `/teas` is
  `teas/index.wasp`. A page's source, a module's (`/lib/teas.wl`), the configuration and `web.config` are never served.
- **A layout.** Every page names its layout at its top, after its imports,
  `<% layout "/layouts/main.wasp", { title: "…", section: "…" }; %>`; the rest of the page is the body, which the layout
  writes where it calls `renderBody()`.
- **Includes.** The layout includes `/includes/header.wasp` and `/includes/footer.wasp` as if their text stood there:
  the header reads the layout's `section` to mark the current page.
- **A component.** `await renderComponent("/components/tea-card.wasp", { tea: tea, detailed: true });` renders a tea,
  the component declaring what it takes, `param tea; param detailed = false;`.
- **A module.** `lib/teas.wl` holds a class, `Tea`, the teas, and the functions the pages import:
  `<% import { featured } from "/lib/teas.wl"; %>`.
- **Escaping.** `<%= value %>` writes text escaped for HTML, so that `about.wasp`'s motto, `Tea & patience <always>`,
  shows as it is; an interpolated string formats a number, `$"€{tea.price:N2}"`.

Each page is compiled the first time it is asked for, and then served from memory; nothing is written to the site's
folder.

### Running it locally

With the .NET 10 SDK, from the repository's root, the folder that holds `Examples` and `WebLingo`:

```bash
dotnet run --project WebLingo/src/WebLingo.Wasp.Cli -- serve --root Examples/example1
```

and open <http://localhost:8080/>. Both paths are from the folder you run it in: from the solution's folder, `WebLingo`,
the one that holds `WebLingo.slnx`, the command is `dotnet run --project src/WebLingo.Wasp.Cli -- serve --root
../Examples/example1`. `--port` chooses another port. Locally, an error in a page shows in the browser;
under IIS it does not, unless the site's `wasp.config` says `{ "environment": "development" }`.

### Deploying it to IIS

WebLingo is installed once on a server, and every site shares it, as PHP is installed once for every PHP site: a site
holds only its own files.

Once for the server:

1. Turn on IIS (Windows Features' **Internet Information Services**, or, on Windows Server, the **Web Server (IIS)**
   role), then install the ASP.NET Core Hosting Bundle for .NET 10, which brings the .NET and ASP.NET Core runtimes
   and IIS's ASP.NET Core Module. Install the bundle after IIS: one installed before it does not add the module to IIS
   until it is run again and repaired.
2. On a Windows machine with the .NET 10 SDK and this repository, publish WebLingo from the solution's folder,
   `WebLingo`:

   ```powershell
   powershell -ExecutionPolicy Bypass -File publish.ps1
   ```

   which publishes it into `WebLingo/publish/WebLingo`: `wasp.exe`, `weblingo.exe`, their libraries, and the standard
   modules, `std\`. `-ExecutionPolicy Bypass` lets this one script run, without changing the machine's policy, where
   Windows's default runs no script ("running scripts is disabled on this system").
3. Copy that folder to the server as `C:\Program Files\WebLingo`. On the server itself, when it has the SDK and the
   repository, `powershell -ExecutionPolicy Bypass -File publish.ps1 -Destination "C:\Program Files\WebLingo"`, from an
   elevated prompt, does both.

For this site:

4. Copy this folder, `example1`, to the server.
5. In IIS, add a website whose physical path is that folder, in an application pool whose .NET CLR version is
   "No Managed Code".

IIS then hands every request to the ASP.NET Core Module, which starts `C:\Program Files\WebLingo\wasp.exe serve`, as
`web.config` says, in the site's folder, and passes the requests to it. If WebLingo is installed in another folder,
change `processPath` in `web.config` to match.

To see what `wasp` writes, its errors when it cannot start included, set `stdoutLogEnabled` to `true` in `web.config`:
the module then writes it to `C:\inetpub\logs\WebLingo`, outside the site, in files whose names start with the site's,
`example1`. Create that folder first, and give the site's application pool (`IIS AppPool\<pool>`) write access to it.

Updating WebLingo updates every site at once. A running `wasp.exe` keeps its files in use, so stop the sites'
application pools, or IIS (`iisreset /stop`), before copying the new version over the old, and start them again after.

## example2: Lingo Aquatics, a fish store with a database

A tropical fish store's site, an example of a site with a database: its fourteen fish, their photos' credits and its
orders are in a SQLite database, which its pages read and change through `std:db`, WebLingo's module for databases.
The database is outside the site's folder, so that it is never served, its place named by the site's `wasp.config`.

```text
example2/
  database/             outside the site: the database, and the script that makes it
    setup.wl            makes fish.db: its tables, the fish and a few orders
    weblingo.config     names the database "fish" for setup.wl: { "databases": { "fish": "sqlite:fish.db" } }
    fish.db             made by setup.wl, not in the repository
  site/                 the site, and wasp's root
    web.config          IIS's configuration, as example1's
    wasp.config         names the database "fish" for the pages: "sqlite:../database/fish.db"
    index.wasp          /                 the two waters, and this week's favourites
    fish/index.wasp     /fish             the fish, of one water, ?water=marine, up to a price, ?max=10, or by name, ?q=tetra
    fish/item.wasp      /fish/item?id=6   one fish, and a form to order it
    order.wasp          /order            an order, posted by that form, placed in a transaction
    orders.wasp         /orders           the latest orders, a join of two tables
    credits.wasp        /credits          each photo's author and licence, from the database
    api/fish.wl         /api/fish         the fish as JSON, a route handler
    lib/store.wl        the module the pages ask the database through
    layouts/, includes/, components/, css/   as example1's
    images/             the fish's photos, 1024 x 768, and their credits, CREDITS.md
```

### What it shows

- **A database a site names.** `wasp.config` names it, `{ "databases": { "fish": "sqlite:../database/fish.db" } }`, a
  relative path being from the configuration's folder, and a script opens it by that name, so that where it is, and
  which database it is, stay out of the scripts: `using (var db = await Database.open("fish")) { … }`.
- **Queries with values.** `await db.query(sql, { water: water, max: max })` gives an array of maps, a map for each row,
  each value named in the SQL as `@water` and `@max`, never pasted into its text; `lib/store.wl` asks the database for
  each page.
- **A transaction.** `order.wasp` checks a fish's stock, lowers it and records the order in one
  `await db.transaction(async () => { … })`, which commits, or rolls back where its function throws, as it does when
  fewer fish are left than ordered.
- **Values a database holds.** A price is a decimal, `DECIMAL(10, 2)`, read as one, `24.90m`; an order's time a date,
  `DATETIME`, formatted by an interpolation, `{order['Placed']:yyyy-MM-dd HH:mm}`.
- **The photos' provenance.** Each photo is from Wikimedia Commons, its author, licence and page in the database, which
  `/credits` lists, and in `images/CREDITS.md`.

### Running it locally

With the .NET 10 SDK, from the repository's root, make the database first, then serve the site:

```bash
dotnet run --project WebLingo/src/WebLingo.Cli -- run Examples/example2/database/setup.wl
```

```bash
dotnet run --project WebLingo/src/WebLingo.Wasp.Cli -- serve --root Examples/example2/site
```

and open <http://localhost:8080/>. `setup.wl` makes `fish.db` beside itself, anew each time it runs: run it again to
put the stock and the orders back as they were.

### Deploying it to IIS

As example1, with WebLingo installed once on the server, and then:

1. Copy both folders, `site` and `database`, to the server, side by side, so that `../database/fish.db` from the site
   is the database; or put the database elsewhere, and change the path in `wasp.config`.
2. Make the database there, `weblingo run setup.wl` in the `database` folder, with `C:\Program Files\WebLingo\weblingo.exe`.
3. Give the site's application pool (`IIS AppPool\<pool>`) write access to the `database` folder: SQLite writes its
   journal beside the database while it changes it.
4. In IIS, add a website whose physical path is the `site` folder, in an application pool whose .NET CLR version is
   "No Managed Code".
