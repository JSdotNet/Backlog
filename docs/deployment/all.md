# Combined deployment

`Deploy All` (`.github/workflows/deploy-all.yml`) takes the Azure AI Foundry models, the
cloud sync tier's infrastructure and the release — the desktop MSIX, the Android APK and the
sync service, under one number — out in one run.

It contains no deployment logic of its own. Each component is the workflow that already
deployed it, called as a [reusable workflow][reusable]:

| Component | Called workflow | Documented in |
| --- | --- | --- |
| Foundry | `deploy-foundry.yml` | [`foundry.md`](foundry.md) |
| Sync infrastructure | `deploy-sync.yml` (never its `deploy` mode) | [`sync.md`](sync.md) |
| Release | `release-all.yml`, which calls `deploy-sync.yml` in `service` mode | — |

So a component behaves identically whether it runs on its own or from here, and changing
how sync deploys still means editing `deploy-sync.yml` and nothing else. Every workflow
keeps the triggers it already had: the nightly release still runs on its schedule, and
deploys the sync service when it publishes; sync's infrastructure has no trigger of its own,
its push-to-`main` trigger still parked, and runs from its own **Run workflow** button as
well as from here.

[reusable]: https://docs.github.com/actions/using-workflows/reusing-workflows

## The release ships both apps and the sync service

`release-all.yml` builds the desktop MSIX and the Android APK from one commit, deploys the
sync service from it under the same number, and publishes the packages as one GitHub
Release, so the release component ships all three or none. The sync component here is
therefore the infrastructure alone: Deploy Sync's `deploy` mode would push the service a
second time in the same run, unversioned. A mobile build
failure now reddens a run that also deployed Foundry and sync; it no longer can leave the
two apps on different numbers. Before Release All, the Android head was held out of this
workflow until it was trusted enough for that, and released from its own workflow under
separate `mobile-v*` tags.

## Running it

**Actions -> Deploy All -> Run workflow.**

| Input | Default | Notes |
| --- | --- | --- |
| `mode` | `what-if` | How far to take the infrastructure. See the table below. |
| `components` | `foundry+sync` | Which components take part. `release` requires `mode: deploy`. |
| `foundry_environment` | `backlog-ai` | Selects the GitHub environment and `infra/foundry/<name>.bicepparam`. |
| `sync_environment` | `backlog-sync` | Selects the GitHub environment and names the azd environment, for the sync infrastructure and for the service the release deploys alike. |
| `release_version` | blank | Release version of both apps without the leading `v`. Blank releases the next number — see below. |
| `include_embedding_model` | `from-parameter-file` | Foundry: `text-embedding-3-small`, which backs knowledge search by meaning. |
| `include_speech_model` | `from-parameter-file` | Foundry: `gpt-4o-transcribe`. |
| `include_balanced_model` | `from-parameter-file` | Foundry: the optional `gpt-5.6-sol`. |

### What each mode does

The two infrastructure workflows spell their modes differently, so `Deploy All` translates
one choice into both:

| `mode` | Foundry | Sync | Release |
| --- | --- | --- | --- |
| `what-if` | `what-if` — validates and previews, changes nothing | `preview` — `azd provision --preview` | dropped |
| `provision` | `deploy` — creating the account *is* provisioning it | `provision` — infrastructure only, no service push | dropped |
| `deploy` | `deploy` | `provision` — infrastructure only; the release pushes the service | deploys the sync service, then publishes the MSIX and APK release |

Publishing a release has no dry run, so `release` takes part only in `deploy`. In the other
two modes it is dropped with a `::notice::` rather than failing the run, so the components
that *do* have a preview to show still show it. A run left with no components at all fails.

### How the release is numbered

`Release All` numbers a release from the tags already published, not from
`github.run_number`: the highest `MAJOR.MINOR.PATCH` over the `v*` and `mobile-v*` tags,
with the patch raised by one (`build/Get-NextReleaseVersion.ps1`). A blank
`release_version` therefore takes the next number, the same one the nightly would have
taken.

That is what used to make the version mandatory here. The desktop release numbered an
unversioned run `0.1.<github.run_number>`, and inside a called workflow `run_number` is the
*caller's* — Deploy All's counter, which starts at 1 — so a blank version would have
republished `v0.1.1`, `v0.1.2`, … over releases that already shipped.

An explicit `release_version` lower than the highest release is refused before anything
builds: a lower MSIX version or APK versionCode would not install over what the devices
already have. One equal to an existing release republishes that release's assets, but only
from the commit its tag points at — a re-run. From any other commit it is refused, because the
same MSIX version and versionCode would then carry a different build that no installed device
picks up; release a higher version instead.

Runs of `Release All` queue behind each other workflow-wide, so a combined run and a nightly
never take the same number. GitHub keeps only one *pending* run per queue, though: when a
third run arrives while one is publishing and another is waiting, the waiting one is
cancelled, with no failure issue. A cancelled tag push leaves a tag with no release; release
it again with **Release All -> Run workflow**, choosing the tag itself under *Use workflow
from*, so the run builds the tag's commit and takes its version from it.

## Ordering

Foundry and sync run in parallel: different resource groups, different runners, different
lifecycles, and neither reads the other's output.

The release runs last and only if neither infrastructure component failed — both apps and
the sync service talk to both tiers, and shipping them against infrastructure that just
failed to deploy is worse than not shipping. A component that was simply *not selected* reports `skipped`, which does
not block the release; only a real failure or a cancellation does.

The gate is `!cancelled()` rather than `always()` on purpose. `always()` is true on a
cancelled run too, so cancelling in the window after sync reports success but before the
release starts would still publish a release — the one irreversible thing this workflow does.

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
| Sync | `contents: read`, `id-token: write` | `id-token` is what mints the OIDC token Azure trusts, so no Azure secret is stored. |
| Release | `contents: write`, `issues: write`, `id-token: write` | It publishes a GitHub Release, opens an issue when the release fails, and deploys the sync service through Deploy Sync's OIDC sign-in. |

The release and sync jobs pass `secrets: inherit`; Foundry does not. The release needs it for
the Windows signing certificate, the Android keystore and, through its own call to Deploy
Sync, `SYNC_TOKEN_SIGNING_KEY`. Sync needs it for
`SYNC_TOKEN_SIGNING_KEY`, the key the sync service
signs device tokens with (see [`sync.md`](sync.md#the-device-token-signing-key)) — and
`inherit` rather than passing it by name because it is an *environment* secret: the calling
job has no environment to resolve it in, only the called workflow's job does. Foundry reads
`vars.*` alone and runs on the self-hosted runner, which is the last place to pre-authorise
the repository's whole secret set against a future `secrets.` reference.
