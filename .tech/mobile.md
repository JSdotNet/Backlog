# Mobile Stack

```meta
status: candidate
related: [".tech/technology-graph.md", ".arc42/04-solution-strategy.md#technology-choices"]
```

> The phone channel: quick capture and read-mostly access. It is sync-dependent
> by design and exposes only a subset of the domains.

## Android

```meta
status: candidate
kind: platform
related: [".tech/cloud.md#firebase-cloud-messaging", ".tech/shared.md#net-maui"]
```

The primary mobile target platform; the platform head that `.tech/shared.md#net-maui`
uses on this channel.

- **Used for** — speech-shortcut capture, share-sheet capture, inbox review, and
  push notifications.
- **Why** — the personal-use scope is Android-first; iOS is not in the current
  baseline.

The mobile app shell itself (.NET MAUI, optionally with Blazor Hybrid) is
documented once in `.tech/shared.md#net-maui` and `.tech/shared.md#blazor-hybrid`,
since the desktop channel now uses the same framework
(`.arc42/adr/0001-desktop-stack-maui-blazor-hybrid.md`).

## Local Offline Store

```meta
status: candidate
kind: library
depends-on: [".tech/shared.md#json"]
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync"]
alternatives: ["SQLite"]
```

JSON-backed on-device storage for captures made while offline.

- **Used for** — queuing captures and cached reads until the next sync flush.
- **Why** — mobile is not canonical, so it only needs a durable queue plus a
  cache, not the full Markdown tree.
