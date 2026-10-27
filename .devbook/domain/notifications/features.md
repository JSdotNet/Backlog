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
agent polls `get_answer` for the answer, or withdraws its question with `close`
once it no longer needs one. The MCP `notify` tool tells the person something the
same way, with nothing to wait for.

## Answering from another device

```meta
status: proposed
type: feature
related: [.devbook/domain/notifications/requirements.md#answering-from-another-device, .devbook/arc42/06-runtime-view.md#notification-reply]
```

The person sees a notification on the Android phone or on another desktop and
replies there. The reply reaches the owner, which accepts it as the answer or
rejects it with a reason. Every viewer shows which desktop owns the notification
and whether it is online, because an answer counts only once that desktop accepts
it.

On the phone this is the Notifications tab in `Backlog.Mobile.UI`. It pulls the
feed when it opens and when the app comes back to the front, and every 30 seconds
while a reply made there waits for its owner. Each card shows the kind, the
severity, the age, the owner desktop's name and whether it is online; an owner
offline reads "delivers on wake". The detail draws the Markdown body, Mermaid
included. A question takes choice buttons, checkboxes for multiple choice, or a
plain-text answer, and the reply leaves through the device outbox as a
`notification-reply` entry, so a reply made offline is kept. Once the owner's
verdict syncs back the card reads "Answered from this phone", "Answered
elsewhere" with the answer that won, or the owner's reason for rejecting it with
the controls back. A question that says `answerOn: owner` is read-only there.

The phone reads presence off the owner's own pushes: an owner desktop pushes each
open question again every 10 minutes, and a phone that has seen no push from it
for 15 minutes reads it as offline.

The phone also hears of a notification without being opened, through an Android
push. When a notification is published or changes state, the phone gets an
Android notification with its title and summary. The presence re-push sends
none. What the tray offers depends on the question:

| The question | The tray offers |
| --- | --- |
| Single choice, up to three choices | one action per choice |
| Open answer only | an inline reply |
| Choices with an open answer, up to two choices | the choices, then the inline reply |
| Anything else: multiple choice, more choices than fit, `answerOn: owner`, an alert, a reply already on its way | no actions |

An action or an inline reply leaves through the same `notification-reply` outbox
entry as a reply made in the app. A tap opens the notification in the app. A
notification that is answered, acknowledged or closed leaves the tray. The phone
registers for the push once it is paired, and again when Firebase hands it a new
token.

On the owner desktop, the push row of **Settings › Notifications** stays *not
reported*. The push carries no delivery receipt, so the desktop cannot tell
whether a phone showed it.

## Asking from the contexts

```meta
status: proposed
type: feature
related: [.devbook/domain/notifications/requirements.md#asking-from-the-contexts]
```

Three contexts reach the person through notifications. A delivery run that
reaches Personal Validation asks to be approved, and the answer is recorded on the
run; the pull request is still opened at the desktop. A device that redeems a
pairing code is asked about on the desktop that showed the code, and only there:
Allow keeps it, Refuse revokes it. A remark left on a Devbook chapter on another
device raises an alert that links to the chapter.

spec-manager reaches the person the same way. While a desktop is signed in to an
installation, the questions on its connected products that are assigned to the
person, and the unassigned ones they may answer, arrive as questions: each option a
choice, "Other" the open answer, and a note beside either. A product's "Only
questions assigned to me" leaves the unassigned ones out. The desktop follows
spec-manager's notification stream and pulls on each change, every five minutes
besides, and every two minutes while the stream is down. The answer goes back to
spec-manager. If the product does not take answers from a connected app, or someone
answered first, an alert says so. While the desktop is signed out, the answer
waits, and the sign-in line asks the person to sign in. A question answered,
deleted or reassigned away in spec-manager closes here.

The person's other spec-manager notifications arrive as alerts on the same pulls:
a mention, a reply, a change proposal's or an input item's fate, review work, and
administration. Only the unread ones come in, and only those created after the
person first signed in to that installation from this desktop; older ones stay in
spec-manager. A mention is a warning, the rest are info, and each opens where
spec-manager points. Acknowledging the alert here, on any device, marks it read in
spec-manager. One read in spec-manager closes here as "Read in spec-manager".

## Closing a notification

```meta
status: proposed
type: feature
related: [.devbook/domain/notifications/requirements.md#closing-a-notification]
```

A notification stops asking for attention when the publisher closes it or when
the thing it is about ends. Nobody answers a question about work that is already
gone.
