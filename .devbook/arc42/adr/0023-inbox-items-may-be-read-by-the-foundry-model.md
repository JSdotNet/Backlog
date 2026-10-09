# ADR 0023: Inbox items may be read by the Foundry model

```meta
status: proposed
date: 2026-10-08
related: [".devbook/domain/inbox/features.md#classification-and-enrichment", ".devbook/domain/inbox/features.md#ai-triage-cards", ".devbook/domain/inbox/features.md#ai-triage-pass", ".devbook/domain/inbox/features.md#create-plan-from-an-item", ".devbook/domain/inbox/features.md#order-a-batch-with-the-drafter", ".devbook/domain/inbox/requirements.md#ai-triage", ".devbook/arc42/05-building-block-view.md", ".devbook/arc42/adr/0007-import-reuses-the-entry-text-grammar.md"]
```

While the reader triages, the Inbox may send an item, and the backlog and inbox
titles it is compared against, to the Azure Foundry deployment the app is already
configured with. It does so only when the reader opens an item in triage or asks
for the AI triage pass, never in the background. Without Foundry, or when a call
fails, no AI surface is shown, and the rule-based suggestions stand as they are.

## Status

```meta
```

Proposed, 2026-10-08. This is the first entry (`spec-ai-triage`) of the
`inbox-redesign` plan, written first so that every later entry of the plan
builds this one decision. It becomes accepted when the repository owner approves
it at the Personal Validation gate of the draft pull request that carries it.

A **local** decision, numbered in the local sequence. Every bare ADR number below
means the local one. Number 0022 is taken by the `task-views` plan, still open.

The repository owner settled four choices in the plan. They are inputs here, not
open questions:

| Choice | Settled as |
|---|---|
| What is sent | The item's title, link, notes, kind, source, person, tags and repository ids. For duplicate and plan matching also the titles, ids, repositories and statuses of the open backlog tasks and of the other unprocessed inbox items, and the names of the configured repositories and inbox lists. |
| When | Only when the reader opens an item in triage, or asks for the AI triage pass. Never in the background. |
| Which model | The Azure Foundry deployment the app is already configured with, the one `AzureFoundryInboxPlanDrafter` uses. Duplicates, plan grouping and the pass all go through it. There is no local similarity fallback. |
| Without Foundry, or on a failed call | Every AI surface is hidden: no AI cards, no **Let AI propose the rest**. The rule-based suggestions are not AI and stay as they are. |

This record **reverses** one line of
`.devbook/domain/inbox/features.md#classification-and-enrichment`, which listed
among the things not built: "Suggestions from anything other than these rules:
no model reads the item."

## Context

```meta
```

**A model already reads an item, but only on a press.** Create plan
(`features.md#create-plan-from-an-item`) and Ask the AI to order
(`features.md#order-a-batch-with-the-drafter`) send an item's title, notes, link,
kind, tags and repository ids to Foundry through `IInboxPlanDrafter`. Each is a
button the reader presses, one call per press, and each stays visible and
disabled, with its reason, when no drafter is configured.

**The suggestions never asked a model.** Classification reads the item's own
text and the reader's routing rules
(`context.md#inbox-routing-rules`). That kept the Suggested row free and
offline, and it is why the feature chapter said no model reads the item.

**The redesign asks for judgement the rules cannot give.** The triage screen of
the redesign shows up to two AI cards over the item: *probably a duplicate*, of
an open backlog task or of another unprocessed capture, and *group into a plan*,
with the other captures that belong with it. The AI triage pass proposes a
decision for every unprocessed item at once. Both compare the item with what is
already in the backlog and the inbox, so the item alone is not enough to send.

## Decision

```meta
```

### 1. What is sent

```meta
```

For the item being decided:

- its title, source link and notes;
- its `Content Kind`, its `Source` and the person who shared it;
- its tags and its repository ids.

For duplicate and plan matching, the item is sent with what it is matched
against:

- the open backlog tasks: title, id, repository and status;
- the other unprocessed inbox items: title, id, repositories and status;
- the names of the configured repositories and of the reader's inbox lists.

Nothing else leaves the machine. Attachments are not sent, as Create plan never
sends them. No token, no setting and no path is sent, and neither is a task's
body. A closed or archived task and a decided inbox item are not sent.

### 2. When

```meta
```

A call is made in two cases only:

- **The reader opens an item in triage.** One call answers both cards for that
  item. The answer is kept until the app closes, so moving back to an item with
  **k** does not ask again.
- **The reader asks for the AI triage pass**, with **Let AI propose the rest**.
  One pass reads every unprocessed item in the slice being triaged.

Nothing is sent on intake, on a timer, while the list is browsed, or when an
item is opened outside triage. The Suggested row in the columns view stays the
rule-based one.

### 3. Which model

```meta
```

The deployment configured in Settings, read through `AzureFoundrySettingsStore`,
the one `AzureFoundryInboxPlanDrafter` already uses. The cards and the pass are
answered by the same chat client, so one endpoint, one key and one deployment
serve every Inbox call to a model. There is no second provider and no local
similarity fallback: a duplicate the model did not name is not guessed at by
string distance.

### 4. Without Foundry, or when a call fails

```meta
```

- **No Foundry configured.** The AI cards and **Let AI propose the rest** are
  not shown at all. This differs from Create plan, whose button stays visible
  and disabled: a card is a proposal, and a proposal nobody can make is noise.
- **A card's call fails.** That item shows no AI cards. Triage carries on, and
  the rule-based suggestions are numbered from 1.
- **The pass fails.** The review screen does not open, nothing changes, and the
  reader stays in triage with one sentence that names the failure.

The rule-based suggestions (`features.md#classification-and-enrichment`) are
not AI. They are shown with or without Foundry, exactly as before.

### 5. Nothing a model proposes is applied without the reader

```meta
```

A card is taken with its number key or a click, like any suggestion. The pass
changes nothing until **Apply**, and a proposal below confidence 0.6 starts
unaccepted. This is the rule the Suggested row already keeps, extended to the
model's proposals.

## Consequences

```meta
```

- The Inbox gains a port beside `IInboxPlanDrafter` for the cards and the pass.
  `Backlog.Infrastructure.AzureFoundry` answers it with the same chat client and
  settings store. A host with no Foundry registers nothing, and the Inbox reads
  "not available".
- The Inbox has to read the open backlog tasks to send them. It reads them
  through the Tasks module's public contract, as Before you route already does
  for an `after:` dependency on an open task.
- An item's notes and the backlog's task titles now leave the machine on an
  ordinary triage step, not only on a button named for AI. The person accepts
  that by configuring Foundry: an unconfigured app sends nothing.
- `InboxItem.DuplicateOf` may name a backlog task as well as an inbox item,
  because merging a capture into a task archives it as a duplicate of that task
  (`features.md#merge-into-a-task`).
- Each item opened in triage costs one model call per app session, and each pass
  costs one call. The Dashboard's Foundry cost reads them with the rest.

## Rejected

```meta
```

- **A local similarity fallback** (string distance or local embeddings) for
  duplicates when Foundry is absent. Rejected in the plan: two engines would
  propose different duplicates for the same item, and a weak guess offered as AI
  would teach the reader to ignore the cards.
- **Asking in the background**, on intake or on a timer, so the cards are ready
  before the item is opened. Rejected in the plan: it sends every capture to the
  model whether or not anyone triages it, and costs a call per capture.
- **Sending only the item**, without the backlog and inbox titles. A duplicate
  or a plan cannot be found without what the item is compared with.
- **Showing the AI surfaces disabled** without Foundry, as Create plan does.
  Rejected in the plan: section 4 gives the reason.
- **A second model or provider** for the cards. One deployment keeps one place to
  configure, one cost line and one failure sentence.
