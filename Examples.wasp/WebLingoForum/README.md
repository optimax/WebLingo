# WebLingo Forum

A discussion forum written in WASP, on SQLite, SQL Server, MySQL or PostgreSQL. Its design, and the parts it is built in,
are in `Documents/weblingo-forum.md`; this is what is built so far: its foundations, a forum's boards and topics read
from its database; its members, who sign up, confirm their address by mail, sign in and out, reset a forgotten password
and keep a profile; and writing, in Markdown: topics started, replies, quotes, one's own posts edited and deleted, with a
preview first, what is new to each member, and topics watched, their replies mailed; and each board's permissions for
each group, and moderation: topics locked, pinned, moved, deleted and restored, others' posts edited and deleted, posts
reported, members banned, and a log of it all; and its administration: its settings, categories and boards, who may do
what on each, groups, and members' groups; private messages, conversations of members, with what is new to each; and
search, by words, on a board and by an author; and members' files, their avatars, posts' attachments and the forum's
logo. A client's own look and code, and its deployment, come in the parts after them.

```text
WebLingoForum/
  setup/        outside the site: setup.wlg, which makes the forum's database, and its weblingo.config;
                copy-editor.wlg, which copies MarkdownEditor.Web, the Markdown editor, into site/js/markdown-editor
  data/         outside the site, not in the repository: forum.db, which setup.wlg makes, and uploads/, members'
                files, kept under random names, which setup.wlg makes and the site writes
  site/         the site, and wasp's root
    index.wasp  /                 the categories and their boards
    board.wasp  /board?id=1       a board's topics, 25 a page, the pinned first
    topic.wasp  /topic?id=1       a topic's posts, 20 a page, and a reply's form; &unread=1 its first new post
    post/       writing: new.wasp, a new topic, reply.wasp, a reply, edit.wasp and delete.wasp, a post, report.wasp, a
                post reported, and watch.wlg, watching a topic or not
    mod/        moderation: reports.wasp, the reports, log.wasp, the log, ban.wasp, a member's ban, and topic.wlg and
                post.wlg, a moderator's actions
    admin/      administration, /admin: settings.wasp, boards.wasp and board.wasp, a board and who may do what on it,
                groups.wasp, and members.wasp and member.wasp, a member's groups
    messages/   private messages, /messages: a member's conversations, new.wasp, a new one, and conversation.wasp
    search.wasp /search?q=tea     the posts with all the words, on a board and by an author where asked
    avatar.wlg  /avatar?id=1      a member's avatar; attachment.wlg, /attachment?id=1, a post's attachment, to those
                who may read its board; logo.wlg, /logo, the forum's logo
    account/    signing up, confirming an address, signing in and out, a forgotten password, the member's profile
    members/    /members, the members by name, and /members/member?id=1, a member's page
    lib/        the forum's modules: the database's SQL, its schema, settings, texts, members, their mail and tokens,
                posts, what each member has read and watches, the Markdown, permissions, moderation, and what each
                page shows and does
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
its administrator, and a first category, board and topic. Run on a database it has made, it does nothing; on one an
earlier version of the forum made, it adds the tables this version adds. It makes `data/uploads`, the folder of members'
files, where it is not there, from the folder it is run in, or where `uploads=` says: the site's `wasp.config` lets the
site write that folder alone, `"files": { "write": ["../data/uploads"] }`, outside the site, so that nothing written
there is served as it is, or reloads the site. On a server, give the site's application pool write access to it, as to
the database's folder.

The forum's own tests, from `tests`:

```bash
weblingo test accounts-tests.wlg administration-tests.wlg markdown-tests.wlg messages-tests.wlg moderation-tests.wlg \
    schema-tests.wlg search-tests.wlg sql-tests.wlg texts-tests.wlg uploads-tests.wlg users-tests.wlg writing-tests.wlg
```

## Mail, and members

A newcomer confirms their address by a link the forum mails them, good for 48 hours; a member who forgot their password
resets it by a link good for an hour; and a member who watches a topic is told of its replies. The forum sends mail
through the server its `site/wasp.config`'s `"mail"` names (the Standard Library Reference, section 31), from the
address its settings' `"mailFrom"` gives:

```json
{
  "databases": { "forum": "sqlite:../data/forum.db" },
  "files": { "write": ["../data/uploads"] },
  "mail": { "default": { "host": "smtp.example.com", "port": 587, "user": "forum@example.com", "password": { "env": "FORUM_SMTP_PASSWORD" } } }
}
```

Without a mail server, a newcomer is told that their mail could not be sent, and gets it when they sign in later. The
settings, which its administration's pages change, `/admin/settings`, have `site/lib/settings.wlg`'s defaults: `"signUp"`,
whether anyone may sign up; `"confirmEmail"`, whether a newcomer confirms their address first; `"mailFrom"`; and
`"address"`, the forum's own address, `"https://forum.example.com"`, for the links in its mail, which, left `null`, is
the address each request came to, its `Host` header's: set it where the forum is served by its name, so that no request
can give its mail another host.

Five failed sign-ins to one account in 15 minutes make the next wait. A signed-in member is their id in the request's
session, kept in `wasp`'s memory: under IIS, or wherever more than one process serves the forum, keep the sessions in
its database instead, `"sessions": { "store": "database", "database": "forum" }`, its table made once by
`wasp sessions create --root site` (the WASP User Manual, section 9, "Sessions in a database").

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
