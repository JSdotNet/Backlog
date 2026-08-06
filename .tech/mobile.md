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
related: [".tech/cloud.md#firebase-cloud-messaging"]
```

The primary mobile target platform.

- **Used for** — speech-shortcut capture, share-sheet capture, inbox review, and
  push notifications.
- **Why** — the personal-use scope is Android-first; iOS is not in the current
  baseline.

## .NET MAUI

```meta
status: candidate
kind: framework
depends-on: [".tech/mobile.md#android", ".tech/shared.md#c-language"]
related: [".arc42/04-solution-strategy.md#technology-choices"]
alternatives: [".NET MAUI Blazor Hybrid", "Blazor WebAssembly PWA", "Kotlin native"]
```

The preferred cross-platform app framework for the phone client.

- **Used for** — the native Android app shell, platform integrations (share
  target, speech, notifications), and offline storage.
- **Why** — named as the preferred mobile stack in
  `.arc42/04-solution-strategy.md#technology-choices`; keeps the phone channel in
  C# alongside desktop and cloud.

## Blazor Hybrid

```meta
status: candidate
kind: framework
depends-on: [".tech/mobile.md#net-maui"]
alternatives: ["XAML-only MAUI UI"]
```

Web-technology UI rendered inside the MAUI shell.

- **Used for** — sharing UI components between the phone client and any future
  web surface.
- **Why** — the closest documented fallback/complement to plain MAUI; keeps a
  path open to a PWA without a rewrite.

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
