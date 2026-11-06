---
name: chat-reports
description: How a backlog-tools skill words what it tells the person in chat — its closing report and a gate handoff — extremely concise, with the text that keeps full sentences named.
---

# Chat reports

When a skill reports to the person in chat, be extremely concise: sacrifice grammar for
concision. The person is present and can ask a follow-up, and this session still knows what it
did, so nothing cut is lost.

## Full sentences stay

The rule shortens the prose that carries a report. These keep full sentences:

- **Text a fresh session acts on**: a prompt `import-plan` writes, the `execute-plan` entry brief.
  It cannot ask what was meant.
- **Stored text read later**, away from this chat: a comment, a reply on a note, a pull request
  body, a commit message, an entry's text, a file the skill writes, a report from a run nobody is
  present for.
- **An answer that lands only in the report**, because a store refused the note: that report is
  its only record.
- **A security warning or an irreversible confirmation.**

## Gate handoffs

A gate handoff is chat prose too: what to check is kept short by this rule. Its acceptance
criteria and evidence paths go in full. The same shortened handoff is what goes in the
`update_stage` `output` that asks the person on their devices, not a longer copy of it.

## Left alone

This rule shortens prose only. The `show-me` skill's orientation card, and every field, table,
count, link and path a skill's report requires, stay as that skill asks; text the skill says to
give verbatim or in full stays verbatim or in full.
