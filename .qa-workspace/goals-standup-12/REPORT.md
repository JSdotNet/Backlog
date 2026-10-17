# QA report: Goals and Stand-up end to end (goals-standup:12)

Plan entry `bfbb4082-6ad5-4825-bc39-14d9507bbf54` (`qa-goals`) of `+goals-standup`, run unattended by
`execute-plan` on 2026-10-17 against `plan/goals-standup` at `94232684`, which holds entries 1–11
(#1359–#1369).

## What was checked

- Aspire AppHost started `--isolated` from `D:/gs/bfbb4082`. `sync`, `cosmos`, `desktop-web-harness`
  and `mobile-web-harness` all reached Running and Healthy.
- The desktop harness was rebuilt once: its first build hit a `CS2012` file lock on an `obj/` DLL,
  held by Defender while several builds ran in parallel.
- `goals` and `sync` were turned on in the desktop harness. Both were originally off and both are
  off again.
- The phone was **really paired** this time. The desktop registered and gave a pairing code, and the
  mobile harness took it.
- The Cosmos emulator came up after stale `cosmos-*`/`storage-*` containers and networks were
  removed. Those belonged to no running AppHost (see Assumptions on the pull request). The
  desktop ⇄ sync ⇄ phone round trip was live.
- The desktop harness and the mobile harness were driven with the Playwright MCP. Evidence is a
  numbered **screenshot sequence**: there is no video and no trace.
- Aspire console and structured logs and traces were read for `desktop-web-harness`, `sync` and
  `mobile-web-harness` throughout.

Evidence root (untracked, in the worktree `D:/gs/bfbb4082`):
`.qa-workspace/evidence/goals-standup-bfbb4082/`.

## Scenarios

| # | Step | Result | Note | Evidence |
|---|---|---|---|---|
| S1 | Create a goal | PASS | "QA12 goal", due 27 Nov, reads 0/0 · On track | `sequence/s1/02`–`03` |
| S1 | Attach a plan from the Goals view search | PASS | `qa-parts-single-q1007` → 1/2 | `sequence/s1/04`–`05` |
| S1 | Attach a task from the Goals view search | PASS | "e2e PR task C" → 2/3. Its merged PR #326 comes along and is not counted again | `sequence/s1/06` |
| S1 | Attach from the task panel button | PASS | "Agenda later 1012-190913" → 2/4 | `sequence/s1/07`–`10` |
| S1 | Attach from the roadmap item button | **FAIL** | Attaching works, but the plan counts 0/0 while the roadmap item gathers one `#`-tagged entry → entry `fdf64d94` | `sequence/s1/12`–`14`, `19` |
| S1 | Attach from the PR row button | PASS | #1369, from the selected PR's detail head → 3/5 | `sequence/s1/16`–`18` |
| S1 | Each item counts once | PASS | Attaching an entry whose plan is already attached keeps 3/5 | `sequence/s1/20` |
| S2 | Day / Week / 2 weeks / Month | PASS | 16 Oct → 19 Oct; 12–16 Oct → 19–23 Oct; 28 Sep–11 Oct → 12–25 Oct; 1–30 Sep → 1–31 Oct. Finished and Planned matched the stored dates item for item | `sequence/s2/01`–`05` |
| S3 | Block a task → a New impediment, goal At risk | PASS | QA12 went from On track to At risk, and a New impediment holds it back | `sequence/s3/01`–`04` |
| S3 | Desktop: Reported → Escalated → Being handled → Resolved | PASS | The picker offers only the allowed moves. Resolve clears the row and QA12 is On track again | `sequence/s3/05`–`07`, `14` |
| S3 | Phone: change state, seen on the desktop | PASS | New → Reported on a second impediment (nothing leaves Resolved) | `sequence/s3/12`–`13` |
| S3 | Phone: check-again, seen on the desktop | PASS | 20 Oct → the desktop reads "check Tue" | `sequence/s3/11`, `13` |
| S3 | Phone: note, seen on the desktop | **FAIL** | The note reaches the desktop store, but no desktop screen shows it → entry `5b6cce3c` | `sequence/s3/11`, `13` |
| S3 | Desktop Resolve seen on the phone | PASS | The phone reads "Resolved 17 Oct" and keeps the check-again and the note | `sequence/s3/15`–`16` |
| S3 | Impediment meta line | **FAIL** | Reported, Escalated and Being handled impediments still read "Not reported yet" on the desktop and in the copy → entry `3dda5231` | `sequence/s3/07`, `13`; `logs/03-s5-copied-clipboard-text.txt` |
| S4 | PR rows: icons and counts, no words | PARTIAL | No visible "checks" or "approvals" text anywhere. The check and approval marks on an **open** PR were not run, because no open PR exists in any readable repository. Merged rows show `#n` and "merged" | `sequence/s4/01`–`04` |
| S5 | Copy copies the four blocks as plain text | PASS | `writeText` was intercepted: one plain string, CRLF, no markup, with Goals, Finished, Planned and Impediments | `sequence/s5/01`, `logs/03-s5-copied-clipboard-text.txt` |
| S6 | Flag off: the desktop In progress view is unchanged | PASS | The "In progress" by-task view is back, with no goal line in the task panel, the roadmap editor or the PR detail | `sequence/s6/01`, `05`, `07`–`09` |
| S6 | Flag off: the phone tabs are unchanged | **FAIL** | After sync the phone still shows the Stand-up tab, its goals and its impediments → entry `376b2a93` | `sequence/s6/06-phone-goals-off-*.png` |
| S6 | No sprint UI anywhere, flag on and off | PASS | The only "sprint" matches are user data: task titles "close-sprint QA …" and the plan tag `+sprint-e2e` | `sequence/s6/02`, `03` |

## Failures written up as new entries of `+goals-standup`

Each is a `prompt` entry, `after:bfbb4082-6ad5-4825-bc39-14d9507bbf54`:

| Entry | Defect | Severity |
|---|---|---|
| `376b2a93-eca8-4a7d-97fa-1c41b0d410c2` | Withdraw the phone Stand-up when goals is switched off. The phone reads goals as on from holding a stand-up context, and the desktop never retires it. The mobile harness seed also always writes one | High (may be moot once entry 13 removes the flag) |
| `3dda5231-3c19-4c6b-b05e-1f7e9924acfd` | Read "Not reported yet" only on a New impediment (`GoalsText.ImpedimentMeta` tests `With`, not the state) | Medium |
| `5b6cce3c-3031-4778-addc-5ff1a5a5ac97` | Show an impediment's note on the desktop. It syncs, but no screen shows it | Medium |
| `fdf64d94-7841-47b8-9598-3c91e39973f4` | Count a roadmap item's gathered entries when its plan is on a goal. `GoalItems.TasksOf` matches only `+plan` tags, while the rollup also gathers `#tag` | Medium |

## Other findings (likely by design, not written up)

- **Existing blocks are not tracked when goals is turned on.** Five tasks that were already blocked
  raised no impediment until the next task write raised all five at once. `BlockedTaskPolicy` runs
  only on a task write.
- **Status and count disagree.** A task whose linked PR is merged counts as done on the goal, while
  its Tasks column still reads "not started".
- **Weekend completions are missed.** On a Saturday, work finished that day shows in no Day or Week
  Finished window, because "next" is the following Monday. This follows the window rule.
- **The phone mixes seed and synced goals.** The mobile harness's seeded goals sit alongside the
  synced ones.

## Logs

- **desktop-web-harness:** no Progress errors. One `TaskCanceledException` came from
  `RoadmapTimeline.DisposeAsync` (`RoadmapTimeline.razor:2560`) on a circuit disconnect after
  leaving the Roadmap. The Polly retries for cancelled GitHub calls are handled.
- **sync and mobile-web-harness:** no exceptions.
- **Traces:** the error-flagged traces are 404 lookups made before a document is created. They also
  show the phone's three edits reaching the server.
- **Excerpts:** `logs/01-desktop-web-harness.txt`, `logs/02-sync-and-mobile-web-harness.txt`.

## State changed and restored

- `goals`: off → on → **off**. `sync`: off → on → **off**.
- The iteration was set back to Week, and the PR pane back to Open / Mine.
- "Agenda later 1012-190913" is unblocked again.
- **Left in place:**
  - the goal "QA12 goal" and its attachments;
  - its resolved impediment;
  - five auto-raised "Blocked at the source" impediments, one of them Reported;
  - the desktop registration and the phone pairing.

  The pairing credentials sit in this worktree's `obj/local-development`.

## What could not be proved

- **The check and approval marks on a live open PR.** None exists, so the glyph and count were seen
  only on the storybook page.
- **"The phone tabs are unchanged" against a pre-feature baseline.** The mobile harness seeds a
  stand-up context, so the Stand-up tab shows even with goals never on.
- **The copied text pasted into Teams or Outlook.** `writeText` was intercepted; the system
  clipboard was not read.
- **A desktop display of the impediment note.** No desktop screen shows it.
