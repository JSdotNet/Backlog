# Requirements

```meta
status: draft
type: requirements
```

> What the sync service promises the devices that pair with it and sync through
> it. The service is the `Backlog.Modules.Sync` module, hosted by
> `Backlog.Modules.Sync.Api`.

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

> What the sync service promises about whose data a call reaches.

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
