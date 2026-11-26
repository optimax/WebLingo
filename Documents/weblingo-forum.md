# WebLingo Forum — Design

This is the design of WebLingo Forum, a discussion forum written in WASP. It lives in `Examples.wasp/WebLingoForum`. It records what the owner asked for, how the forum is built, what it needs from WASP, and the parts it is built in. Each part is a task in `TO-DO.md`.

A choice the owner has not made is marked **Guess** and can still change. A guess never settles a decision the owner keeps (`AGENTS.md`, section 3).

------

# 1. What was asked

The owner's answers to "Extensive WASP Example", 23 November 2026:

- **What.** An original forum written in WASP, not a conversion of a PHP forum. It should be powerful and customizable, so that it can be branded and customized for different clients (A1 (c)). Its name is WebLingo Forum until the owner names it.
- **Where.** `Examples.wasp/WebLingoForum` (A2).
- **What it needs first.** Uploaded files (A3), sessions (A4) and mail (A5), each built into WASP first. All three were done and installed by 25 November 2026: "Uploaded Files in WASP" (`8da8353`), "Sessions in WASP" (`a69a546`) and "Mail: `std:mail`" (`2f44df3`).
- **Its databases.** Every database `std:db` reaches: SQLite, SQL Server, MySQL and PostgreSQL (A6 (b)).

The forum also serves the pilot, "WASP Pilot: the Forum in Real Use": served under IIS on this machine, used by the owner alone (A1 (b) there). That is why it is built like a product, not like a demonstration.

------

# 2. What it does

A forum as people know it from phpBB, FluxBB and Discourse's plain pages:

- **Boards.** Boards are grouped in categories. Each board lists its topics: pinned ones first, then the most recently active, with page numbers. The front page lists the categories and their boards, each with its topic and post counts and its latest post.
- **Topics and posts.** A topic is a title and its posts, in order, with page numbers. A member starts a topic, replies, quotes another post, edits their own posts and deletes them, within the board's permissions. Posts are written in Markdown (section 3.7). A preview shows a post before it is sent. Until each board's permissions come, in part 4, any member signed in writes on any board, and a locked topic takes no replies and no changes. A post has at most 20,000 characters and a title 200. A topic's first post is edited with its title, and is deleted only where it is the topic's only post, the topic going with it. A deleted post is kept, marked deleted, for a moderator to restore (**Guess**, each).
- **Members.** Anyone can sign up with a name, an address and a password; the address is confirmed by mail. Members sign in and out and reset a forgotten password by mail. A profile holds a display name, an avatar, a signature, a time zone and a language. There is a list of members.
- **Reading.** The forum remembers what each member has read, so new posts are marked and a topic opens at the first unread post. A member can watch a topic and get mail when someone replies. A post written before a member joined is never new to them, a member watches the topics they start, and a watcher is mailed of a topic's next reply, then of none until they have read it again (**Guess**, each).
- **Groups and permissions.** Every member belongs to one or more groups: guests (anyone not signed in), members, moderators, administrators, and any others the administrator makes. Each board gives each group its permissions: to see the board, read it, start topics, reply, edit and delete one's own posts, and moderate. A member has every permission any of their groups gives. A reader not signed in has the guests' group's alone, and an administrator every permission on every board. A new board gives guests seeing and reading it, members writing on it too, and moderators moderating it (**Guess**, each). A board a reader may not see is not there for them; one they see but may not read shows them no topics.
- **Moderation.** Moderators lock, pin, move, delete and restore topics. They edit and delete any post on their boards. They ban a member, for a time or for good. Members report posts, and moderators work through the reports. The forum logs every moderator's action. A moderator of a board replies to its locked topics and changes their posts; moves a topic to a board they moderate too; deletes a topic's first post only with the topic; and restores a post they deleted from the log. A deleted topic is kept, and its board's moderators still see it. Whoever moderates any board reads every report and the whole log, on the boards they moderate the reports, and bans any member but an administrator, for a day, a week, 30 days or for good; a banned member cannot sign in, and is told until when and why (**Guess**, each).
- **Private messages.** Members write to one another in conversations of two or more people, with unread counts.
- **Search.** Searches posts and topic titles by words, within a board or by an author.
- **Avatars and attachments.** Members upload an avatar, and attach files to posts, within limits the administrator sets.
- **Administration.** Pages for the forum's settings: its name and description, its logo and colours, which features are on, its language and time zone, and whether sign-up is open. Further pages manage categories and boards, groups and their permissions, and members. Administrators alone see them. A category is removed only without boards, and a board only where it has never had a topic; the forum's own four groups stay, and a group an administrator makes may do nothing until a board says so; an administrator confirms a member's address, and never takes themselves out of the administrators' group (**Guess**, each). The logo waits for the forum to send files back (part 8).
- **Customization** (section 3.5). A client's forum gets its own look, texts and code, kept apart from the forum's, so that a new version of the forum does not overwrite them.

No page needs JavaScript to be read or used (**Guess**). A page may still use a script to make itself nicer, such as a preview that does not reload the page.

------

# 3. How it is built

## 3.1 Its folders

As example2's and example3's sites are, the site is one folder. Everything the site must not serve lives beside it:

```text
Examples.wasp/WebLingoForum/
  README.md         what it is, and how to run and deploy it
  setup/            outside the site: setup.wlg, which makes the forum's database, and its weblingo.config
  data/             outside the site, made by setup.wlg, never in the repository: the SQLite database,
                    the uploaded avatars and attachments
  site/             the site, and wasp's root
    wasp.config     the forum's database, its mail server, the folders it reads and writes, its sessions
    web.config      IIS's configuration, as the other examples'
    index.wasp      /                 the categories and their boards
    board.wasp      /board?id=3       a board's topics
    topic.wasp      /topic?id=42      a topic's posts
    post/           writing: a new topic, a reply, an edit, a preview, each a page and its handler
    account/        signing up, confirming, signing in and out, resetting a password, the profile
    members/        the list of members, and a member's page
    messages/       private messages
    search.wasp     /search
    mod/            moderation: its handlers, the reports, the log
    admin/          administration
    layouts/, includes/, components/, css/      the forum's look, each piece a file of its own (section 3.5)
    lib/            the forum's modules (section 3.2)
    texts/          the forum's texts, a module for each language (section 3.6)
    plugins/        modules that extend the forum (section 3.8)
  tests/            the forum's modules tested with weblingo test
```

The uploaded files are outside the site. Writing a file into the site's folder would reload the site and would put the file where WASP serves it. The site's `wasp.config` lets the site write only `../data/uploads`, through `"files"`.

Pages are found by their path, as WASP finds every page. A topic is therefore `/topic?id=42`, not `/topic/42` (**Guess**): WASP has no route parameters, and they are not needed to build the forum.

## 3.2 Its modules

The pages and handlers hold no SQL. They call the forum's modules in `site/lib`, one for each area: `boards.wlg`, `topics.wlg`, `posts.wlg`, `reading.wlg` (what each member has read, and the topics they watch), `users.wlg`, `tokens.wlg` (the links mailed to members), `groups.wlg`, `permissions.wlg`, `moderation.wlg` (what moderators do, bans, reports and the log), `messages.wlg`, `search.wlg`, `settings.wlg`, `mail.wlg`, `markdown.wlg`, `texts.wlg`, `hooks.wlg`, `sql.wlg` and `schema.wlg`; and, for the pages, `forum.wlg`, `pages.wlg` (the pages that read), `accounts.wlg` (the account's pages), `writing.wlg` (the pages that write), `moderating.wlg` (the moderators' pages, and a post's report) and `administering.wlg` (the administration's pages, whose changes `administration.wlg` makes).

The database is the one the configuration names `"forum"`, as `Database.open("forum")` opens it, closed at the end of its `using`; the provider pools its connections. `forum.wlg` gives a request its forum, its settings and texts read once, and `pages.wlg` what each page shows, read once for each request, so that a page and its layout, which both ask for it, share it: a module runs once for each request (the WASP User Manual, section 8). The modules give data, which the pages and their components write.

## 3.3 One schema, four databases

Each database speaks its own SQL, and `std:db` passes SQL on as it is. So the forum's SQL keeps to what all four share, and `lib/sql.wlg` gives what differs, chosen by `db.provider`:

- **Making tables.** The schema is described once, in `lib/schema.wlg`, as tables, columns, keys, foreign keys and indexes, its columns of a few kinds: an identity, an integer, a Boolean, a short text, a long text and a date. `setup.wlg` writes each database's own DDL from it: an identity is `INTEGER PRIMARY KEY` in SQLite, `BIGINT IDENTITY` in SQL Server, `BIGINT AUTO_INCREMENT` in MySQL and `BIGINT GENERATED BY DEFAULT AS IDENTITY` in PostgreSQL. A long text is `NVARCHAR(MAX)` in SQL Server, `MEDIUMTEXT` in MySQL and `TEXT` in the others; a date is `DATETIME` in SQLite, `DATETIMEOFFSET(3)` in SQL Server, `DATETIME(3)` in MySQL and `TIMESTAMPTZ(3)` in PostgreSQL. No name is a word one of them keeps for itself: the groups' table is `member_groups`, MySQL keeping `GROUPS`.
- **A new row's id.** SQLite and PostgreSQL use `RETURNING id`, SQL Server `OUTPUT INSERTED.id`, and MySQL `SELECT LAST_INSERT_ID()` on the same connection.
- **A page of rows.** SQLite, MySQL and PostgreSQL use `LIMIT @count OFFSET @skip`; SQL Server uses `OFFSET @skip ROWS FETCH NEXT @count ROWS ONLY`.
- **Names.** Every table and column name is lower case, in snake case (`post_count`). PostgreSQL folds names that are not quoted to lower case, and MySQL on Linux compares table names by case, so these names read the same back from all four.
- **Dates.** Every date is a UTC instant the forum writes itself, never the database's own `NOW()`. It goes in as a value and comes back as a date (the Standard Library Reference, section 17). A page shows it in the reader's time zone.
- **Its version.** A table `schema_version` holds the schema's version. A later version of the forum upgrades the database with `setup.wlg`, step by step, in a transaction where the database allows DDL in one.

`setup.wlg`, run with `weblingo` as an administrator runs `wasp sessions create`, makes the tables in the database its `weblingo.config` names. It also makes the first administrator and the forum's first category and board, and for SQLite it makes `data/` (**Guess**: a script, as example2 and example3 have, rather than an install page on the site, which anyone who found it first could use).

## 3.4 Signing in, and keeping members safe

- **Sessions.** A signed-in member is their id in the request's session, `request.session["user"]`. Signing in renews the session (`s.renew()`) and signing out ends it; so does changing one's password renew it. Under IIS, sessions are kept in the forum's database, so that every process serving the forum shares them. A page that only reads its session says so, `session readonly;`, so that a browser's requests do not wait for one another.
- **Passwords** are hashed with `crypto.hashPasswordAsync`. Failed sign-ins are counted for each account: five in 15 minutes make the next wait, even with the right password, and a reset password forgets them. WASP gives a page no client's address, so that they cannot be counted for each address they come from too. A sign-in that names no member checks its password against a hash all the same, so that it takes as long as one that does.
- **Links mailed.** A link to confirm an address, good for 48 hours, or to reset a password, good for an hour, holds 256 random bits, kept only as their SHA-256, used once. The link's address is the settings' `"address"`, where given, so that a request's `Host` header cannot send a member's link elsewhere. Asking for a reset says the same whether the address is a member's or not; signing up says that a name or an address is someone's already, as forums do.
- **Forms.** WASP refuses a form another site sends (the WASP User Manual, section 9, "Requests from other sites"). The forum adds `request.csrfToken` to its forms and checks it too.
- **What members write.** Post text is escaped wherever it goes. Markdown makes only the HTML its renderer writes (section 3.7). Links get `rel="nofollow ugc noopener"`.
- **Uploads.** An avatar or attachment is checked by its size and by its first bytes, not by its name. It is kept under a random name in `data/uploads`. It is sent back with its type, and with `Content-Disposition: attachment` unless it is an image.

## 3.5 Customization

Every piece of the forum's look is a file of its own: the layout, each include (its header, its footer, the trail of links above a page), each component (a post, a topic's row, a board's row, a member's card, the page numbers), and the CSS. The CSS takes its colours from custom properties, which the layout sets in its `<style>` from the forum's settings, each a colour `#rrggbb`, or the default. An administrator therefore changes the name, logo and colours without touching a file.

A client's own files change more: they replace a layout, an include, a component, a stylesheet, a text or a module of the forum's. They stay in a folder of the client's, apart from the forum's, so that a new version of the forum leaves them alone. How a client's folder takes the place of the forum's files is the owner's choice: the task "WebLingo Forum, Part 9: Customization" asks it.

## 3.6 Its texts

Every word the forum shows is a text in a module of its language, `texts/en.wlg`, exporting a map from a text's name to its words. A page reads `texts.get("board.newTopic")`, and texts with values use interpolation's holes, `texts.format("topic.replies", { count: 3 })`. The forum's language and each member's own pick the module, which `Module.import` loads once per request. A client's texts replace some of the forum's. The forum is written in English first, with one module, `en` (**Guess**).

## 3.7 Markdown

Posts, signatures and private messages are written in Markdown, rendered by the forum's own module, `lib/markdown.wlg`, written in WebLingo (**Guess**: a subset of CommonMark):

- paragraphs and line breaks, emphasis and strong emphasis, code spans and fenced code blocks;
- block quotes (a quoted post is one), lists, headings, horizontal rules;
- links and autolinks to `http`, `https` and `mailto` addresses only;
- images only where the forum's settings allow them, its `"images"`, false by default, a link to the image otherwise (**Guess**).

A quoted post begins its reply as a block quote, after a line naming who wrote it, the name escaped so that it is only text.

HTML in a post is text, escaped, never markup. The renderer writes only the elements above, each escaped as it is written. A page writes its output raw, `<%- %>`, which is the only raw output the forum has. Its tests feed it every kind of malformed input and check that nothing it writes escapes its elements.

## 3.8 Extending it

A client's code extends the forum through **hooks**, named points where the forum calls the modules listed in its settings (**Guess**):

- **Events**, such as a post made, a member signed up, or a topic moved, called after the fact: `await hooks.run("post.created", { post: post, user: user })`.
- **Filters**, which can change a value on its way, such as a post's HTML or a page's navigation: `html = await hooks.filter("post.html", html, { post: post })`.

A plugin is a module in `site/plugins`, imported by its name, exporting a function for each hook it serves. The forum keeps no code of its own in a plugin, so a plugin is the client's alone. The task for part 9 adds an example plugin.

## 3.9 Search

The forum keeps its own index (**Guess**): a table of each word's posts, filled when a post is written, edited or removed. A search looks up each word and gives the posts that have all of them. It works the same on all four databases, where each database's own full-text search does not (SQLite's FTS5, SQL Server's full-text catalogues, MySQL's `FULLTEXT` and PostgreSQL's `tsvector` differ in what they need and give).

## 3.10 Mail

The forum sends mail through `std:mail`: confirming an address, resetting a password, and telling watchers of a reply. Its server is `wasp.config`'s `"mail"`, its password kept as `{ "env": … }` or `{ "file": … }`. Each mail's subject and text are texts of the forum's (section 3.6). Mail is sent while the request runs, since WASP has no background work. A reply's notifications are sent after the reply is saved, so a failed mail never loses a post; a watcher whose mail failed is mailed of the next reply instead. Each says who replied to which topic, with a link to its first post new to the reader.

------

# 4. What it needs of WASP

Two things the forum needs are not there yet:

- **A response's bytes.** `response.write` writes text, so a handler cannot send back an avatar or an attachment the forum keeps outside its site. The task for part 8 asks how this is to be solved.
- **A client's files in place of the forum's.** An include and a layout are paths fixed when the page compiles (the WASP User Manual, sections 5 and 6), so the forum cannot choose them by client while it runs. The task for part 9 asks how this is to be solved.

Neither is needed before its part: parts 1 to 7 are built without them.

------

# 5. How it is tested

- **Its modules** are tested with `weblingo test`, in `tests/`: the Markdown renderer, the texts, the permissions, the schema's DDL for each database, and the modules on a SQLite database made for each run.
- **The whole forum** is tested by `ForumTests` in `WebLingo.Templates.Tests`, as `WaspServeTests` tests the examples. It serves the site with `wasp serve` and walks through it as a member does: signing up (its mail caught by the tests' SMTP server), signing in, posting, replying and moderating. It runs on SQLite always. It runs on SQL Server where one answers on this machine, and on MySQL and PostgreSQL where their downloads are in `Databases` (`DatabaseServer`), as the other database tests do.
- **Under IIS and FastCGI**, the forum is tried by hand once it is deployed: by `Scripts/add-wasp-site.wlg` under IIS, and behind nginx through `wasp fastcgi`.

------

# 6. The parts

Each part is a task in `TO-DO.md`, landed on its own and tested on all four databases where they are on this machine:

1. **Foundations.** The folders. `lib/sql.wlg` and `lib/schema.wlg`, and `setup.wlg` making the database on each of the four databases with its first administrator, category and board. The settings and the texts. The layout, its includes and the CSS with its colours from the settings. The front page, a board and a topic, read from the database. The tests' frame: `weblingo test` and `ForumTests`.
2. **Members.** Signing up with an address confirmed by mail, signing in and out with sessions, a forgotten password reset by mail, and limits on failed sign-ins. The profile (without its avatar) and the list of members.
3. **Writing.** Starting topics, replying, quoting, editing and deleting one's own posts. The Markdown renderer and previews. Page numbers. Unread posts. Watching a topic, with its mail.
4. **Permissions and moderation.** Groups and each board's permissions for each group. Moderators' locking, pinning, moving, deleting and restoring. Editing others' posts. Bans. Reports and the moderation log.
5. **Administration.** The settings pages, categories and boards, groups and their permissions, and members.
6. **Private messages.**
7. **Search.**
8. **Avatars and attachments**, once WASP sends a response's bytes, as its task's answer has it.
9. **Customization.** A client's files in place of the forum's, as its task's answer has it. Hooks and an example plugin. An example client, with its own look, texts and plugin.
10. **Deployment.** The forum's `README.md`. Its deployment under IIS by the repository's `Scripts`, and behind nginx and Apache through FastCGI. The WASP User Manual pointing to it. A measurement of its pages under load, with `WebLingo.Benchmarks --load --url`.

The pilot, "WASP Pilot: the Forum in Real Use", then serves it.
