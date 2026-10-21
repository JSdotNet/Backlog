---
name: devbook-writing
description: How a devbook chapter reads. Plain sentences a new team member understands on first read, and a diagram wherever a picture tells the story better than prose.
---

# Writing a devbook chapter

Write a chapter for a person first: a new team member reading it without context. An agent
reads the same text, and plain sentences cost it less than packed ones.

This rule covers the prose. The folder's own rule says which sections a chapter has, and
`devbook-chapter-metadata.md` covers the `meta` block. Skills, agents, and rules use a terser
style meant for a model. Never copy that style into a chapter.

## Show it first

When the `show-me` skill is available, follow it for every chapter except `domain.md` and its
`domain.<name>.md` splits, which keep the diagrams `devbook-domain.md` names. Without it, draw
a Mermaid diagram when a passage describes one of these:

| The passage describes | Use |
| --- | --- |
| Three or more steps in order, or a request passing between parts | `sequenceDiagram` or `flowchart` |
| Something that moves through states | `stateDiagram-v2` |
| Three or more parts and how they connect | `flowchart` or `classDiagram` |
| Options or items compared on the same points | a table |

A Mermaid diagram stays inside one architecture element: its states and its flow. A
diagram whose boxes are people, systems, containers, components, or deployment nodes, and the
relationships between them, is a view of the C4 model, embedded as `devbook-arc42.md` says,
whichever skill or row above asked for it. A chapter outside `arc42/` links to the section
that embeds the view. Only a repository without an `arc42/` folder draws one in Mermaid.

Put the diagram before the prose. The prose then says only what the diagram cannot show: why
it works this way, the exceptions, and the limits. Never narrate a diagram box by box. Keep a
diagram to about nine nodes, and split a larger one.

## Sentences

- Write full sentences with a subject and a verb. Give each sentence one idea in under
  about 25 words, and each paragraph six sentences at most.
- Stack at most three nouns: "the policy that retries a payment order run", not "payment
  order run retry policy".
- Open a section with its point. Background comes after.
- Use at most one dash per paragraph. Never chain clauses with dashes or write fragments
  such as "no X, no Y, no Z": write sentences or a bulleted list.
- Use each term the glossary or ubiquitous language defines in that one meaning, never a
  synonym. Never coin a name, such as a "fill-run", unless the same change defines it there.
- Explain an abbreviation or code name at its first use in a chapter, or link to where it is
  defined.
- Use the active voice and name who acts: "Finance opens the gate", not "the gate is opened".
- Write in the language the chapter is in, as a native speaker would. Never carry English
  sentence structure into another language.
- Cut what the reader does not need: a closing summary, "note that", a hedge.

## Steps

Write a how-to as numbered steps. Open each with an imperative verb, and give it one action
in under about 20 words.

## A journey

A journey reads start to finish, in the order the actor lives it. Give each part
one `###`, titled as the outcome the actor reaches. Give each step one list item:
one action or one thing the actor sees, in under about 20 words. Write a journey
in English whatever language the other chapters use, because its steps are the
test's steps. Its shape is `devbook-chapter-metadata.md`'s.

## Example

Before:

> Het is de zichtbare kant van de poort van Financieel: staat die dicht, dan komt er in dit
> dossier geen euro in beweging — geen betaalopdracht-run, geen vul-run, geen betaalpoging.

After:

> Dit is de zichtbare kant van de poort van Financieel. Zolang die poort dicht is, gebeurt er
> in dit dossier niets met geld: er worden geen betaalopdrachten gemaakt, geen potjes
> aangevuld en geen betalingen geprobeerd.
