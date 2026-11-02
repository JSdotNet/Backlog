# Answering a gate

How an attended `execute-plan` answers an entry session waiting at a gate. Unattended runs have
no gate here: the session parks and ends with `pr <url>`, a draft.

## Two places to answer

An entry session at a gate ends its turn with `gate` and the review handoff. Keep its run —
the delivery surface's `list_runs` for the session's `cwd` — for that entry, and relay the handoff verbatim with the entry's
`<n> - <Title>` as it arrives; several waiting are listed in plan order and each is decided
on its own.

The person answers in this chat — Approve, Revise or Reject — or on any paired device through
the Personal Validation question the session's gate report raised, which also offers
Approve + follow-up. Only that question files a follow-up, so the chat offers none: say once,
with the first handoff, that a follow-up is answered on the Backlog notification.
**The first answer wins.** A later answer for the same gate, from either place, changes
nothing: say it was already decided, and how.

## Reading the run

While any gate waits, keep one one-shot 2-minute wake-up pending, replacing the previous one,
beside whatever else wakes this session. On every wake, call `get_run` for each waiting run
and read its `approval` (fields below). A decision taken after the gate
returned is the notification's answer; one still pending means nothing yet.

## Sending the answer

Send the entry session one line, per **Talking to an entry session** in `item-brief.md`, with
` from notification` appended when the run already holds the decision; what the session does
with it is `item-brief.md` step 3.

| The person answered | Line sent | This session then |
| --- | --- | --- |
| Approve | `approved` | Nothing more |
| Approve + follow-up (notification only) | `approved` with the words | Nothing now: the app filed the follow-up `after:` this entry, and Land's graph re-read dispatches it as an ordinary ready entry once this entry lands |
| Revise | `revise: <words>` | Waits for the gate again |
| Reject | `rejected` | Blocks the entry like any failure — `set_blocked` plus a `comment` — once the session ends with `blocked`; no revise round starts |

`get_run` returns the decision as `approval`: `decision` (`pending`, `approved` or
`rejected`), `note`, `decidedAt`, `choice` and `followUpEntryId`, or `approval: null` before
any gate is decided. `choice` names the notification's answer — `approve`,
`approve-follow-up`, `revise` or `reject` — and is null for a decision taken in chat or at the
desktop. Tell Revise from Reject by `choice`, never by the note: both are `rejected`, Revise
with the person's words as `note`, Reject with a fixed note. `followUpEntryId` is the entry an
Approve + follow-up filed, or null when none could be filed.
