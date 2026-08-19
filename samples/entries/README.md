# Entry samples

These are real backlog entries in the format the app reads and writes. They are
not illustrations that drift out of date — `Backlog.Desktop.UI.UnitTests` parses every file
in this folder and asserts what each one should produce, so a sample that stops
matching the parser fails the build.

Drop any of these into your entries folder (Settings → *Where the backlog is
stored*, then the `entries` subfolder) to see them in the app. They carry no
YAML frontmatter, which is deliberate: this is what you type, not what gets
stored. The app writes the frontmatter itself on the first save.

## The format

```markdown
# Title
`task` `*high` `!in-progress` `@repos`

Prose, with #tags anywhere in it.

## A sub-item
Notes belonging to that sub-item.

- [ ] A checklist sub-item
- [x] One that is done
```

**Line one is the title.** Type anything there and it becomes an `# ` heading
when you leave the entry.

**Line two is the meta line**, if it consists only of backticked tokens. Each
kind of metadata carries its own sigil, so you can tell at a glance which is
which:

| Sigil | Kind | Values |
| --- | --- | --- |
| *(none)* | type | `prompt` `task` `idea` `follow-up` |
| `*` | priority | `*low` `*medium` `*high` `*critical` |
| `!` | status | `!draft` `!ready` `!in-progress` `!done` `!archived` |
| `@` | area | anything — `@repos`, `@inbox`, `@side-project` |
| `#` | tag | written in the body, not the meta line |

Five sigils is the whole vocabulary. Anything to do with *when* — or with what an
entry is waiting on — rides on the same line as a named `name:value` token
instead, because five date-shaped concepts cannot be told apart by punctuation
anybody will remember. Sigils first, then the named tokens:

| Token | Kind | Values |
| --- | --- | --- |
| `due:` | due date | a calendar day, `due:2026-08-21` — no time and no timezone |
| `remind:` | reminder | a local date and time, `remind:2026-08-21T09:00` — no zone, so 09:00 means 09:00 wherever you are |
| `repeat:` | recurrence | `repeat:daily` `repeat:weekly` `repeat:monthly` `repeat:yearly` `repeat:weekdays`, or an interval — `repeat:2w`, `repeat:3d`, `repeat:6m`, `repeat:2y` |
| `myday:` | My Day | the day it was picked for, `myday:2026-08-19` — it expires when that day does |
| `after:` | dependency | an entry id, `after:a1b2c3` — may appear more than once |

An unset field carries no token rather than an empty one: `` `due:` `` with
nothing after it is malformed, not "no due date". Deleting the token is how you
clear the field. A named token this app does not recognize is left alone rather
than dropped, the same bargain the bare metadata words get.

Completing an entry that carries a `repeat:` leaves it completed and creates the
next occurrence as a new entry. The repeat is anchored to the due date, so an
entry finished three days late still comes back on its original weekday.

Status is the one field you cannot set to just anything. An entry moves through
a lifecycle one step at a time, so a fresh `!draft` entry can become `!ready`
but not `!in-progress` — writing a status the entry cannot reach yet leaves it
where it was, and the editor says so under the text rather than dropping the
word silently.

```
draft ⇄ ready ⇄ in-progress ⇄ done ⇄ archived → draft
```

Type is the one bare word because it is the noun the entry already is. Bare
words are still accepted for every kind, so entries written before the sigils
existed keep working — the app just rewrites them in the canonical form on the
next save.

Spelling is forgiving: `in-progress`, `in progress`, `InProgress` and
`in_progress` are the same status.

## Structure

| You write | You get |
| --- | --- |
| `# Second heading` | a whole new entry, split off from this one |
| `## A heading` | a sub-item, rendered as a titled block |
| `## [x] A heading` | a sub-item that is done |
| `- [ ] A line` | a sub-item, rendered as a checkbox |
| `### A heading` | ordinary prose |

Anything inside a fenced code block is left alone — a `#` in a code sample never
splits an entry.

## The files

| File | What it covers |
| --- | --- |
| `minimal.md` | The least an entry can be: one line. |
| `full.md` | Every metadata kind and every structural feature at once. |
| `scheduled.md` | The named tokens: a due date, a reminder, a repeat, a My Day stamp and a dependency. |
| `sub-items.md` | `##` sub-items with notes, mixed with checklists. |
| `checklist.md` | A plain checklist entry, some done. |
| `bare-words.md` | Pre-sigil metadata, still readable. |
| `code-fence.md` | Headings and tags inside code, which must not be parsed. |
| `prose-only.md` | No metadata at all — defaults apply. |
