# Devbook rule text (test fixtures)

Verbatim copies of three rule files from the `devbook` plugin, taken from
`JSdotNet/ai-agent-stack` at commit `dade8777727a98c2bc39127201d2c05b8173a6da`
(`plugins/devbook/rules/`), contract 16:

- `devbook-chapter-metadata.md`
- `devbook-domain.md`
- `devbook-annotations.md`

They are not instructions for this repository and nothing loads them as such.
`tests/Backlog.UI.Components.UnitTests/DevbookRuleTextContractTests.cs` reads
the value tables out of them and pins `DevbookSchema`, `DevbookStatus`,
`DevbookTypeMarkers` and `DevbookAnnotationFence` against them, the way
`DevbookSchemaContractTests` pins the database reader against
`tools/devbook/devbook-schema.mjs`.

To move to a newer contract, replace the three files with the new commit's
copies, update the commit above, and let the failing tests name what changed.
