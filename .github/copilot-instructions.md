# Backlog repository instructions

## Repository scope

Backlog is being organized as a multi-part, AI-first work management product with backlog, prompt, knowledge, and monitoring capabilities across desktop, IDE, and phone channels. This repository is still in the bootstrap phase, so prefer durable instruction-file guidance and deliberate structure decisions over ad hoc scaffolding.

## Authoritative guidance order

See `.github/instructions/mcp-usage.instructions.md` for MCP server usage and authority order.

## Agent usage

See `.github/instructions/workflow-routing.instructions.md` for orchestration-skill and specialist-agent routing by task type.

## Guardrails

- Keep repository instruction files policy-focused; do not duplicate long-form MCP guidance into them.
- Do not invent permanent project structure before architecture and domain decisions make the boundaries clear.
- Ground governance and coding decisions in repository guidance instead of memory.
- Treat checked-in knowledge folders such as `.arc42/`, `.domain/`, `.backlog/`, and `.tech/` as **task-scoped context**, not baseline context. Load only the relevant chapters after routing to the correct orchestration or specialist agent, or when the user explicitly asks for that knowledge.
- Commit changes as they are made; do not leave edits uncommitted across multiple turns of the same task.
- Never open a pull request unless the user explicitly asks for one (via the create-PR action, a PR-creation skill, or a direct request). Committing to the session branch is not an implicit request to open a PR.
