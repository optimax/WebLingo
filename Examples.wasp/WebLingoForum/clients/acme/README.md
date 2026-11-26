# Acme's forum: a client over WebLingo Forum

An example of a client's own forum (`Documents/weblingo-forum.md`, section 3.5): Acme's folder is a site of its own,
which lies over the forum's, its base, as its `site/wasp.config` names it, `"base": "../../../site"`. WASP takes a file
from Acme's folder where it has one, and from the forum's otherwise (the WASP User Manual, section 14, "A site over a
base"), so that Acme's folder holds only what it changes, and a new version of the forum leaves it alone:

```text
clients/acme/
  data/                Acme's own database and members' files, which the forum's setup.wlg makes, not in the repository
  site/                Acme's site, and wasp's root
    web.config         IIS's configuration
    wasp.config        its base, the forum's site, and its own database and folder of files
    css/custom.css     its own look, after the forum's stylesheet
    texts/custom.wlg   its own texts, in place of the forum's of the same names
    plugins/index.wlg  its plugins: tea
    plugins/tea.wlg    :tea: shown as a cup of tea, a link to its rules in the header, and each new post in its log
    rules.wasp         /rules, a page of its own, in the forum's layout
```

Its settings are the forum's `wasp.config`'s where its own gives none; it gives its own `"databases"` and `"files"`, so
that it shares no data with the forum's own site, nor with another client.

## Running it locally

From this folder, make Acme's database with the forum's own setup, then serve Acme's site:

```bash
weblingo run ../../setup/setup.wlg database=sqlite:data/forum.db uploads=data/uploads
wasp serve --root site
```

and open <http://localhost:8080/>. A change to a file of Acme's, or of the forum's, reloads the site.
