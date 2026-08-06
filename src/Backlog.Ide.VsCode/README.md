# Backlog — VS Code extension

Repo-aware IDE channel described in `.arc42/05-building-block-view.md#ide-extensions`.
It contributes a `Backlog Inbox` tree view and a `Backlog: Capture Selection` command
that talk to the cloud sync service (`Backlog.Cloud`).

## Run

Aspire owns the build loop. The `ide-vscode` resource runs `npm run watch`; it is
registered with explicit start, so start it from the Aspire dashboard when you are
working on the extension.

To debug the extension itself, open this folder in VS Code and press `F5`.
Aspire assigns a dynamic port per run, so set `backlog.cloudUrl` to the `cloud`
resource URL shown in the Aspire dashboard.
