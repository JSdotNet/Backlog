# Inbox

```meta
status: draft
index: root
type: context
```

The Inbox is the processing queue for all captured input. Items arrive from
[Capture](../capture/domain.md#capture) as Inbox Items. The Inbox owns
**what happens to items after they arrive** — triage, classification, and
routing — deciding whether each item becomes a
[Task](../tasks/domain.md#task), a
[Knowledge Note](../devbook/domain.md#knowledge-note), is
deferred, or is archived. It owns no capture sources.

On the desktop the Inbox is its own module (`Backlog.Modules.Inbox`) with its
own store — the `inbox_items`, `inbox_lists` and `inbox_groups` tables in
`backlog.db` — and no longer a projection over draft tasks. A capture that
arrives through sync is a `capture`-kind document on the replica and is handed
to the Inbox's intake before the task merge sees it
(`.devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md`).

## inbox-pane

```meta
status: draft
type: feature-flag
key: inbox-pane
related: [".devbook/domain/inbox/features.md#incoming-queue"]
```

Decided at release, from configuration. Name the switch in business language, and say who owns the rollout, what turning it on changes, and when the flag is retired.
