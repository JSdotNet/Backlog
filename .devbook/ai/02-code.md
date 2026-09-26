# Code

```meta
status: adopted
type: stage
```

> How a change is made once it is agreed.

## Code gate through flow-code

```meta
status: adopted
type: guardrail
stage: [code]
depends-on: [".devbook/tech/ai-development.md#claude-code-plugins", ".devbook/tech/ai-development.md#repository-instruction-files"]
related: [".devbook/ai/03-test.md#personal-validation-gate"]
date: 2026-09-25
```

Before the first write under `src/` or `tests/`, a session routes the work through the
delivery engine's `flow-code`, whatever the size of the request. `CLAUDE.md` and
`.github/copilot-instructions.md` state the gate for both hosts.

- **Used for** — every feature, fix, refactor, and tooling change.
- **Adopted by** — every agent session in this repository.
- **Evidence** — the merged pull requests each carry a flow run.
- **Limits** — documentation confined to the devbook folders goes through `flow-spec`
  instead.

## Worktree session per change

```meta
status: adopted
type: practice
stage: [code]
depends-on: [".devbook/tech/ai-development.md#git-worktree-sessions"]
date: 2026-09-25
```

Each change runs in its own git worktree and agent session, so several sessions work in
parallel without touching each other's files or ports.

- **Used for** — every change; Aspire runs `--isolated` so ports stay per session.
- **Adopted by** — the repository owner, routinely with several sessions at once.
- **Evidence** — the parallel worktrees under `.claude/worktrees/`.
- **Limits** — shared machine state (the debug workspace, the Playwright profile) is not
  isolated, and a session changing another repository hands that work to its own session.

## Rules wrapped per host

```meta
status: adopted
type: practice
stage: [code]
depends-on: [".devbook/tech/ai-development.md#repository-instruction-files"]
date: 2026-09-25
```

A rule is written once under `.agents/rules/` and reached by both Claude Code and GitHub
Copilot through a one-sentence wrapper per host, so the two agents follow the same rules.

- **Used for** — shared component adoption, storybook authoring, naming, context loading,
  and the devbook folder rules.
- **Adopted by** — both hosts, on every matching file read.
- **Evidence** — the wrappers under `.claude/rules/` and `.github/instructions/`.
- **Limits** — a rule fires on a read, so a file authored from scratch may not trigger it.
