# ADR 0011: Centralized frontend styling variables

```meta
status: active
related: [".design/color-scheme.md", ".design/typography-and-layout.md", ".arc42/09-architecture-decisions.md"]
issue: null
```

Inherited from the organization's ADR 0011 (decided 2026-06-05,
`guide/adrs/0011-centralized-frontend-styling-variables.md`), imported
2026-08-27.

## Decision

Every color, font family, weight, size, line height, and spacing value is defined
as a **token in one central file**, and referenced from there.

- **Never** hardcode a hex value, font name, or size in a component style.
- **Always** use the token.
- A change that introduces a hardcoded style value is rejected in review; lint it
  where the toolchain allows.
- For Blazor: tokens are CSS custom properties in a stylesheet included once by
  the host, referenced from component-scoped `.razor.css` files.
- Theming is done by swapping token definitions, not by replacing values.

Token names carry intent — `--color-primary`, not `#FF5733`.

## How Backlog applies it

- The single source is
  `src/Core/Backlog.UI.Components/wwwroot/components.css`, served as
  `_content/Backlog.UI.Components/components.css` and included once per host.
- `tests/Backlog.ArchitectureTests/DesignTokenTests.cs` enforces it: every host
  references the library stylesheet, and token definitions live there rather than
  being re-declared per host.
- `.design/color-scheme.md` is the human-readable statement of the same tokens;
  `components.css` is where they bind.
- Backlog is **dark mode only** by product decision (`.design/design-principles.md`),
  so there is one token set rather than a light/dark pair.

## Deviations and gaps

- No stylelint rule; the architecture test is the enforcement, and it checks
  token *location*, not every possible hardcoded value inside a component.
