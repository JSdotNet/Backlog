# Component Libraries

```meta
status: active
related: [".arc42/04-solution-strategy.md#technology-choices", ".design/color-scheme.md#per-stack-token-mapping", ".design/content-editing.md", ".design/interaction-guidelines.md#drag-and-drop-reordering", ".design/accessibility.md#keyboard-navigation"]
```

> Research and recommendation of component libraries covering the **full**
> Backlog architecture: Desktop (.NET MAUI Blazor Hybrid — Razor in WebView2),
> Mobile (.NET MAUI), IDE (VS Code + Visual Studio extensions), and Cloud
> (headless). The channel stacks are fixed by
> `.arc42/04-solution-strategy.md#technology-choices` and
> `.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md`; this file recommends the
> component/library layer per channel against the product's specific needs:
> dark-mode-only, a shared token set, a Markdown WYSIWYG editor, and accessible
> drag-and-drop reorder. Facts about WinUI/MAUI/Fluent reflect published Microsoft
> documentation; where a claim is a judgement call it is marked as such. **No
> packages were installed.**

## Evaluation Criteria

```meta
status: active
```

| # | Criterion | Why it matters |
|---|---|---|
| C1 | Dark-mode-only support | Product ships a single dark theme; libraries must theme cleanly to dark without fighting a built-in light default. |
| C2 | Design-token / theming story | One logical token set must drive MAUI (desktop's Razor/WebView2 surface, mobile's native XAML), and web/webview (see `color-scheme.md#per-stack-token-mapping`). |
| C3 | Markdown rich-text / WYSIWYG editor | Core editing surface where Markdown is canonical (see `content-editing.md`). |
| C4 | Accessible drag-and-drop reorder (list + tree) | Reorder of items and chapters with keyboard parity (see `interaction-guidelines.md#drag-and-drop-reordering`). |
| C5 | Accessibility (WCAG AA) | Platform a11y semantics must be first-class (see `accessibility.md`). |
| C6 | Maintenance / license / longevity | Prefer actively maintained, permissively licensed, first-party-aligned options. |
| C7 | Diagrams, knowledge graphs, and charts | Flow, C4, domain, technology graph, and dashboard visualizations are core knowledge-base surfaces. |

## Key Finding: Tokens Are the Shared Layer

```meta
status: active
related: [".design/color-scheme.md#per-stack-token-mapping"]
```

> **There is no single cross-stack component library** that spans mobile's
> native MAUI XAML and web/webview. Native XAML controls (mobile MAUI) and web
> components (webviews, and now the desktop Razor/WebView2 surface) are
> different rendering technologies. The **shared layer is the design-token
> set**, not shared components. Desktop and the IDE webviews now share the same
> rendering technology (Chromium via WebView2 / VS Code webview), so they can
> also share the same web component library and editor; mobile remains the
> outlier and picks its own native library, mapped to the same logical tokens
> (`color-scheme.md`).

The **Markdown WYSIWYG editor** is the one component worth sharing where
possible: a web-based editor (TipTap/ProseMirror/Milkdown or Monaco for raw) can
run inside the IDE webviews and inside the desktop's WebView2 (`BlazorWebView`)
and mobile's MAUI (`BlazorWebView`/WebView) hosts, giving one editor
implementation across channels. This is the primary reuse opportunity.

## Per-Channel Recommendations

```meta
status: active
related: [".arc42/04-solution-strategy.md#technology-choices", ".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md"]
```

### Desktop — .NET MAUI Blazor Hybrid (Razor in WebView2)

```meta
status: active
related: [".arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md"]
```

Per `.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md`, the desktop app is a
MAUI shell (WinUI 3 head) rendering its entire UI as Razor components in an
embedded WebView2 — not native WinUI 3 XAML. This puts desktop on the same
rendering technology as the IDE webviews.

> **What was actually built is a first-party library, not one of the candidates
> below.** `Backlog.UI.Components` is a Razor class library written for this
> product, reviewed in the storybook. Read the recommendation in this section as
> the option that remains open for the IDE channels, not as a description of the
> desktop today — see `#materialization`.

| Aspect | Recommendation |
|---|---|
| Base controls | **A web component library shared with the IDE webviews** (see IDE recommendation below), rendered via Razor/`BlazorWebView` — not native WinUI 3 XAML controls. |
| Theming (C1/C2) | Expose the product `--color-*` CSS custom properties, same as the webview channels (`color-scheme.md#per-stack-token-mapping`); no separate WinUI `ResourceDictionary` is authored for app UI. |
| Markdown editor (C3) | Host the shared **web editor** directly in the same WebView2 surface — no separate desktop editor implementation needed. |
| Reorder (C4) | Use the same web-based drag-and-drop library as the webview channels (e.g. `dnd-kit`); **keyboard reorder must still be added explicitly** per `interaction-guidelines.md#keyboard-accessible-reordering`. |
| A11y (C5) | ARIA within the WebView2 content; native `AutomationProperties` still apply to the thin MAUI/WinUI 3 shell chrome (window, title bar); verify both with Accessibility Insights. |

### Mobile — .NET MAUI

| Aspect | Recommendation |
|---|---|
| Base controls | **.NET MAUI + .NET MAUI Community Toolkit.** Preferred per architecture. Fallbacks: Blazor Hybrid / Blazor WASM PWA. |
| Theming (C1/C2) | Single dark resource dictionary of the same tokens; disable system light/dark switching (`UserAppTheme = Dark`, no toggle). |
| Markdown editor (C3) | Host the shared **web editor via `BlazorWebView`/WebView**; native MAUI has no first-class Markdown WYSIWYG control. |
| Reorder (C4) | `CollectionView` supports drag reorder (`CanReorderItems`); **keyboard/switch-accessible reorder commands must be added** and single-column drag must autoscroll. |
| A11y (C5) | `SemanticProperties`; test Narrator/VoiceOver/TalkBack + OS text scaling. |

### IDE — VS Code extension (TypeScript webview)

| Aspect | Recommendation |
|---|---|
| Base controls | **`@vscode-elements/elements`** (the maintained successor to the deprecated VS Code Webview UI Toolkit) or **Fluent UI Web Components** for a richer set; either themes from CSS variables. |
| Theming (C1/C2) | Expose the product `--color-*` CSS custom properties; product dark tokens take precedence over host theme vars for content surfaces. |
| Markdown editor (C3) | **TipTap (on ProseMirror)** or **Milkdown** for WYSIWYG; **Monaco** for the raw-Markdown escape hatch (Monaco already ships with VS Code). |
| Reorder (C4) | **dnd-kit** (accessible, keyboard support, `@dnd-kit/sortable` + tree) preferred; **SortableJS** is lighter but weaker on built-in keyboard a11y. |
| A11y (C5) | ARIA + `aria-live`; respect host reduced-motion/high-contrast. |

### IDE — Visual Studio extension (C#, WPF)

| Aspect | Recommendation |
|---|---|
| Base controls | **WPF** with VS shell theming; reuse the same token set as a WPF `ResourceDictionary`. |
| Markdown editor (C3) | Host the **shared web editor in WebView2** for parity with the other channels (recommended), avoiding a second editor implementation. |
| Reorder (C4) | WPF `ItemsControl`/`TreeView` drag reorder + explicit keyboard Move commands. |
| A11y (C5) | UIA/`AutomationProperties`. |

### Cloud — ASP.NET Core Minimal APIs

| Aspect | Recommendation |
|---|---|
| UI | **None.** Headless sync/coordination service (`.arc42/04-solution-strategy.md#thin-cloud-rich-desktop`) — no component library needed. |

## Candidate Comparison

```meta
status: active
```

Rating: ✔ strong · ◑ partial/with work · ✘ weak/absent · — n/a.

| Candidate | Channel fit | C1 Dark-only | C2 Tokens | C3 MD editor | C4 DnD reorder | C5 A11y | C6 Maint/License |
|---|---|---|---|---|---|---|---|
| **WinUI 3 + Windows Community Toolkit** | (superseded — no longer used for app UI, see ADR 0001) | ✔ | ✔ (ResourceDictionary) | ◑ (preview only; WYSIWYG via WebView2) | ◑ (drag yes; keyboard manual) | ✔ (UIA) | ✔ MIT / first-party |
| **.NET MAUI + Community Toolkit** | Mobile | ✔ (`UserAppTheme`) | ✔ (resources) | ✘ native (host web editor) | ◑ (`CanReorderItems`; keyboard manual) | ✔ (SemanticProperties) | ✔ MIT / first-party |
| **FluentUI-Blazor** | Mobile fallback / web | ✔ | ◑ (Fluent tokens; map to product) | ✘ (no MD editor) | ◑ | ✔ | ✔ MIT |
| **MudBlazor** | Mobile fallback / web | ✔ | ◑ (MudTheme; light-oriented defaults) | ✘ | ◑ (MudDropContainer) | ◑ | ✔ MIT |
| **Fluent UI Web Components / React** | Desktop / Webview | ✔ | ✔ (design tokens) | ✘ (no MD editor) | ✘ (bring your own) | ✔ | ✔ MIT |
| **`@vscode-elements/elements`** | Desktop / VS Code webview | ✔ (theme vars) | ◑ (host theme vars) | ✘ | ✘ | ✔ | ✔ MIT (maintained successor) |
| **Radix + shadcn/ui** | Desktop / Webview | ✔ | ✔ (CSS vars) | ✘ | ◑ (pair with dnd-kit) | ✔ (Radix primitives) | ✔ MIT |
| **TipTap (ProseMirror)** | Editor (all via web host) | ✔ | ✔ (CSS) | ✔ (WYSIWYG, MD via config) | — | ✔ (ProseMirror) | ✔ MIT core (some pro modules paid) |
| **Milkdown** | Editor | ✔ | ✔ | ✔ (Markdown-native, plugin-based) | — | ✔ | ✔ MIT |
| **ProseMirror (direct)** | Editor | ✔ | ✔ | ✔ (max control, more work) | — | ✔ | ✔ MIT |
| **Monaco** | Raw-MD hatch | ✔ | ◑ (editor themes) | ◑ (source, not WYSIWYG) | — | ◑ | ✔ MIT |
| **dnd-kit (+ sortable/tree)** | Reorder (web) | — | — | — | ✔ (keyboard + live regions) | ✔ | ✔ MIT |
| **SortableJS** | Reorder (web) | — | — | — | ◑ (weak built-in keyboard a11y) | ◑ | ✔ MIT |
| **Mermaid** | Knowledge Markdown diagrams | ✔ | ◑ (theme variables and CSS) | — | — | ◑ (SVG semantics need labels) | ✔ MIT / active |
| **AntV G6** | (evaluated, not adopted — see `#diagram-and-graph-strategy`) | ✔ | ✔ (CSS/container styling + node palettes) | — | ◑ (canvas interactions; keyboard companion list required) | ◑ | ✔ MIT / active |
| **AntV X6** | Future editable flow editor | ✔ | ✔ | — | ◑ | ◑ | ✔ MIT / active |
| **Apache ECharts** | Charts and dashboards | ✔ | ✔ | — | — | ◑ | ✔ Apache-2.0 / active |
| **diagrams.net / draw.io** | Optional full GUI diagram editor | ✔ | ◑ | — | ◑ | ◑ | ✔ Apache-2.0 / active |

## Recommendation Summary

```meta
status: active
```

| Layer | Recommendation | Rationale |
|---|---|---|
| Shared design layer | **Tokens, not components** — one logical token set (`color-scheme.md`) emitted to XAML + CSS. | No cross-stack component library exists that also covers mobile's native XAML; tokens are the durable shared contract. |
| Desktop | **A web component library shared with VS Code** (e.g. `@vscode-elements`/Fluent Web Components) + product CSS tokens, rendered via Razor/`BlazorWebView`. | Per ADR 0001, desktop UI is Razor in WebView2, not native WinUI 3 XAML — it shares the webview channels' rendering technology and reuse story. |
| Mobile | **.NET MAUI + Community Toolkit** (`UserAppTheme = Dark`). | Preferred stack; native mobile feel. |
| VS Code | **`@vscode-elements`/Fluent Web Components** + product CSS tokens. | Themeable, host-aligned, maintained. |
| Visual Studio | **WPF + shared tokens**, editor via WebView2. | Reuses the shared web editor; avoids a second editor. |
| Markdown WYSIWYG editor (shared) | **TipTap or Milkdown** (WYSIWYG) + **Monaco** for the raw hatch, hosted in the desktop's and VS's WebView2, in VS Code's webview, and in mobile MAUI's `BlazorWebView`. | The single highest-value reuse; Markdown-canonical, round-trip-friendly, one implementation. |
| Drag-and-drop reorder (web surfaces) | **dnd-kit** (`sortable` + tree). | Best accessible DnD: built-in keyboard support and live-region announcements — satisfies C4/`interaction-guidelines`. Native channels add explicit keyboard Move commands. |
| Knowledge Markdown diagrams | **Mermaid** for rendered Flow, C4, sequence, state, class/domain-model style diagrams from fenced Markdown. | Text-as-code keeps Markdown canonical and supports the checked-in knowledge folders without a binary diagram format. |
| Technology and knowledge graphs | **A first-party canvas renderer** in `Backlog.UI.Components`, drawing a 3D-projected, layer-clustered atlas over the `.tech` graph. | The graph is 66 nodes of checked-in Markdown on a local-first desktop. A general-purpose graph library is sized for data this product does not have, and the only way it was ever going to arrive here was over a CDN — which `#risks-and-gaps` forbids in production. Writing the projection is smaller than vendoring the alternative. |
| Future editable diagramming | **AntV X6** for node/edge flow editors; **diagrams.net/draw.io** only if a full general-purpose GUI editor is needed. | Still true, and still future. Both are editing tools, and nothing in the product edits a diagram yet — the atlas reads the graph and writes one field of it back. Either would arrive vendored, not from a CDN. |
| Charts and dashboards | **Apache ECharts**. | Broad chart coverage, active maintenance, permissive license, dark theme support, and strong dashboard fit. |


## Diagram and Graph Strategy

```meta
status: active
related: [".tech/technology-graph.md", ".tech/tooling.md#archify", ".tech/tooling.md#c4hero", ".design/content-editing.md", ".design/accessibility.md"]
```

Backlog should solve diagrams in layers instead of relying on one component suite:

| Need | Choice | Guidance |
|---|---|---|
| Flow, C4, sequence, state, class/domain-model diagrams in knowledge Markdown | **Mermaid** | Render fenced `mermaid`/`mmd` blocks directly inside knowledge-base Markdown. Keep Markdown as the canonical source and show the source fallback when rendering fails or assets are unavailable. |
| A considered picture for the diagrams that earn one | **Archify** (generated artifact) | An optional second rendering of a fence somebody has authored a specification for. Never a replacement for Mermaid and never automatic: a fence with no artifact, or whose text has moved on, is drawn by Mermaid. See `#archify-artifacts` below. |
| A C4 model of the system, beside the chapters rather than inside one | **c4hero** (authoring) + **a first-party renderer** (drawing) | A Structurizr workspace under `.arc42/_c4/`, edited in c4hero and drawn by the app from the DSL. Mermaid drew it first and could not be themed into the shape a C4 view wants — it sizes its own boxes, draws no glyph and writes every colour inline. Additive: it replaces no fence, and the `C4Context`/`C4Container` fences in chapters 03 and 05 stay canonical. See `#c4-workspaces` below. |
| Technology graph / knowledge graph exploration | **A first-party canvas renderer** | Draw the `.tech` graph as a 3D-projected atlas: one cluster per layer, node size by in-degree, curved edges, and a camera that moves to what is selected. Colour comes from `color-scheme.md#chart-roles` — the palette carries one saturated hue, so an ordinal status reads as a position on that hue's ramp and never as a hue of its own, with shape carrying what the ramp cannot. The keyboard companion below is part of the renderer, not an addition to it. |
| Editable workflow/flowchart designer | **AntV X6** (future) | Add when users need to create or edit node-edge diagrams visually. It is better suited to diagram editing than a read-and-select atlas is. |
| Charts and operational dashboards | **Apache ECharts** (future) | Preferred for metrics, trend charts, dependency health, and monitoring dashboards. |
| Full general-purpose diagram editor | **diagrams.net / draw.io** (optional future) | Consider only when a broad GUI editor is more valuable than Markdown-first diagrams. Bundle assets locally if embedded. |

All diagram and graph rendering must follow the product defaults: dark mode only,
local/offline asset bundling for production, no save buttons, Markdown remains
canonical for knowledge documents, and every pointer interaction needs a
keyboard-accessible companion surface with announced state changes.

For a canvas the last of those is not a caveat, it is the design. A `canvas`
element publishes nothing to the accessibility tree, and a node drawn at the size
a readable layout allows is roughly a third of the 44px target
`accessibility.md#target-sizes-and-text` requires — so the pointer surface cannot
be the primary one however carefully it is labelled. The canvas is therefore
`aria-hidden`, and the graph is **also** rendered as a focusable list of its
nodes: on screen rather than visually hidden, ordered as the folder reads, and
carrying each node's status and relation counts as text. Selection is one model
behind both, announced once through a polite live region as position, status and
counts. A reviewer should be able to unplug the mouse and lose nothing but the
picture.

### C4 workspaces

```meta
status: active
related: [".tech/tooling.md#c4hero", ".design/content-editing.md", ".design/accessibility.md"]
```

A C4 view is not a chapter diagram. It is one view of a model that lives beside the
architecture chapters, authored somewhere else entirely, and the rules that follow
are about it reading as part of the section rather than as a second product bolted
to the side of it.

| Rule | Guidance |
|---|---|
| Drawn here, in this palette | A view is laid out and drawn by the product rather than by a diagram library, because a library that sizes its own boxes and writes its own colours cannot be made to sit inside a section. Levels are told apart by weight and glyph on the single hue `#chart-roles` allows — solid for the subject, outline for anything outside it — never by a hue per level. |
| A tab beside the chapters, not an entry inside them | The Architecture panel keeps two tabs, the shape Technology already uses: chapters on the first, the model on the second. A C4 view is not a chapter and is in no chapter, so it does not belong in the chapter list — and the tab is absent rather than empty when there is no workspace. |
| Every gesture has a control beside it | Drilling in by clicking a box is discoverable and invisible to a keyboard, so the same move is also a button — the Views panel and the Drill into row. `accessibility.md#target-sizes-and-text` is the rule; a shape in an SVG is not a focusable target, so the pointer cannot be the only way. |
| Where you are and how you got here are two controls | The breadcrumb is derived from the open view's scope and the Back button walks the history. Collapsing them into one makes the survivor a worse version of both. |
| Dimmed, not hidden | The Highlighter takes non-matching elements down to a low opacity and leaves them in place, so the shape of the diagram stays readable and a filter reads as emphasis rather than as a different picture. |
| The reference is drawn on both sides | A view lists the chapters it documents; a chapter lists the views that say so. Same single authored statement — `_c4/references.json` — read from either end, so the two cannot disagree. A view nothing documents says so in words rather than showing an empty row. |
| What could not be read is said, not hidden | A workspace the reader only partly understood still draws a complete-looking picture. The unreadable constructs appear as a plain list under the diagram: a footnote under something that rendered, not an error that stopped one. |
| The generated Mermaid is never shown as the source | The `.dsl` is the authored text. A reader who wants the source wants the workspace, not the Mermaid it was turned into on the way to the screen. |

Off by default, behind `c4-diagrams`. `tools/diagrams/C4.md` carries the
arrangement, the DSL subset, and what the drawing cannot say.

### Archify artifacts

```meta
status: active
related: [".tech/tooling.md#archify", ".design/design-principles.md", ".design/accessibility.md"]
```

An Archify artifact is a whole generated document rather than a picture, and the
rules that follow are about showing somebody else's viewer inside a chapter
without it reading as a foreign object.

| Rule | Guidance |
|---|---|
| The reader chooses the renderer | A diagram with an artifact carries an Archify/Mermaid switch. An artifact is a *re-authoring* of the fence, not a rendering of it, so a reader who doubts the picture must be able to see the fence drawn instead. Both wear the same badge as the rest of the header, with the selected one highlighted. |
| The frame is sized to the drawing | Never a fixed height. An artifact's height follows the width it is given, and it differs per diagram — a landscape runtime view and a portrait building-block view are not the same shape. A fixed frame clips one and leaves the other in empty space. |
| The diagram sits on the chapter | The artifact's own background, panel and grid are cleared so the drawing composites onto the pane. A diagram in a body is part of the prose, not a card laid on top of it. |
| No control that visibly refuses | The artifact's theme toggle is hidden, because the app pins the dark theme from outside per `design-principles.md`; its visual-style picker is hidden when it offers fewer than two options. Everything the viewer can actually do — zoom, drag-to-pan, presets, presentation, export — stays. |
| It is a document, not an image | The frame is not labelled `role="img"`: that would collapse a structured document into one opaque graphic for a screen reader. The frame's title carries the accessible name instead, and what is inside stays reachable. |

**Not every diagram can have one.** Archify has five types — architecture,
workflow, sequence, dataflow, lifecycle. Class and ER diagrams have none, so a
bounded context's aggregate model is always Mermaid, and the app offers nothing
for it rather than offering something it cannot deliver.

The review surface is Storybook → *Diagrams* → **With an Archify artifact**,
which draws one committed artifact through `DiagramView`'s own artifact mode — the
switch, the default, full screen — and **Artifact out of date**, which draws the
same fence after an edit so the notice and the render offer can be seen. The
retired Mermaid-beside-Archify comparison page put the two renderers side by side
in a frame of its own; these stories exercise the component the app actually
ships.

## Risks and Gaps

```meta
status: active
```

| Risk / gap | Impact | Mitigation |
|---|---|---|
| No shared cross-stack component library covering mobile | Duplicated component effort between mobile's native MAUI XAML and the web-rendered channels | Accept it; invest in the shared **token pipeline** and the shared **web editor** as the reuse points. Desktop no longer duplicates this effort since ADR 0001 moved it to Razor/WebView2. |
| Token pipeline not yet decided | Drift between XAML and CSS token values | Adopt a build-time token source (e.g. Style Dictionary → XAML + CSS). `[TODO: clarify]` if in scope for v1 (also flagged in `color-scheme.md#per-stack-token-mapping`). |
| Native controls lack accessible keyboard reorder out of the box | C4 gap on mobile MAUI/WPF (desktop's web-rendered surface reuses the webview reorder story) | Implement explicit Move up/down/top/bottom commands + live announcements per `interaction-guidelines.md#keyboard-accessible-reordering`. |
| Web editor hosted in WebView2/BlazorWebView | Startup cost, bridge complexity, offline asset bundling | Bundle editor assets locally (local-first); measure cold-start; keep a native raw-text fallback. |
| Diagram libraries can become remote-CDN dependencies | Local-first UX breaks offline and can leak usage metadata | Prefer vendored/local static assets for Mermaid, X6, and ECharts in production; remote loading is acceptable only as a development fallback with source-visible fallback rendering. This is what took the graph renderer first-party — see `#materialization`. |
| Canvas graphics have no native keyboard semantics | Users may be unable to inspect graph-only relationships by keyboard or screen reader | The focusable node list in `#diagram-and-graph-strategy` is the answer, and it is the primary surface rather than an alternative to one. Preserve the layer cards, relationship chips and source Markdown alongside it; announce selection state before adding editable graph interactions. |
| A first-party renderer inherits every semantic a library would have brought | Keyboard operability, focus order, announcements and reduced motion are all the product's own work | Pin them: a test that fails when the node list stops matching the model is cheaper than the gap it prevents. |
| A hand-written projection has no upstream to inherit fixes from | Layout bugs, depth-sorting artefacts and performance regressions are found here or not at all | Keep the renderer data-driven and free of domain knowledge, as `components.js` already is, so it can be exercised from the storybook against models the app never produces. |
| Round-trip Markdown fidelity | Editor could rewrite/lose untouched content | Enforce `content-editing.md#round-trip-fidelity`; prefer Markdown-native editors (Milkdown/ProseMirror with a strict serializer); add round-trip tests. |
| TipTap "Pro" modules / SortableJS keyboard a11y | Cost or accessibility shortfall | Prefer TipTap/ProseMirror OSS core or Milkdown; prefer dnd-kit over SortableJS for accessible reorder. |
| Fluent/Mud default palettes are light-oriented | Fighting built-in light themes (C1) | Override with product dark tokens; verify no light defaults leak; test high-contrast. |
| Blazor Hybrid fallback (mobile) reduces native feel | UX divergence if fallback is used | Keep MAUI native as primary for mobile; treat Blazor Hybrid strictly as a fallback there, per architecture — this does not apply to desktop, where Blazor Hybrid is the accepted choice (ADR 0001). |

`[TODO: clarify]` final selection between **TipTap** vs **Milkdown** for the
shared editor, and whether the token pipeline is delivered in the first release.

## Materialization

```meta
status: active
related: [".design/README.md#living-reference-the-ui-storybook"]
```

What the product actually uses today, against the recommendations above:

| Layer | Recommended | In use |
|---|---|---|
| Desktop base controls | A shared web component library (`@vscode-elements`, Fluent Web Components) | **`Backlog.UI.Components`** — a first-party Razor class library with no domain in it, at `src/Core/Backlog.UI.Components`, rendered on its own in the storybook |
| Design tokens | One logical set emitted per stack | One `:root` block in `components.css`, linked by every host. This is the shared layer working as intended — it is just hand-maintained, not generated |
| Markdown editor | TipTap or Milkdown, hosted in the WebView2 | None. A text area over the source with a live read view beside it — see `content-editing.md#materialization` |
| Reorder | dnd-kit | Hand-written HTML5 drag-and-drop with arrow-key equivalents, in the desktop app rather than in the library — see `interaction-guidelines.md#materialization` |
| Diagrams | Mermaid | **Mermaid**, as recommended — `DiagramView`, storybook *Diagrams* |
| Graphs | A first-party canvas renderer | **A first-party canvas renderer**, as recommended — `GraphAtlas` over the technology graph, with `GraphView` and `GraphExplorer` still drawing the list, lane, spine and cluster layouts beside it; storybook *Graph atlas* and *Graph explorer* |
| Charts | Apache ECharts | None yet |

Notes:

- Choosing a first-party library over a third-party one is a live decision, not
  an oversight, and it buys exactly what the "tokens are the shared layer"
  finding predicted: no library theme to fight, and every component under review
  in one place. The cost is that everything — a11y semantics, keyboard support,
  reorder — is the product's own work rather than inherited. The gaps listed in
  `accessibility.md#materialization` are that cost.
- **Mermaid is fetched from a CDN on first use** (`jsdelivr`), with the diagram
  source shown as the fallback when the fetch fails. That matches the
  development-fallback allowance in `#risks-and-gaps` and violates the
  local-first rule for production: it MUST be vendored into the library's own
  `wwwroot` before the desktop app ships. On a machine with no egress today, the
  storybook's *Diagrams* page shows source instead of a picture — which is the
  fallback behaving correctly.
- **G6 was never actually loaded.** The loader existed, pointed at `unpkg`, and
  nothing ever called it; no `.tech` chapter recorded G6 either, so the library
  was a recommendation the product had written a hook for and never taken.
  Recording that plainly is better than leaving a dead CDN reference in the
  library and a recommendation above it that reads as though it shipped. The hook
  is gone with the recommendation.

