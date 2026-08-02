# Workflow routing

Prefer the named orchestration skill for each task category. Fall back to the paired
specialist agent only when the skill is unavailable.

- Architecture, ADRs, arc42 docs, or blueprint work: use `orch-architecture`, `orch-adr`,
  `orch-arc42`, or `orch-blueprint`. Fall back to `architecture:architect`.
- Technical debt records: use `orch-tdr`. Fall back to `architecture:architect`.
- Feature implementation spanning planning, coding, and validation: use `orch-feature`.
  Fall back to `csharp-coding:coding`.
- Bug fixes spanning triage, fix, and validation: use `orch-bug`. Fall back to
  `csharp-coding:coding`.
- New module scaffolding inside an existing project: use `orch-create-module`. Fall back
  to `csharp-coding:coding`.
- New service scaffolding inside an existing project: use `orch-create-service`. Fall
  back to `csharp-coding:coding`.
- Dependency or package updates: use `orch-update-packages`. Fall back to
  `csharp-coding:coding`.
- Domain modeling, bounded contexts, or ubiquitous language: use `domain-design:domain-architect`
  (no dedicated orchestration skill for this repository yet).
- Repository documentation, explanatory docs, or governance text: use
  `documentation:documentation` (no dedicated orchestration skill for this repository yet).
- User flows, wireframes, or UX review: use `ux-design:ux-designer`.
- GitHub issue creation or updates, and pull request automation: use `create-github-issue`,
  `update-github-issue`, and `pr-jsdotnet`.

If neither the preferred skill nor its fallback agent is installed, use the closest
available specialist agent and note that orchestration routing was unavailable.
