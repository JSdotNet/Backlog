# Test

```meta
status: adopted
type: stage
```

> How a change is proven before a person approves it.

## QA agent on the running application

```meta
status: adopted
type: agent
stage: [test]
depends-on: [".devbook/tech/ai-development.md#subagents", ".devbook/tech/ai-development.md#model-context-protocol-servers"]
date: 2026-09-25
```

A QA subagent starts the application through Aspire, drives it with Playwright, and watches
Aspire logs and traces for the whole run, returning evidence for every scenario it reports.

- **Used for** — validation of new functionality at full depth, and targeted checks for
  fixes and refactors.
- **Adopted by** — every code-modifying flow run.
- **Evidence** — screenshots and logs under `.qa-workspace/`, attached to the run.
- **Limits** — its browser profile is shared per machine, so parallel sessions stagger QA.

## Personal Validation gate

```meta
status: adopted
type: guardrail
stage: [test, release]
related: [".devbook/ai/concepts.md#human-in-the-loop"]
date: 2026-09-25
```

No pull request is opened and no flow is marked complete until the repository owner has
reviewed the change and approved it in the session.

- **Used for** — every flow, documentation and code alike.
- **Adopted by** — every flow run; the engine refuses to let configuration remove it.
- **Evidence** — the approval recorded on each run.
- **Limits** — an unattended run parks at the gate rather than approving itself.
