# Devbook rule text (test fixtures)

Verbatim copies of three rule files from the `devbook` plugin, taken from
`JSdotNet/ai-agent-stack` at commit `1cae539c687e15711969a60db659ac039ed09fd0`
(`plugins/devbook/rules/`), contract 19:

- `devbook-chapter-metadata.md`
- `devbook-domain.md`
- `devbook-annotations.md`

They are not instructions for this repository and nothing loads them as such.
`tests/Backlog.UI.Components.UnitTests/DevbookRuleTextContractTests.cs` reads
the value tables out of them and pins `DevbookSchema`, `DevbookStatus`,
`DevbookTypeMarkers` and `DevbookAnnotationFence` against them, the way
`DevbookSchemaContractTests` pins the database reader against
`tools/devbook/devbook-schema.mjs`. It also pins `DevbookSchema.ContractVersion`
to the contract named above.

To move to a newer contract, replace the three files with the new commit's
copies, update the commit and the contract above, and let the failing tests name
what changed.
