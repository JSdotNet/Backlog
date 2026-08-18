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

## APK Packaging

```meta
status: adopted
kind: tool
depends-on: [".tech/mobile.md#android"]
related: [".arc42/07-deployment-view.md#installation-and-updates-mobile"]
```

The packaging and release format for the Android client.

- **Used for** — building the mobile app as a signed, sideloadable APK and
  publishing it to GitHub Releases; `build/Install-AndroidApp.ps1` uses the
  same publish shape for developer sideloads.
- **Why** — the app is installed by sideloading rather than through Google
  Play, and an `.apk` (not `.aab`) is required because `.aab` bundles are only
  consumable by the Play Store.
- **How** — `dotnet publish` targets `net10.0-android` with
  `AndroidPackageFormat=apk`, `ApplicationDisplayVersion` set to the semantic
  version, and `ApplicationVersion` set to a packed integer `versionCode`
  (`major*10^7 + minor*10^5 + patch`) so a newer release always installs over
  an older one. Signing values
  (`AndroidSigningKeyStore`/`AndroidSigningKeyAlias`/`AndroidSigningStorePass`/
  `AndroidSigningKeyPass`) are passed on the publish command line from
  repository secrets in `.github/workflows/release-mobile.yml`, which refuses
  to produce an unsigned package and runs `apksigner verify` before
  publishing; `Backlog.Mobile.csproj` itself is unchanged. Because the
  keystore is self-signed, an APK signed with a different key cannot upgrade
  an existing install (Android rejects signature changes).
