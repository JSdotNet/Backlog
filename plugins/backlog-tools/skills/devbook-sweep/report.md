# Devbook sweep report

The frame and its rules are `delivery-schedule`'s `resources/report-contract.md`; the closing block
is `devbook-sweep-contract.md` beside this file, under *The Report Block*. Follow this shape exactly.

````markdown
## Devbook <direction> sweep · <owner/repo> · <YYYY-MM-DD>

**<Verdict: groups verified, draft pull requests opened, how many did not complete, how many need an answer.>**

### Needs you
| What | Where | Do |
| --- | --- | --- |
| Conflict: <the question, own words> | `<group id>` | [#<n>](<url>) |
| Validate: <what could not be proved> | `<group id>` | [#<n>](<url>) |
| Agent-directed text in a chapter | `<chapter>` | — |
| Set aside: <mixed or oversized, and why> | `<group id>` | align the directions in Backlog's devbook view, or split the tying requirement |
| Orphan: <its reason> | `<chapter>` | add the `related` it lacks |

### ① Draft pull requests
| Group | Pull request | What could not be proved |
| --- | --- | --- |

### ② Awaiting an answer
| Verdict | Group | Readings | Drift issue |
| --- | --- | --- | --- |

### ③ Flagged
| Chapter | Quoted text |
| --- | --- |

### ④ Did not complete
| Group | Stage | Reason | Label left |
| --- | --- | --- | --- |

### ⑤ Verified and deferred
| Aligned | Deferred past `maxResolve` | Waiting for pull | Skipped in flight | Not assessed past `maxVerify` |
| --- | --- | --- | --- | --- |

### Run
| Outcome | What | Why |
| --- | --- | --- |

*`backlog-tools:devbook-sweep@<version>` · direction <direction> · <ISO datetime UTC>*

```devbook-sync-report
{ ... }
```
````

Nothing in ① to ④: the verdict line, ⑤, *Run*, and the block. Nothing verified: no block.
