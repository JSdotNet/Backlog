# backlog-herdr

The herdr half of the Backlog tooling. Install it under herdr beside
[`backlog-tools`](../backlog-tools/README.md), the shared plugin, which it declares as a
dependency; Claude Desktop installs [`backlog-desktop`](../backlog-desktop/README.md) instead.

It is empty for now: no skill, hook or resource. It is the home of the parts that only work in
herdr — the herdr side of the session-host port local ADR 0038 names — so a herdr counterpart of
`backlog-desktop`'s `execute-plan` or `dispatch-sessions` lands here, never in `backlog-tools`.

The plugin ships both manifests: `.claude-plugin/plugin.json` for Claude Code, carrying the
dependency on `backlog-tools`, and `.github/plugin/plugin.json` for GitHub Copilot.

## Install

Under herdr, inside a session started at the repository root, after `backlog-tools`:

```
/plugin marketplace add .
/plugin install backlog-herdr@jsdotnet-backlog
```

Not in Claude Desktop: there, install `backlog-desktop`.
