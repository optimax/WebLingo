# WebLingo Forum

A discussion forum written in WASP, on SQLite, SQL Server, MySQL or PostgreSQL. Its design, and the parts it is built in,
are in `Documents/weblingo-forum.md`; this is what is built so far: part 1, its foundations, a forum's boards and topics
read from its database. Members, writing, moderation and the rest come in the parts after it.

```text
WebLingoForum/
  setup/        outside the site: setup.wlg, which makes the forum's database, and its weblingo.config
  data/         outside the site: forum.db, which setup.wlg makes, not in the repository
  site/         the site, and wasp's root
    index.wasp  /                 the categories and their boards
    board.wasp  /board?id=1       a board's topics, 25 a page, the pinned first
    topic.wasp  /topic?id=1       a topic's posts, 20 a page
    lib/        the forum's modules: the database's SQL, its schema, settings, texts, and what each page shows
    texts/      the forum's texts, a module for each language: en.wlg
    layouts/, includes/, components/, css/    its look, each piece a file of its own
  tests/        the forum's modules' own tests, run by weblingo test
```

## Running it locally

With the .NET 10 SDK, from the repository's root, make the database first, then serve the site:

```bash
cd Examples.wasp/WebLingoForum/setup
dotnet run --project ../../../WebLingo/src/WebLingo.Cli -- run setup.wlg
cd ../../..
dotnet run --project WebLingo/src/WebLingo.Wasp.Cli -- serve --root Examples.wasp/WebLingoForum/site
```

and open <http://localhost:8080/>. `setup.wlg` asks for the forum's first administrator, their name, address and
password, unless its parameters give them, `name=Anna email=anna@example.com`; it makes the forum's tables, its groups,
its administrator, and a first category, board and topic. Run on a database it has made, it does nothing.

The forum's own tests, from `tests`:

```bash
weblingo test sql-tests.wlg schema-tests.wlg texts-tests.wlg users-tests.wlg
```

## Another database

SQLite's database is `data/forum.db`, as `setup/weblingo.config` and `site/wasp.config` name it. For SQL Server, MySQL
or PostgreSQL, make an empty database, then give its connection string in both files, the forum's database being the
one they name `"forum"`:

```json
{ "databases": { "forum": "sqlserver:Server=localhost;Database=Forum;Integrated Security=true;TrustServerCertificate=true" } }
```

```json
{ "databases": { "forum": "mysql:Server=localhost;Database=forum;User ID=forum;Password=…" } }
```

```json
{ "databases": { "forum": "postgresql:Host=localhost;Database=forum;Username=forum;Password=…" } }
```

then run `setup.wlg`. MySQL makes each table as its own statement, so that a setup that fails there leaves the tables it
made: drop them before running it again.
