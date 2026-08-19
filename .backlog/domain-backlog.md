# Backlog Management Work

```meta
status: draft
```

## Add roadmap planning to the backlog

```meta
status: done
implements: [.domain/backlog/features.md#feature-roadmap-planning]
related: [.domain/roadmap/domain.md#aggregate-roadmap-plan, .domain/roadmap/features.md, .domain/environment/features.md#feature-environment-aware-work-context]
```

**Delivered, but not as written.** This item asked for roadmap planning to be
documented *inside* Backlog Management, as a view over selected entries. It is
documented — as its own bounded context,
[Roadmap Planning](../.domain/roadmap/domain.md#aggregate-roadmap-plan) — and the
feature chapter this item implements is now marked superseded and points there.

As originally stated: a person managing work across projects and repositories
wants to understand what is planned next without duplicating work items into a
separate planning system.

What changed, and why, is one premise. A roadmap over *selected backlog entries*
can only plan work that has already been refined into an entry, and that is not
when planning happens — most of it happens before there is anything to select. So
the plan is stored in its own right, with its own priority and its own
dependencies, and a planned item may *optionally* name the Backlog Entry that
executes it. That is not the "separate planning system" this item wanted to avoid:
there is still one work model, one status, one execution priority, all in Backlog
Management. What is separate is the intent, which never had a home before.

### Acceptance criteria

```meta
status: done
implements: [.domain/backlog/features.md#feature-roadmap-planning]
```

Recorded as met, with criterion 1 and 2 restated where the model moved:

1. ~~Roadmap-ready backlog entries can be grouped by Now/Next/Later, milestone, or
   custom planning lane.~~ Superseded: planned work is grouped by **repository
   band and planning lane** and read against dates, per
   [reading the plan by repository](../.domain/roadmap/features.md#sub-feature-reading-the-plan-by-repository).
   Now/Next/Later was a horizon, and a horizon is a grouping rather than a plan.
2. ~~Roadmap progress is derived from the underlying Backlog Entries instead of
   stored independently.~~ Half superseded, and the surviving half matters:
   **Backlog Entry remains the source of truth for status and execution
   priority**, and a linked item reads its progress from the entry. What *is*
   stored independently is the plan itself — dates, planning priority,
   dependencies — because none of those exist on an entry.
3. Met. Environment shortcuts stay
   [Environment's own feature](../.domain/environment/features.md#feature-environment-aware-work-context);
   Roadmap Planning records no dependency on Environment.
4. Met as stated — this item produced documentation only. The product feature that
   follows it is tracked by its own pull request, not by this chapter.

### Test instructions

```meta
status: done
```

Read `.domain/context-map.md` and confirm Roadmap Planning appears as a Core
bounded context, that its relationship to Backlog Management is a `Partnership`
carrying one optional foreign id, and that the strategic rules state plainly which
context owns which kind of priority. Then confirm
`.domain/backlog/features.md#feature-roadmap-planning` and
`.domain/backlog/naming.md#term-roadmap` are marked superseded and point at the new
context rather than describing a model that no longer holds.
