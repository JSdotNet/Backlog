# devbook-import fixture

A repository's default branch as the one-time devbook import reads it, for
`DevbookMarkdownImportTests` and `DevbookImportWorkerTests`. Each file is one row of the
import's acceptance criteria: a subject whose `status` imports, one that does not, a state
outside its subject's row, and open and resolved `annotation` fences.

Not a devbook of this repository. The devbook check reads `.devbook/` at the repository root
only, so nothing here is checked as a chapter; edit it as test data.
