# Plan entry brief

`execute-plan` starts each entry session with `start_session`: `initiation: user_asked`,
`context: fresh`, `use_worktree: true`, `title` per **Names** in `handoff.md`,
`wake_when_done: true`, `background` the one sentence
`This session runs one entry of the Backlog plan <tag>; execute-plan orchestrates the plan from another session.`,
and `prompt` the brief below, its `<…>` filled in per `landing-modes.md`: `<slug>` is as `landing-modes.md` defines it; `<cut-from>` is the
Landing table's *cut from* ref there; `<entry-branch>` is the name resolved there; `<switch>` is `git switch -c <entry-branch> <cut-from>` for
a new entry branch, or `git switch <entry-branch>` when **Names** there finds earlier work;
`<target>` is the branch the pull request targets; `<stacked-on>` is the two lines **Record
the cut** in `landing-modes.md` names, for a stacked entry, else empty; `<pr-title>` is `prTitle`
there with its `{ticket}` filled in and `{title}` left for the session. The plan-progress band
knows each session's entry by the brief's first line, so that line keeps its shape.

**Talking to an entry session.** Every message to one — a fix pass, a resume —
goes by `SendMessage` to its `local_…` id, or a fallback's agent id. `notify_when_idle` is
refused for these sessions, so `wake_when_done` is what wakes the orchestrator: only once every
sibling still working has ended its turn too, so a session's return waits on the slowest.

**Fallback.** The host refuses `start_session` on a turn the person neither typed nor chained
to by finished sessions — a wake-up, a task notification, any turn of a session another
started. Start the same brief as a background sub-agent: `general-purpose`, in its own
worktree, the session title as its description, the prompt unchanged. The engine runs no flow
as a sub-agent because a sub-agent cannot hold Personal Validation; no one holds it here anyway, so the entry
parks at its first gate as the engine's own sub-agent rule says and lands the draft. Its final
message is its return, its completion wakes the orchestrator, and its worktree is the
`git worktree list --porcelain` row on `<entry-branch>`. It is no session: `list_sessions` and
`list_events` never show it, its flow's usage is counted in the orchestrator's session, and
the host has no row to open or archive.

---

You run one entry of the Backlog plan `<tag>`: `<n> - <Title>` (id `<id>`) in `<repository>`,
**unattended**.

1. **Worktree.** The host made this session's worktree; work only inside it. `git fetch origin`,
   then `<switch>`, leaving the host's own worktree branch behind for its clean-up, and check
   the tree is clean. Confirm `git rev-parse --show-toplevel` and the
   branch before the first edit. Every file you produce
   — screenshots, traces, QA evidence — gets an absolute path inside the worktree; a tool that
   saves relative to another folder is given one.
2. **Run the entry** by invoking `backlog-tools:run-plan-item` with everything under
   **Entry** below as its argument, the `entry` line included — so the run's recorded prompt
   names the entry by id — and follow it and the repository's flow to the letter, except:
   - Keep this session's title: skip run-plan-item's step-2 title and the entry's
     `Title this session` line — the orchestrator already named it `<n> - <Title>`.
   - Never ask — not at a gate, not run-plan-item's outstanding-work questions, not a flow's
     question. Take the reasonable assumption and record it for the pull request body under
     **Assumptions**, or return `blocked <stage>: <the question>`.
   - Stacked: the stack parent's open branch, which you were cut from, satisfies its
     `after:`; so does every `after:` the orchestrator found reachable from it.
   - Pushing and opening the pull request are part of this brief, not an offer.
3. **Gates.** At the first gate the repository declares — the earlier of Personal Validation and,
   when `.devbook/config.json` lists it, `create-pr` — park with `approval: pending`, never self-approved,
   and land the change as the draft that stands in for every gate. Finish the flow's run as
   `parked`.
4. **Pull request.** Commit with a conventional subject, push, and
   `gh pr create --draft --base <target> --title "<title>"`. `<title>` is `<pr-title>` with
   `{title}` that subject. A title rule in the repository's own instructions for this kind of
   change, such as a prefix for a documentation-only pull request, wins; never make up a ticket. The body, in order: `<stacked-on>`; what changed;
   the acceptance criteria and how each was verified; Build & Test with its counts; review
   findings left open; **What could not be proved** — the section the person validates;
   **Assumptions**; and a line saying an unattended `execute-plan` run opened it.
   An empty diff opens nothing: return `done-before` when run-plan-item found the entry done,
   else `blocked implement: empty diff`.
5. **Close.** Do everything `run-plan-item` step 7 asks — `link_change` once, `comment`, with
   the decision given as `pending — validate on draft <url>` — except the
   `transition` to Done: leave the entry In progress. Never merge your own pull request. Stop
   this worktree's application, the way the repository's run procedure stops it scoped to this
   worktree, before you return.
6. **Return** by ending the turn: the first line of your final message is `done-before` with
   the evidence, `pr <url>`, or `blocked <stage>: <reason>`; a card goes
   under that line, never above it. Leave the worktree in place: it is the host's, and a later
   message from the orchestrator — a fix pass — continues this brief in it.

Entry:

/backlog-tools:run-plan-item entry `<id>`:
<entry text, exactly as the plan or `read_item` gives it>
