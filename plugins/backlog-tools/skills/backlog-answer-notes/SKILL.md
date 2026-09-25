---
name: backlog-answer-notes
description: Answer the private reading notes a person left on this repository's Devbook chapters in the Backlog app, and close the loop on each one. Use when asked to answer, clear, work through or empty the Devbook notes, remarks or annotations for a repository, or when a session is told there are open notes waiting. Writes each answer into the chapter as a devbook `annotation` fence and then resolves the note in Backlog; reports the changed chapters and offers the commit, and never pushes.
---

# Answer the notes on a repository's Devbook chapters

A person reading a Devbook chapter in Backlog leaves remarks against its blocks. This skill
empties that inbox: read the open ones, work out each answer, write the answer into the
chapter where a reviewer will find it, and mark the remark answered.

**Two different things are both called an annotation, and neither becomes the other**
(`.devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md`, §6):

- the **private note** — Backlog's `DevbookAnnotation`: the person's own data, on their
  devices, never in the repository. It is the inbox, read and resolved over MCP.
- the **fence** — the devbook `annotation` block written into the chapter itself: a shared
  review artefact that travels through git. `annotations.mjs` is its one writer; nothing
  else writes a fence, and Backlog never writes one at all.

## Workflow

1. **Place.** Read `owner/name` off the git remote — every Backlog call carries it as
   `repository`. Confirm the checkout is the repository whose notes are being answered.
2. **Read the inbox.** `list_annotations` for the repository. Without a reachable Backlog
   there is no inbox and nothing to do: say so and stop. Take the open notes only — a
   resolved one is already answered, and a draft was never sent.
3. **Answer each note.** Read the chapter and whatever the remark is actually about — the
   code, the decision, the neighbouring chapters. Work out the answer before writing
   anything. A remark you cannot answer stays open; say which and why.
4. **Write the fence**, in the session's checkout, with the devbook plugin's own
   `annotations.mjs` (`tools/devbook-meta/annotations.mjs`; this repository has no copy of
   its own until the contract v6 adoption lands):
   - `annotations.mjs add --chapter <path#slug> --after "<the block's text>" --author "<the
     person>" --date <the note's date> --kind question --body "<the remark, in their words>"`
     — `--after` both places the fence under the block the remark's `BlockIndex` names and
     becomes the fence's `quote`, so it has to be text from that block.
   - `annotations.mjs reply --chapter <path#slug> --index <n> --author "<you>" --body "<the
     answer>"`
   - `annotations.mjs resolve --chapter <path#slug> --index <n>` — closes the fence, leaving
     the exchange readable in the pull request that raised it.
5. **Resolve the note** with `resolve_annotation` for that id, when Backlog is reachable.
   Fence first, resolve second, always: a fence written and not resolved is recoverable, the
   reverse loses the answer's address.
6. **Report.** Name every chapter file changed, every note answered, and every note left
   open with the reason. Offer the commit. **Never push**, and never open a pull request.

## Never

- Write a fence with anything but `annotations.mjs` — no hand-edited block, no regex of
  your own.
- Resolve a note whose answer is not in the chapter, or invent an answer to close one.
