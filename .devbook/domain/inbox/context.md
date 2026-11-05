# Inbox

```meta
index: root
type: context
deployment: module
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

The Inbox is the processing queue for all captured input. It decides what
happens to an item after it arrives. Triage turns it into a
[Task](../tasks/domain.md#task) or a
[Knowledge Note](../brain/domain.md#knowledge-note) in the person's
[Brain](../brain/domain.md#brain), defers it, or archives it.

Inside the boundary:

- the Inbox Items that [Capture](../capture/domain.md#capture) delivers;
- their triage, classification and routing;
- the reader's own lists and groups.

Outside it: the capture sources, which [Capture](../capture/domain.md#capture)
owns, and what an item becomes once it is routed. A Task belongs to Tasks, and
a Knowledge Note to Devbook. How a synced capture reaches the Inbox's intake is
decided in `.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md`.

## Inbox pane

```meta
type: feature-flag
key: inbox-pane
default: off
related: [".devbook/domain/inbox/features.md#incoming-queue", ".devbook/domain/capture/features.md#run-capture-now", ".devbook/arc42/adr/0022-the-shell-shows-one-main-view-picked-by-a-view-switch.md"]
```

Turning it on shows the Inbox option in the Home shell's header, the only way to reach the incoming queue on the desktop. It gives the Inbox the whole screen in place of the main view, and pressed again returns to that view (local ADR 0022, amendment of 2026-10-12). The flag also shows the Inbox page of the settings screen, where the [routing rules](#inbox-routing-rules) are written.

It ships off and marked `DEV` while triage is under construction. The key is the Inbox's own, `InboxFeatures.Pane`, because the Shell and the Inbox settings page both read it. `AppFeatures.InboxPane` names the same key for the Shell. The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json`. The flag is retired when the Inbox is released, and the Inbox option then always shows.

## Inbox routing rules

```meta
type: setting
key: inbox-routing-rules.json
scope: user
default: no rules
related: [.devbook/domain/inbox/features.md#classification-and-enrichment]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxRoutingRulesStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsInboxRoutingRulesTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxRoutingRuleTests]
```

This setting says which repository the reader wants an item to go to, as a list of rules. Each
rule is one line, `pattern => owner/repo`. The pattern's first character says
what it is read against:

- `#design` matches one of the item's tags.
- `@alice` matches the person who shared the item.
- Anything else matches the capture channel or the source link: `youtube`,
  `*github.com/JSdotNet/*`.

`*` stands for any run of characters, and the match ignores case.

A rule only proposes. Every rule an open item matches puts that repository on
the item's suggestion row, and nothing is assigned until the reader takes the
chip. A rule that matches too much costs a rejected chip, not a wrong entry.

The rules are written in the one card of the Inbox page of Settings, Inbox
routing rules. The page's headline counts the rules in force:
`2 routing rules sort what lands in the Inbox.`, or
`No routing rules yet; everything lands in the Inbox unsorted.` A refused set
does not move it. The page shows only while the [Inbox pane](#inbox-pane) is on.

The whole set is refused when one line does not read, and the message names that
line, so the rules in use are always the last set that read. They are the
reader's own, kept in `inbox-routing-rules.json` next to the app's other
per-user settings, and never synced.
