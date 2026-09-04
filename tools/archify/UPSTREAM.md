# Vendored Archify

Upstream: <https://github.com/tt-a1i/archify> — MIT, see `LICENSE`.
Pinned revision: `af45e517fb9441e769593c1bf0a6395de1acb7ca`.

## What this folder is

The upstream repository's `archify/` subfolder, verbatim except for the one omission
and the two changes below. That folder is the documented unit of distribution: `npx skills add
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

## What was changed

`assets/template.html` — the ambient trace loops instead of running once.

Upstream's trace is a single pass, and it is a deliberate one: the two rules it emits
carry an `animation-iteration-count` of `1`, and the Motion Governor latches ambient
motion off for good once every animated element has fired `animationend`. Both halves
had to move, because either one left alone still stops the diagram — a looping
stylesheet is inert the moment the governor stops setting
`data-ambient-motion="running"`, which it does on the reader's first pause.

- The iteration count on `archify-edge-flow` and `archify-node-pulse` is `infinite`.
- `@keyframes archify-edge-flow` ends where it begins, so the loop has no visible
  seam. Upstream's last two frames existed to land the edge on its authored solid
  stroke and stop there; looped, that is a flash of solid line and an `opacity`
  snap from 1 back to 0.42 once every cycle.
- The Motion Governor derives ambient state from suppression on every render rather
  than latching it once, so the loop comes back when the reader presses Live or
  returns to the tab. Its `animationend`/`animationcancel` completion path is gone:
  an infinite animation never ends, and the cancel that does fire is a suppression
  rule taking the animation away — a pause, not a finish.

Nothing about the reader's control over motion changed. `prefers-reduced-motion`, the
Live/Still button, page hiding, print, embed mode, and a Semantic Lens or guided story
claiming the motion budget all still stop the trace, through the same
`animation: none !important` rules as before, and each one still returns the edge to
its authored stroke — so the static reading and the canonical export are untouched.
What changed is only that lifting the suppression puts the loop back instead of
leaving the diagram settled for the life of the document.

**Why the change is here rather than in this repository's own code.** The app has two
ways of showing an artifact and only one of them can be reached from outside the file:
`DiagramView.razor` hands the HTML to `backlogDiagrams.renderArtifact`, which injects a
stylesheet of its own, but `DiagramPair.razor` in the storybook points an `<iframe>`
straight at the committed file and injects nothing. A host-side override would have
fixed the knowledge panes and left the storybook comparison page frozen.

`ArchifyArtifactMotionTests` fails if a re-copy drops this. It checks the template as
well as all 42 artifacts, so the loss is reported on the re-copy itself rather than
after the next regeneration — by which point every artifact is static again.
`assets/template.html` — the Motion Governor guards its guided-views pause on the
function, not just on the object.

Upstream's `render()` calls `Archify.guidedViews.isPlaying()` behind nothing but a test
that `Archify.guidedViews` exists. The object is always there to be truthy: its module
returns a stub — `{ count: 0, active }` — when the document carries no guided views, and
that is every artifact this repository renders. So the call throws `TypeError:
Archify.guidedViews.isPlaying is not a function` on every pause, tab hide and
reduced-motion transition.

- The call site reads `Archify.guidedViews && Archify.guidedViews.isPlaying &&
  Archify.guidedViews.isPlaying()`. That is upstream's own shape, not a new idiom: the
  two sibling call sites in the same file — the camera's `interruptCamera` and the
  diagram guide's `open` — already guard the function this way.

The throw is worse than it looks. It lands after the ambient-motion state has been
settled, so pause and resume themselves still work — but it aborts the rest of
`render()`, skipping the `lastEffectivePaused` update and the motion button's
`aria-label` and `title` refresh. The Live/Still button's accessible name therefore never
moves off "Pause motion" for the life of the document. With the trace above now looping
indefinitely, that button is this artifact's WCAG 2.2.2 pause affordance, so a pause
control whose name is stuck is a defect on exactly the control the loop depends on.

`ArchifyArtifactMotionTests` fails if a re-copy drops this one too, and checks the
template alongside all 42 artifacts for the same reason.

## `bin/` needs a .gitignore negation

`.gitignore` carries `[Bb]in/` for .NET build output. That rule matches
`tools/archify/bin/`, and when this folder was first vendored it silently took
all four entrypoints with it — including the `archify.mjs` that
`tools/diagrams/archify-artifacts.mjs` spawns. Every `render` failed with
`MODULE_NOT_FOUND` on a fresh clone, which is precisely when somebody would be
trying to fix an artifact, while `scan` and `verify` — neither of which shells
out — both reported green.

The negation at the end of `.gitignore` is what keeps it:

```gitignore
!tools/archify/bin/
```

It names the directory rather than its contents on purpose. Git does not look
inside an excluded directory, so `!tools/archify/bin/**` alone would leave the
folder invisible. When re-copying at a newer revision, check `git status` shows
`bin/` — a copy that is on disk but untracked passes locally and fails for
everyone else. `ArchifyArtifactMotionTests.The_vendored_archify_cli_is_committed`
fails if it goes missing again.

`examples/` is **kept** even though it is 3.5 MB of the 5.4 MB total. `SKILL.md` step 2
requires reading a matching example before authoring a specification, so removing it
would break the documented authoring path — the one thing this vendoring exists to
support.

## Updating

Re-copy the folder at a newer revision, drop `test/`, record the new SHA above,
re-apply both changes in **What was changed**, then regenerate every artifact and confirm
each still reports 9 of 9 checks:

```bash
node tools/diagrams/archify-artifacts.mjs verify
```

An Archify upgrade that changes the renderer changes every artifact's bytes. That is
expected; a *validation* regression is not.
