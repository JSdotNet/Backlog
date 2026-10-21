# Sessions

```meta
index: root
type: context
deployment: module
sync: push
related: [.devbook/arc42/05-building-block-view.md#desktop-app]
```

The Sessions context owns the record of what the AI coding agents have been doing. It
says which sessions an environment has run and which agent ran each one. It also says
where each worked, when it was last active, and whether it is still going. It owns no work item, machine
or repository. Each of those belongs to a supplier context, and this one holds only the
identifier it was given.

It is a **read model over evidence somebody else wrote.** The agents leave records
on the environments they run on; this context reads them and says what they mean. It
never starts, stops or names a session. That is why nothing here is a command, and why
the [Session Log](domain.md#session-log) aggregate has no invariant about state
transitions: a session's state is derived on every reading, not advanced.

One record here is not evidence: a [Session Request](domain.md#session-request) asks
for a session that does not exist yet. A person creates it with a click, and a
dispatcher outside this context starts the session (local
[ADR 0037](../../arc42/adr/0037-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md)).
The request is the context's one command, and it still never starts a session itself.

An optional Collections MCP can add a second kind of evidence: sanitized activity
updates that a configured session reports as it moves. Those updates are an
**enrichment layer**, not a second authority. They say more about what a known
session was doing; they do not decide that a session existed in the first place.

Records can also **travel between the person's own machines**, so the environment
that ran a session need not be the one they are sitting at. That is replication, not
a third kind of evidence; the [Session Log](domain.md#session-log) says what does and
does not change when records replicate.

"Which agent, in which repository, for how long" is this context's question. "How is
this PC configured" is [Dev PC Management](../dev-pc-management/domain.md#machine-registry)'s,
and that context models no sessions.

## Sessions area

```meta
status: draft
type: feature-flag
key: sessions
default: on
related: [".devbook/domain/sessions/features.md#turn-the-area-off"]
```

Turning it on shows the Sessions segment and its list in the Home shell. Switching it
off withdraws the area everywhere else too:

- the `list_sessions` tool and the eight delivery-surface operations leave the MCP
  endpoint;
- the dashboard's hours worked and the roadmap's actual hours stop reading agent
  activity.

It is on by default and marked `DEV`, the settings screen's mark for a feature still
under construction
([Feature Enablement](../../arc42/08-crosscutting-concepts.md#feature-enablement)).
The key is `SessionFeatures.Sessions` in the Sessions module. Its string is the one
`DevPcFeatures` used, so a choice a person already stored still applies. The settings
screen lists it from the feature catalog in `AppFeatures.cs`, and a person's choice is
kept in `features.json` beside the app's other per-device choices. It is retired when
the Sessions area leaves `DEV`.

## Session requests

```meta
status: draft
type: feature-flag
key: session-requests
default: off
related: [".devbook/domain/sessions/features.md#start-a-session-from-an-entry", ".devbook/arc42/adr/0037-backlog-queues-session-requests-and-a-manager-session-dispatches-them.md"]
```

Turning it on offers Start session on an entry and on a plan's roadmap item, maps
`GET /session-requests/wait` on the MCP listener, and adds the tools a dispatcher
claims and reports with. Off, the action is not shown, the route answers `404` and the
tools leave the endpoint.

It is off by default while the dispatchers are built
([Feature Enablement](../../arc42/08-crosscutting-concepts.md#feature-enablement)).
The repository owner decides when it turns on. It is retired once the Manager skill
has shipped and the feature leaves `DEV`.
