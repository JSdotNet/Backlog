# ADR 0048: Meeting audio is recorded as AAC in an ADTS stream

```meta
date: 2026-11-10
related: [".devbook/domain/capture/features.md#meeting-recording", ".devbook/domain/inbox/requirements.md#recordings-on-a-note", ".devbook/arc42/06-runtime-view.md#meeting-recording", ".devbook/arc42/06-runtime-view.md#talk-note-upload", ".devbook/arc42/adr/0014-attachments-travel-through-a-blob-store-beside-the-replica.md", ".devbook/arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md", ".devbook/tech/mobile.md#android-mediarecorder"]
```

The phone records a meeting as AAC in an ADTS stream (`.aac`, `audio/aac`), 16 kHz
mono at 32 kbps, because a recording the app never got to stop stays playable up
to the moment it was ended.

## Status

```meta
```

Proposed, 2026-11-10, by the flow-spec run that drafted it for the plan
`+meeting-recording`. It awaits acceptance at that run's Personal Validation gate.
No code is built yet.

A **local** decision, numbered in the local sequence. Every bare ADR number below
means the local one.

## Context

```meta
```

**A meeting cannot be recorded twice.** The person records an hour on the phone,
often with the screen off. Android may end the app to free memory, or a vendor
battery saver may end it, and nothing then stops the recording properly. What
is left in the file at that moment is all the person has.

**Every file rides the note's upload.** A recording is a note file, uploaded
through the staged path of ADR 0014 within its per-file limit of 25 MB. The
format decides how long one file runs.

**The minimum Android version is 24.** The MAUI head targets API 24 and later, so
an encoder newer than that cannot be the only one.

## Decision

```meta
```

1. **The container is ADTS.** Every AAC frame carries its own header, so the file
   is a plain stream with no index written at the end. A file cut off mid-write
   plays up to its last whole frame.
2. **16 kHz mono at 32 kbps.** That is plenty for speech. An hour is about 15 MB,
   frame headers included, so one file holds about 1 h 45 min before the
   per-file limit.
3. **Parts at 24 MB.** A recording that reaches 24 MB closes and goes on in
   "Recording · part 2", then part 3 and on, with no gap, so a part never meets
   the limit.
4. **Android `MediaRecorder` writes it,** with output format `AAC_ADTS` and
   encoder `AAC`, inside a foreground service of type microphone
   (`.devbook/tech/mobile.md#android-mediarecorder`).
5. **The note accepts `audio/aac`, and `audio/mp4` and `audio/x-m4a` beside it**
   for an `.m4a` file the person picks, since Android reports either; the sync
   service's allowed types grow by all three, as `SyncTranscriptionLimits`
   already does for dictation.

## Consequences

```meta
```

- A killed recording is attached on the next start as "Recording (interrupted)"
  and plays up to the kill, with no repair step.
- The desktop's WebView2 and the Android WebView both play ADTS AAC, so the
  players need no conversion.
- A transcription service that does not take ADTS needs the stream wrapped into
  M4A first, without re-encoding. Which service is a later decision.
- The sync service must take the audio types before a phone build that records
  ships, or every recording is set aside on a 415.
- A cloud dictation turn keeps recording M4A at 64 kbps (ADR 0045): a turn lasts
  five minutes at most, and a file a killed turn leaves is swept, not kept.

## Rejected

```meta
```

- **AAC in MP4 (`.m4a`).** Supported everywhere and widely accepted by
  transcription services, but MP4 writes its index only when the recording stops
  properly. A killed recording leaves an unplayable file.
- **Opus in Ogg (`.ogg`).** Survives a kill up to its last page and is widely
  accepted, but `MediaRecorder` encodes it only from Android 10 (API 29), above
  the minimum of 24.
- **WAV / PCM through `AudioRecord`.** Survives a kill whole, but runs to about
  115 MB an hour: four files or more for a meeting, and far more upload over
  mobile data.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-10 | Proposed: meeting audio is AAC in an ADTS stream, 16 kHz mono, 32 kbps, in 24 MB parts, written by `MediaRecorder` in a microphone foreground service. | The Notes tab gains a meeting recorder, and a killed recording must stay playable. |
