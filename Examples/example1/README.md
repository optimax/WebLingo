# The Lingo Tea House: a website built with WASP

A small website, an example of WASP, Web Application Server Pages, WebLingo's server pages: four pages that share a
layout, a header and a footer, a component that shows a tea, and a module that holds the tea house's catalog.

```text
example1/
  web.config            IIS's configuration: the ASP.NET Core Module runs WebLingo's wasp
  site/                 the application, wasp's root
    index.wasp          /          the home page, with this week's teas
    teas/index.wasp     /teas      every tea, by kind
    brewing.wasp        /brewing   a table of water temperatures and steeping times
    about.wasp          /about     a page of text, and an escaped value
    layouts/main.wasp   the layout every page has: its title, its header and its footer
    includes/           the header, with the current page marked, and the footer
    components/         tea-card.wasp, which a page renders for each tea, with its parameters
    lib/teas.wl         the catalog: a class, the teas, and the functions the pages call
    css/site.css        a file served as it is
```

## What it shows

- **Pages and routes.** A page is found by its path without `.wasp`: `/brewing` is `brewing.wasp`, and `/teas` is
  `teas/index.wasp`. A page's source, a module's (`/lib/teas.wl`) and the configuration are never served.
- **A layout.** Every page begins `<% layout "/layouts/main.wasp", { title: "…", section: "…" }; %>`; the rest of the
  page is the body, which the layout writes where it calls `renderBody()`.
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

## Running it locally

From the repository's `WebLingo` folder, with the .NET 10 SDK:

```bash
dotnet run --project src/WebLingo.Wasp.Cli -- serve --root ../Examples/example1/site
```

and open <http://localhost:8080/>. `--port` chooses another port. Locally, an error in a page shows in the browser;
under IIS it does not, unless the site's `weblingo.config` says `{ "environment": "development" }`.

## Deploying it to IIS

WebLingo is installed once on a server, and every site shares it, as PHP is installed once for every PHP site: a site
holds only its own files.

Once for the server:

1. Install the ASP.NET Core Hosting Bundle for .NET 10, which brings the .NET runtime and IIS's ASP.NET Core Module.
2. On a Windows machine with the .NET 10 SDK, run the solution's `publish.ps1` (`WebLingo/publish.ps1`), which publishes
   WebLingo into `WebLingo/publish/WebLingo`: `wasp.exe`, `weblingo.exe`, their libraries, and the standard modules,
   `std\`.
3. Copy that folder to the server as `C:\Program Files\WebLingo`. On the server itself, `publish.ps1 -Destination
   "C:\Program Files\WebLingo"`, from an elevated PowerShell, does both.

For this site:

4. Copy this folder, `example1`, to the server.
5. In IIS, add a website whose physical path is that folder, in an application pool whose .NET CLR version is
   "No Managed Code".

IIS then hands every request to the ASP.NET Core Module, which starts `C:\Program Files\WebLingo\wasp.exe serve --root
site`, as `web.config` says, and passes the requests to it. If WebLingo is installed in another folder, change
`processPath` in `web.config` to match.

Updating WebLingo updates every site at once. A running `wasp.exe` keeps its files in use, so stop the sites'
application pools, or IIS (`iisreset /stop`), before copying the new version over the old, and start them again after.
