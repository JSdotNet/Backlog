# Features

```meta
status: proposed
type: features
```

> What Notifications lets the person and the other contexts do, in the context's
> own language. What each feature guarantees is in
> [`requirements.md`](requirements.md).

## Publishing a notification

```meta
status: proposed
type: feature
related: [.devbook/domain/notifications/requirements.md#publishing-a-notification]
```

A source on a desktop tells the person something with an alert, or asks them
something with a question, through one port. The desktop that publishes becomes
the notification's owner instance. A Claude session asking through the MCP `ask`
tool is the first case: the tool returns the notification's id at once, and the
agent polls for the answer.

## Answering from another device

```meta
status: proposed
type: feature
related: [.devbook/domain/notifications/requirements.md#answering-from-another-device]
```

The person sees a notification on the Android phone or on another desktop and
replies there. The reply reaches the owner, which accepts it as the answer or
rejects it with a reason. Every viewer shows which desktop owns the notification
and whether it is online, because an answer counts only once that desktop accepts
it.

## Closing a notification

```meta
status: proposed
type: feature
related: [.devbook/domain/notifications/requirements.md#closing-a-notification]
```

A notification stops asking for attention when the publisher closes it or when
the thing it is about ends. Nobody answers a question about work that is already
gone.
