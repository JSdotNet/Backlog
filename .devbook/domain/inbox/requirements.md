# Requirements

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## Capture attachments

```meta
type: requirements
status: draft
related: [.devbook/domain/inbox/features.md#capture-attachments]
```

> The requirements of bringing a capture's files to the desktop Inbox. The
> feature chapter says what the capability is and why it exists. This chapter
> says what it promises.

### Requirement: A capture lands whatever happens to its files

```meta
type: requirement
status: draft
```

The desktop SHALL receive a capture into the Inbox even when one of its files
cannot be downloaded or kept, with that file's reason recorded on it.

#### Scenario: The download fails

- **Given** a capture from the phone that names one file
- **When** the desktop syncs and the file cannot be downloaded
- **Then** the item is in the Inbox with the file listed and the reason it failed

#### Scenario: The file cannot be written on this machine

- **Given** a capture that names one file, and a download that succeeds
- **When** the file cannot be saved into the item's attachment folder
- **Then** the item is in the Inbox and the file says this machine could not keep it

### Requirement: The item is saved before any file is downloaded

```meta
type: requirement
status: draft
```

The desktop SHALL record the files a capture names on its item, and save the
item, before it downloads any of them.

#### Scenario: A file is still on its way

- **Given** a capture that names a file
- **When** the item appears in the Inbox before the file has been downloaded
- **Then** the file is listed on the item as *Waiting to download*

### Requirement: A file is kept only when it matches what was sent

```meta
type: requirement
status: draft
```

The desktop SHALL keep a downloaded file only when its bytes match the sha256
the capture recorded for it.

#### Scenario: The bytes do not match

- **Given** a capture naming a file with a recorded sha256
- **When** the bytes the desktop downloads have a different sha256
- **Then** no file is written and the file is marked failed with its reason

### Requirement: A replayed sync page downloads nothing twice

```meta
type: requirement
status: draft
```

When the same capture arrives again, the desktop SHALL download only the files
that have never been tried, while recording any file the item did not have yet.

#### Scenario: The same page arrives twice

- **Given** an item whose files were all downloaded
- **When** the same capture arrives again
- **Then** nothing is downloaded and nothing is written

#### Scenario: A failed file is left for the person

- **Given** an item with one failed file
- **When** the same capture arrives again, now naming one more file
- **Then** the new file is recorded and the failed file is not tried again

#### Scenario: The file is already on disk

- **Given** a file already in the item's attachment folder with the recorded sha256
- **When** the capture is received
- **Then** the file is marked downloaded without being downloaded again

### Requirement: A person can retry a failed file

```meta
type: requirement
status: draft
```

The Inbox SHALL download a failed file again when the person presses Retry on
it, and show either the downloaded file or the new reason.

#### Scenario: The retry succeeds

- **Given** an item showing a failed file with its reason and Retry
- **When** the person presses Retry and the download succeeds
- **Then** the file is shown as downloaded and its reason is gone

#### Scenario: The retry fails again

- **Given** an item showing a failed file
- **When** the person presses Retry and the download fails again
- **Then** the file shows the new reason and still offers Retry

### Requirement: The detail view shows every file an item arrived with

```meta
type: requirement
status: draft
```

The Inbox detail view SHALL show every file an item arrived with: pictures on
this machine as thumbnails that open the file, and every other file as a row
with its size and Open.

#### Scenario: A picture and a document

- **Given** an item whose capture brought a photo and a PDF, both downloaded
- **When** the person opens the item
- **Then** the photo shows as a thumbnail above the body and the PDF as a row with its size and Open

#### Scenario: A picture not on this machine

- **Given** an item with a picture that has not been downloaded
- **When** the person opens the item
- **Then** the picture shows as a row saying it is waiting, not as a thumbnail

### Requirement: An item's kind follows its files

```meta
type: requirement
status: draft
```

The desktop SHALL mark an item as an `image` when its only content is
pictures, and as a `document` when any of its files is not a picture.

#### Scenario: Only a photo

- **Given** a capture with a photo and no text or link
- **When** it lands in the Inbox
- **Then** the item's kind is `image`

#### Scenario: A PDF

- **Given** a capture with a PDF attached
- **When** it lands in the Inbox
- **Then** the item's kind is `document`

### Requirement: Routed tasks carry the item's files

```meta
type: requirement
status: draft
```

Routing an item with attachments to Tasks SHALL give every task it creates the
item's attachment folder as that task's attachment.

#### Scenario: One repository

- **Given** an item with a downloaded file, assigned to one repository
- **When** the person routes it to Tasks
- **Then** the new task's attachment is the item's attachment folder

#### Scenario: No files

- **Given** an item with no attachments
- **When** the person routes it to Tasks
- **Then** the new task has no attachment
