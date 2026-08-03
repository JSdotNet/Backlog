# 10. Quality Requirements

```meta
status: draft
related: [".arc42/01-introduction-and-goals.md#quality-goals"]
```

Refines the quality goals from chapter 01 into concrete, testable scenarios. Marked
`draft` because target values are indicative and need validation against real usage.

## Quality Scenarios

```meta
status: draft
```

| # | Quality attribute | Scenario | Target |
|---|---|---|---|
| Q1 | **Availability (offline)** | Network is unavailable; user captures, triages, edits backlog, browses knowledge, and views monitoring. | All core workflows succeed with no cloud calls. |
| Q2 | **Credential privacy** | Cloud service and its database are inspected. | No YouTube / email / website credentials are present anywhere in the cloud. |
| Q3 | **Capture friction** | User captures an item on the phone while offline. | Item is stored locally in ≤1 interaction and auto-flushed when online. |
| Q4 | **Sync latency** | Desktop pushes a state change; phone is online. | Phone reflects the change on next pull; GitHub webhook reaches desktop within ~5s. |
| Q5 | **Sync reliability** | A sync POST fails or times out. | Item enters `SyncFailed`, retries with exponential backoff (max 5), and self-heals without data loss. |
| Q6 | **Conflict handling** | Same item edited on two devices. | Edits resolve last-write-wins; new items never overwrite; conflicts are surfaced for manual review. |
| Q7 | **Operational cost** | Cloud service under normal personal load. | Runs on a single App Service / Container App instance; TTL cleanup keeps storage bounded. |
| Q8 | **Search performance** | Full-text search across all domains on the desktop. | Returns from local SQLite FTS with no cloud round-trip. |
| Q9 | **Portability** | Same domain data accessed at workspace, repo, and project scope. | Dot-folder contract (`.inbox/`, `.backlog/`, `.brain/`) resolves consistently at every scope. |

These scenarios should be reviewed once MVP scope per domain and per channel is
defined (see `.arc42/11-risks-and-technical-debt.md`).
