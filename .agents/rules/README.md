# Repository rules

A rule that applies to one kind of file is authored **once** here and wrapped per host —
the devbook convention (`JSdotNet/devbook`, `.agents/rules/README.md`).

```
.agents/rules/<topic>.md                      the rule: name, description, paths. Host-neutral.
  ├── .claude/rules/<topic>.md                wrapper: paths copied verbatim → pointer
  └── .github/instructions/<topic>.instructions.md
                                              wrapper: applyTo = paths joined with commas,
                                              description copied → pointer
```

A wrapper is frontmatter and one sentence. It never restates the rule. Change a rule's
`paths` or `description` and both wrappers in the same commit.

A rule fires when a host **reads** a matching file, so authoring a file from scratch may not
trigger it. Open a sibling first, or read the rule directly. `CLAUDE.md` and
`.github/copilot-instructions.md` point at the rules by path for the same reason.

Naming inside the knowledge folders stays with the `knowledge-base` plugin's own instruction
files; `naming.md` here covers the rest of the repository.
