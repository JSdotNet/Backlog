---
name: handle-remarks
description: Handle the private reading notes (remarks) a person left on this repository's Devbook chapters in the Backlog app, and close the loop on each one. Use when asked to handle, answer, clear, work through or empty the Devbook notes, remarks or annotations for a repository, or when a session is told there are open notes waiting. Answers each note in the app's own note store — a default-branch note by a reply through `devbook_post_note` and `devbook_resolve_note`, any other in the session report and closed with `resolve_annotation` — and never writes an `annotation` fence. Commits nothing and never pushes; a chapter fix an answer needs goes through the delivery gate.
---

# Handle the remarks on a repository's Devbook chapters

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

A person reading a Devbook chapter in Backlog leaves remarks against its blocks. This skill
empties that inbox: read the open ones, work out each answer, record the answer where the
note is, and mark the note resolved.

**A review note lives in Backlog's note store, never in the chapter**
(`.devbook/arc42/adr/0044-devbook-subject-state-lives-in-backlogs-own-store.md`,
`.devbook/arc42/adr/0011-devbook-annotations-are-a-third-replica-container.md`). Devbook contract 38
retired the `annotation` fence: the checker rejects one in a chapter, and the one-time
import has already moved every default-branch fence into the store. This skill writes no
file at all — the answer goes to the note, through the `backlog` MCP server.

## Which tool answers which note

| The note | Reply | Resolve |
| --- | --- | --- |
| A repository's note on its default branch | `devbook_post_note`, `replyTo` the note's id | `devbook_resolve_note` |
| A note those refuse — `unknown-note` once retried with the note's own address from `devbook_list_notes`, a note left on a branch snapshot, a Brain note (`brain.no_workflow_state`) | the answer in the session report, naming the note | `resolve_annotation` |

`devbook_post_note` and `devbook_resolve_note` keep only a repository's default-branch notes
and refuse the Brain. `resolve_annotation` takes any note `list_annotations` reports,
Brain notes included, but carries no reply — so for those the report is the record of the
answer.

## Workflow

1. **Place.** Read `owner/name` off the git remote — every Backlog call carries it as
   `repository`. Confirm the checkout is the repository whose notes are being answered.
2. **Read the inbox.** Both listings answer one chapter at a time, not the whole repository:
   take the chapters from the request, the notification or the item that sent you here.
   `devbook_list_notes` lists a chapter address's default-branch notes;
   `list_annotations` lists one `chapterPath`'s notes on the branch the repository is read
   from, or one Brain note's. Without a reachable Backlog there is no inbox and nothing to do:
   say so and stop. When `list_annotations` answers `archived: true`, the repository is
   archived and its devbook read-only: reply to and resolve nothing, say so and stop. Take the
   open notes only — a resolved one is already answered, and a draft was never sent.
3. **Answer each note.** Read the chapter and whatever the remark is actually about — the
   code, the decision, the neighbouring chapters. Work out the answer before writing
   anything. A remark you cannot answer stays open; say which and why. Where the answer is
   that the chapter or its code must change, that fix is a change of its own: it goes through
   the delivery gate (`CLAUDE.md` — `delivery:flow-spec` for a chapter, `delivery:flow-code`
   for code), and the answer says it is being made there. This skill edits and commits
   nothing.
4. **Record the answer**, by the table: a reply through `devbook_post_note` with `replyTo`
   the note's id and the answer as `body`; or, for a note it refuses, the answer written into
   the session report against that note.
5. **Resolve the note**, by the table: `devbook_resolve_note` for a default-branch note,
   `resolve_annotation` for the rest. Answer first, resolve second, always: an answered note
   still open can be found, while a note resolved without its answer has lost it.
6. **Report**, opened with the `show-me` skill's orientation card when it has one. Name every note answered and how, with the full answer for each note answered
   in the report alone; every note left open with the reason; and every fix handed to the
   gate. Keep the report's prose to `../../resources/chat-reports.md`. **Never push**, and never open a pull request.

## Never

- Write an `annotation` fence into a chapter, by any means — the checker rejects it and the
  note store is where the answer lives.
- Resolve a note without an answer recorded for it, or invent an answer to close one.
- Reply to, or resolve a note of, an archived repository.
