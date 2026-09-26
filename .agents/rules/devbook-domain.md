---
name: devbook-domain
description: Structure and authoring rules for the domain devbook folder, including root strategic DDD context mapping and per-bounded-context documentation.
---

# Domain documentation (`domain/`)

`domain/` is the durable, ubiquitous-language record of the domain model,
organized by bounded context. It is the authoritative source for "what the
domain looks like" — complementary to `arc42/` (system architecture), `tech/`
(technology stack), and `design/` (UX guidelines).

## Context-loading policy

- `domain/` is **not** baseline repository context. Load it only for domain
  modeling, bounded-context, or ubiquitous-language tasks, normally after
  routing through the repository's domain flow or a domain specialist
  agent.
- When `domain/` is needed as task context, load only the relevant bounded
  context's chapters instead of reading the whole folder by default.
- Implementation work consults `domain/` when the change touches domain
  behavior, an aggregate boundary, or naming — not by default.

## Structure

`domain/` contains one root strategic artifact plus one folder per bounded
context.

Each bounded context gets its own subfolder, named in kebab-case after the
context (e.g. `.devbook/domain/order-management/`). Use the same name consistently
across `domain/`, ADRs, and code module names where practical.

```
.devbook/domain/
  context-map.md
  <bounded-context-name>/
    context.md       # the boundary: what the context is responsible for, its
                     # feature flags and settings, and — until they outgrow
                     # it — its actors and its dependencies
    domain.md
    domain.invariants.md  # what the aggregates on domain.md enforce: one rule
                          # per chapter, where it is enforced, and the scenarios
                          # that prove it
    actors.md        # optional: the actors, once context.md is too small
                     # for them
    features.md      # what the context lets a user do, in business language
    skills.md        # the alternative to features.md, for a repository whose
                     # product is procedures rather than a running application
    requirements.md  # what those features guarantee: one SHALL sentence per
                     # requirement, with the scenarios that prove it
    model.md
    flow.md          # optional: when the context has lifecycle/process flows
    dependencies.md  # optional: the dependencies, once context.md is too
                     # small for them
    domain.<name>.md        # optional: one chapter split out of the file it
    features.<name>.md      #   is named after — an aggregate or domain
    skills.<name>.md        #   service, a feature, a skill, one feature's
    requirements.<name>.md  #   requirements, one aggregate's structure, one
    model.<name>.md         #   flow — when it is large enough or read often
    flow.<name>.md          #   enough to stand alone. See "A split file" below.
    domain.<name>.invariants.md  # what the aggregate on domain.<name>.md
                                 # enforces, beside it
```

When starting a new bounded context, create the folder with `context.md`,
`domain.md`, `model.md`, one of `features.md` or `skills.md`, and
`requirements.md` and `domain.invariants.md`, using the templates below. Add
`flow.md` when the context has lifecycle or process flows, and split
`actors.md` or `dependencies.md` out of `context.md` only when it has grown
past what one file reads well with.

**The listed files are the ones with documented responsibilities, not the only
files permitted.** A context may add a page for something it has to record
that no listed file holds — a go-live takeover of a predecessor system's data,
a regulatory annex, whatever that domain turns out to need. It is a file like
any other: a kebab-case name, a file-level `meta` block whose `type` is the
filename, and `##` sections that carry no blocks of their own. It reads after
the listed files, and nothing here has to be changed to allow it.

**`context.md` is the boundary, and the context's root document.** It opens
with what the context is responsible for — inside the boundary, outside it,
and where the outside is answered — as prose under the file-level block, with
no chapter of its own. After that come the chapters that describe the context
as a whole rather than its model: the feature flags its capabilities are
switched by, the settings a person chooses, its actors, and its dependencies. A reader
arrives here first and leaves with the shape of the context before opening
`domain.md`, which is the model and nothing else.

**A kind lives in its own file or in `context.md`, never both.** Actor
chapters (`user`, `organisation`, `technical`) sit in `context.md` until the
context has enough of them that the boundary file stops being about the
boundary; then all of them move to `actors.md`. The dependency tables sit
under one structural `## Dependencies` section in `context.md`, and move whole
to `dependencies.md` the same way. A chapter's type says what it is wherever
it sits, so a move changes addresses and nothing else; a context with an actor
in each place has split one answer across two files, exactly as one holding
both `features.md` and `skills.md` has.

**A flag is decided at release; a setting is decided by a person.** A
`feature-flag` chapter is a switch decided at release, from configuration: the
team turns it on for an environment, a ring, or everyone, and retires it once
the capability is simply there. A `setting` chapter is a value a person
chooses at runtime — a user for themselves, an administrator for a tenant, an
operator for the system, which is what its `scope` records. Whether that value
turns a capability on or shapes how it behaves is not a second kind: a setting
that enables a feature is a setting whose values are `on` and `off`, and the
feature chapter's `setting` reference is what says the capability hangs on it.
Both carry `key`, the identifier as the code spells it, which is what lets a
flag check or a configuration read found in code resolve to a chapter instead
of to prose. A feature points at what gates or configures it through
`feature-flag` or `setting`. A deployment or environment value — a retry
budget, a timeout, a maximum an operator sets per environment — is a `setting`
with `scope: system`: a person chooses it, it is not decided at release, and it
is never retired the way a flag is. A `feature-flag` is only ever a capability
switch.

**A context takes `features.md` or `skills.md`, never both.** They answer the
same question — what does this context let someone do — for two different kinds
of repository, so a context holding both has split one answer across two files.
Pick per context, not per repository, though in practice a repository lands on
one of them throughout.

**Behaviour lives in `requirements.md` and the invariants subpages, not in
prose.** The prose chapters keep what only prose can carry — why the thing
exists, who works with it, where the boundary runs, how it moves through its
lifecycle, what the domain calls it — and every rule that is either kept or
broken moves to a behaviour file, one rule per chapter, each with the scenarios
that prove it. A requirement goes in `requirements.md`; an invariant goes in the
invariants subpage of the domain page its aggregate is on. Each behaviour
chapter points back at the chapter it belongs to through `related`, and that
chapter points at it, so either half is reachable from the other.

**Invariants are a subpage of their domain page.** `domain.invariants.md` holds
the rules of the aggregates and domain services on `domain.md`, and
`domain.order.invariants.md` those of the aggregate on a split `domain.order.md`.
The name is the pairing: a reader who opens a domain page finds its rules one
file over, and splitting an aggregate out of `domain.md` moves its invariants
chapter to the new page's subpage in the same change. A subpage exists only
beside its page and only when that page's aggregates have rules; it is never
split on its own. Requirements keep their own file, because they pair with a
feature and a feature does not live on a domain page.

The two words are not a house style. A **requirement** is what the product
promises someone outside it, and the word is OpenSpec's, kept along with its
`### Requirement:` / `#### Scenario:` file layout so a tool that reads OpenSpec
reads these files. An **invariant** is what a type guarantees no matter who
calls it, and the word is DDD's, because that is the question the model is
asked and the aggregate is the thing that answers it. The distinction is not
about importance: it is about who is held to the rule, and it decides where the
rule sits and what proves it.

**The level of proof follows the file.** A requirement's `tests` are `e2e` —
it is a promise to someone outside, so what proves it is the product driven the
way that someone drives it; `integration` where the requirement is a policy no
user triggers, reached by a scheduler, a callback, or another context. An
invariant's `tests` are `unit`: it is what the type guarantees, and a test that
has to start the product to reach it is not asserting the guarantee. A chapter
whose links disagree with its file is reported as a coverage warning, never an
error — the link may be right and the level mislabelled, and a check that
refused the chapter would only teach people to leave `tests` empty.

**A domain service's rules split by who is held to them.** A service that
reacts — a policy, a process manager — makes a promise about what happens when
something occurs, and that promise is a **requirement** of whatever it acts on:
it goes in `requirements.md` under the feature the reaction belongs to. The
rules the service enforces itself, the ones it will not let a caller break, are
**invariants** and get a `## <ServiceName>` chapter in the invariants subpage of
its domain page, like an aggregate's.

**The actor chapters say who; the rest of the context says what.** No other
chapter states which role the someone in "the case worker files a request" is.
They are that record — in `context.md`, or in `actors.md` once they have
outgrown it: one flat `##` chapter per actor, headed by its name alone,
with `type` carrying which of three kinds it is. A `user` is a person with an
account who operates the context — it needs a right, and it issues the actions
the feature chapters describe. An `organisation` is a person or body the context
acts toward or models without operating it: a creditor answering a request, a
bank receiving a file, a judge whose ruling unblocks a case. A `technical` actor
is a system or timer that triggers a use case from outside — a scheduler that
closes the month, an inbound callback, the system account an audit trail records.
Users first, then organisations, then technical actors, with no grouping headings
— `type` already says which is which. Generalisation ("every Consultant is an
Employee") is a sentence in the first beat, never a nested chapter.

A `user` chapter carries `role`: the role, claim, or group name as the
authorization layer spells it, a string or a list. It is what lets a role check
found in code resolve to the actor that holds it instead of to prose. Omit it
where it has no value; an `organisation` or `technical` chapter rarely has one.

An actor is the EventStorming and Domain Storytelling actor — the one that issues
a command — and deliberately not a persona. A persona is a UX archetype of goals
and frustrations, is not ubiquitous language, and belongs in `design/` where a
repository wants one. Nor is a `technical` actor the actor-model kind: a mailbox
object, grain, or process is an implementation building block and belongs in
`arc42/`. A context nobody works with directly — a library, or one reached only
by another context — omits the file, and its absence is not a missing file.

**The ubiquitous language is the model, so it lives on the model.** A term that
is already a chapter — an aggregate, an entity, a value object, an enum, a
domain service, a domain event, an actor — carries its surface names in that
chapter's `aliases` field and earns no second chapter. Only a term with no
chapter to sit on — a role word, a process word, a name a consumer uses for
something this context never models as a thing — becomes a `term` chapter, under
the `## Ubiquitous Language` grouping at the end of `domain.md`. There is no
separate glossary file: a registry that names what the model already names is a
second copy, and it goes stale on the side nobody reads.

**A split file holds one chapter of the file it is named after.** `domain.md`,
`features.md` or `skills.md`, `requirements.md`, `model.md`, and `flow.md` each
split the same way:
`<file>.<name>.md`, where the suffix is the kebab-case name of the one thing the
file holds — `domain.order.md` is the `## Order` aggregate with everything it
owns, `features.checkout.md` one feature with its sub-features,
`requirements.checkout.md` one feature's requirements with their scenarios,
`model.order.md` one aggregate's structure, `flow.flow-code.md` one flow, named after the skill it
belongs to. A split file carries the `type` of the file it came from, because it
is the same kind of document at a smaller scope, and its chapter reads exactly as
it did inside that file: same heading, same block, same sub-chapters. Split when
the file stops being readable, or when readers arrive looking for one chapter
rather than for the context; keep the base file for the chapters still better
read together. An invariants subpage is not a split file: it follows its page,
so `domain.order.md` brings `domain.order.invariants.md` with it and
`domain.invariants.md` keeps only the rules of what stayed on `domain.md`.
`features.md`, `skills.md`, `requirements.md`, `model.md`, and `flow.md` may be
dropped once every chapter is split out, and `domain.invariants.md` once no
aggregate left on `domain.md` has a rule; `domain.md` never is, because it holds
what belongs to no single aggregate — the `## Shared Value Objects`,
`## Shared Enums`, and `## Ubiquitous Language` groupings — and `context.md`
does not split at all, because it is the root document and what it holds is
small by construction. A split chapter moves rather than copies: `## Order` lives at
`domain.order.md#order` and nowhere else in the context. Wherever a rule or a
skill names `domain.md`, `features.md`, `skills.md`, `requirements.md`,
`model.md`, or `flow.md`, it means that file or any split file of it; wherever
one names the invariants subpage, it means `domain.invariants.md` or any
`domain.<name>.invariants.md`.

Reading order comes from this convention, not from a metadata field and not from
filenames. `context-map.md` is `domain/`'s root document and is read first,
followed by the bounded contexts in alphabetical order; inside a context,
`context.md` is the root document and the rest read in the order listed in the
tree above — `domain.md`, `actors.md`, `skills.md` or `features.md`,
`requirements.md`, `model.md`, `flow.md`, `dependencies.md` — with a split file
read directly after the file it is named after, in filename order among its
siblings, and in that file's place when the file itself is gone. An invariants
subpage reads directly after its page: `domain.md`, `domain.invariants.md`,
`domain.order.md`, `domain.order.invariants.md`.
Adding a context or a file needs no declaration anywhere; just regenerate
`_meta/`. See `devbook-chapter-metadata.md`.
## File responsibilities

- **context-map.md** — Strategic DDD view across bounded contexts at the
  `domain/` root.
  - Documents the subdomain landscape/classification (core/supporting/generic
    as applicable).
  - Captures bounded-context relationships in a context map.
  - Records published languages/contracts used across context boundaries.
  - States strategic rules that constrain cross-context collaboration.
- **context.md** — The bounded context as a whole: its boundary, its
  switches, and — until they outgrow it — its actors and dependencies. The
  context's root document.
  - Opens with the boundary: what the context is responsible for, what is
    inside it, what is outside it and where that is answered instead. Prose
    under the file-level block, no chapter of its own.
  - **`type: feature-flag`** — a capability switch decided at release, from
    configuration. Carries `key` (the flag key the code checks) and, where the
    catalog states one, `default: on` or `off`. The prose says who owns the
    rollout, what turning it on changes, and when the flag is retired.
  - **`type: setting`** — a value a person chooses at runtime, whether it
    turns a capability on or shapes how it behaves. Carries `key` (the setting
    key the code reads), `scope` (`user`, `tenant`, or `system` — who may
    change it), and `default`, the value the product ships with. The prose
    says what each value does. A deployment or environment value an operator
    sets per environment is this type, at `scope: system`.
  - Both are headed by their name in business language, never the key; the
    key is the field. Their `related` points at the feature chapters they gate
    or configure, and those chapters point back through `feature-flag` or
    `setting`.
  - The actor chapters, exactly as `actors.md` describes them, when the
    context keeps them here.
  - A structural `## Dependencies` section, last, holding the tables
    `dependencies.md` describes, when the context keeps them here. It carries
    no metadata block, like `flow.md`'s sections.
- **domain.md** — One chapter per Aggregate, Domain Service, Domain Event, or
  Shared Value Objects / Shared Enums grouping in the context.
  - Aggregate chapters include sub-chapters for their owned Entities, Value
    Objects, and Enums, each carrying its own metadata block.
  - The rules an aggregate guarantees are **not** here: they are `### Invariant:`
    chapters in this page's invariants subpage, and the aggregate chapter points
    at its `## <AggregateName>` chapter there through `related`.
  - Domain Service chapters describe the service's responsibility and the
    aggregates/policies it coordinates.
  - Domain Event chapters are first-class addressable chapters and carry
    metadata blocks like other `domain.md` chapters.
  - Value Objects and Enums **shared across multiple aggregates** within the
    context get their own separate chapter — do not duplicate them under each
    aggregate that uses them.
- **actors.md** — Who works with this bounded context: one flat chapter per
  actor, headed by its name alone, with `type` carrying which of the three
  kinds it is. The chapters live in `context.md` until they outgrow it; this
  file exists only once they have moved, and then holds all of them.
  - **`type: user`** — a person with an account who operates the context.
    Four beats, in order, skipping any the context has no answer for: who it
    is, in one sentence, naming the term the screens use where it differs from
    the model term; what it does here, in business language, with `related`
    pointing at the feature chapters those actions live in; what the model holds
    it to, named only where the model actually records the role — a required
    field, an audit trail, a recorded submitter; and which right it needs, and
    where that right is configured. Its `role` field carries the name the
    authorization layer checks for that right.
  - **`type: organisation`** — a person or body the context acts toward or
    models without operating it. Three beats: who it is, what the context needs
    from it or does toward it, and how it appears in the model.
  - **`type: technical`** — a system or timer that triggers a use case from
    outside: a scheduler, an inbound callback, the system account. The same
    three beats: what it is, what it triggers here, and how the model records
    it. Its contract, where it has one, stays in the dependency tables.
  - A beat the repository cannot answer is left out and recorded as an
    `annotation` fence, never filled in with a plausible sentence. "Nobody has
    stated which right this needs" is information; an invented right is not.
  - Another bounded context or a module is a dependency and belongs in the
    dependency tables, never here. Name a bank, a portal, or a scheduler here
    only for what the context needs from it or what it triggers, and leave its
    contract to the tables. A tenant is not an actor either: it appears as the
    administrator user that changes settings.
  - Rights are stated here, not argued. Why a right is split — separation of
    duties, four eyes — is a modeling decision and belongs in `domain.md`, beside
    the invariant it protects. A modeling decision that was contested, is
    expensive to reverse, or would otherwise be re-litigated is an
    `arc42/adr/` record instead — one per concern, with its history — and the
    chapter's `related` points at it. There is no `decisions.md` here: a
    decision log beside the model is a second copy of the *why*, and it goes
    stale on the side nobody reads.
    A context whose rights need a screen-to-right matrix closes the file with one
    `## Rights` section carrying that table.
- **features.md** — The features and sub-features this bounded context
  supports, in business language. Group sub-features under their parent
  feature.
- **skills.md** — The same question for a repository whose product is
  procedures rather than a running application: one chapter per skill the
  context ships, in the language of what the skill does for whoever runs it.
  A skill *is* a feature here, so its chapters carry `type: feature` and
  `type: sub-feature` like `features.md`'s — the file name says which kind of
  repository is being described, and the chapter type stays the same because
  the thing being described has not changed.
  - One chapter per skill, headed by the skill's own name, so the chapter is
    addressable by the name a user types.
  - Group a skill's own stages or modes under it as sub-chapters; do not give a
    stage a chapter of its own at `##` level.
  - Describe what the skill lets someone do and what it guarantees, not how it
    is implemented. Where it needs a diagram, that belongs in `flow.md` or in
    its own `flow.<skill-name>.md`.
- **requirements.md** — What the context's features guarantee, one chapter per
  feature. Each `## <Feature>` chapter carries `type: requirements` and a
  `related` reference to the feature chapter in `features.md` or `skills.md`;
  the feature chapter points back the same way.
  - **`type: requirement`** — one `### Requirement: <name>` chapter per rule,
    holding exactly one SHALL sentence and nothing else, with one
    `#### Scenario: <name>` per case beneath it: Given, When, Then, one clause
    per line. A chapter with two SHALL sentences is two requirements, and
    neither can be accepted on its own.
  - `tests` on a requirement chapter are `e2e`, or `integration` for a policy
    no user triggers.
- **domain.invariants.md**, **domain.<name>.invariants.md** — What each
  aggregate on the domain page it is named after enforces, one chapter per
  aggregate. Each `## <AggregateName>` chapter carries `type: invariants` and a
  `related` reference to the aggregate chapter on that page; the aggregate
  chapter points back. A chapter whose aggregate is on another page is reported
  as a warning. A domain service that enforces rules of its own gets a chapter
  here too, pointing at its `domain-service` chapter.
  - **`type: invariant`** — one `### Invariant: <name>` chapter per rule: one
    sentence stating a claim that is either true or false, then an
    `Enforced at:` line naming where the guarantee is made — `constructor`, a
    named transition (`Confirm()`, `AddLine()`), `all mutations` where it
    genuinely holds across every one, or `open` for a rule nobody has settled.
  - Scenarios under an invariant read in the aggregate's own terms: the events
    already applied, the command issued, and the events raised or the rejection
    that follows.
  - `tests` on an invariant chapter are `unit`.
  - The rules an owned Entity or Value Object enforces are chapters here too,
    under the aggregate that owns it; `Enforced at:` names the type.
- **model.md** — The structural domain model: relationships between
  aggregates, entities, and value objects, ideally as a Mermaid class diagram,
  plus relationship notes. Lifecycle/process flows live in `flow.md`, not
  here.
- **flow.md** — Lifecycle and process flows for the context (state machines,
  sequence diagrams, flowcharts) — how aggregates move through their states and
  how work moves across the context over time. Moved out of `model.md` so
  `model.md` stays purely structural. Include only when the context actually
  has a flow. Its `##` sections do not carry metadata blocks.
- **<file>.<name>.md** — One chapter split out of `domain.md`, `features.md`,
  `skills.md`, `requirements.md`, `model.md`, or `flow.md`,
  under the rules of that file: a
  `domain.<name>.md` chapter and its sub-chapters carry their blocks, a
  `model.<name>.md` or `flow.<name>.md` carries only the file-level one. Where
  a `flow.<name>.md` belongs to a skill, the file and that skill's chapter in
  `skills.md` are two halves of one subject — the chapter says what it does,
  the flow file draws how it moves — and each carries a `related` reference to
  the other.
- **dependencies.md** — Outbound dependencies on other bounded contexts or
  modules, and known inbound dependents. The tables live under
  `## Dependencies` in `context.md` until they outgrow it; this file exists
  only once they have moved.
  - Use explicit DDD relationship semantics (`ACL`, `Customer/Supplier`,
    `Partnership`, `OHS + Published Language`, etc.) instead of ad hoc
    integration prose.
  - For each relationship, document DDD pattern, integration mechanism,
    contract, and why/what the dependency relies on.
  - An actor chapter never restates one of these relationships: another

    bounded context or a module is a dependency, not an actor, and a

    `technical` actor is named there only for what it triggers. One file

    describes a relationship, so the two have no way of contradicting each

    other.


## Folder rules

These rules describe the persisted shape of `domain/` assets only. Authoring
workflow, routing, and cross-document governance are handled by separate
instructions.
- Every Aggregate, Domain Service, Domain Event, Shared Value Objects, and
  Shared Enums chapter in `domain.md`, every Entity/Value Object/Enum
  sub-chapter inside an Aggregate, every Feature/Sub-feature chapter in
  `features.md` or `skills.md`, every Feature and Requirement chapter in
  `requirements.md`, every Aggregate and Invariant chapter in an invariants
  subpage,
  every Feature Flag and Setting chapter in
  `context.md`, every User, Organisation, and Technical chapter in
  `context.md` or `actors.md`, and every Term chapter under `domain.md`'s
  `## Ubiquitous Language` grouping must carry a
  metadata block as described in
  `devbook-chapter-metadata.md`. `type` is required; `status` is
  optional here (see below); the optional cross-folder tags (`related`) and
  issue link (`issue`) are included only when they have a value.
- Every file in `domain/` — `context-map.md` and, per bounded context,
  `context.md`, `domain.md`, `actors.md` (when present), `features.md` or
  `skills.md`, `requirements.md`, `model.md`, `flow.md`, `dependencies.md` (when
  present), each split file and invariants subpage (when present), and any additional page the context carries
  — must also carry the file-level metadata
  block described in `devbook-chapter-metadata.md`, placed directly under the
  file's top-level `#` heading. This applies even to `context-map.md`,
  `model.md`, `flow.md`, their split files, `dependencies.md`, and any
  additional page, whose `##` sections do not carry their own per-chapter
  blocks — the file-level block is the only metadata those files carry.
  `context.md` declares `index: root`, so that it sorts first even in a
  context whose `domain.md` declared it before contract 11.
- The metadata block's `status` field uses `draft`, `proposed`, `active`, or
  `deprecated` in this folder. This folder describes the current (or
  agreed-future) model, not a task queue, so there is no `done`: `active`
  means "this is the current model", `deprecated` means superseded.
- On top of that ladder sit the two decision rungs, defined once in
  `devbook-chapter-metadata.md` and belonging to **this folder only**:
  `approved`, a person agreed this chapter, recorded with `approved-by` and
  `approved-at`; and `accepted` above it, a person saw the built work against
  this chapter and accepted it, recorded with `accepted-by` and `accepted-at`
  beside the approval record it stands on. Each may carry a content
  fingerprint — `approved-hash`, `accepted-hash` — which is what makes a lapse
  a check result rather than something a reader has to establish. Both are
  written explicitly, never rested at, and both come off together the moment
  the content changes.
- **`active` is this folder's resting value, so it is written by omitting the
  field.** State `status` only while the chapter is in transition (`draft`,
  `proposed`) or carries a standing warning (`deprecated`); drop the line when
  it settles. Most of a mature bounded context is the current model, and
  restating that on every chapter says nothing while hiding the few chapters
  that are genuinely moving. Writing `status: active` explicitly is reported.
- The metadata block's `type` field records what kind of thing the chapter or
  file is — the classification that is **never** written into the heading. This
  folder's value sets are:

  | Level | Values |
  |---|---|
  | Chapter | `bounded-context`, `aggregate`, `entity`, `value-object`, `enum`, `shared-value-objects`, `shared-enums`, `ubiquitous-language`, `domain-service`, `domain-event`, `feature`, `sub-feature`, `requirements`, `requirement`, `invariants`, `invariant`, `feature-flag`, `setting`, `user`, `organisation`, `technical`, `term` |
  | File | `context-map`, `context`, `domain`, `actors`, `features`, `skills`, `requirements`, `invariants`, `model`, `flow`, `dependencies` — or, for an additional page, its own filename |

  `requirements` and `invariants` are each in both sets, and mean the same
  thing at both levels: the file holds a context's, a `##` chapter holds one
  feature's or one aggregate's. Everywhere else the two levels have disjoint
  vocabularies, so the repetition is worth naming — a `## <Feature>` chapter in
  `requirements.md` is `type: requirements`, plural, and only the
  `### Requirement:` chapters under it are `type: requirement`.

  There is no `skill` chapter type, deliberately. A skill in `skills.md` is a
  `feature` and its stages are `sub-feature`s: the file already says which kind
  of repository is being described, and a second vocabulary for the same
  relationship would make every consumer of the graph branch on the filename to
  learn nothing.

  Each file's `type` matches its filename: `context.md` is `type: context`,
  `domain.md` is `type: domain`, `features.md` is `type: features`, and so on,
  with `context-map.md` at the `domain/` root carrying `type: context-map`. A
  split file carries the type of the file it is named after — `domain.order.md`
  is `type: domain` — because the suffix narrows the scope and not the kind. An
  invariants subpage is the exception the name spells out: a trailing
  `.invariants` makes `domain.invariants.md` and `domain.order.invariants.md`
  `type: invariants`, and one declaring any other type is an error. The
  `invariants.md` and `invariants.<name>.md` of contract 16 still validate with a
  warning; `017-invariants-under-domain` moves them.
- Heading text in `domain/` carries the **name only** — `## Order`, not
  `## Aggregate: Order`. Anchors are therefore slugs of the bare name
  (`.devbook/domain/order-management/domain.md#order`). The two exceptions are the
  `## Shared Value Objects` and `## Shared Enums` chapters, whose headings name
  a grouping rather than a single thing, so the descriptive text *is* the name.
  `## Ubiquitous Language` is a third of the same kind.

  `### Requirement:`, `### Invariant:`, and `#### Scenario:` are the deliberate
  fourth. They carry the kind in the heading because that heading shape is
  OpenSpec's, and keeping it is the whole reason these two files exist in this
  layout: a tool that reads OpenSpec finds the requirements in a devbook
  repository without being taught anything. The cost is one prefix in three
  heading kinds, paid once, and their anchors carry it —
  `domain.invariants.md#invariant-an-order-cannot-be-confirmed-twice`. No other
  `domain/` heading may take a prefix on the strength of this one; the
  exception is bought by an external format, not by taste.
  File titles are the bounded-context name alone (`# Order Management`), with
  the file's own `type` distinguishing the files of a context. A split file is
  no exception: its title stays the context name and the chapter's own name
  goes in its `##` heading, exactly as it did inside the file it came from.

  `context-map.md` is the one `domain/` file that is not about a single bounded
  context, so it has no context name to carry. Prefer titling it after the
  system or product the map covers — `# Order Platform` — with
  `type: context-map` carrying the kind, exactly as everywhere else. The
  generator composes its node label as `Order Platform (context-map)`, so the kind
  stays visible in the graph and the label stays distinct when several
  repositories' devbook folders are viewed together.

  A plain `# Context Map` is also accepted, and the generator suppresses the
  redundant suffix so it renders as `Context Map` rather than
  `Context Map (context-map)`. It restates the `type` in the heading, which is
  mildly against the grain of this convention, but it reads unambiguously and
  reasonable people prefer it. Pick one per repository and stay with it; do not
  churn an existing title to switch.

  A `domain/` folder written the old way (kind prefixes in headings, no `type`,
  `#### <Name>` sub-chapters under `### Entities`) is migrated with the steps in
  the devbook plugin README under "Migrating to schema version 2".
- `features.md` and `skills.md` Feature/Sub-feature chapters may carry an
  additional `depends-on` field: a list of `<path>#<heading-slug>` references (see
  `devbook-chapter-metadata.md` for the reference
  format) to other features that must be delivered first, e.g.
  `depends-on: [.devbook/domain/order-management/features.md#refunds]`.
  `domain.md` chapters (Aggregates, Domain Services, Domain Events, Shared
  Value Objects/Enums) do not use `depends-on` — they describe standing
  structure, and their relationships belong in `model.md`, the dependency
  tables, or the `related` field instead.
- `features.md` and `skills.md` Feature/Sub-feature chapters may carry a
  `feature-flag` field and a `setting` field: `<path>#<heading-slug>`
  references to the `feature-flag` chapters that gate this capability and the
  `setting` chapters that gate or configure it, in the context's `context.md`,
  e.g.
  `feature-flag: .devbook/domain/inbox/context.md#inbox-pane` or, when several
  together deliver one chapter,
  `feature-flag: [.devbook/domain/inbox/context.md#inbox-pane, .devbook/domain/inbox/context.md#inbox-filters]`.
  One flag or setting may equally be pointed at by several chapters. A
  `feature-flag` reference produces a `gated-by` edge, a `setting` reference a
  `configured-by` edge, and each must resolve to a chapter of the matching
  type — a `feature-flag` reference to an aggregate is an error. Omit either
  field when the chapter has nothing to point at. `domain.md` chapters and
  `term` chapters use neither: a flag or a setting belongs to a capability,
  not to a structural element or a term.

  Before contract 11 `feature-flag` held the bare application key, because the
  flag's catalog lived outside the repository. The catalog is `context.md` now;
  the key sits on the switch's own chapter, and a bare key in the field is an
  error that names the `011-context-md` migration.

  Either link is an **identity** link only — it says "this chapter and that
  switch are the same capability". It is deliberately **not** a status mapping.
  The `status` values above describe how settled the written model is; a
  flag's rollout or a setting's default describes whether the running behaviour
  can be relied on. Those answer different questions, so do not translate one
  vocabulary into the other, and do not infer a chapter's `status` from its
  switch or the reverse.
- `context.md` Feature Flag and Setting chapters carry `key`: the identifier
  as the code spells it, a single plain string — `key: checkout.express` — and
  never a reference. It is what a flag check or a configuration read found in
  code resolves to, so a switch chapter without one is an error. Both may
  carry `default`: `on` or `off` for a flag, the shipped value for a setting.
  A Setting chapter also carries `scope`, one of `user`, `tenant`, `system` —
  who may change it at runtime. A flag has no `scope`: it is decided at
  release, and writing one on it is an error. No other chapter carries any of
  the three.
- A `bounded-context` chapter in `context-map.md` may carry `deployment`:
  how the context ships. `service` is a deployable of its own, released and
  scaled apart from the others; `module` runs inside a modular monolith beside
  other contexts, sharing its process and its release. It is one plain value,
  never a reference, and no other chapter carries it — an aggregate or a
  feature ships with its context. The context's `context.md` carries the same
  value on its file-level block, so a reader of the context sees it without
  opening the map; where the chapter's `related` names that `context.md`, the
  two must agree, and the check reports a difference or a value on one side
  only. A repository that lists its contexts only in tables writes it on
  `context.md` alone. Omit it until the choice is made. Name the
  deployable itself — the service, or the monolith hosting the module — through
  `related`, pointing at its `arc42/` building block; which contexts share one
  host is read from those links, not restated here. Moving a context from
  `module` to `service`, or back, is a decision worth an `arc42/adr/` record
  that the chapter's `related` points at.
- Actor chapters may carry a `role` field: the role, claim, or group name
  the authorization layer checks for this actor, as the code spells it — e.g.
  `role: Consultant` or, where one actor holds several, `role: [Consultant,
  TeamLead]`. Like `key`, entries are plain application identifiers,
  not `<path>#<heading-slug>` references: the name lives in the repository's
  authorization configuration, so the field produces no graph edge and the value
  is never validated here. It is the fourth beat made addressable — a role check
  found in code resolves to the `user` whose `role` matches. Omit it when the
  actor has none, which is the usual case for `organisation` and `technical`;
  no other chapter type carries it. `role` here is the RBAC role a right is
  granted to, never the role a domain object plays in a relationship — that is
  modelled in `domain.md`.
- In the dependency tables — under `## Dependencies` in `context.md` or in
  `dependencies.md` — use explicit DDD relationship terminology for each
  cross-context row when applicable (for example: `ACL`,
  `Customer/Supplier`, `Partnership`, `OHS + Published Language`) and identify
  the contract/published language entry used by consumers.
- One rule per chapter, in both files. A chapter holding three related rules
  cannot be checked, enforced, or accepted as one thing, and it is a chapter a
  brief has to split before it can quote it. A requirement states its one rule
  as a single SHALL sentence; an invariant as a single claim that is either
  true or false, in the domain's own language.
- `Enforced at:` is the line prose loses, and every invariant chapter carries
  one. An invariant is what the type guarantees no matter who calls it, and
  *where* it is guaranteed is what tells an `apply-change` pass whether a guard
  clause belongs in the constructor or in one transition. A rule whose
  enforcement point cannot be named is usually a caller's rule rather than an
  invariant — see `assets/code-sync-protocol.md`. A requirement carries no such
  line: it is a promise about the product, and where it is kept is the
  implementation's business.
- A rule that is **not yet settled** is an invariant chapter with `open` on its
  `Enforced at:` line and the open question itself in a `kind: question`
  annotation fence beside it, per `devbook-annotations.md`. A requirement not
  yet settled is the same, at `status: draft`. This is the hot spot of an Event
  Storming session kept in place rather than resolved by guessing, and it is
  where `assets/code-sync-protocol.md` means a rule to go when it says to record
  it as an open question instead of capturing it as fact.

  An `open` rule does not stop the aggregate reaching `active` — a model can
  be the current one and still carry a known unanswered question. It does stop
  that one rule being *built*: an `apply-change` pass names it as needing a decision
  instead of briefing an implementation of a rule nobody has agreed. The
  existing gate already refuses `approved` or `accepted` over an open question,
  so a rung on a chapter carrying one is reported without a second rule here.
- A chapter with no scenarios is reported as a coverage warning. A rule with no
  case that exercises it is a sentence nobody can tell has been broken, and a
  requirement whose scenarios are missing is one a brief cannot derive an
  acceptance check from. It is a warning and not an error because the rule is
  still worth recording before its cases are written — but a chapter left that
  way is not finished.
- `tests` entries are checked against the file they sit in: `unit` for an
  invariant, `e2e` — or `integration` for a policy no user triggers — for a
  requirement. A requirement backed only by unit tests, or an invariant backed
  only by end-to-end tests, is reported as a coverage warning. A chapter with no
  `tests` at all is reported for neither, because the absence of the field
  carries no claim; that rule is `devbook-chapter-metadata.md`'s and is not
  changed here.
- In Domain Service chapters, state invocation semantics when it clarifies
  behavior boundaries: whether logic is command-invoked, scheduled,
  query/composition-oriented, or event-triggered policy/process-manager
  behavior.
- Do not introduce a separate `policy.md` or a distinct `Policy` chapter type
  just to document process-manager behavior; keep that semantics in the
  relevant Domain Service chapter unless a separate structure is later decided
  explicitly.
- Any chapter may carry an `aliases` field: a list of plain-string surface
  names the thing is also known by — a code class or identifier name, a
  snake_case id field, a consumer context's local copy name, a host's own word
  for it. Unlike `related`/`depends-on`, `aliases` entries are plain strings,
  not `<path>#<heading-slug>` references. Omit the field when there are none;
  the file-level block may not carry it, like every other folder-specific
  field. This is how a modelled concept is its own glossary entry: the aggregate
  chapter `## Order` with `aliases: [OrderRoot, order_id]` is the term *Order*,
  and every synonym resolves to that one chapter. A `term` chapter exists only
  for a word that has no chapter to carry the field, and its `related` field
  points at the chapters it is about.

## Templates

### context-map.md

```markdown
# <System or Product Name>

\`\`\`meta
status: draft
type: context-map
\`\`\`

> `domain/`'s root document. Prefer titling it after the system the map covers,
> since the `type` above already carries the kind and the generator labels this
> node `<System Name> (context-map)`; a plain `# Context Map` is also accepted.
> Its structural `##` sections — the four from `## Subdomain landscape` on —
> carry no metadata blocks; the file-level block above is the only metadata
> they need.
>
> A `##` section naming **one bounded context** is the exception, and it takes
> `type: bounded-context`. Give a context its own section and block when another
> chapter needs to address it — `.devbook/domain/context-map.md#order-management` — which
> is how a building block, a technology, or an arc42 chapter points at the
> context it belongs to, or when its `deployment` is decided: `service` or
> `module`. A repository whose contexts are only listed in the tables below
> needs no such sections.

## <Bounded Context Name>

\`\`\`meta
type: bounded-context
deployment: module
related: [.devbook/domain/<bounded-context-name>/context.md, .devbook/arc42/05-building-block-view.md#<host-heading-slug>]
\`\`\`

What the context is for, in one sentence, and where it runs: the service it
is, or the modular monolith that hosts it as a module.

## Subdomain landscape

| Subdomain | Classification | Bounded context |
|---|---|---|
| <Subdomain> | Core / Supporting / Generic | <Bounded Context Name> |

## Context map

<Diagram or table of the relationships between bounded contexts, using
explicit DDD relationship terminology — ACL, Customer/Supplier, Partnership,
OHS + Published Language.>

## Published languages

<The contracts used across context boundaries, and which contexts consume
each one.>

## Strategic rules

<Rules that constrain cross-context collaboration.>
```

### context.md

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
index: root
type: context
deployment: module
\`\`\`

What this context is responsible for, in one or two sentences.

Inside the boundary: <the things this context owns and decides>.

Outside it: <what it deliberately does not own, and which context answers it
instead>.

## <Switch Name>

\`\`\`meta
status: draft
type: feature-flag
key: <flag.key.as.the.code.checks.it>
default: off
related: [.devbook/domain/<context>/features.md#<heading-slug>]
\`\`\`

Decided at release, from configuration. Who owns the rollout, what turning it
on changes, and when the flag is retired.

## <Setting Name>

\`\`\`meta
status: draft
type: setting
key: <setting.key.as.the.code.reads.it>
scope: user
default: <the shipped value>
related: [.devbook/domain/<context>/features.md#<heading-slug>]
\`\`\`

Chosen at runtime by whoever `scope` names. What each value does — whether it
turns the capability on or shapes how it behaves.

## <User Name>

\`\`\`meta
status: draft
type: user
role: <RoleNameAsTheAuthorizationLayerSpellsIt>
related: [.devbook/domain/<context>/features.md#<heading-slug>]
\`\`\`

The actor chapters, exactly as in `actors.md` below, while the context keeps
them here.

## Dependencies

### Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| <OtherContext> | <ACL / Customer-Supplier / Partnership / OHS + Published Language> | <event, API call, registry lookup, id link, etc.> | <published language / contract chapter reference> | <reason this context needs it> |

### Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| <OtherContext> | <ACL / Customer-Supplier / Partnership / OHS + Published Language> | <how the consumer integrates> | <published language / contract chapter reference> | <what would break if changed> |
```

`## Dependencies` is the file's one structural section: it carries no
metadata block, and it comes last. The tables are the ones `dependencies.md`
describes, one heading level down.

### domain.md

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: domain
\`\`\`

> One chapter per Aggregate, Domain Service, Domain Event, or Shared Value
> Objects / Shared Enums grouping in this bounded context.
> Aggregate chapters include sub-chapters for their owned Entities, Value
> Objects, and Enums. Value Objects/Enums shared across multiple aggregates
> get their own chapter at the end instead of being duplicated.

## <AggregateName>

\`\`\`meta
status: draft
type: aggregate
related: [.devbook/domain/<context>/domain.invariants.md#<aggregate-heading-slug>]
\`\`\`

Responsibility, lifecycle, and why this aggregate exists as a consistency
boundary. The rules it guarantees are not here: they are `### Invariant:`
chapters under `## <AggregateName>` in `domain.invariants.md`, which `related`
above points at.

### <EntityName>

\`\`\`meta
status: draft
type: entity
\`\`\`

Role within the aggregate, identity, and lifecycle notes.

### <ValueObjectName>

\`\`\`meta
status: draft
type: value-object
\`\`\`

Meaning, equality semantics, and validation rules.

### <EnumName>

\`\`\`meta
status: draft
type: enum
\`\`\`

Values and what each one means in business terms.

## <NextAggregateName>

...

## <DomainServiceName>

\`\`\`meta
status: draft
type: domain-service
\`\`\`

Responsibility of the service, which aggregates/policies it coordinates, and
why the behavior does not belong on a single aggregate.

Invocation semantics: <command-invoked | scheduled |
query/composition-oriented | event-triggered policy/process manager>.

What it reacts to is a requirement of the unit that reacts, in
`requirements.md`; what it enforces itself is a `## <ServiceName>` chapter in
`domain.invariants.md`, pointed at from `related` when it has one.

## <EventName>

\`\`\`meta
status: draft
type: domain-event
\`\`\`

Published when <business trigger>.

### Payload

- `<field>` - <meaning and type/shape expectations>

### Consumers

- <Consumer context/service and why it consumes the event>

### Published language rules

- <contract stability and interpretation rules for consumers>

## Shared Value Objects

\`\`\`meta
status: draft
type: shared-value-objects
\`\`\`

> Value Objects used by more than one aggregate in this bounded context.

### <SharedValueObjectName>

\`\`\`meta
status: draft
type: value-object
\`\`\`

Meaning, equality semantics, validation rules, and which aggregates use it.

## Shared Enums

\`\`\`meta
status: draft
type: shared-enums
\`\`\`

> Enums used by more than one aggregate in this bounded context.

### <SharedEnumName>

\`\`\`meta
status: draft
type: enum
\`\`\`

Values and what each one means in business terms, and which aggregates use it.

## Ubiquitous Language

\`\`\`meta
status: draft
type: ubiquitous-language
\`\`\`

> The terms this context owns that are not chapters above. A term that is a
> chapter carries its aliases on that chapter instead. A grouping heading, so
> its descriptive text *is* its name; omit the grouping when there are none.

### <Canonical Term>

\`\`\`meta
status: draft
type: term
aliases: [<AliasA>, <AliasB>]
related: [.devbook/domain/<context>/domain.md#<heading-slug>]
\`\`\`

Definition of the term and, where useful, when each alias appears.
```

The Entity, Value Object, and Enum sub-chapters sit directly under their
aggregate as `###` headings. There are no `### Entities` / `### Value Objects`
/ `### Enums` grouping headings — `type` already says which is which, and the
grouping headings only pushed every real chapter a level deeper and added
anchors nobody references.

`### Payload`, `### Consumers`, and `### Published language rules` under a
Domain Event, and `#### Scenario:` under a Requirement or an Invariant, are
structural
sub-sections of that one chapter rather than addressable chapters, so they carry
no metadata block. `build.mjs --check` warns on each of them, as it does on
every structural heading: the validator cannot know which headings a folder means
to be addressable, so the warning is expected here and never driven to zero.
`## Rights` in `actors.md` and `## Dependencies` in `context.md` are the same
case one level up — structural sections of the file rather than addressable
chapters — and warn the same way.

A scenario is structural on purpose. It is a case *of* its rule and has no life
apart from it: nothing addresses one, a brief quotes the requirement and carries
its scenarios along, and giving each a block would put two `meta` fences on
every rule for no reader's benefit. The rule is the addressable unit; its
scenarios are how it is stated. The cost is one warning per scenario, which is
the same warning every structural heading in this folder already produces.

### actors.md

Only once the actor chapters have outgrown `context.md`; then every one of them
moves here.

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: actors
\`\`\`

> Who works with this bounded context: the users that operate it, the
> organisations it acts toward, and the technical actors that trigger it.
> Another bounded context or module is a dependency and belongs in the
> dependency tables, never here.

## <User Name>

\`\`\`meta
status: draft
type: user
role: <RoleNameAsTheAuthorizationLayerSpellsIt>
related: [.devbook/domain/<context>/features.md#<heading-slug>]
\`\`\`

Who this user is, in one sentence, naming the term the screens use where it
differs from the model term.

What it does in this context, in business language, pointing at the feature
chapters those actions live in.

What the model holds it to: the required field, audit trail, or recorded
submitter where the model actually records the role.

Which right it needs, and where that right is configured.

## <Organisation Name>

\`\`\`meta
status: draft
type: organisation
\`\`\`

Who this organisation is, what the context needs from it or does toward it,
and how it appears in the model.

## <Technical Actor Name>

\`\`\`meta
status: draft
type: technical
related: [.devbook/domain/<context>/context.md#dependencies]
\`\`\`

What this actor is, what it triggers in this context, and how the model records
it. Its contract lives in the dependency tables.

## Rights

| Action | <User> | <User> |
|---|---|---|
| <what can be done> | <the right it needs, or —> | <the right it needs, or —> |
```

The `## Rights` section is present only where the context's rights want a matrix.
It is the file's one structural section: it carries no metadata block, and it
comes last.

### features.md

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: features
\`\`\`

> Features and sub-features this bounded context supports, described in
> business/ubiquitous language rather than implementation terms.

## <FeatureName>

\`\`\`meta
status: draft
type: feature
feature-flag: [.devbook/domain/<context>/context.md#<switch-heading-slug>]
setting: [.devbook/domain/<context>/context.md#<setting-heading-slug>]
related: [.devbook/domain/<context>/requirements.md#<feature-heading-slug>]
\`\`\`

Short description of the capability and the business value it delivers. What it
guarantees is not here: those are `### Requirement:` chapters under the matching
`## <FeatureName>` chapter in `requirements.md`, which `related` points at.

### <SubFeatureName>

\`\`\`meta
status: draft
type: sub-feature
\`\`\`

Description of the sub-feature and how it fits under the parent feature.

### <NextSubFeatureName>

...

## <NextFeatureName>

...
```


### skills.md

The alternative to `features.md`, for a repository whose product is procedures.
A skill is a feature here, so the chapter types are the same.

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: skills
\`\`\`

> One chapter per skill this bounded context ships, described by what it lets
> someone do rather than by how it is implemented.

## <skill-name>

\`\`\`meta
status: draft
type: feature
related: [.devbook/domain/<context>/flow.<skill-name>.md]
\`\`\`

What the skill does for whoever runs it, what it guarantees, and where it
stops. Name the skill exactly as a user types it, so the anchor is the name.

### <Stage or Mode Name>

\`\`\`meta
status: draft
type: sub-feature
\`\`\`

One stage of the skill, or one mode it can run in.

## <next-skill-name>

...
```

### requirements.md

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: requirements
\`\`\`

> What this context's features guarantee, one chapter per feature. Each
> requirement is one SHALL sentence with the scenarios that prove it.

## <FeatureName>

\`\`\`meta
status: draft
type: requirements
related: [.devbook/domain/<context>/features.md#<feature-heading-slug>]
\`\`\`

> The requirements of one feature. The feature chapter itself says what the
> capability is and why it exists; this says what it promises.

### Requirement: <what it promises, as a short name>

\`\`\`meta
status: draft
type: requirement
tests: e2e:playwright:tests/e2e/<spec>.ts#<test title>
\`\`\`

The system SHALL <one promise, stated once>.

#### Scenario: <the case this covers>

- **Given** <the state the case starts in>
- **When** <the one thing that happens>
- **Then** <what is true afterwards>

#### Scenario: <the next case>

...

### Requirement: <the next promise>

...

## <NextFeatureName>

...
```

### domain.invariants.md

The same template serves `domain.<name>.invariants.md`, with `related` pointing
at `domain.<name>.md`.

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: invariants
\`\`\`

> What each aggregate on `domain.md` enforces, one chapter per aggregate. Each
> invariant is one rule, where it is enforced, and the scenarios that prove it.

## <AggregateName>

\`\`\`meta
status: draft
type: invariants
related: [.devbook/domain/<context>/domain.md#<aggregate-heading-slug>]
\`\`\`

> The invariants of one aggregate, including those its owned entities and value
> objects enforce. The aggregate chapter itself says what the boundary is and
> why it exists; this says what it will not let a caller break.

### Invariant: <the rule, as a short name>

\`\`\`meta
status: draft
type: invariant
tests: unit:dotnet:<Ordering.Domain.Tests.OrderTests.TheRule>
\`\`\`

<One rule, as a claim that is either true or false.>

Enforced at: <constructor | <Transition>() | all mutations | open>

#### Scenario: <the case this covers>

- **Given** <the events already applied>
- **When** <the command issued>
- **Then** <the events raised, or the rejection>

#### Scenario: <the next case>

...

### Invariant: <the next rule>

...

## <NextAggregateName>

...
```

An invariant's scenarios read as events applied, a command, and events raised or
a rejection, because that is the vocabulary the aggregate is written in and the
shape an Event Storming session already produced. A requirement's read in the
product's terms — a state, an action, an outcome — because it is a promise to
someone outside the model. Same three clauses, two vocabularies, and the file
says which one applies.

### model.md

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: model
\`\`\`

> Structural view of the domain model for this bounded context: aggregates,
> entities, value objects, and their relationships. Keep this in sync with
> `domain.md` (which describes responsibilities/invariants in prose) — this
> file focuses on structure and relationships.

## Model diagram

\`\`\`mermaid
classDiagram
    class AggregateName {
        +Identity Id
        +Value fields...
    }
    class EntityName
    class ValueObjectName

    AggregateName "1" --> "many" EntityName : contains
    AggregateName --> ValueObjectName : has
\`\`\`

## Relationship notes

- Describe cardinalities, ownership direction, and any relationships that
  aren't obvious from the diagram alone (e.g. why an association is one-way,
  or why two aggregates only relate by id reference rather than direct
  object reference).
```

### flow.md

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: flow
\`\`\`

> Lifecycle and process flows for this bounded context: how aggregates move
> through their states and how work moves across the context over time.
> Complementary to `model.md` (structure) and `domain.md`
> (responsibilities/invariants).

## <Flow Name>

\`\`\`mermaid
<mermaid state/sequence/flow diagram>
\`\`\`

- Optional notes: transitions, emitted events, and which state is persisted
  vs. which is a workflow-only phase.
```


### <file>.<name>.md

One chapter split out of its file, under that file's template: the file-level
block carries the base file's `type`, and the chapter follows as it stood there.
`domain.order.md` is the `## <AggregateName>` section of the `domain.md`
template under a `type: domain` file block; `features.checkout.md` and
`skills.<skill-name>.md` the same for one feature or skill;
`requirements.checkout.md` one feature's `## <FeatureName>` chapter with its
`### Requirement:` chapters under a `type: requirements` file block;
`model.order.md`
the `model.md` template narrowed to one aggregate. A `flow.<name>.md`:

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: flow
related: [.devbook/domain/<context>/skills.md#<skill-name>]
\`\`\`

> One flow: <what moves, and from where to where>. Structure is in
> [model.md](model.md); what the skill does is in
> [skills.md](skills.md#<skill-name>).

## <Flow Name>

\`\`\`mermaid
<mermaid state/sequence/flow diagram>
\`\`\`

- Optional notes: transitions, emitted events, and which state is persisted
  vs. which is a workflow-only phase.
```

### dependencies.md

Only once the dependency tables have outgrown `context.md`'s `## Dependencies`
section; then the section moves here whole.

```markdown
# <Bounded Context Name>

\`\`\`meta
status: draft
type: dependencies
\`\`\`

> Dependencies this bounded context has on other bounded contexts or
> modules, and known dependents. Use explicit DDD relationship semantics,
> integration mechanism details, and contract references.

## Outbound dependencies

| Depends on (context/module) | DDD pattern | Integration mechanism | Contract | Why |
|---|---|---|---|---|
| <OtherContext> | <ACL / Customer-Supplier / Partnership / OHS + Published Language> | <event, API call, registry lookup, id link, etc.> | <published language / contract chapter reference> | <reason this context needs it> |

## Inbound dependents (known)

| Consumer (context/module) | DDD pattern | Integration mechanism | Contract | What it relies on |
|---|---|---|---|---|
| <OtherContext> | <ACL / Customer-Supplier / Partnership / OHS + Published Language> | <how the consumer integrates> | <published language / contract chapter reference> | <what would break if changed> |

## Notes

- Prefer explicit DDD pattern names over free-text integration wording.
- Flag any dependency that crosses a bounded-context boundary without an
  anti-corruption layer or published language, so it can be revisited.
- Link to the relevant `domain-interaction-diagram` / `context-mapping`
  artifact if one exists for this relationship, instead of duplicating it.
```
