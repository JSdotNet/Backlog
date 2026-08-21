# Repository Management

```meta
type: flow
status: draft
```

> Lifecycle and process flows for this bounded context. Flows describe how a
> repository scan turns into a health score and downstream actions —
> complementary to `model.md` (structure) and `domain.md`
> (responsibilities/invariants).

## Health scoring flow

```mermaid
flowchart TD
    Scan["Repository Scan"] --> Packages["Package freshness"]
    Scan --> GitHub["GitHub metrics"]
    Scan --> Coverage["Test coverage"]
    Scan --> Security["Security alerts"]
    Packages --> Score["HealthScore (0-100)"]
    GitHub --> Score
    Coverage --> Score
    Security --> Score
    Score --> Action["Recommendations to Monitoring / Backlog"]
```
