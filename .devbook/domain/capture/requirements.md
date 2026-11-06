# Requirements

```meta
type: requirements
related: [.devbook/domain/capture/features.md]
```

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## One-tap entry

```meta
type: requirements
related: [.devbook/domain/capture/features.md#one-tap-entry]
```

> The requirements of the phone's capture sheet. The feature chapter says what
> the sheet offers and why; this says what it promises. Every scenario opens
> the sheet from Today, because a save from Focus returns to Focus.

### Requirement: The capture sheet files the thought where it was told

```meta
type: requirement
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

## Speech-to-text capture

```meta
type: requirements
related: [.devbook/domain/capture/features.md#speech-to-text-capture, .devbook/arc42/adr/0045-the-sync-service-relays-dictation-audio-to-foundry-and-keeps-none-of-it.md]
```

> The requirements of dictation on the phone and its two engines, On-device and
> Cloud (local ADR 0045). The feature chapter says what the engines are and why
> Cloud is opt-in; this says what dictation promises. Unit tests prove these
> rules, because no journey walks dictation yet.

### Requirement: Dictation stays on the device until the person chooses Cloud

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.DictationEngineTests.A_phone_that_was_never_asked_dictates_on_the_device, unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.On_device_is_the_engine_until_the_person_chooses_the_cloud, unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.A_change_of_engine_applies_from_the_next_turn, unit:dotnet:Backlog.Mobile.UI.UnitTests.SettingsSheetTests.The_dictation_group_offers_the_engine_on_the_device_until_the_person_chooses_the_cloud, unit:dotnet:Backlog.Mobile.UI.UnitTests.SettingsSheetTests.Choosing_the_cloud_engine_is_kept_in_the_phone_settings]
```

The phone SHALL take every dictation turn on the device until the person chooses Cloud under Settings › Dictation › Engine.

#### Scenario: Never chosen

- **Given** a phone whose Dictation engine was never chosen
- **When** the person opens Settings
- **Then** the Engine reads On-device
- **And** the next dictation turn listens on the device

#### Scenario: Cloud chosen

- **Given** the Engine reads On-device
- **When** the person chooses Cloud and presses the mic
- **Then** that turn records for the cloud
- **And** the Engine still reads Cloud when Settings opens again

### Requirement: Audio leaves the phone only on a turn the person starts with Cloud chosen

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.On_device_is_the_engine_until_the_person_chooses_the_cloud, unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.The_cloud_takes_the_turn_when_chosen_and_the_phone_is_online, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.A_stopped_recording_is_posted_to_the_sync_service_and_its_text_heard, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.A_failed_recording_is_a_failed_turn_with_nothing_posted, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.An_empty_recording_is_nothing_heard_with_nothing_posted]
```

The phone SHALL send a recording to the sync service only for a dictation turn the person started with Cloud chosen, and only after the recording stops.

#### Scenario: On-device turn

- **Given** the Engine reads On-device
- **When** the person dictates a turn
- **Then** nothing is sent to the sync service

#### Scenario: Cloud turn

- **Given** the Engine reads Cloud and the phone is online
- **When** the person dictates and stops
- **Then** the phone sends that one recording
- **And** the text that comes back is added to the field

#### Scenario: Nothing recorded

- **Given** the Engine reads Cloud
- **When** a turn ends with an empty or failed recording
- **Then** nothing is sent to the sync service

### Requirement: A phone that cannot reach the cloud dictates on the device without a word

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.An_offline_or_unpaired_phone_listens_on_the_device_without_a_word, unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.A_phone_that_cannot_record_dictates_on_the_device_even_with_the_cloud_chosen]
```

With Cloud chosen, a phone that is offline, unpaired, or unable to record SHALL take the turn on the device at once, with no notice.

#### Scenario: Offline

- **Given** the Engine reads Cloud and the phone is offline
- **When** the person presses the mic
- **Then** the turn listens on the device
- **And** no notice is shown

#### Scenario: Cannot record

- **Given** the Engine reads Cloud on a phone that cannot record
- **When** the person presses the mic
- **Then** the turn listens on the device

### Requirement: A cloud that cannot transcribe here hands the rest of the app run to the device

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.A_service_without_the_route_makes_cloud_dictation_unavailable, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.A_service_with_no_model_configured_makes_cloud_dictation_unavailable, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.An_unreachable_service_makes_cloud_dictation_unavailable, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.No_answer_within_sixty_seconds_makes_cloud_dictation_unavailable, unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.A_cloud_that_cannot_transcribe_here_says_so_once_and_the_device_takes_every_later_turn, unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.An_unreachable_cloud_falls_back_to_the_device_for_later_turns, unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.The_fallback_holds_for_every_screen_of_the_app_run_and_not_past_it]
```

When the sync service lacks the route, has no model configured, cannot be reached, or does not answer within 60 seconds, the phone SHALL end the turn with "Cloud dictation is unavailable here; using on-device." and take every later turn of that app run on the device.

#### Scenario: Service cannot transcribe

- **Given** the Engine reads Cloud and the sync service has no model configured
- **When** the person dictates and stops
- **Then** the turn reads "Cloud dictation is unavailable here; using on-device."
- **And** every later turn, on any screen, listens on the device

#### Scenario: Next start

- **Given** cloud dictation fell back to the device in an earlier app run
- **When** the app starts again and the person dictates
- **Then** the turn records for the cloud again

### Requirement: A failed transcription is a failed turn, not a fallback

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.SelectingSpeechTranscriberTests.A_failed_transcription_is_no_fallback_and_the_next_turn_tries_the_cloud_again, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.A_refused_upload_is_a_failed_turn_and_the_cloud_stays_in_use]
```

When the model fails to transcribe a recording, the phone SHALL end the turn with "Transcription failed, try again." and give the next turn to the cloud again.

#### Scenario: Model fails

- **Given** the Engine reads Cloud
- **When** the person dictates and the model fails the recording
- **Then** the turn reads "Transcription failed, try again."
- **And** the next turn records for the cloud again

### Requirement: A cloud turn records for five minutes at most

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.A_recording_is_stopped_at_five_minutes]
```

A cloud turn SHALL stop recording after five minutes and send what it recorded.

#### Scenario: Long dictation

- **Given** the Engine reads Cloud
- **When** the person dictates for five minutes without stopping
- **Then** the recording stops and is sent

### Requirement: A cloud turn names the language only when one is chosen

```meta
type: requirement
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.DictationEngineTests.A_cloud_turn_hints_only_a_single_language, unit:dotnet:Backlog.Mobile.UI.UnitTests.CloudSpeechTranscriberTests.The_upload_hints_the_dictation_language]
```

A cloud turn SHALL tell the model `nl` when the Dictation language is Dutch, `en` when it is English, and no language for Dutch and English or the phone's language.

#### Scenario: One language

- **Given** the Engine reads Cloud and the Dictation language is Dutch
- **When** the person dictates a turn
- **Then** the recording is sent with the language `nl`

#### Scenario: Two languages

- **Given** the Engine reads Cloud and the Dictation language is Dutch and English
- **When** the person dictates a turn
- **Then** the recording is sent with no language, and the model detects it
