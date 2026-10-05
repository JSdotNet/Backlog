# Plan entry brief

The brief `backlog-implement-plan` hands each sub-agent, with the `<…>` filled in.

---

You run one entry of the Backlog plan `<tag>`: `<n> - <Title>` (id `<id>`) in `<repository>`.

1. **Worktree.** Create your own: `git fetch origin`, then
   `git worktree add <short-path> -b <tag-without-sigil>/<id> origin/<base>`, and work
   only inside it. Confirm `git rev-parse --show-toplevel` and the branch before the first
   edit. `<base>` is `<base-branch>`, and every pull request you open targets it.
2. **Run the entry** by invoking `backlog-tools:backlog-run-plan-item` with the entry text
   below as its argument. It decides whether the entry is still outstanding and runs it
   through the repository's own flow; follow both to the letter.
3. **Personal Validation.** You cannot ask the person. When the flow reaches its gate, stop
   and return the review handoff — links, what to check, the acceptance criteria — and wait.
   The orchestrator relays it and answers with the decision. Approved: continue to the pull
   request. Revise: do it and return at the gate again.
4. **Close.** Do everything `backlog-run-plan-item` step 7 asks, except the `transition` to
   Done: leave the entry In progress. The orchestrator moves it to Done when the pull request
   merges. Never merge your own pull request.
5. **Return** one line: `done-before` with the evidence, `pr <url>`, or `blocked <reason>`.
   Leave the worktree in place; the orchestrator removes it after the merge.

Entry:

<entry text, exactly as the plan or `read_item` gives it>
