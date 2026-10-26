# Requirements

```meta
status: draft
type: requirements
```

> What the sync service promises the devices that pair with it and sync through
> it, and what each device's sync client promises about the service it talks to.
> The service is the `Backlog.Modules.Sync` module, hosted by
> `Backlog.Modules.Sync.Api`; the client is `Backlog.Infrastructure.Sync`.

## Pairing a device

```meta
type: requirements
status: draft
related: [.devbook/domain/sync/features.md#pairing-a-device]
```

> What the sync service promises about pairing codes, credentials and device
> tokens.

### Requirement: A pairing code works for ten minutes

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_stops_working_after_ten_minutes, unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_still_works_a_minute_before_it_expires]
```

The sync service SHALL refuse a [Pairing Code](domain.md#pairing-code) redeemed ten minutes or more after it was issued.

#### Scenario: A code entered too late

- **Given** a pairing code the first device showed eleven minutes ago
- **When** the person enters it on a second device
- **Then** the second device is not paired

### Requirement: A pairing code pairs one device

```meta
type: requirement
status: draft
tests: [unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.A_code_pairs_one_device_and_no_more, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DevicePairingEndpointTests.The_same_code_cannot_pair_two_devices]
```

The sync service SHALL accept a pairing code once and refuse every later redemption of it.

#### Scenario: A code entered twice

- **Given** a pairing code a second device has already redeemed
- **When** a third device enters the same code
- **Then** the third device is not paired

### Requirement: A refused device is revoked

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.The_device_list_names_the_caller_and_the_device_whose_code_paired_each, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.A_revoked_device_can_no_longer_mint_a_token_and_leaves_the_list, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.A_device_cannot_revoke_itself_or_another_owners_device, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DeviceRosterEndpointTests.Only_the_device_whose_code_paired_another_may_revoke_it]
```

The sync service SHALL list an owner's devices with the device whose code paired each, and SHALL let the device whose code paired another revoke it, and no other device, after which the revoked device's credential mints no token.

#### Scenario: The person refuses a device

- **Given** a phone that redeemed the code the desktop showed
- **When** the person answers Refuse to "Pair *phone*?" on the desktop
- **Then** the phone is no longer in the owner's device list, and its credential is refused at the token exchange

### Requirement: The registration credential is shown once

```meta
type: requirement
status: draft
tests: unit:dotnet:Backlog.Modules.Sync.UnitTests.DevicePairingTests.Only_the_hash_of_the_credential_is_stored
```

The sync service SHALL hand a device its [Registration credential](domain.md#registration-credential) only in the answer to its registration or pairing, and SHALL keep only a hash of it.

#### Scenario: A device registers

- **Given** a device registering with the sync service
- **When** the service answers
- **Then** the answer carries the credential, and the service's store holds only its hash

### Requirement: A device token expires

```meta
type: requirement
status: draft
tests: integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.SyncEndpointAuthenticationTests.An_expired_token_is_refused
```

The sync service SHALL refuse a [Device token](domain.md#device-token) after its lifetime, which is thirty minutes unless the deployment sets it between 15 and 60.

#### Scenario: A token past its lifetime

- **Given** a device token that expired five minutes ago
- **When** the device calls the sync service with it
- **Then** the call is refused as unauthorized

## Syncing between devices

```meta
type: requirements
status: draft
related: [.devbook/domain/sync/features.md#syncing-between-devices]
```

> What the sync service promises about whose data a call reaches, and what a
> device promises about a service newer than its app.

### Requirement: Every sync call is scoped to the token's owner

```meta
type: requirement
status: draft
tests: [integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.TaskSyncEndpointTests.A_separate_owner_pulls_nothing, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.TaskSyncEndpointTests.A_cursor_minted_for_another_owner_is_refused, integration:dotnet:Backlog.Modules.Sync.Api.UnitTests.DevicePairingEndpointTests.A_device_only_ever_sees_its_own_owners_captures]
```

The sync service SHALL answer a call only from the partition of the [Owner](domain.md#owner) its device token names.

#### Scenario: Two unpaired backlogs

- **Given** two devices registered as separate owners, one of which pushed tasks
- **When** the other device pulls
- **Then** it receives none of the first owner's tasks

### Requirement: A device sends nothing to a newer sync service

```meta
type: requirement
status: draft
related: [.devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md]
tests: [unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SyncCompatibilityHandlerTests.A_newer_service_is_sent_nothing_and_the_answer_asks_for_an_update, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SyncCompatibilityRegistrationTests.A_sync_client_sends_nothing_to_a_newer_service, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SyncCompatibilityRegistrationTests.The_token_request_is_held_too, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.TaskSyncWorkerTests.A_service_newer_than_the_app_is_sent_nothing_and_the_progress_stays, unit:dotnet:Backlog.Infrastructure.Sync.UnitTests.SessionSyncWorkerTests.A_service_newer_than_the_app_is_sent_nothing_and_the_progress_stays]
```

A device SHALL send no sync request, its token request included, while the sync service reports a version higher than the device's app version.

#### Scenario: The service was released past the app

- **Given** a desktop at app version 0.1.4 with task changes waiting to push
- **And** a sync service whose root reports version 0.2.0
- **When** the desktop runs a sync cycle
- **Then** it sends no request but the version question
- **And** it says "Update Backlog to keep syncing."
- **And** its push watermark and pull cursor stay where they were

#### Scenario: The service is rolled back

- **Given** a device that stopped syncing for a service at version 0.2.0
- **When** the service is rolled back to 0.1.0 and ten minutes pass
- **Then** the device syncs again without a restart

### Requirement: A phone holds its outbox for a newer sync service

```meta
type: requirement
status: draft
related: [.devbook/domain/sync/features.md#carrying-captures-to-the-inbox, .devbook/arc42/06-runtime-view.md#mobile-capture-and-sync]
tests: [unit:dotnet:Backlog.Mobile.UI.UnitTests.DeviceOutboxTests.Entries_for_a_service_newer_than_the_app_wait_rather_than_being_refused, unit:dotnet:Backlog.Mobile.UI.UnitTests.DeviceOutboxTests.Waiting_entries_go_once_the_service_is_no_newer_than_the_app]
```

The phone SHALL hold every outbox entry in its place, with its attempts, while the sync service reports a version higher than the phone's app version.

#### Scenario: A capture made while the service is newer

- **Given** a phone whose sync service reports a version higher than its app's
- **When** the person makes a capture
- **Then** the capture waits in the outbox, not set aside as refused
- **And** it is sent once the service reports a version no higher than the app's
