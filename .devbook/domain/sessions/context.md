# Sessions

```meta
index: root
type: context
```

The Sessions context owns the record of what the AI coding agents have been doing:
which sessions an environment has run, which agent ran each one, where it worked,
when it was last active, and whether it is still going. It owns no work item, no machine
and no repository — every one of those belongs to a supplier context, and this one
only holds the identifier it was given.

It is a **read model over evidence somebody else wrote.** The agents leave records
on the environments they run on; this context reads them and says what they mean. It
never starts, stops or names a session, which is why nothing here is a command and
why the aggregate below has no invariant about state transitions — a session's state
is derived on every reading, not advanced.

An optional Collections MCP can add a second kind of evidence: sanitized activity
updates that a configured session reports as it moves. Those updates are an
**enrichment layer**, not a second authority. They say more about what a known
session was doing; they do not decide that a session existed in the first place.

Evidence can also **travel between the person's own machines**, so the environment
that ran a session need not be the one they are sitting at. That is replication,
not a third kind of evidence: a record read on the second machine is still the
record the first machine wrote. See
`.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md`.

This subject was first modelled inside
[Dev PC Management](../dev-pc-management/domain.md#machine-registry) as
Copilot Session Tracking on the Machine, when Copilot was the only agent the
machines ran. Two agents run on them now, and "which agent, in which repository, for
how long" is a different question in a different language from "how is this PC
configured" — so the subject moved out to here and Dev PC Management stopped
modelling it.
