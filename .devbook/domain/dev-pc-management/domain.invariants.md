# Invariants

```meta
type: invariants
```

What each aggregate on [`domain.md`](domain.md) enforces, one chapter per aggregate: each rule, where it is enforced, and the unit test that proves it.

## Machine Registry

```meta
type: invariants
related: [.devbook/domain/dev-pc-management/domain.md#machine-registry, .devbook/domain/dev-pc-management/domain.md#machine]
```

> The rules the Machine Registry holds its machines and their [updates](domain.md#pending-update) to.

### Invariant: A machine is known by its device identity

```meta
type: invariant
```

A machine's `MachineId` is the device identity Sync pairs on, never its name. The
registry lists a machine once per id, this device first, and passes over a
record whose environment id is not a device identity.

Enforced at: `SessionMachineRegistry.GetMachinesAsync()`, proved by `SessionMachineRegistryTests`

### Invariant: Per-PC choices follow the machine

```meta
type: invariant
```

The tools pane keeps a machine's choices and last run under its `MachineId`, so a
rename keeps them. A folder named after the machine is copied under the id once,
when none exists there yet. The copy never overwrites the id's folder and
leaves the name's folder for older builds. A copy the disk refuses reads and
writes the name's folder instead, so no choice is lost.

Enforced at: `DevToolConfiguration.AdoptLegacyPcFolder()`, proved by `DevToolMachineFolderTests`

### Invariant: All machine changes go through the registry

```meta
type: invariant
```

A registered machine changes only through the Machine Registry.

Enforced at: open

### Invariant: Updates go only to registered machines

```meta
type: invariant
```

A machine receives tool updates only once it is registered.

Enforced at: open

### Invariant: Connections go only to registered machines

```meta
type: invariant
```

A remote connection is made only to a registered machine.

Enforced at: open

### Invariant: Coming online needs a heartbeat

```meta
type: invariant
```

A machine moves from `offline` to `online` only on an explicit heartbeat ([Machine status lifecycle](domain.md#machine-status)).

Enforced at: open

### Invariant: Pending updates are bounded

```meta
type: invariant
```

A machine's `pending_updates` holds no more than a fixed number of updates.

Enforced at: open

### Invariant: A finished update moves to history

```meta
type: invariant
```

A completed or failed update leaves `pending_updates` and joins `updates_history` as an [Update Record](domain.md#update-record).

Enforced at: open
