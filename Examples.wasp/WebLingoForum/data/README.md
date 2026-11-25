# The forum's data

This folder is outside the site, so that nothing in it is ever served. It holds what the forum keeps on a SQLite database:
`forum.db`, which `../setup/setup.wlg` makes, and, once members upload them, their avatars and attachments. None of it is
in the repository.

On a server, give the site's application pool (`IIS AppPool\<pool>`) write access to this folder: SQLite writes its
database, and the files beside it, here.
