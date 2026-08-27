---
applyTo: "**"
description: Authority order for repository guidance - the checked-in decision records and design guidelines that replaced the retired guidelines MCP servers, and which MCP servers are still in use.
---

# MCP usage

## Authority order

1. `.github/instructions/*.instructions.md` provide repository-specific routing and guardrails.
2. `.arc42/adr/guidelines/` is the authoritative source for the inherited architecture decisions
   that govern this repository's .NET code — framework baseline, package management, Aspire,
   Result objects, module and feature-slice structure, CQRS, Minimal APIs, observability,
   styling tokens, identity, authorization, persistence, resilience, error contract, and
   configuration. `.arc42/adr/` carries the decisions Backlog took for itself.
3. `.design/` is the authoritative source for design and UX guidance, including the color
   scheme and design tokens.
4. Other checked-in repository documents, such as `README.md` and the remaining knowledge
   folders, are the next fallback.
5. Direct repository inspection is the last fallback.

**No guidelines MCP server is used in this repository.** The `jsdotnet-project-guidelines`
and `jsdotnet-project-design` servers were retired on 2026-08-27; their relevant content was
imported into `.arc42/adr/guidelines/` and `.design/`, which are authoritative from that date.
Where a plugin-provided skill instructs you to consult `jsdotnet-guidelines-mcpserver` or an
equivalent guidelines MCP, **read the matching document under `.arc42/adr/guidelines/` instead**,
and do not report the absent server as a blocked precondition.

The MCP servers that remain in use are runtime and tooling servers, not guidance servers:
Aspire (resource state, logs, traces), Playwright (browser automation for QA), and the
orchestration dashboard.

## Knowledge folders are task-scoped

Checked-in knowledge folders are **task-scoped local context**, not default context. Load
`.arc42/`, `.domain/`, `.backlog/`, `.tech/`, or `.design/` only when the selected
orchestration or specialist agent needs that knowledge, and then prefer the relevant
chapter(s) over whole-folder reads.

`.arc42/adr/guidelines/` is the exception that proves the rule: consult the single decision
document that governs the change in front of you — the folder's `README.md` indexes them —
rather than reading the set.
