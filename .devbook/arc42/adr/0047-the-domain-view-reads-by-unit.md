# ADR 0047: The Domain view reads by unit

```meta
date: 2026-10-20
related: [".devbook/domain/devbook/features.md#domain-view-by-unit", ".devbook/domain/devbook/requirements.md#domain-view-by-unit", ".devbook/domain/devbook/context.md#new-domain-view", ".devbook/domain/devbook/features.md#rendered-devbook-documents", ".devbook/design/color-scheme.md#domain-view-element-tones", ".devbook/arc42/adr/0016-knowledge-folders-adopt-the-devbook-convention.md"]
```

The Domain area of the Devbook lists a bounded context's files as units. Each
row opens a page drawn for the kind of file it is, and the menu is derived from
the file names alone. No chapter gains a metadata field for the view.

## Status

```meta
```

Proposed, 2026-10-20. Built behind the
[New domain view](../../domain/devbook/context.md#new-domain-view) switch by the
plan `+devbook-domain-view`. Checked in the running app on 2026-10-20, and the
Demo button on 2026-11-09. It awaits acceptance at the
Personal Validation gate of the flow-spec run that drafted it. Renumbered from 0040 to 0044 on 2026-10-30, when the plan
merged `main`, which had taken 0040 for the Brain record and 0041–0043 since. Renumbered again from 0044 to 0045 on 2026-11-02, when the plan merged `main`, which had taken 0044 for the devbook subject state record. Renumbered a third time from 0045 to 0047 on 2026-11-06, when the plan merged `main`, which had taken 0045 for the dictation relay record; 0046 was skipped because the open `plan/c4-zoom-navigation` branch claims it. Amended in the same merge by §7 and §8, for devbook contracts 37 and 38; the History records each change.

A **local** decision, numbered in the local sequence. Every bare ADR number below
means the local one.

## Context

```meta
```

**The Domain area reads like a file browser.** A bounded context holds a fixed
set of files: its boundary, its domain, features, requirements, model, flow and
dependencies. Today each one is a page among pages, shown with its metadata. A
reader who wants to know what an aggregate guarantees opens the domain file, then
the invariants file, and puts the two together.

**Large contexts split their files.** The devbook convention lets a context
split `domain.md` into one file per aggregate, `domain.<name>.md`, each with its
own rules file. Fincent's `.devbook/domain/financieel/` is a context split that
way, and it is the worked example this decision is checked against. A flat list
of its files hides which rules belong to which aggregate.

**Functional and technical text are mixed.** Field types, how a domain service is
called and what an event carries sit beside the sentences that say what the
domain does. A reader after the second has to read past the first.

**The chapters already hold the words.** Every chapter has a heading, a first
sentence that says what it is, and the dependency tables say why each relation
exists. A view that needs new metadata would make every repository edit its
devbook before it reads well.

## Decision

```meta
```

| File in a context | In the menu | On the page |
| --- | --- | --- |
| `context.md` | The context's row | One page: the neighbours, then **Overview** |
| `dependencies.md` | No row | The context page's neighbours section, not a tab |
| `domain.md` | **Domain** | A unit picker |
| `domain.<name>.md` | A child of **Domain**, titled by its heading | **About** |
| `domain.<name>.invariants.md` | No row | **Always true** on `domain.<name>.md`'s page |
| `features.md`, `requirements.md`, `model.md` | Their kind's row | The kind's page; Model has **Diagram**, **Fields** and **Events** |
| A diagram a domain chapter opens with | No row | Drawn on its unit's Domain page, below **About** or the service's summary |
| A legacy `flow.md` | **Flow** | Its lifecycles, for a devbook from before contract 37 |
| `<kind>.<name>.md` | A child of its kind's row, titled by its heading | Its kind's page |
| `*.demo.html` | No row | A **Demo** button on its page |
| A file or folder named `_*` | Hidden | Nothing |

1. **The menu follows the reading order.** It lists a context's files in the
   devbook reading order (ADR 0016), so the order the convention gives is the
   order the reader sees.
2. **A split file is a child of its kind.** `<kind>.<name>.md` sits under its
   kind's row and is titled by its own heading. Each further dot nests one level
   deeper, so `domain.order.line.md` sits under `domain.order.md`.
3. **Some files are tabs, sections or buttons, not rows.**
   `domain.<name>.invariants.md` is the Always true tab of its page. A
   `*.demo.html` is a Demo button on its page. `dependencies.md` is the
   neighbours section at the top of its context's page, and the Overview follows
   below it. A context's page has no tabs, so every way into a context — the
   Context map, a related link, a `dependencies.md` link, the menu — shows it the
   same way. Every other file follows its kind, and a file whose name starts with
   `_` stays hidden.
4. **An unsplit domain picks in the page.** A context whose domain is the one
   `domain.md` offers its aggregates, domain services and Glossary as a chapter
   bar inside the Domain page, rather than as rows.
5. **The wording is the chapters'.** Headings show as written. A unit's summary
   is its chapter's first sentence. A relation among a context's neighbours
   reads the "Why" column of the dependency tables. No meta field is added.
6. **Technical detail lives on Model.** Field types, invocation semantics and
   event payloads show on Model only. The functional pages carry the domain's own
   language.
7. **A flow draws where its chapter is read.** Devbook contract 37 retired the
   flow page: each lifecycle and process opens the chapter it draws, under its
   `meta` block and a bold line naming it. The Domain page draws every such
   diagram of an aggregate, and of the typed chapters inside it, below its
   About card, and a domain service's below its summary, each in the
   lifecycle's own cards and lines, never as Mermaid. The menu stays derived
   from file names, so no Flow row stands for diagrams found inside a file;
   a devbook that still has a `flow.md` keeps its Flow row and page.
8. **No workflow state is read from a chapter.** Devbook contract 38 took
   `status` out of every domain chapter. The view reads a block's `rating`,
   else its `status`, as the devbook reader does, so a devbook past 38 shows
   no status badge and no **How settled** bar, and a legacy one still does.

## Consequences

```meta
```

Positive:

- **A split context reads as its aggregates.** In Fincent's
  `.devbook/domain/financieel/`, each aggregate is a row with its rules beside it
  as a tab, rather than two files the reader pairs by name.
- **Every devbook reads well as it is.** The view asks nothing of a repository:
  no field, no register, no reading-order file.
- **A functional reader is not stopped by types.** Model is where a developer
  looks, and the other pages stay in the domain's language.

Negative:

- **A chapter with a weak first sentence gets a weak summary.** The view shows
  what the chapter says. The fix is the chapter's, not the view's.
- **Two Domain views live side by side** until a later plan removes the old one
  and retires the switch.
- **A diagram outside the domain files is not drawn by the view.** Contract 37
  also opens feature and skill chapters with their flows; the Features page
  shows a feature's summary, not its prose, so those diagrams stay in the
  chapter as written.
- **The file name carries meaning.** A split file named outside the
  `<kind>.<name>.md` shape falls back to its kind's page as a page of its own.

## Options considered

```meta
```

| Option | Reads by unit | Asks the repository for | Outcome |
| --- | --- | --- | --- |
| Keep the file list | no | nothing | rejected |
| A metadata field naming each unit's parent and tab | yes | a field on every split chapter | rejected |
| Derive units from file names and kinds | yes | nothing | **chosen** |

- **Keep the file list.** It leaves the reader pairing a domain file with its
  rules and reading types beside behaviour.
- **A metadata field per unit.** It would let a file name anything as its
  parent, but every devbook would have to be edited before it read well, and the
  convention already says the same thing in the file name.

## Verification

```meta
```

Look for tests that show these behaviours:

1. **Switch off.** With `devbook-domain-view` off, the Domain area reads as before.
2. **Order.** The menu lists a context's files in the reading order.
3. **Nesting.** `domain.<name>.md` is a child of Domain, titled by its heading,
   and a further dot nests one level deeper.
4. **No row.** An invariants file, a demo and `dependencies.md` have no row, and
   show as Always true, a Demo button and the neighbours atop the context's page.
   A context's page has no tabs, however the reader reached it.
5. **Hidden.** A `_` file or folder is never listed.
6. **Unsplit domain.** One `domain.md` offers its aggregates, domain services and
   Glossary in a chapter bar.
7. **Wording.** Titles are headings as written, and summaries are first sentences.
8. **Model only.** No functional page shows a field type, an invocation detail or
   an event payload.
9. **Flows in place.** A lifecycle or process a domain chapter opens with is
   drawn on its unit's Domain page, and every diagram of the repository's
   domain files is one the view draws itself.

## History

```meta
```

| Date | Change | Why |
| --- | --- | --- |
| 2026-11-09 | Built the Demo button: one per demo above the page, hidden until pressed. | The last page element the plan had left unbuilt. |
| 2026-11-06 | Renumbered from 0045 to 0047. Amended by §7 and §8: a lifecycle or process draws on its unit's Domain page, below About or a service's summary, and the Flow row and page stay for a legacy `flow.md`; the view reads `rating ?? status` and shows no How settled bar where no chapter states one. | The plan merged `main` at devbook 1.35.0, whose contract 37 folds every `flow.md` into the chapters it draws and whose contract 38 takes workflow state out of the Markdown; `main` took 0045 and the open `plan/c4-zoom-navigation` claims 0046. |
| 2026-11-02 | Renumbered from 0044 to 0045. | The plan merged `main`, which took 0044 for the devbook subject state record. |
| 2026-10-30 | Renumbered from 0040 to 0044. | The plan merged `main`, which took 0040 for the Brain record and 0041–0043 since. |
| 2026-10-20 | Proposed: the Domain view reads a context by unit, its menu derived from file names alone. | The Domain area read like a file browser. |
