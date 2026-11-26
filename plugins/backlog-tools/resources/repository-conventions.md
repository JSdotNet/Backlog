---
name: repository-conventions
description: The branch and pull request names a Backlog plan lands under in one repository — the keys of its committed .devbook/backlog-tools.json, their defaults, placeholders and ticket rule — read by execute-plan, final-check and close-merged alike.
---

# Repository conventions

Read once at the start of the run from `.devbook/backlog-tools.json` in this checkout's
repository, committed there because they are the team's. No file, or a key absent, is the
default, so a repository with no configuration behaves as it always did.

| Key | Default | Meaning |
| --- | --- | --- |
| `entryBranch` | `{slug}/{id}` | Pattern of an entry branch; must hold `{slug}` and `{id}` |
| `integrationBranch` | `plan/{slug}` | Pattern of the integration branch; must hold `{slug}` and literal text |
| `prTitle` | `{title}` | Pattern of a pull request title; must hold `{title}`, the conventional subject |
| `integrationBuilds` | unset | `true` or `false`: whether pull requests into the integration branch get a build |

Placeholders are `{slug}`, `{id}`, `{ticket}` and, in `prTitle` only, `{title}`. Any other, a
missing required one, or a rendered name `git check-ref-format --branch` refuses stops the run
and names the key.

- **Ticket.** An opaque value a skill only substitutes: it never looks one up, and never
  assumes a tracker. An entry's ticket is its `Ticket:` line (grammar's `## Ticket line`),
  else `ticket:<value>` from the request, else the `plan` entry's `Ticket:` line. The integration
  branch and the closing pull request take the last two only.
- **Refuse, never invent.** When a pattern holds `{ticket}` and a name or title still to be
  created has no ticket, stop before anything is created or dispatched, naming the key and the entries, and
  say how to supply one. An entry whose branch and pull request already exist needs none.
