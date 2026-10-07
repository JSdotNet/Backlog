# Plan entry brief

The brief `execute-plan` hands each sub-agent, with the `<…>` filled in per
`landing-modes.md`: `<attendance>` is `attended` or `unattended`; `<worktree-add>` is the
whole `git worktree add` command — with `-b <slug>/<id> <cut-from>` for a new branch, without
`-b` for one an earlier run left, or empty when that branch already has a worktree at
`<path>`; `<target>` is the branch the pull request targets; `<draft>` is `--draft` in branch
and stacked mode and in any unattended run, else empty; `<stacked-on>` is the two lines
**Record the cut** in `landing-modes.md` names, for a stacked entry, else empty.

---

You run one entry of the Backlog plan `<tag>`: `<n> - <Title>` (id `<id>`) in `<repository>`,
**<attendance>**.

1. **Worktree.** `git fetch origin`, then `<worktree-add>`, and work only inside `<path>` —
   when it existed already, check it is clean first. Confirm `git rev-parse --show-toplevel`
   and the branch before the first edit. Every file you produce
   — screenshots, traces, QA evidence — gets an absolute path inside the worktree; a tool that
   saves relative to another folder is given one.
2. **Run the entry** by invoking `backlog-tools:run-plan-item` with the entry text below as
   its argument, and follow it and the repository's flow to the letter, except:
   - Skip titling the session and `link_session`: you have no session of your own, and the
     orchestrator has moved the entry to In progress and linked its session.
   - Stacked: the stack parent's open branch, which you were cut from, satisfies its
     `after:`; so does every `after:` the orchestrator found reachable from it.
   - Unattended: never ask. Take the reasonable assumption and record it for the pull request
     body under **Assumptions**, or return `blocked <stage>: <the question>`.
   - Pushing and opening the pull request are part of this brief, not an offer.
3. **Gates.** Every gate the repository declares — Personal Validation, and `create-pr` when
   `.devbook/config.json` lists it.
   - *Attended*: at each gate, return `gate` with the review handoff — links, what to check,
     the acceptance criteria — leaving the application running; the orchestrator answers
     through the person, so the engine's rule that a worker parks does not apply. Approved:
     continue. Revise: do it and return at the gate again. Never stop the app behind an
     unanswered gate yourself; the orchestrator tells you when to.
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
6. **Return** one line: `done-before` with the evidence, `gate` with the handoff, `pr <url>`,
   or `blocked <stage>: <reason>`. Leave the worktree in place; the orchestrator removes it
   once the entry reaches `<base>`.

Entry:

<entry text, exactly as the plan or `read_item` gives it>
