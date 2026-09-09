# Combined deployment

`Deploy All` (`.github/workflows/deploy-all.yml`) takes the Azure AI Foundry models, the
cloud sync tier and the desktop release out in one run.

It contains no deployment logic of its own. Each component is the workflow that already
deployed it, called as a [reusable workflow][reusable]:

| Component | Called workflow | Documented in |
| --- | --- | --- |
| Foundry | `deploy-foundry.yml` | [`foundry.md`](foundry.md) |
| Sync | `deploy-sync.yml` | [`sync.md`](sync.md) |
| Desktop | `release-desktop.yml` | — |

So a component behaves identically whether it runs on its own or from here, and changing
how sync deploys still means editing `deploy-sync.yml` and nothing else. Every workflow
keeps the triggers it already had: a push to `main` that touches the sync paths still
deploys sync directly, and the nightly desktop release still runs on its schedule.

[reusable]: https://docs.github.com/actions/using-workflows/reusing-workflows

## Mobile is excluded, on purpose

`release-mobile.yml` is not called from here, and keeps its own triggers. This is a
deliberate scope decision, not an oversight: the Android head is held out until it is
trusted enough that a mobile failure should be allowed to redden a run that also deploys
Foundry and sync.

Adding it later is a `mobile` entry in the `components` input, a `workflow_call` trigger on
`release-mobile.yml`, and a job here — the same three pieces every other component has.

## Running it

**Actions -> Deploy All -> Run workflow.**

| Input | Default | Notes |
| --- | --- | --- |
| `mode` | `what-if` | How far to take the infrastructure. See the table below. |
| `components` | `foundry+sync` | Which components take part. `desktop` requires `mode: deploy`. |
| `foundry_environment` | `backlog-ai` | Selects the GitHub environment and `infra/foundry/<name>.bicepparam`. |
| `sync_environment` | `backlog-sync` | Selects the GitHub environment and names the azd environment. |
| `desktop_version` | blank | Release version without the leading `v`. **Required** when `components` includes `desktop` — see below. |
| `include_embedding_model` | `from-parameter-file` | Foundry: `text-embedding-3-small`, which backs knowledge search by meaning. |
| `include_speech_model` | `from-parameter-file` | Foundry: `gpt-4o-transcribe`. |
| `include_balanced_model` | `from-parameter-file` | Foundry: the optional `gpt-5.6-sol`. |

### What each mode does

The two infrastructure workflows spell their modes differently, so `Deploy All` translates
one choice into both:

| `mode` | Foundry | Sync | Desktop |
| --- | --- | --- | --- |
| `what-if` | `what-if` — validates and previews, changes nothing | `preview` — `azd provision --preview` | dropped |
| `provision` | `deploy` — creating the account *is* provisioning it | `provision` — infrastructure only, no service push | dropped |
| `deploy` | `deploy` | `deploy` — provision, then push the service | publishes the MSIX release |

Publishing a release has no dry run, so `desktop` takes part only in `deploy`. In the other
two modes it is dropped with a `::notice::` rather than failing the run, so the components
that *do* have a preview to show still show it. A run left with no components at all fails.

### Why `desktop_version` is required here

`Release desktop` numbers an unversioned run `0.1.<github.run_number>`. That is correct when
it runs on its own, but **not** when `Deploy All` calls it: the `github` context inside a
called workflow belongs to the *caller*, so `run_number` is Deploy All's counter, which
starts at 1. Its publish step clobbers an existing tag and marks it latest, so a combined run
with a blank version would republish `v0.1.1`, `v0.1.2`, … over releases that already shipped
and demote the real latest — pushing App Installer clients backwards.

So `Deploy All` refuses in its first job rather than deriving a version. Pass
`desktop_version`, or release the desktop head from `Release desktop` directly, where the
numbering is its own and correct.

## Ordering

Foundry and sync run in parallel: different resource groups, different runners, different
lifecycles, and neither reads the other's output.

Desktop runs last and only if neither infrastructure component failed — the client talks to
both tiers, and shipping it against infrastructure that just failed to deploy is worse than
not shipping. A component that was simply *not selected* reports `skipped`, which does not
block desktop; only a real failure or a cancellation does.

The gate is `!cancelled()` rather than `always()` on purpose. `always()` is true on a
cancelled run too, so cancelling in the window after sync reports success but before desktop
starts would still publish a release — the one irreversible thing this workflow does.

## What the run tells you afterwards

The summary job writes one table of component results, then — when Foundry deployed — the
endpoint and deployment name to paste into the desktop app's **Settings -> Azure Foundry**
to turn the AI features on. The API key is not in it; [`foundry.md`](foundry.md#enabling-the-ai-features-after-a-deploy)
explains where it is and why it is kept out.

## Permissions

The calling job grants each component exactly what it needs, because a reusable workflow's
token can only be the same or more restrictive than its caller's:

| Component | Permissions | Why |
| --- | --- | --- |
| Foundry | `contents: read` | The self-hosted runner supplies Azure access; the workflow signs in to nothing. |
| Sync | `contents: read`, `id-token: write` | `id-token` is what mints the OIDC token Azure trusts, so no secret is stored. |
| Desktop | `contents: write`, `issues: write` | It publishes a GitHub Release, and opens an issue when the release fails. |

Only the desktop job passes `secrets: inherit`, and only because the signing certificate is a
secret. Foundry and sync read `vars.*` alone, which need no inheriting — and Foundry runs on
the self-hosted runner, which is the last place to pre-authorise the repository's whole secret
set against a future `secrets.` reference.
