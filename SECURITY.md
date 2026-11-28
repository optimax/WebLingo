# Security Policy

## Reporting a vulnerability

Please report a vulnerability in WebLingo or WASP privately, never in a public issue, a pull request or a discussion:
through GitHub's private vulnerability reporting, the repository's **Security** tab, then **Report a vulnerability**.
Only the project's maintainers see the report.

Say what is affected, `weblingo`, `wasp`, a standard module, an installer or a document; the version, as
`weblingo --version` or `wasp --version` prints it; how to reproduce it, the smallest script, page or site that shows it,
and its configuration; and what it lets someone do.

The maintainers will answer the report, work on a fix with you, and say when it is published. Please keep the
vulnerability to yourself until then.

## Supported versions

WebLingo is in beta: the latest beta, on the repository's releases page, and the latest commit of the `main` branch are
supported. Fixes go to `main`, and reach testers with the next beta.

## What a site's security rests on

The WASP User Manual says what a site's configuration limits, and what it does not, in its section 19, "Sites sharing a
server": a site's `wasp.config` limits its pages, not its native modules, its COM objects or its process's account, so
that sites that do not trust each other run each in a process of its own, under an account of its own. A report that
one site's pages reach past their `wasp.config`'s limits is a vulnerability; one that a native module, an allowed COM
object or the process's account reaches what its account may is not.
