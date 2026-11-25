# ADR 0069: Model calls stay on the Foundry adapter, not Agent Framework, and the prompts gain an evaluation set

```meta
date: 2026-11-25
related: [".devbook/arc42/adr/0023-inbox-items-may-be-read-by-the-foundry-model.md", ".devbook/arc42/adr/0033-the-dashboard-sends-its-figures-to-foundry-whichever-part-was-pressed.md", ".devbook/arc42/adr/0049-the-desktop-transcribes-a-meeting-recording-through-foundry-then-removes-the-audio.md", ".devbook/arc42/adr/0062-the-sync-tier-deploys-from-hand-written-bicep-through-azd.md", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog.desktop]
c4-touches: [azureFoundry]
```

Backlog calls Foundry through `Backlog.Infrastructure.AzureFoundry`, a hand-written
adapter over the Azure OpenAI REST routes. It does not move to Microsoft Agent
Framework or `Microsoft.Extensions.AI`, because Backlog runs no agent. The
**Evaluation** rule of org ADR 0012 is adopted for the prompts whose answers a person
acts on.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records one deliberate difference from, and one move to,
[org ADR 0012: Agents in Applications](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0012-agents-in-applications.md).

## Context

```meta
```

Org ADR 0012 governs agents: a model that plans, calls tools and acts, hosted in a
module's Api with Agent Framework, through `IChatClient` and `AIAgent`. Backlog's model
calls are single requests with no tool loop:

| Caller | Route | Decided by |
| --- | --- | --- |
| `AzureFoundryInboxTriageAdvisor` | Chat completion: triage advice for an inbox item | Local ADR 0023 |
| `AzureFoundryInboxPlanDrafter` | Chat completion: draft a plan from an item | Local ADR 0023 |
| `AzureFoundryChatClient` (summary) | Chat completion: the Dashboard's Ask AI | Local ADR 0033 |
| `AzureFoundryEmbeddingsClient` | Embeddings for devbook search | — |
| `AzureFoundryTranscriptionClient` | Audio transcription | Local ADRs 0045, 0049 |

The agents that act on Backlog, Claude Code and Copilot, run outside it and reach it
over MCP (local ADR 0068). Their framework is not Backlog's choice.

The modules depend on their own ports (`IInboxTriageAdvisor`, `IInboxPlanDrafter`), not
on a model SDK, which is the aim of org ADR 0012's `IChatClient` rule. The tests replace
the adapter with stubs.

What the adapter lacks is any measure of answer quality. A prompt change in
`AzureFoundryTriagePrompt` or `AzureFoundryPlanPrompt` is checked only for its request's
shape (`AzureFoundryPlanFixtureTests`), never for whether the advice got worse.

## Decision

```meta
```

1. **Keep.** Model calls stay on `Backlog.Infrastructure.AzureFoundry` behind each
   module's port. No Agent Framework and no `Microsoft.Extensions.AI`. The first feature
   that gives a model tools and a loop inside Backlog adopts org ADR 0012 whole, in its
   own record.
2. **Move.** The triage advisor and the plan drafter each get an evaluation set in
   `tests/`: fixed inputs, a scored expectation per input, and a stored baseline. A
   scheduled workflow runs it against the Foundry deployment and fails when a score
   falls below the baseline. A prompt or model change in a pull request runs it on
   demand. Embeddings and transcription are measured by their own features, not by this
   set.

## Follow-up

```meta
```

| Work | Size | Entry |
| --- | --- | --- |
| Evaluation sets for the triage advisor and the plan drafter, a stored baseline, and a scheduled and on-demand workflow with Foundry credentials held as repository secrets | M | `+app-defaults-rollout`: *Add evaluation sets for the Foundry triage and plan prompts* |

## Consequences

```meta
```

- A prompt edit that makes the advice worse fails a run instead of reaching the person.
- Each run spends Foundry tokens. The set stays small, and the workflow runs on a
  schedule and on demand, not on every pull request.
- An app-defaults review reads this record for the 0012 row.

## Rejected

```meta
```

- **Move the adapter onto Agent Framework and `IChatClient` (M).** It would add a
  framework built for agents to five one-shot calls, and change no behaviour. The
  module ports already keep the model SDK out of the modules.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep the Foundry adapter for one-shot calls; add evaluation sets for the triage advisor and the plan drafter. | Step 15 of plan `app-defaults-rollout` (PR #1967) listed the org ADR 0012 difference with no local decision. |
