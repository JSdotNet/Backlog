# Leaflet, vendored

**Version 1.9.4**, from the npm package
[`leaflet@1.9.4`](https://www.npmjs.com/package/leaflet/v/1.9.4)
(tarball sha512 `nxS1ynzJOmOlHp+iL3FyWqK89GtNL8U8rvlMOsQdTTssxZwCXh8N2NB3GDQOL+YR3XnWyZAxwQixURb+FA74PA==`,
sha1 `23fae724e282fa25745aff82ca4d394748db7d8d`). BSD 2-Clause, see `LICENSE`.

`components.js` loads `leaflet.js` and `leaflet.css` from here the first time a `TileMap`
has a tile source. Every host serves this folder as
`_content/Backlog.UI.Components/lib/leaflet/` through the library's static web assets, so
the library loads with the network down; only the tiles need it (local ADR 0061).

Leaflet draws the tiles and handles panning and zooming, nothing else. `TileMap` turns off
its zoom control, its attribution control and its keyboard handler, and draws the pins, edge
pills, controls, legend and attribution as the library's own buttons and markup over it.

The files are Leaflet's own build output, copied unchanged. Never reformat, lint or edit
them. `.gitattributes` marks this folder `-text`, so a Windows checkout leaves the bytes as
published.

## What is here

- `leaflet.js` is the UMD build (`package/dist/leaflet.js`). Loaded as a classic script, it
  sets `window.L`.
- `leaflet.css` is `package/dist/leaflet.css`. It points at `images/` by relative path.
- `images/*.png` is `package/dist/images/`, five files. `TileMap` draws no Leaflet marker or
  layer control, so nothing requests them today; they stay so the stylesheet resolves.
- `LICENSE` is the package's `LICENSE`.

The `.map` files and the `leaflet-src*` builds are left out. `leaflet.js` ends with a
`sourceMappingURL` comment, so a browser's developer tools may report the missing map;
nothing else reads it.

## Updating

1. Pick the version and read its changelog for changes to `L.map` options, `setView`,
   `fitBounds`, `getCenter`, `getZoom`, `getSize`, and the tile layer's `tileload`,
   `tileerror`, `loading` and `load` events, which `components.js` uses. Stay on the 1.x line
   until 2.0 is stable (local ADR 0061 §2).
2. In a new, empty directory outside the repository, run
   `npm pack leaflet@<version> --ignore-scripts`, then extract the tarball with
   `tar -xzf leaflet-<version>.tgz`. Run nothing from the package. Check the tarball's
   sha512 against the registry's `dist.integrity`.
3. Delete `leaflet.js`, `leaflet.css` and `images/` here.
4. Copy `package/dist/leaflet.js`, `package/dist/leaflet.css`, every
   `package/dist/images/*.png` and `package/LICENSE` in, keeping the `images/` layout.
5. Update the version and the tarball hashes at the top of this file, and the Leaflet entry
   in `.devbook/tech/desktop.md`.
6. Open the Storybook `/diagrams/tile-map` page and check that the map draws its stand-in
   tiles, pans, zooms, and that the offline story still shows its note.
