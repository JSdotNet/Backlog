# ADR 0068: The `backlog` MCP server keeps grant tokens, and serves its own skill as a prompt and a resource

```meta
date: 2026-11-25
related: [".devbook/arc42/adr/0012-backlog-is-an-mcp-server-inside-the-desktop-app.md", ".devbook/arc42/adr/0058-agents-and-devices-authenticate-by-grant-and-key-the-owner-by-github-sign-in.md", ".devbook/arc42/adr/0059-tokenless-mcp-calls-get-the-scopes-the-person-opens-to-them.md", ".devbook/arc42/adr/0065-sync-identity-stays-github-and-device-keys-and-every-anonymous-route-is-rate-limited.md", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog.desktop]
c4-touches: [claude, codingAgents, backlog.cloud]
```

The `backlog` MCP server in the desktop app keeps the authentication local ADR 0058
built: a grant per registration, a token broker on a named pipe, 15-minute scoped
tokens, and an audit entry per call. It does not become an OAuth 2.1 protected resource
with Client ID Metadata Documents and RFC 8693 token exchange. One part of org ADR 0011
is adopted: the server ships its own skill, and serves it as a prompt and as a resource.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records one deliberate difference from, and one move to,
[org ADR 0011: Setting up MCP](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0011-setting-up-mcp.md).
It names org ADR 0011 for the choices local ADRs 0012, 0058 and 0059 already made, and
changes none of them.

## Context

```meta
```

Step 15's list (PR #1967) said the server "uses a bearer token and writes no per-call
audit record". That predates the end of plan `zero-trust-auth`. Against org ADR 0011
the server now stands like this:

| Org ADR 0011 says | The `backlog` server | Where |
| --- | --- | --- |
| OAuth 2.1 protected resource; the app's IdP is the authorization server | Local: loopback only, a grant's 15-minute JWT from the token broker, or with no token the scopes the person opened (local ADR 0059). Remote: OAuth 2.1 on the sync service, RFC 9728 metadata, audience-bound tokens | Local ADR 0058 §3, §8 |
| Clients identify with CIMD, PKCE always | Local: a grant id, no secret in a file. Remote: one static client, `claude-connector`, PKCE | Local ADR 0058 §3, §8 |
| Two identities, RFC 8693 exchange naming user and agent | The grant is the agent's identity, and the person is the one Windows user the broker admits | Local ADR 0058 §3 |
| Per-call policy for the user and the agent | A scope per tool group under fallback-deny; Windows Hello for `tasks:admin` | Local ADR 0058 §4, §6 |
| An audit record per call | Grant, tool, target, outcome and correlation id, shown in Settings (`McpCallAudit`, `SqliteMcpAuditLog`) | Local ADR 0058 §3 |
| The server's own skill, also served as a prompt and a resource | No skill describes the server's tools. It serves no prompt and no resource | — |

Local agents run as the person's own Windows user on the same machine. There is no
identity provider to exchange a token with, and the named pipe's ACL already proves
which user is calling. CIMD exists so that unknown clients can identify themselves. The
remote endpoint admits exactly one client, registered by hand.

The skill gap is real. The workflow skills in `plugins/backlog-tools` each use a few
tools, but none says which tool to call when, in what order, and what to avoid. A client
without the plugin, such as the claude.ai connector or Copilot, gets only the tool
descriptions.

## Decision

```meta
```

1. **Keep.** The local server authenticates by grant and broker, and the remote endpoint
   by OAuth 2.1 with one static client, as local ADR 0058 says. No CIMD and no RFC 8693
   exchange.
2. **Move.** The server gets its own skill, in `plugins/backlog-tools` beside the
   workflows that use it: which tool for which job, the order of the common workflows
   (find, read, transition, link, comment), and what to avoid. The desktop listener
   serves the same text as an MCP prompt and as a read-only resource, so a client
   without the plugin can read it.

## Follow-up

```meta
```

| Work | Size | Entry |
| --- | --- | --- |
| Write the `backlog` server skill; serve it as a prompt and a resource from the listener under a read scope; test that the served text matches the plugin's file | M | `+app-defaults-rollout`: *Ship the backlog MCP server's own skill and serve it as a prompt and a resource* |

## Consequences

```meta
```

- A client without the plugin learns the workflow from the server itself.
- The skill and the tool set have to change together. The test that compares the served
  text with the plugin file catches a copy that drifted, but not a tool the skill fails
  to mention.
- An app-defaults review reads this record for the 0011 row.

## Rejected

```meta
```

- **OAuth 2.1 with CIMD and token exchange on the local listener (L).** It needs an
  identity provider that Backlog does not have (local ADR 0065), and it would prove only
  what the pipe's ACL already proves.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep grant tokens and the static remote client; ship the server's own skill and serve it as a prompt and a resource. | Step 15 of plan `app-defaults-rollout` (PR #1967) listed the org ADR 0011 difference with no local decision. |
