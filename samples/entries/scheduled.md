# Deploy SpecManager
`task` `*high` `!ready` `@repos` `#deploy` `due:2026-08-21` `remind:2026-08-21T09:00` `repeat:weekly` `myday:2026-08-19` `after:a1b2c3`

Five facts about *when*, all on the metadata line and all optional. They are
named tokens rather than sigils because five date-shaped concepts cannot be told
apart by punctuation anybody will remember, and this line is hand-edited.

`due:` is a calendar day — no time, no timezone, so it stays Friday when the
laptop moves. `remind:` is wall-clock intent: 09:00 means 09:00 wherever you are
when it arrives. `repeat:` says the entry comes back; completing it leaves this
occurrence completed and creates the next one, due a week after **this** entry's
due date rather than a week after you got round to it. `myday:` is the day it
was picked to work on, which is why it expires on its own. `after:` names an
entry this one waits on and may appear more than once — an id naming nothing you
can see still blocks. #scheduling

## Cut the release notes
Anchored to the parent's deadline: a step has no due date of its own.

- [ ] Tag the build
- [x] Write the announcement
