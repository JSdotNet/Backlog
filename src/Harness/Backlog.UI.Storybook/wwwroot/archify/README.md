# Archify artifacts

Generated diagrams for the `diagrams/archify` storybook page, which shows each one
beside the mermaid diagram it was authored from.

The `.json` files are the source; the `.html` files are generated from them and are
committed only because generating them needs Node, which building this solution does
not otherwise require. Do not hand-edit the HTML — regenerate it.

## What these are

Archify (<https://github.com/tt-a1i/archify>, MIT) takes a typed JSON specification
and renders one self-contained HTML document: inline SVG, its stylesheet, and a viewer
runtime that carries the theme toggle, the guided views and the export buttons. There is
no CLI SVG export, and the SVG alone is unstyled, so the whole document is the artifact.
That is why each file is roughly 675 KB.

Each specification is a re-authoring of a mermaid source, not a conversion of one — no
mermaid-to-Archify converter exists. The mermaid the specification came from is the
constant in `ArchifyComparisonPage.razor`; the two are kept in step by hand.

## Regenerating

Archify needs no install: its only dependencies are `devDependencies`, and its
validators and brand marks are committed pre-generated. Node 18+ is enough.

```bash
git clone --depth 1 https://github.com/tt-a1i/archify.git
cd archify/archify
```

Then, from that directory, for each specification — with `$WW` pointing at the folder
this README is in:

```bash
node bin/archify.mjs deliver architecture $WW/slice-flow.architecture.json     $WW/slice-flow.architecture.html     --quality showcase --json
node bin/archify.mjs deliver sequence     $WW/auto-save.sequence.json          $WW/auto-save.sequence.html          --quality showcase --json
node bin/archify.mjs deliver lifecycle    $WW/entry-lifecycle.lifecycle.json   $WW/entry-lifecycle.lifecycle.html   --quality showcase --json
```

`deliver` validates before it writes and exits non-zero if it cannot. All three are
expected to report 9 of 9 artifact checks with 0 errors and 0 warnings; anything less
means the specification regressed, not that the bar moved.

`node bin/archify.mjs visual-check <file.html> --json` checks that nothing overflows at
the four desktop sizes. It writes screenshots and a report beside the file it is given,
so run it on a copy rather than on the committed artifact.
