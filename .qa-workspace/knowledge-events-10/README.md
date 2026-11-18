# Event tracking, end to end in the desktop web harness

QA evidence for plan `+knowledge-events` entry 10 (`validate-events`), run on 11 November 2026
against `plan/knowledge-events` at `e78972289`, through Aspire (`aspire start --isolated`), in the
desktop web harness, driven by Playwright. The harness used the shared Debug workspace.

| # | Scenario | Result | Evidence |
| --- | --- | --- | --- |
| 1 | Event pages in Inbox › Sources, Run capture now; Techorama delivers nothing; a second run adds nothing | Pass, with a substitution (below) | `02`–`06`, `03b`, `05b` |
| 2 | Route the dotNET Friday item To Event; Spotted, in a dotNET Friday Series | Pass; one display defect | `07`–`09` |
| 3 | Going, a key date, a prep task; on the Calendar with Show events | Pass | `10`, `11` |
| 4 | A Going event whose end has passed; Harvest task under the Brain scope after a restart; Harvested completes it | Pass | `12`–`15` |
| 5 | Flag off: no Events surface, To Event route, Event page source or Show events | Pass | `16`–`18` |

## Scenario 1, the substitution

- `https://techorama.nl/` and `https://techorama.be/` answered "the edition it shows has passed"
  (28 October and 13 May 2026) and delivered nothing.
- `https://dotnetfriday.nl/` hopped to its Eventbrite listing `2003121143262` (HTTP 200 in the
  harness logs) and delivered nothing: the shared workspace already held that edition, archived,
  from entry 6's QA run (`03b-preexisting-entry6-item.json`). That is the "already known" rule
  working, but it hid the first delivery.
- Removing that row from the shared workspace was refused, so a fresh first delivery was proved
  from the repository's own fixtures (`tests/Backlog.Infrastructure.Capture.UnitTests/Fixtures/EventPages`)
  served on `http://127.0.0.1:58731/`, with the Eventbrite link pointed at the local copy and the
  listing number changed to `9003121143262` (`fixture-site/`). It delivered one item whose event
  facts are start `2026-11-27 16:30 +01:00`, end `2026-11-27`, place Nieuwegein, page title
  DotnetFriday (`05b-fresh-item-row.json`). The two runs after it delivered nothing.
- The Series is "DotnetFriday", new, named after the page title, because the watched page was the
  local copy.

## Defect found

The Events list reads the routed edition as `27 Nov 2026 16:30 – 27 Nov 2026`: `EventPresentation.When`
collapses to one day only when start and end are equal as a whole, time included (`09`). Filed in
plan `+knowledge-events`.

## Monitoring

No error in the harness's structured logs. The error traces are the environment's: `sync` answers
401 because the harness's paired device is not in this run's fresh Cosmos emulator, and
`api.github.com` answers 404 for the `acme/*` fixture repositories.
