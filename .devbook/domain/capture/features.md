# Capture

```meta
type: features
```

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## Mobile capture

```meta
type: feature
related: [".devbook/arc42/05-building-block-view.md#mobile-app"]
```

Frictionless capture from a phone while away from the desktop, offline first.

A capture needs only a title and where it came from. Everything else is optional:

- a Markdown body;
- tags;
- the one person it is about or from;
- files: pictures, PDFs, text and Office documents that travel beside the capture.

The phone names every capture with an id of its own before the first
send, so sending it again is never a second capture. One-tap entry uses the
body, tags and person, offline-first sync uses the id, and [Notes](#talk-note)
use the files.

### One-tap entry

```meta
type: sub-feature
related: [.devbook/domain/capture/requirements.md#one-tap-entry]
```

Rapid title + body capture with optional tags and source context.

On the phone, one tap on the capture button opens the capture sheet at
`/capture`: a sheet up from the bottom edge, over the screen it was opened from.
It asks where the thought goes, and the hint over the text field, the field's
prompt and the one save button follow the choice:

| Destination | Save button | Line after a save | Dictation and photos | What a save creates |
| --- | --- | --- | --- | --- |
| **Inbox** (the default) | `Add to inbox` | `Added to inbox — capture another` | both | An ordinary capture, the same one the Inbox tab's own field sends. |
| **Note** | `Save note` | `Note saved — capture another` | both | A note the phone keeps and edits (see [Notes on the phone](../inbox/features.md#notes-on-the-phone)). |
| **Today** | `Add to today` | `Added to today — capture another` | text only | A task picked for today, as My Day's add does (see [My Day](../tasks/features.md#my-day)). |

A save leaves the sheet open and cleared, with the line from the table, so
several thoughts in a row need no reopening. Opened from Focus, a save goes back
to Focus instead: the thought is set down and the task taken up again. Cancel, a tap on the dimmed screen behind, or Escape go back
to the screen the sheet came from.

On Android, a long press on the Backlog icon lists **Capture**. A tap opens the
same sheet over the Inbox, with Inbox chosen, whether the app was closed or open
on any tab; Cancel lands on the Inbox. The tap is followed once, so reopening the
app from Recents after Android ended it does not bring the sheet back.

The sync service takes the body, the tags and a person alongside the title and
the source, each optional and bounded. A person is sent as the person, never as
a tag. On the desktop it becomes the item's source person, and the tags
are stored bare and once each. A phone that sends only a title and a source gets
the same answer as one that sends everything.

### Speech-to-text capture

```meta
type: sub-feature
related: [.devbook/domain/capture/requirements.md#speech-to-text-capture, .devbook/arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md, .devbook/arc42/05-building-block-view.md#mobile-app]
```

```mermaid
flowchart TD
    Mic[Person presses the mic] --> Chosen{Engine is Cloud?}
    Chosen -- no --> Device[On-device: words appear as heard]
    Chosen -- yes --> Ready{Online, cloud not found unavailable, phone can record?}
    Ready -- no --> Device
    Ready -- yes --> Record[Record until stop, at most 5 minutes]
    Record --> Send[The cloud transcribes the recording]
    Send -- text --> Field[Text added to the field]
    Send -- cannot serve --> Notice[Notice, then on-device for the rest of the app run]
    Send -- transcription failed --> Retry[Failed turn: try again]
```

Dictation turns speech into text in a field on the phone, keeping the capture's source. The capture sheet, the Notes editor and a notification's answer boxes take it. What was heard is shown and appended to the field, so nothing is sent unread.

The phone has two engines, chosen under **Settings › Dictation › Engine**, beside
the Dictation **Language**:

| Engine | How a turn reads | Where the audio goes |
| --- | --- | --- |
| **On-device** (the default) | Words appear as they are heard. | Nowhere: the phone's own recogniser listens. |
| **Cloud** | `Recording…`, then `Transcribing…`, then the text. | To the sync service, which hands it to Backlog's Azure model and keeps none of it. |

- **Why:** Cloud is opt-in because the audio leaves the phone. Choosing it is the
  person's consent, and the phone sends audio only on a turn the person starts.
- **Why:** On-device stays the default and the fallback, so dictation works with
  no cloud at all.

A phone that is offline listens on the device at once, with no notice. A sync
service that cannot transcribe answers with the notice "Cloud dictation is
unavailable here; using on-device." The phone then dictates on the device until
the app next starts. A transcription that fails is no fallback: the recording is
already made, so the person simply tries again.

With one Dictation language chosen, Dutch or English, a cloud turn tells the model
which. With Dutch and English, or the phone's language, the model detects it.
[ADR 0045](../../arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md)
records the relay, its limits and what the service never keeps. The rules are in
[the requirements](requirements.md#speech-to-text-capture).

### Offline-first sync

```meta
type: sub-feature
```

```mermaid
stateDiagram-v2
    [*] --> Waiting: capture made, kept on the phone
    Waiting --> Waiting: send fails, retried on its own
    Waiting --> TapToRetry: retries used up
    TapToRetry --> Waiting: tap, app reopened, or network back
    Waiting --> Sent: the service takes it
    Sent --> [*]: the desktop takes it in
```

Every capture is kept on the phone before it is sent, so a missing signal never
loses one. It appears in the Inbox list at once, marked `Waiting`, and
captures leave in the order they were made. A failed send is retried on its own a
few times, with growing waits, before it reads `Waiting — tap to retry`. The
status line says how many captures are waiting. A capture reads `Sent` the moment
the service takes it. It stays in the list until the next pull brings it back,
even when that pull was asked before it landed, or fails.

Resending after a lost answer delivers a capture once: the service answers with the
capture it already holds.

The Inbox tab says how many captures wait to be sorted, as `3 items to sort`,
over a field that adds one more. Each row in its list shows:

- when the capture was made;
- a glyph and a word for what kind of thing it is;
- the first line of its body;
- where it stands: `Waiting`, `Waiting — tap to retry`, or `Sent`.

A tap opens the whole capture with its source, tags and person. The list holds
only this phone's captures, and each leaves it once the desktop takes it
in. The phone never triages, so a row offers no decide or dismiss action: see
[Triage stays on the desktop](../inbox/features.md#triage-stays-on-the-desktop).
The list refreshes on a pull down, on the refresh button and on returning to the
app. When the service cannot answer, the last list stays on screen
with a line saying why it is not newer.

### Talk note

```meta
type: sub-feature
related: [".devbook/arc42/06-runtime-view.md#talk-note-upload", ".devbook/arc42/06-runtime-view.md#mobile-note-sync", ".devbook/domain/inbox/features.md#notes-on-the-phone", ".devbook/domain/inbox/domain.md#note", ".devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md"]
```

A note taken during a conference talk: what was said, the slide photo, the
handout. It is a [note](../inbox/domain.md#note), written in the phone's Notes tab
([Notes on the phone](../inbox/features.md#notes-on-the-phone)). It is never
called a session note, because Sessions is a bounded context of its own.

The Notes editor takes dictation into the body, photos from the
camera, and pictures and files chosen from the phone, several to a note. A note
is an item of kind `note` that stays on the phone, and either side may edit it. It
carries no speaker and no tags, and has no send button: it saves on its
own. A talk note an earlier build left in the outbox is still sent as an
ordinary capture.

Each attached file shows its name, type and size. Cloud sync refuses a file over
the per-file limit, or one that is not a picture, PDF, text or Office file. Such a
file stays off the note, with the reason written under it, until it is taken off
the list of files not attached.

Pictures are made smaller before they leave:

- at most 1600 pixels on the longest side;
- as JPEG;
- with the location, camera and time details removed, and only the way-up kept.

Files go as they are, each uploaded before the note that
names it. A file already sent is never sent again, and a failed one is
retried on its own. A note saved without a title is named after the first line of
its body. With no body, it is "Photo · <date>" when a picture is attached, else the
first file's name.

### Share-sheet and shortcuts

```meta
type: sub-feature
```

Another app hands its content to Capture through the phone's share sheet or a
shortcut, without opening the capture sheet first.

The launcher icon's **Capture** shortcut hands over no content: it only opens the
capture sheet, as [One-tap entry](#one-tap-entry) describes.

## Automation capture

```meta
type: feature
```

Unattended monitors that watch external sources and create captures on a
configurable schedule, with retry/backoff and failure logging.

### Run capture now

```meta
type: sub-feature
related: [.devbook/domain/capture/domain.md#source-adapter, .devbook/domain/inbox/features.md#incoming-queue]
feature-flag: .devbook/domain/inbox/context.md#inbox-pane
```

The reader runs the monitors on demand from the Inbox, without waiting for a
schedule. A folded Sources panel on the Inbox pane, beside the button that
runs them, sets which sources are watched and what each one looks at. The sources
are YouTube, website, event page and email; the targets are channels, URLs, page
URLs and senders. A source is off until switched on, and the event page source
shows only while the [Events area](../events/context.md#events-area) is on.

A run answers per source. It says how many new captures it delivered, or that no
`Source Adapter` exists for that source yet. A source that cannot be watched says
so instead of silently finding nothing. With no source on, the run points
at the panel.

Each source's row also carries when it was last looked at and how many new
captures that run delivered. A log of earlier runs sits below, with the same
per-source line. The log is a bounded window, not an archive (the capture run log in
[Capture sources](context.md#capture-sources)).

### YouTube monitor

```meta
type: sub-feature
```

Polls a subscribed channel's video feed for new videos. The channel is given as
a feed URL, a channel id, or a handle, user or `c` page that resolves to one.

New videos go straight into the Inbox. There is no folder filing and no
`#capture/youtube` auto-tag yet.

### Website monitor

```meta
type: sub-feature
```

Watches a configured URL for a feed it can discover, and delivers new entries into
the Inbox. It finds the feed through `<link rel=alternate>`, a feed link on the
same host, or a well-known path. A site with no feed has no fallback yet: the
monitor does not compare the page between runs. There is no folder filing and no
`#capture/web/{domain}` auto-tag yet.

### Event page monitor

```meta
type: sub-feature
feature-flag: [.devbook/domain/inbox/context.md#inbox-pane, .devbook/domain/events/context.md#events-area]
related: [.devbook/domain/capture/requirements.md#event-page-monitor, .devbook/domain/capture/domain.md#event-facts, .devbook/domain/capture/context.md#capture-sources, .devbook/domain/events/features.md#to-event-route, .devbook/domain/inbox/features.md#route-to-event]
```

```mermaid
flowchart TD
    Page[A watched event page] --> S1{schema.org event on the page?}
    S1 -- yes --> Dates[Item with dates and place]
    S1 -- no --> S2{Registration link with a schema.org event?}
    S2 -- yes --> Dates
    S2 -- no --> S3{Date pattern in the page text?}
    S3 -- yes --> Dates
    S3 -- no --> Link[Item with the page link only]
```

Watches the pages where organisers announce their events, and brings each new
edition into the Inbox for the person to route [To Event](../inbox/features.md#route-to-event).
It is a capture source of its own, `event_page`. Its targets are page URLs, kept in
the Inbox's Sources panel the way YouTube channels are. It shows only while the
[Events area](../events/context.md#events-area) is on.

The monitor reads each page by fixed rules and asks no AI model. It stops at the first step that
gives a date:

1. A schema.org event in the page's structured data.
2. One hop to a registration link, and the schema.org event on that page. A
   registration link points at an Eventbrite host, or its text says *register*,
   *registreer* or *tickets*.
3. A date pattern in the page text, with English or Dutch month names, such as
   `26 - 28 OCT 2026`. A date with a year is read day first or month first, as in
   `November 27, 2026`. The page text includes what its images say in their `alt`
   and `title`, because Techorama shows its dates only there.
4. No date found: the item arrives with only the page link.

The dates and place it found ride with the capture as its
[Event Facts](domain.md#event-facts). A date has a time and a UTC offset only when the
source gave them. An edition whose end date has passed is skipped, because an
organiser's page often still shows last year's edition.

Each edition is delivered once. Its external id is the listing id when the
registration page has one. Otherwise it is the page URL, the year and the place, so
next year's edition on the same page is a new item.

The monitor runs only when the person presses [Run capture now](#run-capture-now).
[Scheduled scans](#scheduled-scans) does not cover it.

- **Known limit:** a date that moves after its edition was delivered is not
  noticed. A run only asks whether an external id is already known.
- **Known limit:** a page whose layout changes can hide its dates. The item then
  arrives with only the page link, which is the visible symptom.
- **Known limit:** a page that still carries last edition's schema.org event, or a
  link to its listing, stops at that step. The run says the edition has passed,
  even when the page text already names the next one.
- **Known limit:** two editions on one page in the same year and place, with no
  listing id, share an external id. Only the first is delivered.

### News email ingestion

```meta
type: sub-feature
```

Polls an email inbox over IMAP (the Internet Message Access Protocol) for
newsletters and summaries, and tags each capture `#capture/email/{sender}`.

### Scheduled scans

```meta
type: sub-feature
```

The monitors run on a schedule the reader sets, so nobody has to press Run
capture now.

### Composable monitors

```meta
type: sub-feature
related: [.devbook/domain/capture/domain.md#source-adapter, .devbook/domain/monitoring/features.md#automation-status]
```

Let the user compose a monitor from a trigger, a condition, and the
capture it produces, instead of choosing only from the shipped monitors.
A composed monitor carries the same schedule, retry/backoff, and
failure-logging guarantees as a built-in one. It still delivers a normalized
capture, so nothing downstream can tell the two apart.

This is a possibility, not a commitment. What prompted it is xyOps
(<https://github.com/pixlcore/xyops>), a self-hosted automation platform that wires
events, triggers, actions, and monitors into pipelines through a visual editor.
The technology choice is open: build the capability in-product, or run such a platform behind
the `Source Adapter` anti-corruption layer. Either way the local-first rule holds:
a composed monitor's source credentials stay on the machine and never route
through the optional Cloud Service.

Boundary: this covers monitors watching sources *outside* the product. Signals
about the product's own health continue to reach the Inbox as Monitoring's
[`FollowUpCaptured`](../monitoring/domain.md#followupcaptured), not as a capture.

## Web clipper capture

```meta
type: feature
```

A browser extension or bookmarklet clips web content:

- the URL;
- the title;
- the selected text;
- the page metadata.

It converts the clip to markdown and keeps the source link.

## IDE capture

```meta
type: feature
```

An adapter lets an IDE-class host capture selected code or text, or a session
note. The capture carries context metadata: the file path, line number and
branch, or the session and worktree. It covers editor extensions (VS Code, Visual
Studio) and agentic session tools (GitHub Copilot App).

### Copilot App session capture

```meta
type: sub-feature
related: [.devbook/domain/capture/domain.md#source-adapter]
```

Let a GitHub Copilot App session capture a backlog idea, follow-up, or knowledge
note from its agent conversation. The capture carries the session
id, the local worktree path, and the current branch. It runs
against the local worktree only, so no source credential leaves the machine. The desktop's source monitors work
under the same local-first rule.

## Manual import

```meta
type: feature
```

The reader drags and drops files or pastes content directly. The product converts
it to markdown (MarkItDown or equivalent) and extracts tags, links, and source
metadata automatically.

### Import manifest

```meta
type: sub-feature
related: [.devbook/arc42/adr/0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md, .devbook/domain/capture/domain.md#capture-source, .devbook/domain/inbox/domain.md#inbox-list]
```

```mermaid
flowchart LR
    E[Another tool's export] --> S[import-inbox skill]
    S --> M[Manifest]
    M --> R[Import file… row]
    R --> I[unprocessed Inbox Items, source import]
```

Bring the items from another tool into the Inbox by importing a manifest.
Microsoft To Do is the first tool. The `import-inbox` skill sits in the `backlog-tools`
plugin, outside the product. It leaves out completed items, maps the tool's lists to Inbox
Lists, and maps the labels the person chooses to tags.

The manifest is Markdown with front matter. A `---` block names the `schema` and
the `tool`. Then comes one `#`-titled item per capture, with its notes as the body,
after a `meta` fence that carries:

- `external_id` and `captured_at`;
- optionally `url`, `kind`, `tags`, `person`, and `list`.

The Inbox's Sources panel has an **Import file…** row below the monitors. A
manifest picked or dropped there is imported at once, through the same run the
Capture button uses. The row shows the import's line, last run, and a log
of earlier imports, like each monitor's row.

An item's
`url` and `captured_at` are kept as written. A stated `kind` is kept; without
one, the kind is read off the item the way any capture's is. An item is filed
in the Inbox List its `list` names when that list exists. It lands unfiled when
the item names no list, or names one that does not exist. An import never
creates a list.

Importing the same or an overlapping manifest again adds only the
items not already there. An item is known by its id, from the tool
and the item's `external_id`; nothing records past imports. An item
without an `external_id` is known by a fallback id: the hash of
its title and `captured_at`, the same one the generating skill writes. A
hand-written item also imports once, until its title changes.

The run answers with one line, in the form the feed monitors use:
`Import (microsoft-todo): 12 new items · 30 already known.`

A manifest is often hand-edited, so an item that cannot be read is skipped
and the rest still come in. The line names each skipped item and what to change, as in
`Item 4 "Buy oat milk" has no captured_at — add one like captured_at:
2026-09-20T08:14:00Z`. A missing list is named too:
`no list is called "Errands", so what was meant for it landed unfiled`. Only
front matter that cannot be read refuses the whole file, because without its
`tool` no item has an identity.

## Normalized delivery

```meta
type: feature
related: [.devbook/domain/inbox/features.md#incoming-queue]
```

Every capture source produces a standard Inbox Item (title, `body_md`, source,
tags, `captured_at`) and delivers it to the Inbox's
[incoming queue](../inbox/features.md#incoming-queue), preserving the original
source link and capture timestamp.

## In-app feedback capture

```meta
type: feature
feature-flag: .devbook/domain/capture/context.md#feedback-reporting
related: [.devbook/domain/repository-management/features.md#github-access-resolution]
```

Report a problem from inside the app, at the moment it happens.
The report carries a title, optional detail, and the area of the screen the
problem concerns. The product attaches a picture of the current screen. The report is filed
as an issue against the product's own repository. A failed screen capture
is stated in the report.
