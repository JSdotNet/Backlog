# Capture

```meta
status: draft
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

Capture owns **how items enter the system**. It acquires raw content from a
source, normalizes it, and delivers it to the [Inbox](../inbox/domain.md#inbox-item)
as an Inbox Item. Once an item is delivered, Capture's responsibility ends. The
sources are:

- mobile devices;
- automated monitors;
- email subscriptions;
- web clippers;
- IDE-class hosts: IDE extensions, and agentic session tools such as the GitHub
  Copilot App;
- manual import.

Outside it: triage, routing, and everything that happens to an item after it
arrives, which the [Inbox](../inbox/domain.md#inbox-item) answers. In-app feedback
is the one capture that does not reach the Inbox: it is filed as an issue on the
product's own repository.

## Feedback reporting

```meta
status: draft
type: feature-flag
key: feedback-reporting
default: on
related: [".devbook/domain/capture/features.md#in-app-feedback-capture"]
```

Turning it on puts a report action in the app chrome. The action files a Desktop
app issue on GitHub with a title, details, and an attached screenshot, and is the
in-app route into Capture. It is released: on by default, and without the `DEV`
mark (under construction) or the `BETA` mark (built, not yet proven) that the
settings screen puts on an unfinished feature
([Feature Enablement](../../arc42/08-crosscutting-concepts.md#feature-enablement)).
The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a
person's choice is kept in `features.json` beside the app's other per-device
choices. It is retired once nobody needs to switch the report action off.

## Capture sources

```meta
status: draft
type: setting
key: capture-sources.json
scope: user
default: every source off, with no targets
related: [.devbook/domain/capture/features.md#run-capture-now, .devbook/domain/capture/features.md#youtube-monitor, .devbook/domain/capture/features.md#website-monitor, .devbook/domain/capture/features.md#news-email-ingestion, .devbook/domain/capture/domain.md#capture-source, .devbook/domain/inbox/context.md#inbox-pane]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.CaptureSourcesSettingsStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.CaptureSourcesPanelTests, unit:dotnet:Backlog.Modules.Capture.UnitTests.RunCaptureCommandHandlerTests]
```

Which sources Capture watches, and what each one is pointed at. It holds the
[capture sources](domain.md#capture-source) that can be monitored, and only
those: YouTube, website, and email, one entry each and always in that order.
Each entry has the source's kind, written as its slug (`youtube`, `website`,
`email`), whether it is on, and its list of targets. A target is a channel or
feed URL or handle, a page URL, or a sender address. Targets are identifiers
only, never a credential: the file is plain text meant to be read and
hand-edited, so a mailbox password does not belong in it. Nothing is watched
until the reader switches a source on.

The reader edits it in the Sources panel on the [Inbox pane](../inbox/context.md#inbox-pane),
where the switch and the targets commit the moment they change. The panel shows
the file's path. [Run capture now](features.md#run-capture-now) reads it at the
moment a run starts and polls only the sources that are switched on.

The setting is the reader's own and kept on the machine, in `capture-sources.json`
under `%LOCALAPPDATA%\Backlog`, and is never synced between devices. It has its
own file rather than a section of `settings.json`, which only points at the
workspace. Two other files sit beside it, each for a different job:

- the **capture run log**, `capture-runs.json`, keeps what the last runs found,
  per source, for the reader;
- the **target ledger**, `capture-targets.json`, remembers per target the entries
  a run has already passed over, so a run delivers each entry once.

The file is read tolerantly, so a hand-edit never stops the app from opening.
Targets are trimmed, blank ones are dropped, and duplicates are folded into one.
A kind nobody can read, or one that cannot be monitored, is dropped, and a
source missing from the file comes back switched off. A corrupt or unreadable
file falls back to nothing watched. A save that cannot be written keeps the
change for the session and says it could not be kept for next time.
