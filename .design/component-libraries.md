# Component Libraries

```meta
status: active
related: [".arc42/04-solution-strategy.md#technology-choices", ".design/color-scheme.md#per-stack-token-mapping", ".design/content-editing.md", ".design/interaction-guidelines.md#drag-and-drop-reordering"]
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
| **AntV G6** | Technology / knowledge graphs | ✔ | ✔ (CSS/container styling + node palettes) | — | ◑ (canvas interactions; keyboard companion list required) | ◑ | ✔ MIT / active |
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
| Technology and knowledge graphs | **AntV G6** for interactive `.tech` and repository knowledge graph visualizations. | Graph navigation, force layouts, zoom/pan, and animated exploration fit technology maps better than component-suite diagrams. |
| Future editable diagramming | **AntV X6** for node/edge flow editors; **diagrams.net/draw.io** only if a full general-purpose GUI editor is needed. | Keeps read-only knowledge rendering simple now while leaving a professional editing path for later. |
| Charts and dashboards | **Apache ECharts**. | Broad chart coverage, active maintenance, permissive license, dark theme support, and strong dashboard fit. |


## Diagram and Graph Strategy

```meta
status: active
related: [".tech/technology-graph.md", ".tech/tooling.md#archify", ".design/content-editing.md", ".design/accessibility.md"]
```

Backlog should solve diagrams in layers instead of relying on one component suite:

| Need | Choice | Guidance |
|---|---|---|
| Flow, C4, sequence, state, class/domain-model diagrams in knowledge Markdown | **Mermaid** | Render fenced `mermaid`/`mmd` blocks directly inside knowledge-base Markdown. Keep Markdown as the canonical source and show the source fallback when rendering fails or assets are unavailable. |
| A considered picture for the diagrams that earn one | **Archify** (generated artifact) | An optional second rendering of a fence somebody has authored a specification for. Never a replacement for Mermaid and never automatic: a fence with no artifact, or whose text has moved on, is drawn by Mermaid. See `#archify-artifacts` below. |
| Technology graph / knowledge graph exploration | **AntV G6** | Use G6 for interactive node-link graphs with zoom, pan, drag, force/layout animations, and status/layer coloring. Keep list/card views as keyboard-accessible alternatives. |
| Editable workflow/flowchart designer | **AntV X6** (future) | Add when users need to create or edit node-edge diagrams visually. It is better suited to diagram editing than G6. |
| Charts and operational dashboards | **Apache ECharts** (future) | Preferred for metrics, trend charts, dependency health, and monitoring dashboards. |
| Full general-purpose diagram editor | **diagrams.net / draw.io** (optional future) | Consider only when a broad GUI editor is more valuable than Markdown-first diagrams. Bundle assets locally if embedded. |

All diagram libraries must follow the product defaults: dark mode only, local/offline asset bundling for production, no save buttons, Markdown remains canonical for knowledge documents, and every pointer interaction needs a keyboard-accessible companion surface with announced state changes.

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
| Diagram libraries can become remote-CDN dependencies | Local-first UX breaks offline and can leak usage metadata | Prefer vendored/local static assets for Mermaid, G6, X6, and ECharts in production; remote loading is acceptable only as a development fallback with source-visible fallback rendering. |
| Canvas graph libraries have weaker native keyboard semantics | Users may be unable to inspect graph-only relationships by keyboard or screen reader | Preserve the layer cards, relationship chips, and source Markdown as keyboard/screen-reader alternatives; announce graph selection state before adding editable graph interactions. |
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
| Graphs | AntV G6 | **AntV G6**, as recommended — `GraphView` and `GraphExplorer`, storybook *Graph explorer* |
| Charts | Apache ECharts | None yet |

Notes:

- Choosing a first-party library over a third-party one is a live decision, not
  an oversight, and it buys exactly what the "tokens are the shared layer"
  finding predicted: no library theme to fight, and every component under review
  in one place. The cost is that everything — a11y semantics, keyboard support,
  reorder — is the product's own work rather than inherited. The gaps listed in
  `accessibility.md#materialization` are that cost.
- **Mermaid and G6 are fetched from a CDN on first use** (`jsdelivr`, `unpkg`),
  with the diagram source shown as the fallback when the fetch fails. That
  matches the development-fallback allowance in `#risks-and-gaps` and violates
  the local-first rule for production: both MUST be vendored into the library's
  own `wwwroot` before the desktop app ships. On a machine with no egress today,
  the storybook's *Diagrams* and *Graph explorer* pages show source instead of a
  picture — which is the fallback behaving correctly.

