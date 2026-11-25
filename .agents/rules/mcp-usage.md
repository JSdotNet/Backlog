---
name: mcp-usage
description: Authority order for repository guidance - the checked-in decision records and design guidelines that replaced the retired guidelines MCP servers, and which MCP servers are still in use.
paths:
  - "**"
---

# MCP usage

## Authority order

1. `.agents/rules/*.md` provide repository-specific routing and guardrails; each host applies
   them through its wrapper under `.claude/rules/` or `.github/instructions/`.
2. `.devbook/arc42/adr/guidelines/` is the authoritative source for the inherited architecture decisions
   that govern this repository's .NET code — framework baseline, package management, Aspire,
   Result objects, module and feature-slice structure, CQRS, Minimal APIs, observability,
   styling tokens, identity, authorization, persistence, resilience, error contract, and
   configuration. `.devbook/arc42/adr/` carries the decisions Backlog took for itself.
3. `.devbook/design/` is the authoritative source for design and UX guidance, including the color
   scheme and design tokens.
4. Other checked-in repository documents, such as `README.md` and the remaining knowledge
   folders, are the next fallback.
5. Direct repository inspection is the last fallback.

**No repository-guidelines MCP server is used here.** The `jsdotnet-project-guidelines`
and `jsdotnet-project-design` servers were retired on 2026-08-27; their relevant content was
imported into `.devbook/arc42/adr/guidelines/` and `.devbook/design/`.
Where a plugin-provided skill instructs you to consult `jsdotnet-guidelines-mcpserver` or an
equivalent guidelines MCP, **read the matching document under `.devbook/arc42/adr/guidelines/` instead**,
and do not report the absent server as a blocked precondition.

`.mcp.json` does configure `jsdotnet-app-defaults` (`dnx JSdotNet.AppDefaults.Mcp --yes`, org
ADR 0013): the organization's own defaults, the `JSdotNet/app-defaults` decisions that the
checked-in guideline records cite as "app-defaults ADR". Read it for an org default a
checked-in record names; where the two differ, the checked-in record above wins.

The MCP servers that remain in use are runtime and tooling servers, not guidance servers:
Playwright (browser automation for QA), the
delivery surfaces, and `backlog` — the running desktop app's own server, bound as the
delivery tracker in `.devbook/config.json`. `.mcp.json` also declares `microsoft-learn`, the
delivery engine's default for .NET and Azure API reference. Its Playwright entry runs
`--isolated` so it never shares a browser profile with the `qa` plugin's own instance.

**Aspire is read through its CLI, not an MCP server** (org ADR 0013, CLI first): resource
state with `aspire describe`, console output with `aspire logs`, structured logs and traces
with `aspire otel`, each with `--apphost` naming this worktree's AppHost. `.mcp.json`
configures no `aspire` server; the `run` skill and `.agents/skills/diagnose.md` carry the
commands, and a QA or monitor brief names them rather than an Aspire MCP tool.

## Guidelines are read one at a time

`AGENTS.md` makes the devbook folders task-scoped context. For
`.devbook/arc42/adr/guidelines/` that means the single decision document that governs the
change in front of you — the folder's `README.md` indexes them — never the set.
