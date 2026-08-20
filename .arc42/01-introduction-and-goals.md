# 01. Introduction and Goals

```meta
status: active
```

Prompt Backlog is a personal, AI-first productivity system that captures input from
many sources, organizes project knowledge, maintains a personal backlog, and monitors
progress across multiple repositories and projects. It runs fully standalone on a
single desktop and optionally connects to a thin cloud layer for multi-device sync.

The overriding architectural driver is **local-first**: the desktop application is
fully functional offline, owns the canonical data, and runs all capture workers
locally. The cloud is additive coordination — never a dependency for core workflows.

## Requirements Overview

```meta
status: active
```

The system is decomposed into independent functional domains, each designable,
buildable, and extendable on its own.

| Domain | Purpose |
|---|---|
| **Capture** | All input sources: mobile, automations (YouTube, website, email), web clipper, IDE, manual |
| **Inbox** | Triage, classification, and routing of captured items (independent of capture sources) |
| **Backlog Management** | Refine, prioritize, and route backlog items with sub-items (multi-repo entries) |
| **Roadmap Planning** | Plan what happens when across repositories, with its own priorities and the dependencies between planned work |
| **Second Brain** | Organize project knowledge and cross-project context (PARA structure) |
| **Monitoring & Dashboard** | Track progress signals, queue health, and operational follow-up views |
| **Technology Stack** | Define baselines, version requirements, and adoption signals |
| **Dev PC Management** | Register machines, track compliance, and orchestrate remote updates |
| **Sessions** | Record what the AI coding agents have been doing on each environment |
| **Repository Management** | Track repositories, package versions, issues, and health scoring |

Capture delivers normalized `InboxItem`s to the Inbox incoming queue; Inbox does not
know or care how items were captured. See
`.arc42/03-context-and-scope.md#business-context` for the domain boundaries and
`.arc42/05-building-block-view.md` for how these domains map onto containers.

## Quality Goals

```meta
status: active
```

The top quality goals shaping the architecture, in priority order:

| # | Quality goal | Motivation |
|---|---|---|
| 1 | **Local-first availability** | Capture, triage, backlog, knowledge, and monitoring all work offline with no cloud dependency. |
| 2 | **Privacy of credentials** | External-service credentials (YouTube, email, website) never leave the user's machine; the cloud stores no such secrets. |
| 3 | **Frictionless capture** | Input from any channel (phone, IDE, automations, manual) reaches the inbox with minimal steps and offline buffering. |
| 4 | **Sync reliability** | Multi-device sync is eventually consistent, conflict-aware, and self-healing (retry with backoff, TTL cleanup). |
| 5 | **Low operational cost** | The optional cloud layer runs on minimal, single-region infrastructure. |

Detailed, measurable scenarios are elaborated in
`.arc42/10-quality-requirements.md`.

## Person

```meta
status: active
```

| Name | Concern |
|---|---|
| **ME** | Personal owner of the system across projects and devices; wants fast capture, organized backlog/knowledge, and progress visibility. |


