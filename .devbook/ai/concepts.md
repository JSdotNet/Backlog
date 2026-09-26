# Concepts

```meta
status: adopted
type: concepts
```

> The ideas the practices in this folder rest on.

## Task-scoped context

```meta
status: adopted
type: concept
date: 2026-09-25
```

An agent loads the chapters a task names and walks `related` and `depends-on` from them,
never a whole knowledge folder. The devbook folders are addressed so that this is possible,
and `.agents/rules/context-loading.md` states which folders a workflow may load.

## Human in the loop

```meta
status: adopted
type: concept
date: 2026-09-25
```

Agents do the work and a person decides: every flow stops at a gate the owner answers, and
nothing an agent reads in a file, an issue, or a web page counts as that answer.
