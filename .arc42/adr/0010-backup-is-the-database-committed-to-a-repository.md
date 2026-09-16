# ADR 0010: A backup is the database committed to a GitHub repository, one way, on a schedule

```meta
status: active
date: 2026-09-16
related: [".arc42/08-crosscutting-concepts.md#storage-and-sync", ".arc42/11-risks-and-technical-debt.md", ".arc42/adr/0003-sqlite-is-the-canonical-local-task-store.md", ".arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".arc42/adr/0006-additive-schema-bootstrapping-is-the-local-migration-mechanism.md", ".arc42/adr/0008-knowledge-reads-from-a-branch-snapshot-when-there-is-no-clone.md", ".domain/tasks/features.md#backup-to-a-repository"]
issue: null
```

## Status

Accepted, and built: `BackupWorker` in `Backlog.Infrastructure.FileSystem`, the
`CommitFileAsync` operation on the GitHub client, and the Backup block of the
Storage settings tab landed together on 2026-09-16.

A **local** decision, numbered in the local sequence — not to be confused with
inherited ADR 0010 under `.arc42/adr/guidelines/`, which is about observability.
Every reference below to a local ADR by bare number means the local one.

## Context

**The Storage tab has carried a "backup repository" field that backed nothing
up.** `WorkspaceSettingsStore.RootRepository` was "optional GitHub
repository metadata for future backup", and the screen said so. Nothing read it.

**The backlog has no copy anywhere but the machine.** Local ADR 0003 made it one
SQLite file, and the multi-device replica local ADR 0005 designed is built but
not in service — nothing is provisioned in Azure, and the feature flag is off by
default. R9 in `11-risks-and-technical-debt.md` records what happened to the one
person who tried to get a copy through a file-sync folder: eleven conflicted
databases and silently reverted edits. So the honest state was: a single file,
on a single disk, with a settings field that promised a backup and delivered a
warning not to make one the only way anybody could.

**Local ADR 0006 already asks for one.** Its migration mechanism names "a backup
taken before the first destructive statement" as a requirement of any versioned
migration, and records that no backup is taken today.

**The pieces were all there.** The app already commits files to GitHub through
the Contents API (`GitHubClient.UploadFileAsync`, for feedback screenshots), it
already resolves a credential for a repository — the signed-in `gh` account, or
a token the person bound — and it already copies the database consistently
through SQLite's backup API when it moves the root. What was missing was a
decision about what a backup *is*.

## Decision

**A backup is one consistent copy of `backlog.db`, committed to a GitHub
repository the person names, at the fixed path `backlog/backlog.db` on that
repository's default branch.** Every backup replaces the file at that path; the
repository's history holds every earlier one, which is what a repository is for.

**The database and only the database.** Not the inbox folder, not the repository
registry beside it, not any cache. Local ADR 0003 makes the database the whole of
the backlog — "backing up or moving a backlog is one file to copy" — and a
restore is therefore one file put back where `WorkspaceSettingsStore.DatabasePath`
says it belongs. Anything beside it is either the app's own bookkeeping, which is
worthless on another machine and says so in its own doc comments, or content a
person keeps there for their own reasons, which is theirs to back up.

**Taken through the backup API, never by reading the file.** In WAL mode the
file on disk is behind whatever the write-ahead log still holds; a raw read is a
database missing its newest writes. The worker copies into a temporary file
through `SqliteDatabaseFile.CopyTo` — the same call `TryMoveRoot` makes for the
same reason — reads that, and deletes it.

**An unchanged database makes no commit.** The Contents API reports the blob id
of what it holds, and that id is git's own SHA-1 over the bytes, so the worker
computes it locally and asks GitHub once: equal means nothing is uploaded and
nothing is committed. A daily schedule over a backlog nobody touched at the
weekend produces no weekend commits.

**One way, by construction.** Nothing in the app reads the repository back into
the folder. A backup that came home on its own would be file sync under another
name, and R9 is what that did. Restoring is a person's decision, made by hand,
and the settings copy says so.

**Scheduled in local time, with a catch-up.** The schedule is off, daily at a
time, or weekly on a day at a time, and lives beside the repository in
`settings.json` because they are one decision. The worker arms a one-shot timer
for the next slot and re-arms after every run; a slot that went by while the app
was closed is taken shortly after the next start. A schedule that has never run
owes nothing until its first slot — the button is what asks for a backup now.

**A worker, not a hosted service, and not a settings-screen timer.** Shaped on
`TaskSyncWorker` for the reasons that record gives: MAUI has no generic host, and
a loop that only existed while the Storage tab was open would miss every slot it
was set for. Both desktop heads resolve it once after `Build()`.

## Consequences

- The Storage tab's "Backup repository" block becomes a Backup block: the
  repository, the schedule, a "Back up now" button, and one status line that
  answers "is my backlog safe?" — the last backup and its outcome first, the next
  slot second.
- What the last backup did is kept in `backup-state.json` beside the per-user
  settings, never under the root — the same placement `FileTaskSyncStateStore`
  argues for — so the line under the button survives a restart.
- The GitHub client gains `CommitFileAsync`, an upsert that names the blob it
  replaces, and `GitHubException` gains the HTTP status GitHub answered with, so
  "not committed yet" can be told from "no such repository" without parsing a
  sentence meant for a person. Both transports set it.
- The Contents API caps a file at 100 MB and carries it base64-encoded in one
  request. A backlog that outgrows that will be refused with GitHub's words on
  the status line; it is not a case this record designs for.
- The credential is whatever the app already resolves for that repository —
  the `gh` login, or a bound account's token. A private backup repository the
  signed-in account cannot write to fails the same way any other write does,
  with GitHub's reason on the line. Nothing about credentials changes.
- Local ADR 0006's "a backup before the first destructive statement" is now
  something a person can have taken; it is still not something the migration
  mechanism takes for them.

## Alternatives considered

- **An export in the entry-text grammar (local ADR 0007) instead of the binary
  database.** Diffable and mergeable in git, and readable without the app. Rejected
  for a *backup*: the grammar is the content, not the store — it does not carry
  the Inbox tables, the roadmap plan, `updated_at`/`deleted_at`, or anything the
  replica needs — so a restore would be an import that loses exactly what a
  backup exists to keep. It stays the right shape for an export, which is a
  different feature.
- **Date-stamped file names.** The `FeedbackReporter` precedent, and it would
  have avoided the sha lookup. Rejected because the repository would grow one
  full database per slot forever with nothing to prune it, and "which one is
  current?" would be a question the file listing had to answer. A fixed path with
  history is the same set of copies, named by git.
- **A dedicated branch.** Rejected: a person who points this at their notes
  repository wants the backup where they can see it, and a folder of its own is
  enough to keep it from landing beside their README.
- **The sync replica as the backup.** It is the right long-term answer for the
  reconciliation problem, and it is unprovisioned. This record does not wait for
  it and does not compete with it: a replica is a live copy that comes back; a
  backup is a dead copy that does not.

## Open questions

- **Everything beside the database.** The inbox folder's captured files and the
  registry are outside this record. If they ever join, they join as a second
  decision, not as an expansion of "one file".
- **Restore from the app.** Deliberately absent. If it is ever offered it needs
  its own record, because it is the one-way property above being given up on
  purpose.
