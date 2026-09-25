---
name: capture
description: "Capture screenshots and recordings as evidence for the feature being built — at every scenario checkpoint, at every failure, and for a demo of finished work. Use when: capturing QA evidence, screenshotting a flow, recording a multi-step scenario, or a phase asks for evidence."
goal: "Return evidence a reviewer can open instead of taking your word for it: one file per checkpoint and per failure, every path under the git worktree root, and the form named honestly — a screenshot sequence is never called a video or a trace."
---

# Capture Evidence

**Edit this file** — the layout and the tooling below are yours. What comes back is fixed by
the wrapper's goal: one entry per checkpoint and per failure, paths under the worktree root,
the form named honestly.

## First: what can this server actually record?

**Read the live tool list before choosing a form.** Do not assume video is available.
`@playwright/mcp` 0.0.79 exposes no tracing or video tools — `browser_start_tracing` and
`browser_stop_tracing` do not exist, and there is no `--save-trace` or `--save-video` option.
Only `--save-session` and screenshots.

| The tool list shows | Capture as |
| --- | --- |
| No tracing tools — the common case | A numbered screenshot sequence |
| `browser_start_tracing` / `browser_stop_tracing` | A video or trace file |

**Never call a screenshot sequence a video or a trace.** Name the form you actually produced,
in the report and in the filename.

Tool names here are bare. Resolve the prefix from your own tool list — a plugin-provided
server is namespaced with its plugin, a repository-registered one is not.

## Capture

1. **Stabilize first.** Wait for a specific expected element or text, never a fixed sleep. A
   frame taken mid-transition is misleading evidence, not evidence.
2. **Take the shot.** Full page by default; scope to one element when only that component's
   state matters — a field error, a toast, a modal.
3. **Capture the failure before recovering.** The moment a check fails is the one frame that
   cannot be retaken.

## Where it lands

```
.qa-workspace/evidence/<feature>/screenshots/<NN>-<what-it-shows>.png   single checkpoints
.qa-workspace/evidence/<feature>/sequence/<scenario>/<NN>-<step>.png    a multi-step flow
.qa-workspace/evidence/<feature>/video/<scenario>.webm                  only if tracing exists
```

`.qa-workspace/` is partly tracked: `backlog.db` and `config/` are committed fixtures,
everything else is ignored by `.gitignore` (`.qa-workspace/*`). Evidence stays local — never
write into `backlog.db` or `config/`, and never stage with `git add -A`; stage by explicit
path.

`<NN>` is zero-padded so evidence sorts in execution order. Name a failure so the failure is
obvious: `05-submit-500-error.png`. Never reuse a filename — an overwritten frame is a lost
one.

Change this layout to suit the repository. Keep every path under the worktree root: a path
outside it is rejected, and a sub-agent in its own checkout must copy evidence back before
reporting it.

## In the report

Every visual claim cites the path that proves it. "The form validated correctly" with no path
attached is not a finding. For a sequence, cite the folder and say which step each frame is;
for a failure inside a recording, give the timestamp too.
