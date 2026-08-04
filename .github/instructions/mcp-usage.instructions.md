# MCP usage

1. `.github/instructions/*.instructions.md` provide repository-specific routing and guardrails.
2. `jsdotnet-project-guidelines` is the authoritative source for repository guidance and conventions.
3. `jsdotnet-project-design` is the authoritative source for design and UX guidance.
4. Checked-in repository documents such as `README.md` are the local fallback when MCP guidance is unavailable.
5. Direct repository inspection is the last fallback.
6. Checked-in knowledge folders are **task-scoped local fallbacks**, not default context. Load `.arc42/`, `.domain/`, or `.backlog/` only when the selected orchestration or specialist agent needs that knowledge, and then prefer only the relevant chapter(s) over whole-folder reads.

If an authoritative MCP source is unavailable, read the checked-in instruction files directly and state that authoritative guidance could not be verified.
