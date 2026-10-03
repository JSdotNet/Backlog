# The storybook's click demo

`features.demo.html` is devbook's sample demo, copied verbatim from
`plugins/devbook-procedures/assets/demo-sample/features.demo.html` in the devbook
repository (commit `10d8ba63`). The *Diagrams* page shows it in `DemoView`, which is
how the Domain devbook pane frames a page's demo: sandboxed with `allow-scripts`
only, the document as it is, and a `demo:goto` once the demo reports `demo:ready`.

Do not edit it here. Copy it again from devbook when the template changes. It is
embedded in the storybook assembly (see the csproj) so the story works wherever the
host runs.
