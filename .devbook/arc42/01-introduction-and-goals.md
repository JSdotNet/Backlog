# 01. Introduction and Goals

```meta
index: root
```

Backlog is a personal, AI-first productivity system. It does four things:

- It captures input from many sources.
- It organizes project knowledge.
- It keeps a personal backlog.
- It monitors progress across repositories and projects.

It runs standalone on one desktop and optionally connects to a thin cloud layer for multi-device sync.

The overriding architectural driver is **local-first**, as `.devbook/arc42/12-glossary.md` defines it; the desktop also runs all capture workers locally.

## Requirements Overview

```meta
```

The system is split into independent functional domains, each designed, built and extended on its own. `.devbook/domain/context-map.md#subdomain-landscape` classifies them.

| Domain | Purpose |
|---|---|
| **Capture** | All input sources: mobile, automations (YouTube, website, email), web clipper, IDE, manual |
| **Inbox** | Triage, classification, and routing of captured items (independent of capture sources) |
| **Work** | Refine, prioritize, and route tasks with sub-items across repositories, and plan what happens when, in what order and at what pace |
| **Devbook** | Read, build, render, and search the devbook each repository carries beside its code |
| **Brain** | Keep the person's own notes, linked to their work by id and into a repository's devbook |
| **Insights** | Turn the work the person and their agents do into personal insight on the dashboard |
| **Repositories** | Track repositories, package versions, issues, and health scoring, and launch each repository's named environments |
| **Dev PC Management** | Register machines, track compliance, and orchestrate remote updates |
| **Goals** | Keep the person's goals and impediments, and read both over an iteration as a stand-up |
| **Sessions** | Record what the AI coding agents have been doing on each environment |
| **Events** | Keep the knowledge events the person attends, from spotting one to its harvest |
| **Notifications** | Let any domain tell the person something or ask a question, and hand the answer back |
| **Sync** | Keep one person's data the same across their devices |

The change journal is generic too, shared across these domains the way Sync is. The Calendar and the desktop shell are compositions and the MCP server is a channel; none of them is a domain (local ADR 0073 §6).

Capture delivers normalized `InboxItem`s to the Inbox incoming queue; Inbox does not know how items were captured. See `.devbook/arc42/03-context-and-scope.md#business-context` for the boundaries and `.devbook/arc42/05-building-block-view.md` for the container mapping.

## Quality Goals

```meta
```

| # | Quality goal (priority order) | Motivation |
|---|---|---|
| 1 | **Local-first availability** | Capture, triage, backlog, knowledge, and monitoring all work offline with no cloud dependency. |
| 2 | **Privacy of credentials** | External-service credentials (YouTube, email, website) never leave the user's machine; the cloud stores no such secrets. |
| 3 | **Frictionless capture** | Input from any channel (phone, IDE, automations, manual) reaches the inbox with minimal steps and offline buffering. |
| 4 | **Sync reliability** | Multi-device sync is eventually consistent, conflict-aware, and self-healing (retry with backoff, TTL cleanup). |
| 5 | **Low operational cost** | The optional cloud layer runs on minimal, single-region infrastructure. |

Measurable scenarios are in `.devbook/arc42/10-quality-requirements.md`.

## Person

```meta
```

| Name | Concern |
|---|---|
| **ME** | Personal owner of the system across projects and devices; wants fast capture, organized backlog/knowledge, and progress visibility. |


