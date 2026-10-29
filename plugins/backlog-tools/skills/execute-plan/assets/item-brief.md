# Plan entry brief

`execute-plan` starts each entry session with `start_session`: `initiation: user_asked`,
`context: fresh`, `use_worktree: true`, `title` per **Names** in `handoff.md`,
`wake_when_done: true`, `background` the one sentence
`This session runs one entry of the Backlog plan <tag>; execute-plan orchestrates the plan from another session and answers its gates.`,
and `prompt` the brief below, its `<…>` filled in per `landing-modes.md`: `<attendance>` is
`attended` or `unattended`; `<slug>` is as `landing-modes.md` defines it; `<cut-from>` is the
Landing table's *cut from* ref there; `<switch>` is `git switch -c <slug>/<id> <cut-from>` for
a new entry branch, or `git switch <slug>/<id>` when **Names** there finds earlier work;
`<target>` is the branch the pull request targets; `<draft>` is `--draft` in branch and
stacked mode and in any unattended run, else empty; `<stacked-on>` is the two lines **Record
the cut** in `landing-modes.md` names, for a stacked entry, else empty. The plan-progress band
knows each session's entry by the brief's first line, so that line keeps its shape.

**Talking to an entry session.** Every message to one — a gate's answer, a fix pass, a resume —
goes by `SendMessage` to its `local_…` id with `notify_when_idle: true` in the same call; after
`start_session`, send one with that and no message. `wake_when_done` wakes the orchestrator
only once every sibling still working has ended its turn too, so without it a session's return
waits on the slowest.

---

You run one entry of the Backlog plan `<tag>`: `<n> - <Title>` (id `<id>`) in `<repository>`,
**<attendance>**.

1. **Worktree.** The host made this session's worktree; work only inside it. `git fetch origin`,
   then `<switch>`, leaving the host's own worktree branch behind for its clean-up, and check
   the tree is clean. Confirm `git rev-parse --show-toplevel` and the
   branch before the first edit. Every file you produce
   — screenshots, traces, QA evidence — gets an absolute path inside the worktree; a tool that
   saves relative to another folder is given one.
2. **Run the entry** by invoking `backlog-tools:run-plan-item` with everything under
   **Entry** below as its argument, the `entry` line included — so the run's recorded prompt
   names the entry by id — and follow it and the repository's flow to the letter, except:
   - Attended: never ask anything in this session — not at a gate, not run-plan-item's
     outstanding-work questions, not a flow's question, not any decision you do not own. Every
     question is a return: `gate`, then the question, its options and your recommendation. The
     host never wakes the orchestrator while you wait at a prompt answered here; the
     orchestrator relays your return to the person and sends the answer back as a message.
   - Stacked: the stack parent's open branch, which you were cut from, satisfies its
     `after:`; so does every `after:` the orchestrator found reachable from it.
   - Unattended: never ask. Take the reasonable assumption and record it for the pull request
     body under **Assumptions**, or return `blocked <stage>: <the question>`.
   - Pushing and opening the pull request are part of this brief, not an offer.
3. **Gates.** Every gate the repository declares — Personal Validation, and `create-pr` when
   `.devbook/config.json` lists it.
   - *Attended*: at Personal Validation, first report that stage `in_progress` again in one
     `update_stage` call carrying both its review `links` and the handoff — what to check, the
     acceptance criteria, the evidence paths — as `output`: the first report of the attempt
     with either is what asks the person on their devices, with this call's content as the
     question's body, so never send them in two calls. At every gate, return `gate` with the
     handoff, leaving the application running; **Attendance** in `landing-modes.md` says why
     you do not park. The answer comes back as one of the lines below; one marked
     `from notification` is already recorded on the run, so record it only when it came from chat:
     - `approved`: continue. Words on an `approved … from notification` line came with a
       follow-up the app filed after this entry; it is not yours to do.
     - `revise: <words>`: do it and return at the gate again, reporting it as above.
     - `rejected`: open no pull request; finish the flow's run as `blocked` and return
       `blocked <stage>: rejected`.
     Never stop the app behind an unanswered gate yourself; the orchestrator tells you when to.
   - *Unattended*: park at the first gate with `approval: pending`, never self-approved, and
     land the change as the draft that stands in for every gate. Finish the flow's run as
     `parked`.
4. **Pull request.** Commit with a conventional subject, push, and
   `gh pr create <draft> --base <target>`. The body, in order: `<stacked-on>`; what changed;
   the acceptance criteria and how each was verified; Build & Test with its counts; review
   findings left open; **What could not be proved** — the section the person validates;
   **Assumptions**; and, unattended, a line saying an unattended `execute-plan` run opened it.
   An empty diff opens nothing: return `done-before` when run-plan-item found the entry done,
   else `blocked implement: empty diff`.
5. **Close.** Do everything `run-plan-item` step 7 asks — `link_change` once, `comment`, with
   the decision given as `pending — validate on draft <url>` when unattended — except the
   `transition` to Done: leave the entry In progress. Never merge your own pull request. Stop
   this worktree's application, the way the repository's run procedure stops it scoped to this
   worktree, before every return except `gate`.
6. **Return** by ending the turn: the first line of your final message is `done-before` with
   the evidence, `gate` with the handoff or question, `pr <url>`, or `blocked <stage>: <reason>`. Leave the
   worktree in place: it is the host's, and a later message from the orchestrator — a gate's
   decision, a fix pass — continues this brief in it.

Entry:

/backlog-tools:run-plan-item entry `<id>`:
<entry text, exactly as the plan or `read_item` gives it>
