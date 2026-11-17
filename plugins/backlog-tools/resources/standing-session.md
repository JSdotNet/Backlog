---
name: standing-session
description: How a backlog-tools skill a person keeps running under /loop marks its Claude Desktop session on its first turn — an icon and an ALL CAPS title, pinned, filed under the Agents section — so a long-running agent stands out from task sessions.
---

# Standing session

A skill a person starts as `/loop /backlog-tools:<name>` is a long-running agent, not a task.
On its first turn in a session — never on a later tick — it marks that session so the sidebar
sets it apart:

1. **Title** it `<icon> <NAME>`, the pair its own skill names, with the app's session-title tool
   on `self`. The title already reads that → skip.
2. **File** it under the sidebar section `Agents`: take it from the app's section list, else
   create it, then move `self` there.
3. **Pin** it with the app's pin tool on `self` — after filing, since a move unpins.

| Skill | Title |
| --- | --- |
| `dispatch-sessions` | `📡 SESSION DISPATCHER` |
| `tend-brain` | `🧠 BRAIN TENDER` |
| `track-goals` | `🎯 GOAL TRACKER` |

- A tool the host lacks, or one the person declines → skip that step, say so once, go on.
  Marking never holds up the tick's own work.
- Never mark a session the skill did not run in, and never mark it again once the person
  retitled, unpinned or moved it: that is their answer.
