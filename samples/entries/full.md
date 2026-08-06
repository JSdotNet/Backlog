# Ship the offline sync spike
`task` `*high` `!in-progress` `@repos`

The desktop app should keep working on a train. Everything is already a file on
disk, so the hard part is not storage — it is deciding what wins when the same
entry changed in two places. #sync #offline

## Decide the conflict rule
Last-write-wins is tempting and wrong: it silently eats the loser. Prefer
keeping both and surfacing the collision as an entry.

## [x] Measure how often it actually happens
Two weeks of single-user telemetry: 0 conflicts. This may be a problem worth
not solving yet.

- [ ] Write the merge test corpus
- [x] Read how Obsidian handles this
