# Weekly cost analysis — the shape

The message `weekly-cost-analysis` ends on. Follow it exactly — same headings, same columns,
same order. Placeholders are in `<angle brackets>`.

```markdown
## Weekly cost analysis · <YYYY-MM-DD>

**<Verdict, 25 words at most: the window's uncached total and the one change that would save the most.>**

| Repository | Runs | Tokens | Uncached | Share | Change on last window |
| --- | --- | --- | --- | --- | --- |
| <owner/repo, or "Not placed"> | <n> | <n> | <n> | <pct>% | <+/-pct> or — |

### Needs you
| Repository | What | Where | Do |
| --- | --- | --- | --- |
| <owner/repo> | <repository-specific action, own words> | `<skill or agent file>` | <the edit, one line> |

### <owner/repo>

| Uncached | Top model | Top stage | Top agent | Compacted runs |
| --- | --- | --- | --- | --- |
| <n> | <model> (<pct>%) | <stage> (<pct>%) | <agent> (<pct>%) | <n> of <runs> |

#### Tips
| Tip | Expected saving |
| --- | --- |

#### Breakdown
| Model, stage or agent | Uncached | Share |
| --- | --- | --- |

### Run
| Outcome | What | Why |
| --- | --- | --- |

*`backlog-tools@<version>` weekly-cost-analysis · <from>..<to> · <ISO datetime UTC><, change on last window approximate by part of a day, for a days window>*
```

- **Repository first.** One `### <owner/repo>` section per repository, largest uncached total
  first; the runs nothing placed come last, as `### Not placed`.
- **Tips** holds the top N across the whole message in the `summary` format, each under the
  repository it is about, and every tip in `full`. *Breakdown* appears only in `full`.
- **Share** is of the window's uncached total in the first table, and of the repository's
  inside its own section — a model's of the reported models' uncached sum, which comes from
  Claude Code's request telemetry rather than the run files.
- **Change on last window** is against the window just before, of the same length; `—` where
  the repository had no run then.
- **Run** has one row per source that could not be read (`unread`, naming the run folder),
  and one for the runs that recorded no usage (`skipped`, with their count).
- **One row per item, own words, eight at most.** Ten rows per table, most expensive first;
  the rest become one closing row, `… and <n> more`. Omit an empty section. Tables, not
  prose, except the verdict. No emoji.
- With no run in the window the message is the heading, the verdict `No run in <from>..<to>.`,
  and *Run*.
