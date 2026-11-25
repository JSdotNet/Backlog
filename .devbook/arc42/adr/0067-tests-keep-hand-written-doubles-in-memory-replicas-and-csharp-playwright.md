# ADR 0067: Tests keep hand-written doubles, in-memory replicas and C# Playwright

```meta
date: 2026-11-25
related: [".devbook/arc42/adr/0005-azure-hosted-task-replica-for-multi-device-sync.md", ".devbook/arc42/adr/0063-data-access-stays-in-infrastructure-adapters-without-ef-core-or-an-outbox.md", ".devbook/arc42/08-crosscutting-concepts.md", ".devbook/tech/testing.md", ".devbook/ai/03-test.md", ".devbook/arc42/09-architecture-decisions.md"]
c4: [backlog]
```

Backlog's tests keep three choices that differ from org ADR 0010:

- hand-written `Stub*`, `Fake*` and `InMemory*` doubles instead of NSubstitute, and no
  Bogus;
- in-memory replicas instead of throwaway containers for integration tests;
- the C# `Microsoft.Playwright` suite under xUnit instead of `@playwright/test`.

## Status

```meta
```

Proposed, 2026-11-25. Plan `app-defaults-rollout`, entry 37 (`backlog-deviation-adrs`).

A **local** decision. It records deliberate differences from
[org ADR 0010: Testing](https://github.com/JSdotNet/app-defaults/blob/main/defaults/arc42/adr/0010-testing.md).
The rest of that ADR applies. Plan entry 34 (`backlog-e2e`) moves the E2E setup onto it:
the suite runs `aspire start` isolated, reads its URLs from Aspire, and checks every
resource's error logs. That entry kept C# Playwright and left the choice to this record.

## Context

```meta
```

| Org ADR 0010 says | Backlog has | Size of a move |
| --- | --- | --- |
| NSubstitute doubles, Bogus builders | About 4,500 references to hand-written doubles across 38 test projects | XL |
| Real dependencies in throwaway containers | In-memory replicas and stores (`InMemoryTaskStore`, the `*SyncDoubles`); the Cosmos emulator only under the AppHost | L |
| A `@playwright/test` suite, which devbook journeys reuse | `tests/Backlog.EndToEndTests`, C# xUnit with `Microsoft.Playwright` | L |

**Doubles.** The doubles encode behaviour: an in-memory store that honours the
store's contract, a clock, a GitHub that answers like GitHub. Many tests depend on
that behaviour, not only on a canned return. NSubstitute would replace a shared
double with per-test setup that repeats the same behaviour in many places.

**Containers.** The only real dependency in the cloud tier is Cosmos. Its Linux
emulator is slow to start, exhausts Docker subnets when instances pile up, and loses
its data overnight. A container per test run would add minutes and a flaky start to
every run of the suite. The local store is a SQLite file, which the adapter tests
already open for real with no container.

**Playwright.** The E2E suite drives the MAUI-hosted Blazor UI through the web
harnesses and the shared C# page helpers (`DesktopHarness.*`), written in the
language the rest of the repository is. A TypeScript suite would duplicate them.
The cost of staying in C#: devbook's scenario tools find only `*.spec.ts`-style files,
so a journey in a Backlog `scenarios.md` has no spec those tools can derive or run. On
2026-10-15 the person chose a C# scenario runner over a TypeScript suite. That runner
is not built.

## Decision

```meta
```

**Keep**, all three:

1. **Doubles stay hand-written.** A new test reuses the module's existing double, or
   adds one beside it. NSubstitute and Bogus are not added.
2. **Integration tests stay on in-memory replicas** and real SQLite files. Cosmos is
   exercised against the emulator only through the AppHost-run E2E suite.
3. **E2E stays C# `Microsoft.Playwright` under xUnit**, started by its own `aspire
   start` (plan entry 34). Devbook journeys get a C# runner, decided in its own record
   when it is built, not a second suite in TypeScript.

## Consequences

```meta
```

- Each double has to keep up with its interface by hand. A compile error points at the
  double when the interface changes.
- A Cosmos behaviour that the in-memory replica gets wrong is caught only by E2E or in
  production. The replica tests have to state the Cosmos behaviour they copy.
- Until the C# scenario runner exists, no devbook journey in this repository has a
  scenario-run result. E2E evidence comes from the xUnit suite.
- An app-defaults review reads this record for the 0010 rows.

## Rejected

```meta
```

- **Move to NSubstitute and Bogus (XL).** Rewriting about 4,500 references buys a
  library, not a defect fixed.
- **Testcontainers for Cosmos (L).** It brings the emulator's start-up time and
  flakiness into every integration run.
- **Port the E2E suite to `@playwright/test` (L).** It would give devbook's scenario
  tools a suite to run. It would also duplicate the C# harness helpers in TypeScript,
  against the person's 2026-10-15 choice.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-25 | Proposed: keep hand-written doubles, in-memory replicas and the C# Playwright suite; devbook journeys wait for a C# runner. | Step 15 of plan `app-defaults-rollout` (PR #1967) listed the org ADR 0010 differences as XL and L with no local decision. |
