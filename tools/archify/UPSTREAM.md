# Vendored Archify

Upstream: <https://github.com/tt-a1i/archify> — MIT, see `LICENSE`.
Pinned revision: `af45e517fb9441e769593c1bf0a6395de1acb7ca`.

## What this folder is

The upstream repository's `archify/` subfolder, verbatim except for the one omission
below. That folder is the documented unit of distribution: `npx skills add
tt-a1i/archify -g` and the manual `archify.zip` install both produce exactly this
directory as a skill folder. Vendoring it is therefore the supported way to use
Archify, not a repackaging of it.

It is committed rather than fetched because Archify needs no install — its only
dependencies are `devDependencies`, and its validators and brand marks are committed
pre-generated — so a checked-in copy makes `node tools/archify/bin/archify.mjs` work
offline on a fresh clone. `.design/component-libraries.md` asks for local assets over
remote ones, and a diagram generator that needs the network to run would not meet it.

Node 18+. Verified on Node 24.18.0.

## What was omitted

`test/` (1.2 MB): upstream's own test suite, which this repository does not run.

`examples/` is **kept** even though it is 3.5 MB of the 5.4 MB total. `SKILL.md` step 2
requires reading a matching example before authoring a specification, so removing it
would break the documented authoring path — the one thing this vendoring exists to
support.

## Updating

Re-copy the folder at a newer revision, drop `test/`, record the new SHA above, then
regenerate every artifact and confirm each still reports 9 of 9 checks:

```bash
node tools/diagrams/archify-artifacts.mjs verify
```

An Archify upgrade that changes the renderer changes every artifact's bytes. That is
expected; a *validation* regression is not.
