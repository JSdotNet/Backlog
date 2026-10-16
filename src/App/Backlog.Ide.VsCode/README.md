# Backlog — VS Code extension

Repo-aware IDE channel described in `.devbook/arc42/05-building-block-view.md#ide-extensions`.
It contributes a `Backlog Inbox` tree view and a `Backlog: Capture Selection` command
that talk to the sync service (`Backlog.Modules.Sync.Api`).

## Pair with the desktop

Every inbox route requires a paired device, so the extension joins your desktop's
owner the way a second device does (local ADR 0005):

1. In the desktop app, open **Settings > Devices** and choose **Generate a code**
   under **Let another device in**. The code is single-use and expires in minutes.
2. In VS Code, run **Backlog: Pair with Desktop** and type the code.

The extension redeems the code at `POST /api/sync/devices/pair` and keeps the device id
and credential it gets back in VS Code's secret storage — the OS credential store,
never settings or a log. Before each inbox call it trades the credential for a
short-lived token at `POST /api/sync/devices/token` and sends it as
`Authorization: Bearer`. Without a pairing it sends no inbox request at all.

When the inbox answers 401, the extension fetches a fresh token and tries once more.
When the token endpoint answers 401, the service no longer accepts this editor's
credential: the extension forgets it and offers **Pair with Desktop** again rather than
reporting the cloud as unavailable. **Backlog: Forget Pairing** drops the pairing by hand.

## Run

Aspire owns the build loop. The `ide-vscode` resource runs `npm run watch`; it is
registered with explicit start, so start it from the Aspire dashboard when you are
working on the extension.

To debug the extension itself, open this folder in VS Code and press `F5`.
Aspire assigns a dynamic port per run, so set `backlog.cloudUrl` to the `cloud`
resource URL shown in the Aspire dashboard.

## Test

```powershell
npm test
```

compiles the extension and runs `src/test/*.test.ts` with Node's built-in test runner.
The tests drive `src/sync-client.ts` — pairing, the token exchange and the inbox calls —
with a stub `fetch` and an in-memory secret store, so they need no VS Code and no
service. CI runs them in the `vscode-extension` job of `pull-request.yml`.
