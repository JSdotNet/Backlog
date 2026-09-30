---
name: capture
description: "Capture screenshots and recordings as evidence for the feature being built — at every scenario checkpoint, at every failure, and for a demo of finished work. Use when: capturing QA evidence, screenshotting a flow, recording a multi-step scenario, or a phase asks for evidence."
goal: "Return evidence a reviewer can open instead of taking your word for it: one file per checkpoint and per failure, every path under the git worktree root, and the form named honestly — a screenshot sequence is never called a video or a trace."
---

# Capture Evidence

Evidence for Backlog is taken against the harnesses `run` reports, saved under this
worktree's `.qa-workspace/evidence/`, and cited by path. It never lands in the committed
fixtures beside it.

## Choose the form from the live tool list

Before choosing a form, read the live tool list; do not assume video is available.

| The tool list shows | Capture as |
| --- | --- |
| Playwright `browser_take_screenshot`, and no tracing tools. This is the usual case: `@playwright/mcp` 0.0.79 has no tracing, no video, and no `--save-trace` or `--save-video`. | A numbered screenshot sequence |
| `browser_start_tracing` / `browser_stop_tracing` | A trace or video file |
| No Playwright server, or its Chrome profile is locked by a parallel session | The host's in-app browser screenshot. If even that is unavailable, say so and capture nothing rather than describing a frame you did not take. |

Name the form you actually produced, in the report and in the filename. A screenshot
sequence is never a video or a trace.

Tool names here are bare. Resolve the prefix from your own tool list: a plugin-provided
server is namespaced with its plugin (`mcp__plugin_qa_playwright__…`), and a
repository-registered one is not (`mcp__playwright__…`).

## Capture

1. **Stabilize first.** Wait for a specific element or text, never for a fixed time. Blazor
   Server renders after the first paint, so a frame taken mid-render is misleading.
2. **Take the shot.** Take the viewport by default. Scope the shot to one element when only
   that component's state matters: a field error, a toast, a modal.
   - Avoid full-page shots of `desktop-web-harness` panes. Removing the scrollbar widens the
     container past its breakpoint and changes the layout you are trying to show.
3. **Capture a failure before you recover from it.** The moment a check fails is the one
   frame that cannot be retaken.
4. **For phone width,** resize `desktop-web-harness` to 390×844 and say so in the filename
   (`-390w`). `mobile-web-harness` shows only Inbox quick-capture.

## Where it lands

```
.qa-workspace/evidence/<branch-slug>/screenshots/<NN>-<what-it-shows>.png   single checkpoints
.qa-workspace/evidence/<branch-slug>/sequence/<scenario>/<NN>-<step>.png    a multi-step flow
.qa-workspace/evidence/<branch-slug>/video/<scenario>.webm                  only if tracing exists
.qa-workspace/evidence/<branch-slug>/logs/<NN>-<resource>.txt               an excerpt a finding cites
```

- `<branch-slug>` is the current branch name without its `claude/` prefix, so evidence from
  parallel worktrees never mixes.
- `<NN>` is zero-padded, so files sort in execution order.
- Name a failure so the failure is obvious: `05-submit-500-error.png`.
- Never reuse a filename. An overwritten frame is a lost one.

### What is tracked and what is not

`.qa-workspace/` is partly tracked. `.gitignore` ignores `.qa-workspace/*` and then
un-ignores two paths:

| Path | What it is |
| --- | --- |
| `.qa-workspace/backlog.db` | Committed fixture: the task database QA scenarios run against |
| `.qa-workspace/config/` (`repos.json`) | Committed fixture: the repository registry QA scenarios use |
| `.qa-workspace/evidence/` and everything else | Run evidence: ignored and kept local |

- Never write evidence into `backlog.db` or `config/`. A QA run that changed either one has
  modified a fixture; restore it with `git checkout -- <path>` and say so.
- Stage by explicit path only. Never use `git add -A`, `git add .` or `git add .qa-workspace`:
  any of them sweeps a changed fixture into the commit.
- Evidence is committed only when the task asks for it. Then run `git add -f <path>` for
  each file, by name.

Keep every path under the worktree root. The dashboard rejects paths outside it, so a
sub-agent working in its own checkout must copy its evidence back before reporting it.

## In the report

Every visual claim cites the path that proves it. "The form validated correctly" with no path
attached is not a finding.

- For a sequence, cite the folder and say which step each frame shows.
- For a failure inside a recording, give the timestamp too.
- A log excerpt names its resource and its time window.
