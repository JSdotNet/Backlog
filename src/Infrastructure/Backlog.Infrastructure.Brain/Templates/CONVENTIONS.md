# Conventions

How this Brain is laid out. Backlog reads this file to know the areas the Brain
has and the folders it leaves out; you can change everything in it.

## Areas

Each area is one top folder of the Brain, and Backlog shows each area as a tab,
in the order of the rows below. Rename, reorder, add or remove rows to suit how
you work; the folder is the name on disk and the area is the name on the tab.
With no rows at all, Backlog falls back to the PARA set the table starts as.
Any other top folder that holds a note gets a tab too, after these, named after
its folder.

| Area | Folder | Holds |
|---|---|---|
| Inbox | `_inbox` | Notes not yet filed; where a capture lands. |
| Projects | `projects` | Active work with a goal and a deadline. |
| Areas | `areas` | Ongoing responsibilities without a deadline. |
| Resources | `resources` | Reference material and collected knowledge. |
| People | `people` | Notes about the people the work involves. |
| Journal | `journal` | Dated entries. |
| Archive | `archive` | Inactive notes kept for future search. |

## Ignore

The folders listed here never get a tab, even when they hold notes, and search
leaves their notes out. Add one per line; Show hidden in the Brain's menu still
shows them when you want to look.

- `_templates`

## Notes

A note is a Markdown file in an area's folder, with a fenced `meta` block under
its title, as a devbook chapter has. Three fields are the Brain's own:

| Field | Holds |
|---|---|
| `para` | The note's area. It follows the folder; write it only to override that. |
| `repos` | The repositories a project note concerns. Project notes only. |
| `tasks` | The tasks the note links to. |

A note may link into a repository's devbook with
`repo:<alias>/<devbook path>#<slug>`. A repository's devbook never links back
into a Brain.
