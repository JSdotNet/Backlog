# Technical Debt Records

```meta
index: root
related: [".devbook/arc42/11-risks-and-technical-debt.md#technical-debt", ".devbook/arc42/adr/README.md"]
```

`11-risks-and-technical-debt.md` lists the debt and says what it costs; this folder
holds the records themselves, one per item, each
with its origin, impact, severity and the remediation options that were weighed.

Debt against an **inherited** decision is not recorded here. It lives in that decision's
own *Deviations and gaps* section under `.devbook/arc42/adr/guidelines/`, beside the rule it
falls short of.

Each record moves through `identified → planned → in-progress → resolved` in its own
`## Status` section. A resolved record stays in the folder; the row in chapter 11 is
struck through, the way D2 and D3 already are.

## Records

```meta
```

- **[0001 — Archify artifacts are invisible when the Devbook reads from a branch snapshot](0001-archify-artifacts-are-invisible-on-a-branch-snapshot.md)** — identified 2026-09-16.
- **[0002 — The coding and testing conventions have no checked-in home](0002-coding-and-testing-conventions-have-no-checked-in-home.md)** — identified 2026-10-05.
- **[0003 — The devbook check is not a zero-warning signal](0003-the-devbook-check-is-not-a-zero-warning-signal.md)** — identified 2026-10-05.
