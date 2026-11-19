# ADR 0049: The desktop transcribes a meeting recording through Foundry, appends the transcript to the note, then removes the audio

```meta
date: 2026-11-18
related: [".devbook/arc42/adr/0048-meeting-audio-is-recorded-as-aac-in-an-adts-stream.md", ".devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md", ".devbook/arc42/adr/0023-inbox-items-may-be-read-by-the-foundry-model.md", ".devbook/arc42/adr/0025-notes-ride-the-task-feed.md", ".devbook/arc42/adr/0030-stored-credentials-go-through-the-os-protected-envelope.md", ".devbook/arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md", ".devbook/arc42/06-runtime-view.md#mobile-note-sync", ".devbook/arc42/adr/guidelines/0015-resilience-for-outbound-dependencies.md", ".devbook/tech/cloud.md#azure-ai-foundry", ".devbook/tech/cloud.md#gpt-4o-transcribe"]
```

When a meeting recording the phone made has downloaded to the desktop with its
note, the desktop sends it to the `gpt-4o-transcribe` deployment on the existing
Foundry account, once per file, keyed by the file's sha256, and tries a failing
file at most three times. It appends `## Transcript` and `## Summary` to the note
body, and then removes the audio file from the note, which removes it from the
blob store and the phone. Nobody presses a button: the download is the trigger.
An audio file a person picks onto a note is never sent.

## Status

```meta
```

Accepted, 2026-11-17, when the plan `+meeting-recording` merged into `main`
(PR #1715). Built in PR #1806: `TranscribeRecordingsCommand` and
`InboxRecordingTranscriptionWorker` in the Inbox module, `FileInboxTranscriptionRecords`
for the kept sha256s, `AdtsRemuxer`, `AzureFoundryInboxRecordingTranscriber` and the
desktop registration of `AzureFoundryTranscriptionClient`, the release in the sync
service's `PushTasksCommand`, and a transcription stand-in in
`Backlog.AzureFoundry.TestService`. Amended on 2026-11-18, as **History** says;
the amendment is still to be built.

A **local** decision, numbered in the local sequence. Every bare ADR number below
means the local one; an inherited one is named "guideline". It amends ADR 0023 and
ADR 0014, as **Consequences** says.

## Context

```meta
```

**A meeting recording will be a file on a note.** The phone records a meeting and
adds it to a note as one more file, as AAC in ADTS (`audio/aac`, `.aac`), 16 kHz
mono at 32 kbps, in parts of 24 MB (ADR 0048). ADR 0048 leaves the transcription
service, and the remux it may need, to this record. The file travels the way a photo does: uploaded to the blob store before
the note that names it (ADR 0014), pulled by the desktop with the note (ADR 0025,
`06-runtime-view.md#mobile-note-sync`). The person wants a transcript and a
summary, not an hour of audio to listen back to.

**The desktop already talks to Foundry.** `Backlog.Infrastructure.AzureFoundry`
holds the desktop's Foundry endpoint, its chat deployment and its DPAPI-sealed key
(`AzureFoundrySettings`, ADR 0030). The same project holds
`AzureFoundryTranscriptionClient`, which the sync service uses for phone
dictation (ADR 0045), with a managed identity and a 50-second budget sized for a
dictation turn. The `backlog-foundry` account, in resource group `JS-AI` in the
Sponsorship subscription, already provisions `gpt-4o-transcribe` behind
`includeSpeechModel`. The phone cannot do this work: its recogniser handles a
dictation turn, not an hour of audio.

**Notes merge whole, by the later `UpdatedAt`.** A note's body travels whole, and
the copy with the later stamp replaces the other (ADR 0025). A transcript written
over the body the desktop read an hour ago would undo an edit the person made
since on the phone. A phone edit built on the body before the transcript would
undo the transcript.

**What Microsoft documents**, read on 2026-11-10:

| Fact | Source | What it decides here |
| --- | --- | --- |
| The transcription model accepts mp3, mp4, mpeg, mpga, m4a, wav and webm. "Other formats return an error." AAC in ADTS is not on the list. | [Quickstart: speech to text with Azure OpenAI transcription models](https://learn.microsoft.com/azure/foundry/openai/whisper-quickstart), *Troubleshooting* | The desktop remuxes ADTS to M4A before sending (**Decision §3**). |
| `gpt-4o-transcribe` takes at most 25 MB per request. | [Foundry Models sold by Azure](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure#audio-models), *Audio models* | One recording file fits: the phone rolls over at 24 MB. |
| Synchronous transcription "is limited to less than 15 to 30 minutes of audio depending on audio compression". | [Transparency note for Azure OpenAI](https://learn.microsoft.com/azure/foundry/responsible-ai/openai/transparency-note#capabilities), *Audio / speech models* | Size is not the binding limit; length is. The desktop sends a recording in parts (**Decision §3**). |
| For `gpt-4o-transcribe` "the only supported format is `json`": no `verbose_json`, so no segment or word timestamps. | [REST API v1 preview reference](https://learn.microsoft.com/azure/foundry/openai/reference-preview-latest#components), `AudioResponseFormat` | The model returns no timestamps. The desktop stamps each part with its own offset (**Decision §4**). |
| `language` is optional. `prompt` may "continue a previous audio segment". | The same reference, *Create transcription* | No language is sent. Each part's prompt is the tail of the part before it. |
| `gpt-4o-transcribe-diarize` is a separate model that labels speakers. | [Foundry Models sold by Azure](https://learn.microsoft.com/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure#audio-models) | Weighed under **Rejected**. |

ADR 0045's **Context** records what Microsoft may keep: the models are stateless
and inputs are not used for training, but abuse monitoring may store a flagged
request for human review, in the Foundry resource's geography.

## Decision

```meta
```

A recording on the desktop, from its download to its removal:

```mermaid
stateDiagram-v2
    [*] --> Downloaded: file pulled, sha256 matches
    Downloaded --> [*]: not made by the recorder, or sha256 already transcribed
    Downloaded --> Waiting: Foundry not configured
    Waiting --> Downloaded: next pull of the note or next start
    Downloaded --> Transcribing: new sha256, fewer than 3 failed attempts
    Transcribing --> Transcribing: next part of at most 5 minutes
    Transcribing --> Summarizing: every part answered
    Transcribing --> Downloaded: a part failed, attempt counted
    Summarizing --> Downloaded: summary or save failed, attempt counted
    Downloaded --> Failed: 3 failed attempts kept
    Failed --> Downloaded: a person presses Retry
    Summarizing --> Written: note re-read, sections appended, sha256 and text kept
    Written --> [*]: file removed from the note, note pushed
```

### 1. When it runs

```meta
```

- **On download.** The desktop transcribes a recording once its file has fully
  downloaded and its sha256 matches the one the note names. No setting turns it
  on and no button starts it.
- **Once per file.** The desktop keeps the sha256 of every file it has
  transcribed, with the sections it wrote, in its local store. A file whose sha256
  is there is never sent again, whether it comes back through a re-sync, a second
  note or a restored backup.
- **One file at a time**, in the background. A note pull that brings a recording
  down queues its note, and on start every open note with a recording is queued
  once, which is the retry. A rolled-over recording's files are transcribed as
  separate files, in the note's order, each appending its own sections; a file
  that cannot be written yet holds back the files after it.
- **Only recordings the app made.** The desktop knows one by the name the
  recorder gives it: exactly "Recording (interrupted)", or starting with
  "Recording · ". It must also have a recording type ADR 0048 lets a note carry
  (`audio/aac`, `audio/mp4`, `audio/x-m4a`). An audio file a person picks keeps
  its own file name, so it is neither sent nor removed and stays on the note as
  any file does. A photo or a document is not sent either.
- **At most three attempts per file.** Beside the done records, the desktop
  keeps a failure record per sha256: the attempt count, the last reason, and
  when. A refused part, a failed summary, an unreadable file or a failed save
  adds an attempt. After the third, the file is not sent again on a pull or a
  start until a person presses Retry (§6). A success removes the record.
- **Only with Foundry configured**, meaning `AzureFoundrySettings.IsConfigured`:
  endpoint, chat deployment and key all set, since the summary needs the chat
  deployment. Otherwise nothing is sent, the recording stays on the note, and no
  attempt is counted. The transcription deployment always has a default and
  cannot be checked offline, so a wrong one shows only as failures (§2).

### 2. The client and the deployment

```meta
```

The desktop reuses `AzureFoundryTranscriptionClient`, against the existing Foundry
endpoint with the desktop's existing key:

- **A key credential.** The client gains an API-key credential beside
  `ManagedIdentity` and `None`, filled from the DPAPI-sealed key in
  `AzureFoundrySettings` (ADR 0030).
- **Options from the desktop's settings.** On the desktop the client's options
  are read from `AzureFoundrySettings` on every call, so a change in Settings
  needs no restart. ADR 0045's rule that the sync service
  never reads `AzureFoundrySettingsStore` still holds: the sync host keeps binding
  from its own `Modules:Sync:Transcription` settings.
- **Its own budget.** Each part gets a budget of its own, 5 minutes rather than
  ADR 0045's 50 seconds, because nobody is waiting on a phone. Per guideline
  0015, each call keeps one retry on a `429` or a `5xx`.

It names a deployment of its own, because the chat deployment cannot transcribe:

| Setting on `AzureFoundrySettings` | Default |
| --- | --- |
| `TranscriptionDeployment` | `gpt-4o-transcribe` |
| `TranscriptionApiVersion` | `2025-03-01-preview`, as ADR 0045 |

The deployment is the one `infra/foundry/main.bicep` already declares on the
`backlog-foundry` account. No Azure resource is added. Settings › AI providers
shows `TranscriptionDeployment` as the *Transcription deployment* field; the
api-version has no field. Under that field, the Azure Foundry card shows the last
transcription failure, with its reason and when. A deployment Foundry does not
know is then seen where it is fixed, and the three-attempt bound (§1) stops the
calls meanwhile.

### 3. The format

```meta
```

The model does not accept AAC in ADTS (see **Context**), so the desktop sends M4A,
as `audio/mp4` with a `.m4a` file name. A recording that is already MP4 or M4A
goes through the same cut, because it may run past the synchronous limit: the
browser harness records M4A in one file, with no rollover.

- **Remux, never re-encode.** The desktop lifts the AAC frames out of the
  recording and writes them into a new MP4 container as they are. It reads them
  from the ADTS stream, or from an MP4's sample table or its movie fragments.
  The audio is the same bytes; only the framing changes. A cut-off recording
  still remuxes, up to its last whole frame.
- **In parts of at most 5 minutes.** The synchronous limit is a length, 15 to 30
  minutes depending on compression, not the 25 MB a 32 kbps recording never
  reaches. The desktop cuts on a frame boundary at or before each 5-minute mark
  and remuxes each part on its own, for both formats alike. An AAC frame stands
  alone, so a cut needs no decoding.
- **Each part carries the last words of the one before** as its `prompt`, up to
  200 characters cut on a word boundary, so a sentence cut at a boundary reads on.
- **The remuxed parts are temporary files** in the desktop's own storage, deleted
  when the call returns, whatever it answers.

### 4. The transcript

```meta
```

- **No language setting.** The desktop sends no `language`, and the model detects
  the language itself, per part.
- **Timestamps per part.** `gpt-4o-transcribe` answers `json` only, with no
  segments. Each part's text is headed by its start offset in its file,
  `[00:04:59]`, computed from the frames before the cut, so the transcript reads
  in steps of about five minutes. A rolled-over file's offsets start again at
  zero, under its own part heading (§5).
- **No speaker labels.** The transcript is the spoken text and nothing else.

### 5. The write-back

```meta
```

The desktop appends, and never replaces:

1. It **re-reads the note** from its store after the transcription returns, not
   the copy it read before sending.
2. It **appends** `## Transcript`, with the parts in order, and `## Summary` to the
   end of that body. The sections are numbered after the transcripts the body
   already holds, so a rolled-over recording's second file appends under
   `## Transcript (part 2)` and `## Summary (part 2)`.
3. The **summary** comes from the desktop's existing chat deployment, given the
   transcript text alone. In the meeting's language, it says in two to four
   sentences what the meeting was about, then lists the decisions and, as
   `- [ ]` lines, the action items, which the person can turn into tasks.
4. The edit is stamped after the copy it replaces, as every local note edit is
   (`06-runtime-view.md#mobile-note-sync`), and pushes as a note document.

An edit the person made on the phone while the transcription ran is in the
re-read body when it arrived first, and is kept.

### 6. Removing the audio

```meta
```

- **After the transcript is written.** In the same local change that appends the
  sections, the desktop removes the recording from the note's files. The pushed
  note no longer names it, and the sync service releases its blob. The phone holds
  no copy by then: it deleted its own once the upload was delivered.
- **Never on failure.** A failed or partial transcription leaves the body and the
  file as they were, and the sha256 is not marked done. The failure adds an
  attempt (§1), the desktop tries again on the note's next pull or its next
  start, and the person can still play the recording.
- **Three failures wait for a person.** After the third attempt, the note's
  recording row on the desktop says that transcription failed, with the last
  reason, and offers Retry. Retry clears the failure record, so the next take-up
  of the note sends the file again.
- **A phone edit that undoes it is repaired.** A phone edit made on the body before
  the transcript arrives later, replaces the whole note, and drops the sections
  and names the file again (ADR 0025). A pulled note that names a recording is
  queued; for each named file the desktop has a sha256 record for, it appends the
  kept sections again where the body lacks them, and removes the file again. The
  audio is gone by then; the kept text is what makes the transcript survive.
- **The transcript is the record.** Once the audio is gone there is nothing to
  transcribe again or listen back to.

## Consequences

```meta
```

**Meeting audio leaves the device for Azure.** This is why the decision is an ADR.

- Every recording the app made on a synced note is sent to Microsoft, in the
  `backlog-foundry` account's geography, and may be processed elsewhere under
  `GlobalStandard`. A
  flagged request may be kept for abuse review (ADR 0045, **Context**).
- **Nobody opts in per recording.** Configuring Foundry on the desktop is the
  only consent; the transcription starts without a click. The people in the
  meeting are not asked by Backlog; the phone's recording reminder is where that
  belongs.
- **The transcript is text in the note**, so it syncs, is backed up (ADR 0010) and
  may be read by the Foundry chat model (ADR 0023), as every note body is.

**How it meets the other records:**

- **ADR 0023 is amended in three places.** It sends only on the reader's press,
  sends no attachment, and sends nothing on intake or in the background. A
  recording is now an attachment sent on intake, in the background, with no
  press, and its transcript is sent to the chat deployment for the summary.
  Everything else 0023 says holds, for the Inbox's triage calls.
- **ADR 0014 is amended.** Today only a tombstone releases blobs. A note edit
  that drops a file must now release that file's blob too, read from the copy the
  service already holds, so a pushed note cannot release a blob it never named.
  The 30-day lifecycle rule stays the backstop.
- **ADR 0045 is beside it, not changed.** The sync service relays phone dictation
  with its managed identity. The desktop reuses the same client and deployment
  with its own key. Neither caller goes through the other.
- **Cost.** Each recording is billed by audio length on the Foundry account, so
  the Dashboard's Foundry cost, read from that account, includes it.

Positive:

- A meeting becomes searchable text with its action items, with no step for the
  person.
- The blob store and the phone stop holding meeting audio once it is transcribed.
- An audio file a person picks onto a note is neither sent to Azure nor removed.
- A file Foundry keeps refusing costs three calls, not one on every pull and
  start.

Negative, and accepted:

- **The audio is gone after the transcript.** A transcription error cannot be
  checked against the recording.
- **Timestamps are coarse**, one per part of about five minutes, because the model
  returns none of its own.
- **A desktop that is off delays it.** Nothing transcribes until a desktop with
  Foundry configured pulls the note.
- **The repair is the desktop's.** Between a phone edit that drops the sections and
  the desktop's next pull, the phone shows the note without its transcript, and
  names a file whose blob is gone. A second desktop that never wrote the sections
  holds no record and cannot repair them.
- **The name decides what is transcribed.** A picked file named like a
  recording is transcribed and removed. A recorder that names its files another
  way is never transcribed. A picked audio file the person wanted as text is not
  transcribed either.
- **A recording can stop being tried.** After three failures it waits for a
  person's Retry, even once the fault is fixed in Settings.

**What the implementation built**, in PR #1806:

- on `AzureFoundryTranscriptionClient`, the API-key credential and
  `AddAzureFoundryDesktopTranscriptionClient`, reading the two new settings from
  `AzureFoundrySettings` with the 5-minute part budget;
- `AdtsRemuxer`, the ADTS-to-M4A remuxer and 5-minute cutter, with no codec, and
  `AzureFoundryInboxRecordingTranscriber`, which sends the parts with their prompts
  and asks the chat deployment for the summary;
- `FileInboxTranscriptionRecords`, the kept sha256s with the sections written, as
  `transcriptions.json` in the workspace's inbox folder, and
  `InboxRecordingTranscriptionWorker`, the background step;
- `TranscribeRecordingsCommand`, the re-read-and-append write-back with the file
  removed in the same save, and the repair;
- in the sync service's `PushTasksCommand`, releasing the blob of a file a note
  edit drops;
- a transcription stand-in for the desktop harness in
  `Backlog.AzureFoundry.TestService`.

The phone needed no change: it keeps no copy of a delivered file.

The 2026-11-18 amendment is still to be built: the name check, the MP4 cut, the
failure record with its Retry, and the last failure under the Settings field.

## Rejected

```meta
```

- **Send ADTS as it is.** Not on the documented format list, and "other formats
  return an error".
- **Re-encode to another codec.** Loses quality and needs a codec on the desktop,
  for what a remux does with the same bytes.
- **Send the whole recording in one request.** A 47-minute recording fits the
  25 MB cap but not the 15-to-30-minute synchronous limit.
- **`gpt-4o-transcribe-diarize`, for speaker labels.** Speaker labels are out of
  scope for the first version. It stays a later change of deployment.
- **Azure AI Speech batch transcription.** Built for long audio, with speaker
  labels and timestamps, but a new resource, a new SDK, and a file the service
  must read from a URL, so the blob store would hand Azure Speech a SAS URL that
  ADR 0014 rejects.
- **Transcribe in the sync service, as ADR 0045 does for dictation.** The service
  would hold the audio for as long as an hour of transcription takes and run the
  summary, beyond the thin cloud that 0045 allows for one short dictation turn.
- **Transcribe on the phone.** Its recogniser handles dictation, not an hour of
  audio, and the phone holds no Foundry key.
- **Keep the audio after the transcript.** The blob store and the phone would keep
  sensitive audio that nothing reads any more.
- **Keep the audio until the phone has the transcript.** The desktop cannot see
  what the phone holds; the kept text repairs the same race without the audio.
- **A language setting.** The model detects the language, and a meeting may mix
  Dutch and English.
- **Transcribe all audio on a note, as before.** A file the person picked would
  be sent to Azure without a press, then removed from the note they put it on.
- **Transcribe picked audio but keep it.** Still sends audio to Azure that the
  person never recorded as a meeting, with no press.
- **A recording flag on the file in the sync contract.** It would change the
  phone's, the sync service's and the desktop's contracts, for what the name the
  recorder already gives says.
- **Refuse an M4A over 15 minutes.** The browser harness records one M4A with no
  rollover, so a longer meeting there would never be transcribed.
- **Stop after the first failure.** A dropped connection, or a `5xx` that
  outlasts the one retry, would need a person's press for every file.
- **Retry without limit.** A file Foundry refuses, or a deployment it does not
  know, is sent again on every pull and every start, for ever.
- **Probe the transcription deployment on save.** A billed call on every save of
  Settings, for what the last failure under the field already shows.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-18 | Only a recording the app made, known by the recorder's name, is transcribed; a picked audio file stays on the note, unsent. An MP4 or M4A recording is cut into 5-minute parts like ADTS. A failing file is tried at most three times, then the note's row shows the failure and a Retry; Settings shows the last failure under *Transcription deployment*. | Plan review of `+meeting-recording` (Backlog entry 26c90ed9) found picked audio transcribed and removed, long M4A sent whole, and failures retried without limit. |
| 2026-11-17 | Accepted and built in PR #1806. Details corrected to the build: a failed or unconfigured run is retried on the note's next pull or the next start, not on configuring Foundry; an M4A file is sent whole; the part budget is 5 minutes and the prompt is the last 200 characters; the summary opens with two to four sentences; the repair runs when a pulled note names a transcribed file; the phone holds no copy to drop. | Plan `+meeting-recording` merged into `main` in PR #1715. |
| 2026-11-10 | Proposed: the desktop transcribes a downloaded meeting recording through `gpt-4o-transcribe` once per sha256, in remuxed M4A parts of at most 5 minutes, appends `## Transcript` and `## Summary`, then removes the audio and repairs a phone edit that drops the sections. | Plan entry `spec-transcription` of plan `meeting-recording`. |
