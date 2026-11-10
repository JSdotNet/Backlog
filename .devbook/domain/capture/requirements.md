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

## Event page monitor

```meta
type: requirements
related: [.devbook/domain/capture/features.md#event-page-monitor, .devbook/domain/capture/domain.md#event-facts]
```

> What the event page source promises. The feature chapter says why it reads pages
> in this order and records its known limits. The examples are the first pages it
> watches: dotNET Friday and Techorama.

### Requirement: The monitor takes the first step that gives a date

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.An_eventbrite_listing_watched_directly_is_read_from_its_own_structured_data, unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.Dotnet_friday_is_read_one_hop_away_on_its_eventbrite_listing, unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.Techorama_nl_is_read_from_the_dates_in_its_image_text]
```

The event page monitor SHALL take an edition's dates and place from the first of these steps that gives a date: a schema.org event on the watched page, a schema.org event one registration link away, a date pattern in the page text.

#### Scenario: Structured data on the page

- **Given** a watched page whose schema.org event starts 2026-11-27T16:30+01:00 in Nieuwegein
- **When** the person presses Run capture now
- **Then** the Inbox gets one item starting 27 November 2026 at 16:30 `+01:00` in Nieuwegein

#### Scenario: One hop to Eventbrite

- **Given** dotnetfriday.nl names 27 November in its text and links to an Eventbrite listing with a schema.org event
- **When** the person presses Run capture now
- **Then** the item takes the listing's start, 16:30 `+01:00`, rather than the page text's date

#### Scenario: A date in the page text

- **Given** techorama.nl has no schema.org event and its text reads `26 - 28 OCT 2026`
- **When** the person presses Run capture now
- **Then** the item starts on 26 October 2026 and ends on 28 October 2026, with no time

### Requirement: Dutch and English month names are read alike

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.A_dutch_page_is_read_like_an_english_one, unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.PageDatesTests.A_date_in_the_text_is_read, unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.PageDatesTests.A_month_is_known_by_its_first_three_letters_in_either_language]
```

The event page monitor SHALL read a date pattern whose month is written in English or in Dutch, in full or abbreviated.

#### Scenario: A Dutch page

- **Given** a watched page whose text reads `11 - 13 mei 2027`
- **When** the person presses Run capture now
- **Then** the item starts on 11 May 2027 and ends on 13 May 2027

### Requirement: A page with no date still arrives

```meta
type: requirement
tests: unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.A_page_with_no_date_arrives_with_its_link_only
```

The event page monitor SHALL deliver an item with the page's title and link and no dates when no step gives a date.

#### Scenario: No date found

- **Given** a watched page with no schema.org event, no registration link and no date in its text
- **When** the person presses Run capture now
- **Then** the Inbox gets one item with the page's link and no dates

### Requirement: A past edition is skipped

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.Techorama_be_still_showing_last_springs_edition_gives_no_entry, unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.Techorama_nl_after_its_edition_gives_no_entry_and_says_why]
```

The event page monitor SHALL deliver no item for an edition whose end date is before today.

#### Scenario: Last year's edition still on the page

- **Given** today is 10 November 2026 and techorama.be still shows `11 - 13 MAY 2026`
- **When** the person presses Run capture now
- **Then** no item is delivered for techorama.be

### Requirement: An edition is delivered once

```meta
type: requirement
tests: [unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventPageAdapterTests.An_edition_keeps_its_id_across_runs_and_next_years_gets_its_own, unit:dotnet:Backlog.Modules.Capture.UnitTests.RunCaptureCommandHandlerTests.Entries_the_receiving_side_already_holds_are_not_counted_as_new]
```

The event page monitor SHALL deliver each edition once, knowing it by its listing id when the registration page has one and otherwise by the page URL, the year and the place.

#### Scenario: A second run

- **Given** the 27 November 2026 dotNET Friday was delivered on an earlier run
- **When** the person presses Run capture now again
- **Then** no new item is delivered for it

#### Scenario: Next year's edition

- **Given** the 2026 edition on techorama.nl was delivered, and the page now shows the 2027 edition
- **When** the person presses Run capture now
- **Then** the Inbox gets one new item, for the 2027 edition

#### Scenario: A moved date

- **Given** an edition delivered on an earlier run whose page now shows a later date
- **When** the person presses Run capture now
- **Then** no new item is delivered, and the item already in the Inbox keeps its first date

### Requirement: The monitor reads pages only when asked

```meta
type: requirement
```

The event page monitor SHALL read its pages only when the person presses Run capture now.

#### Scenario: The app left open

- **Given** the event page source is on with two watched pages
- **When** a day passes with the app open and nobody presses Run capture now
- **Then** neither page is read

### Requirement: The monitor asks no AI model

```meta
type: requirement
```

The event page monitor SHALL read every page by its fixed rules and send no page to an AI model.

#### Scenario: Foundry configured

- **Given** an AI model is configured for the Inbox and the event page source is on
- **When** the person presses Run capture now
- **Then** the run reads its pages without a call to the model

### Requirement: No event page source while the area is off

```meta
type: requirement
tests: [unit:dotnet:Backlog.Modules.Capture.UnitTests.RunCaptureCommandHandlerTests.A_source_that_is_not_offered_is_not_run_and_its_stored_choice_is_kept, unit:dotnet:Backlog.Infrastructure.Capture.UnitTests.EventsAreaSourceAvailabilityTests.The_event_page_is_offered_only_while_the_events_flag_is_on, unit:dotnet:Backlog.Desktop.UI.UnitTests.CaptureSourcesPanelTests.The_event_page_source_is_not_listed_while_it_is_not_offered]
```

The system SHALL neither list nor run the event page source while the `events` flag is off.

#### Scenario: Area off

- **Given** the `events` flag is off and `capture-sources.json` holds an event page source switched on
- **When** the person opens the Sources panel and presses Run capture now
- **Then** the panel lists no event page source, the run reads no event page, and the stored source is kept
