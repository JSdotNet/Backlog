# Inbox

```meta
status: draft
index: root
type: context
deployment: module
sync: pull
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

The Inbox is the processing queue for all captured input. Items arrive from
[Capture](../capture/domain.md#capture) as Inbox Items. The Inbox owns
**what happens to items after they arrive** — triage, classification, and
routing — deciding whether each item becomes a
[Task](../tasks/domain.md#task), a
[Knowledge Note](../devbook/domain.md#knowledge-note), is
deferred, or is archived. Outside it: the capture sources, which
[Capture](../capture/domain.md#capture) owns, and what an item becomes once it
is routed — a [Task](../tasks/domain.md#task) or a
[Knowledge Note](../devbook/domain.md#knowledge-note), owned by their contexts.
How a synced capture reaches the Inbox's intake is
`.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md`.

## Inbox pane

```meta
status: draft
type: feature-flag
key: inbox-pane
default: off
related: [".devbook/domain/inbox/features.md#incoming-queue", ".devbook/domain/capture/features.md#run-capture-now"]
```

Turning it on shows the Inbox option and its pane in the Home shell, the only way to reach the incoming queue on the desktop. It ships off and marked `DEV` while triage is under construction. The Shell is its only reader, so the key sits on the Shell's own catalog (`AppFeatures.InboxPane`). The settings screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is kept in `features.json` beside the app's other per-device choices. It is retired when the Inbox is released and the pane is simply there.

## Inbox routing rules

```meta
status: draft
type: setting
key: inbox-routing-rules.json
scope: user
default: no rules
related: [.devbook/domain/inbox/features.md#classification-and-enrichment]
tests: [unit:dotnet:Backlog.Infrastructure.FileSystem.UnitTests.InboxRoutingRulesStoreTests, unit:dotnet:Backlog.Desktop.UI.UnitTests.SettingsInboxRoutingRulesTests, unit:dotnet:Backlog.Modules.Inbox.UnitTests.InboxRoutingRuleTests]
```

Which repository the reader wants an item to go to, as a list of rules. Each
rule is one line, `pattern => owner/repo`. The pattern's first character says
what it is read against:

- `#design` matches one of the item's tags.
- `@alice` matches the person who shared the item.
- Anything else matches the capture channel or the source link: `youtube`,
  `*github.com/JSdotNet/*`.

`*` stands for any run of characters, and the match ignores case.

A rule only proposes. Every rule an open item matches puts that repository on
the item's suggestion row, and nothing is assigned until the reader takes the
chip. So a rule that matches too much costs a rejected chip, not a wrong
entry.

The rules are written on the Repositories page of Settings, beside the
repositories they name. The field shows only while the [Inbox pane](#inbox-pane)
is on. The whole set is refused when one line does not read, and the message
names that line, so the rules in use are always the last set that read. They
are the reader's own and local to the machine, like the organiser. They are
kept in `inbox-routing-rules.json` next to the app's other per-user settings,
and are never synced.
