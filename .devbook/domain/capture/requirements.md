# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/capture/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## One-tap entry

```meta
type: requirements
status: draft
related: [.devbook/domain/capture/features.md#one-tap-entry]
```

> The requirements of the phone's capture sheet. The feature chapter says what
> the sheet offers and why; this says what it promises. Every scenario opens
> the sheet from Today, because a save from Focus returns to Focus.

### Requirement: The capture sheet files the thought where it was told

```meta
type: requirement
status: draft
```

The capture sheet SHALL file a saved thought where the reader chose: Inbox as an ordinary capture, Note as a note, and Today as a new task in today's My Day.

#### Scenario: Inbox

- **Given** the capture sheet is open over Today with Inbox chosen
- **When** the reader types a thought, presses Add to inbox, and the desktop syncs
- **Then** the desktop Inbox shows the thought as a text item from Mobile

#### Scenario: Note

- **Given** the capture sheet is open over Today with Note chosen
- **When** the reader types a thought, presses Save note, and the desktop syncs
- **Then** the phone's Notes list shows the note, and the desktop Inbox shows it as a note from Mobile

#### Scenario: Today

- **Given** the capture sheet is open over Today with Today chosen
- **When** the reader types a thought, presses Add to today, and the desktop syncs
- **Then** the desktop shows a new task in today's My Day, and the phone shows it under Anytime today

### Requirement: A save leaves the sheet open for the next thought

```meta
type: requirement
status: draft
```

The capture sheet SHALL stay open and cleared after each save, with a line naming what it did and inviting the next thought.

#### Scenario: Inbox

- **Given** the capture sheet is open over Today with Inbox chosen
- **When** the reader saves a thought
- **Then** the sheet stays open with an empty field and reads "Added to inbox — capture another"

#### Scenario: Note

- **Given** the capture sheet is open over Today with Note chosen
- **When** the reader saves a thought
- **Then** the sheet stays open with an empty field and reads "Note saved — capture another"

#### Scenario: Today

- **Given** the capture sheet is open over Today with Today chosen
- **When** the reader saves a thought
- **Then** the sheet stays open with an empty field and reads "Added to today — capture another"
