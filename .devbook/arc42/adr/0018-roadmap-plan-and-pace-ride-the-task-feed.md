# ADR 0018: The roadmap plan and the planning pace ride the task feed as two whole documents

```meta
date: 2026-09-29
related: [".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/0009-captures-are-a-document-kind-on-the-replica.md", ".devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md", ".devbook/arc42/adr/0013-imported-plan-is-a-roadmap-item-laid-out-by-import.md", ".devbook/arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".devbook/arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".devbook/arc42/08-crosscutting-concepts.md#storage-and-sync", ".devbook/arc42/08-crosscutting-concepts.md#task-sync", ".devbook/domain/roadmap/context.md", ".devbook/domain/roadmap/context.md#story-points-a-week", ".devbook/domain/roadmap/features.md#syncing-the-plan-between-devices", ".devbook/domain/roadmap/features.md#carrying-the-pace-with-the-person", ".devbook/domain/roadmap/domain.md#roadmap-plan", ".devbook/domain/tasks/features.md#multi-device-sync"]
```

The roadmap plan replicates between paired devices as one whole document on the
existing `/tasks` feed, carried under the kind token `roadmap-plan`. The planning
pace replicates beside it as a second whole document under the kind token
`planning-pace`. Each document is last-write-wins on its own stamp, and the sync
service never looks inside either one.

## Status

```meta
```

Accepted, 2026-09-29, by the repository owner at the Personal Validation gate of the flow-spec run that drafted it. Nothing is built yet.

A **local** decision, numbered in the local sequence. Inherited ADR 0018 under
`.devbook/arc42/adr/guidelines/` is configuration and options binding, a
different subject. Every bare ADR number below means the local one.

The owner reported on 2026-09-29 that the roadmap does not follow them between
their PCs. Asked how it should, they settled two choices. They are inputs here,
not open questions:

| Choice | Settled as |
|---|---|
| How the plan travels | **One whole-plan document on the existing `/tasks` feed**, with its own kind token, the way captures ride it (ADR 0009). No new route, container, AppHost or bicep change. |
| The pace | **Travels too.** The typed story points a week, the chosen source and the per-repository pairs stop being per-device. |

This record picks how the pace travels. It **answers** the open question ADR 0005
left about the roadmap plan, and it **amends** ADR 0013 ruling 4, which kept the
pace per device and not replicated. Both records carry a dated note pointing here.

## Context

```meta
```

**The plan is ready to replicate and has been since 2026-09-05.** It is one row,
`roadmap_plan`, in `backlog.db`. The row holds the whole plan as one JSON document
(`RoadmapPlanDocument`, version 1: items, milestones, band colours) and an
`updated_at` stamp written on every save, in the same round-trippable format a
task's stamps use. The repository says of that column that nothing reads it yet.
It was added so that a later sync would not have to back-seed it, as the `tasks`
table had to.

**The pace is a per-user file.** `PlanningVelocitySettingsStore` keeps
`%LOCALAPPDATA%\Backlog\planning-velocity.json`: `storyPointsPerWeek`, `source`,
and an optional `repositories` object of per-alias pairs. The file has no stamp.
An absent file reads as the default of seven.

**A pace that stays behind would break a plan that travels.** Since the
2026-09-27 amendment to ADR 0013, every `effort`-placed item is re-projected from
today, at its repository's pace, each time the band loads. The projection saves
the plan whenever a date moves. With the plan shared and the pace not, two
devices with different paces would each redraw the same items to different
dates. Each would save its own dates as the newer plan, and the other would take
that plan and redraw it again. The plan would change hands on every load. So the
pace cannot stay behind once the plan travels.

**The feed can carry an opaque payload today.** The deployed service drops JSON
members it does not know. The `[JsonExtensionData] Unrecognised` passthrough of
commit ebb39b45 is newer than what is deployed. `ContentMd` is a string that every
deployed version keeps whole, and neither the push handler nor the replicas read
it. ADR 0011 rejected riding the task feed for annotations because an annotation
needed fields `TaskPayload` has no honest place for. A plan needs no such field.
It needs an id, a stamp, and one string.

**Every reader of the feed already tolerates a kind it does not know.**
`TaskReplicaMerge` on the desktop maps a document to a task only after deciding
to apply it, and counts a token it cannot parse as Skipped. It does not throw,
and the cursor moves on. The phone's `TaskFold` keeps every non-capture document
as a row, but the Tasks tab lists only rows picked for today (`InMyDayOn`). The
service's inbox listing, which the phone and the IDE use, filters on
`type = 'capture'`.

## Decision

```meta
```

### 1. The plan is one `roadmap-plan` document

```meta
```

The desktop writes the plan to the replica as a task-shaped `TaskChange`:

| Field | Value |
|---|---|
| `Id` | One constant id, the same literal on every device. It is chosen once in the implementing change and never changed, because a changed id forks the plan. The owner partition keeps two people's plans apart. |
| `UpdatedAt` | `roadmap_plan.updated_at` |
| `DeletedAt` | Never set. A plan is not deleted. A cleared plan is an empty plan, saved and sent like any other. |
| `Task.Type` | `roadmap-plan` |
| `Task.ContentMd` | The row's `document` column, verbatim |
| `Task.Title` | `Roadmap plan`, so the activity log and any listing have a name to show |
| `Task.Status`, `Task.Priority` | `draft`, `medium`, the Tasks defaults, so no field other than the type is unusual |
| Everything else | Empty or null |

`roadmap-plan` is not a member of the desktop's task-type vocabulary, for the
same reason ADR 0009 gives for `capture`. The literal is duplicated where it is
read and written, as ADR 0005 does for the other task literals.

### 2. The pace is a second `planning-pace` document

```meta
```

The pace travels the same way under its own constant id and the kind token
`planning-pace`. Its `ContentMd` is the file's stored JSON: `storyPointsPerWeek`,
`source` and `repositories`. The measured paces do not travel. Each device counts
them from the finished work it holds, and that work arrives through task sync.

The file gains an `updatedAt`, written on every change, and that is the
document's stamp. A file written before this change has none. It is stamped once
from the file's last-write time, because that is when the reader last set it.
This is the honest value, the same kind of value `created_at` was for the
`tasks` rows that predated `updated_at`.

**A separate document, not a field inside the plan.** Under whole-document
last-write-wins, a pace changed on one PC and a plan edited on another would
otherwise overwrite each other, and one of the two would be lost. As two
documents, both survive. The pace also lives in a different store with a
different owner (a per-user file, not a row in `backlog.db`), and two documents
keep that boundary.

**The working week stays on the device.** Nothing in placement reads it: a
plan's window is in calendar days and a measured pace counts whole calendar weeks
(ADR 0013 ruling 4). It can be separated from the pace, so it stays per device
with the other workspace settings.

Per-repository pairs are keyed by repository alias. The alias is the name a
repository has on every device (ADR 0011, ADR 0005 §Session records). A device
that names a repository differently reads that repository's global pace, as it
does today for an alias with no entry.

### 3. The desktop's intake and push

```meta
```

**Pull.** `TaskReplicaMerge` hands each `roadmap-plan` and `planning-pace`
document to a port the Roadmap module answers. It does this before it reads a
local task, the way it hands a capture to `IInboxIntake`. The Sync infrastructure
holds no roadmap logic. It routes by kind and passes the string and the stamp
through. The port decides one thing, by stamp:

- **A later `UpdatedAt` than the local one is taken whole.** The local row, or
  the local file, is replaced with the inbound string verbatim and the inbound
  stamp, not the current time. It is never re-serialised, so a newer build's
  fields survive on disk. The write is not announced as a local change, for the
  loop reason `ITaskChangeSignal.Suppress` gives. The roadmap surface reloads
  on it.
- **An equal stamp is an echo** and writes nothing.
- **An older stamp is refused**, as ADR 0005's 2026-09-21 amendment does for
  tasks.
- **A payload that does not parse as the document it claims to be is Skipped**
  and logged. The local plan stays as it is. The same trade `LoadAsync` makes:
  nothing is overwritten with something unreadable.

**Push.** `TaskSyncSession.PushAsync` sends each document after the task
batches, in the same way it drains the capture outbox, whenever the stamp is
later than the one this device last had accepted for it. The replica refuses a
stale copy (ADR 0005, amendment of 2026-09-18).

**A device that never saved sends nothing.** No `roadmap_plan` row means no plan
document. No velocity file means no pace document. So a newly paired machine
takes the other machine's plan and pace. It never replaces them with an empty
plan or with the default of seven.

**A head without Roadmap holds both kinds.** The phone does not apply them.
`TaskFold` skips `roadmap-plan` and `planning-pace` alongside `capture`, so its
view store keeps no rows it will never list.

### 4. The service does not change

```meta
```

No request or response record, route, container, index, AppHost resource or
bicep file changes. The push handler still does not look inside a payload.
`ListInbox` still filters on `capture`. The replica stays free of domain logic,
as ADR 0005 requires: to the service, both documents are ordinary task documents
with a type string it does not interpret.

## Consequences

```meta
```

Positive:

- **It works against the service that is deployed now.** No redeploy has to
  happen before the owner's two PCs share a plan.
- **The plan is always a valid whole.** One device's plan replaces the other's
  in full, so a dependency cycle can never be created by merging two acyclic
  plans (`.devbook/domain/roadmap/features.md#syncing-the-plan-between-devices`).
- **Bars are the same length on every device**, because the pace they are
  divided by travels with the plan.
- **Links already resolve.** A plan item's `backlogEntryId` names a task that
  arrives through task sync. An imported plan's band is derived from its tasks'
  `ImportPlanId`, which already travels. Band colours are keyed by alias, the
  cross-device name.

Negative:

- **Concurrent edits lose the older save.** If two PCs both save the plan
  between syncs, the later stamp wins every device and the other edit is gone.
  The same applies to two pace changes. The window is the few seconds between a
  local write and its push, plus any time a device is offline. Only per-item
  merge would change this. It is rejected below, and it would need the cycle
  check to run on the merged result.
- **The first sync on a device with its own plan takes the newer plan
  wholesale.** A second PC that already had a saved plan keeps it only if it
  saved later than the first PC. The other plan is lost, and nothing merges or
  warns. This happens once per device pairing, and the owner accepted it.
- **The daily re-projection counts as a save.** When the band loads on a new day
  and moves an `effort`-placed date, the plan gets a new stamp. A device that
  opens the roadmap before it has pulled can overwrite an edit the other PC made
  that morning. Once both devices hold the same tasks and pace, the projection
  gives the same dates on both, and a save that moves nothing is not written, so
  the two converge. The implementing change should pull before the roadmap's
  first load when the device is online.
- **An older desktop build does not get the plan.** It meets a type it cannot
  parse, counts the document Skipped, logs a warning and moves on. This is
  harmless, and it lasts until that build is upgraded. An older phone keeps the
  document as a row that is never picked for today, so it never shows.
- **A newer document shape can be narrowed by an older build.** An older build
  stores a newer plan verbatim, but reads it only as far as it can. If the
  person edits the plan on that build, the build saves version 1 and the newer
  fields are gone everywhere. Whole-document last-write-wins has the same cost
  for any field.
- **The payload is opaque to the service.** The service cannot check its size or
  shape. A plan larger than a Cosmos item (2 MB) is refused on push. The plan is
  far below that.
- **The documents are task-shaped without being tasks.** That stretches the
  shape ADR 0011 declined to stretch. It is accepted because the payload is one
  string and the other choice needs a redeploy. The constant ids make a later
  move to a container of its own a one-time copy.

Neutral:

- A plan document is never tombstoned, so the 180-day tombstone TTL never
  applies to it.
- View state (zoom, scroll) is not stored and does not travel.
- `planning-velocity.json` gains one additive key, and an absent key has a
  defined reading. That is within ADR 0006's terms, even though the file is not
  in the database.

## Rejected

```meta
```

- **A dedicated `roadmap` container, like `annotations` (ADR 0011).** It is the
  cleaner shape, with its own feed and a real schema. But it needs a new route, a
  new container and a redeploy before either PC can share anything. The owner
  chose the path that works now.
- **Per-item merge.** Much larger: item-level stamps, tombstones for removed
  items and milestones, and a cycle check over the merged result. The owner
  chose whole-document replacement.
- **The pace inside the plan document.** A pace change and a plan edit made on
  different PCs would overwrite each other.
- **The pace stays per device.** Two devices would redraw the same
  `effort`-placed items to different dates, and the plan would change hands on
  every load.
- **Replicating the working week with the pace.** Placement does not read it, so
  the two can be separated. It stays with the per-device workspace settings.

## Verification

```meta
```

The implementing `flow-code` run turns these into tests:

1. **Two paired devices share a plan.** Device A saves a plan and syncs. Device B
   syncs and loads the same items, milestones and band colours. Its
   `roadmap_plan.updated_at` equals A's.
2. **The newer save wins.** A and B both save before either syncs. After both
   sync, both hold the plan with the later stamp. The older push is refused
   (short `accepted` count), and the older pull writes nothing.
3. **An echo writes nothing.** A plan pulled back at its own stamp writes no row
   and raises no local-change signal.
4. **The pace follows.** A changes the typed pace, the source, or one
   repository's pair and syncs. B then reads the same values and draws the same
   bar lengths. A pace change on A and a plan edit on B, made in the same
   interval, both survive.
5. **A device with no saved plan never overwrites.** B has no `roadmap_plan` row
   and no velocity file, pairs and syncs. It pushes nothing, and afterwards holds
   A's plan and pace. A still holds its own.
6. **An unreadable payload is skipped.** A `roadmap-plan` document whose
   `ContentMd` is not a plan counts Skipped. The local row is unchanged.
7. **An older build skips harmlessly.** A merge without the Roadmap port (the
   pre-change type vocabulary) counts both kinds Skipped. It writes no task and
   still moves the cursor past the page.
8. **The phone never lists them.** `TaskFold` produces no row for either kind.
9. **The service is unchanged.** The existing push, pull and inbox-listing tests
   pass unmodified, and `ListInbox` returns no `roadmap-plan` document.
