# Mermaid, vendored

**Version 11.17.2**, from the npm package
[`mermaid@11.17.2`](https://www.npmjs.com/package/mermaid/v/11.17.2)
(tarball sha1 `e3caf3717582c0e44e5d04cff043e025caec7cba`). MIT, see `LICENSE`.

`components.js` imports `mermaid.esm.min.mjs` from here the first time a diagram renders.
Every host serves this folder as `_content/Backlog.UI.Components/lib/mermaid/` through the
library's static web assets, so diagrams draw with no network. A host that serves Mermaid
from somewhere else can still set `window.backlogDiagramLibrarySources` before
`components.js` runs.

The files are Mermaid's own build output, copied unchanged. Never reformat, lint or edit
them: the entry imports its chunks by relative path and by file name, so a renamed or moved
chunk stops every diagram. `.gitattributes` marks this folder `-text`, so a Windows
checkout leaves the bytes as published.

The bundle includes Mermaid's own dependencies, among them DOMPurify, d3, dagre, KaTeX and
Chevrotain. Each is under its own license; Mermaid's `LICENSE` covers Mermaid only.

## What is here

- `mermaid.esm.min.mjs` is the ESM entry (`package/dist/mermaid.esm.min.mjs`).
- `chunks/mermaid.esm.min/*.mjs` holds the chunks it imports, 103 files
  (`package/dist/chunks/mermaid.esm.min/`). The entry imports some of them statically and
  loads the rest per diagram type.
- `LICENSE` is the package's `LICENSE`.

The `.map` files are left out. They are about 13 MB, and nothing references them: the
minified files carry no `sourceMappingURL`.

## Updating

1. Pick the version and read its release notes for breaking changes to `initialize`,
   `render` or the `base` theme variables that `components.js` sets.
2. In a new, empty directory outside the repository, run
   `npm pack mermaid@<version> --ignore-scripts`, then extract the tarball with
   `tar -xzf mermaid-<version>.tgz`. Run nothing from the package.
3. Delete `mermaid.esm.min.mjs` and `chunks/` here. Chunk names carry a content hash, so
   copying over the old files leaves stale chunks behind.
4. Copy `package/dist/mermaid.esm.min.mjs`, every `package/dist/chunks/mermaid.esm.min/*.mjs`
   (not the `.map` files) and `package/LICENSE` in, keeping the `chunks/mermaid.esm.min/`
   layout.
5. Update the version and the tarball hash at the top of this file, and the Mermaid entry in
   `.devbook/tech/shared.md`.
6. Open the Storybook `/diagrams` and `/markdown/diagrams` pages with the network blocked and
   check that every valid fence draws and the broken one still shows its fallback.
