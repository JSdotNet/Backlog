---
name: backlog-import-inbox
description: Turn an export from another to-do tool (Microsoft To Do first) into a Backlog inbox import manifest — Markdown with front matter, one item per open task, completed tasks dropped — together with a review view of it, always, ready for the Sources panel's Import file.
disable-model-invocation: true
---

# Build an inbox import manifest

Open the reply with `backlog-tools@<version>`, `version` read from `../../.claude-plugin/plugin.json`, not recalled.

A one-shot handoff: a person wants the loose items they keep in another tool brought into
Backlog's Inbox, once or again later without doubling them. This skill reads the export,
writes one manifest plus a review view of it, and stops. It never talks to the Backlog app,
the source tool, or GitHub. Import is the app's: **Import file…** in the Inbox's Sources
panel reads the manifest (local ADR 0017,
`.devbook/arc42/adr/0017-inbox-import-is-a-capture-source-with-a-markdown-manifest.md`).

Read `assets/inbox-import-manifest.md` before writing anything. It is the manifest's
grammar — every field's rule and a worked example — and the one output shape. A new tool
is taught to this skill as another row of [Formats](#formats), never as a second shape.

## Inputs

- **The export.** A file path, or the export pasted inline.
- **The tool.** Which tool it came from, when the export does not say; its slug becomes the
  manifest's `tool`. Sniffing (step 1) confirms or refuses it.
- **Label mapping.** Which of the tool's labels become Inbox tags: all, none, or the ones
  named. A label left out is dropped, and the report says so.
- **List mapping.** Which of the tool's lists file into which Inbox List. A list left out
  lands its items unfiled. Every Inbox List named has to exist in the Inbox before the
  import runs, because an import never creates one.
- **Output.** A folder, or "paste it here". Ask if neither is stated. The review view is
  produced either way.

Completed items are dropped. That is settled, so do not ask about it.

Ask for the two mappings after sniffing (step 1): list the tool's lists with their open
item counts, and its labels with theirs, so the person maps names they can see.

### Getting a Microsoft To Do export

Microsoft To Do has no export button. The route that keeps each task's stable id is
Microsoft Graph, read with Microsoft's own PowerShell module and nothing else. A task's id
is how a second import recognises the items the first one brought in. Without it, every
re-import duplicates. The README's
[Microsoft To Do export](../../README.md#microsoft-to-do-export) section compares this route
with the third-party exporters.

1. In PowerShell 7, install the module once:
   `Install-Module Microsoft.Graph.Authentication -Scope CurrentUser`.
2. Sign in with read-only access to tasks: `Connect-MgGraph -Scopes Tasks.Read`. Pick the
   account whose To Do you are exporting. Work, school, and personal accounts all work. If
   the sign-in refuses a personal account, open
   [Graph Explorer](https://developer.microsoft.com/graph/graph-explorer), sign in there,
   consent to `Tasks.Read` under **Modify permissions**, copy the token from the
   **Access token** tab, and run
   `Connect-MgGraph -AccessToken (ConvertTo-SecureString '<token>' -AsPlainText -Force)`.
3. Run the script below from the folder the export should land in. It writes
   `microsoft-todo-export.json`: every list, with every task in it, as Graph returns them.
4. Give that file to this skill. Delete it once the import is done, because it holds every
   task you have.

```powershell
function Get-All($uri) {
    $items = @()
    while ($uri) {
        $page = Invoke-MgGraphRequest -Method GET -Uri $uri
        $items += $page.value
        $uri = $page.'@odata.nextLink'
    }
    $items
}
$lists = foreach ($list in Get-All 'v1.0/me/todo/lists') {
    [ordered]@{
        id                = $list.id
        displayName       = $list.displayName
        wellknownListName = $list.wellknownListName
        tasks             = @(Get-All "v1.0/me/todo/lists/$($list.id)/tasks")
    }
}
[ordered]@{
    source     = 'microsoft-graph-todo'
    exportedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:ssZ')
    lists      = @($lists)
} | ConvertTo-Json -Depth 20 | Set-Content -Encoding utf8 microsoft-todo-export.json
```

A task's Graph id changes when the task is moved from one To Do list to another. A task
moved between two exports comes in a second time. Say so in the report whenever a
re-import is being prepared.

## Formats

| Tool (`tool`) | Sniff | Stable id | Captured at | Link | Labels | Lists | Completed |
|---|---|---|---|---|---|---|---|
| Microsoft To Do (`microsoft-todo`) | JSON with `source: microsoft-graph-todo`, or any JSON whose tasks carry `@odata.etag`, `status` and `createdDateTime` — a bare Graph `value` array included (then ask which list it is) | task `id` | `createdDateTime` | first `linkedResources[].webUrl`, else the first `http(s)` URL in the title or body | `categories` | the enclosing list's `displayName` | `status: completed` |

To add a tool, add a row here and a sniff in step 1.

## Workflow

1. **Sniff the format** against [Formats](#formats). An export that matches no row stops
   the run: say what it looks like and that this skill does not know that tool yet. Never
   guess a mapping for an unknown shape.
2. **Pick the stable id** from the row's id field. A task with no id gets the fallback id
   the grammar defines, and it is counted in the report.
3. **Drop completed tasks.** Keep every other status. Count what was dropped, per list.
4. **Normalise timestamps** to UTC, `YYYY-MM-DDTHH:MM:SSZ`, dropping fractional seconds.
   Graph's `createdDateTime` is already UTC, often with seven fractional digits.
5. **Keep the original link** as `url`, from the row's link rule. Leave it out when there
   is none. Never make one up.
6. **Map labels and lists** from the person's choices. A label or list outside the mapping
   is collected for the report rather than written.
7. **Write the notes**: the task's body, converted to Markdown when it is HTML. Then any
   checklist steps as `- [ ]`/`- [x]` lines. Then one closing line for the facts the
   manifest has no field for, e.g. `Due 2026-10-03 · high importance in Microsoft To Do.`
   Apply the grammar's rules for notes.
8. **Write `<tool>-inbox-import.md`** into the output folder, in the order the tool listed
   the tasks. Show it inline when the person asked for a paste.
9. **Build the review view**: copy `assets/inbox-import-review.html` and replace
   `{{MANIFEST_TITLE}}` with a short name, e.g. `Microsoft To Do inbox import`. Replace
   `{{MANIFEST}}` with the manifest verbatim, writing each `</script` as `<\/script` (the
   page reverses that), and change nothing else. Publish it as an artifact when the host
   offers one. Otherwise write it beside the manifest as `<tool>-inbox-import.html` and
   say to open it in a browser. The page parses the embedded manifest itself, so it cannot
   disagree with the file. When a check fails, fix the manifest and rebuild the view.
   Never patch the view.
10. **Report**: the manifest's path, the view's link or path, and the counts — items
    written, per Inbox List and unfiled; completed tasks dropped, per source list; ids
    that fell back; and every label and list left unmapped. Name the Inbox Lists that
    must exist before the import runs. Then stop: never open the app, import, or delete
    the export for the person.

## Output expectations

- One Markdown manifest matching `assets/inbox-import-manifest.md`, named
  `<tool>-inbox-import.md`, whose review view shows no failed check.
- No completed task in it, and no item without an `external_id`.
- A review view built from `assets/inbox-import-review.html` every time.
- No file changes beyond the manifest and its view. No call to the Backlog app, the source
  tool, or GitHub.

## Reference

- `assets/inbox-import-manifest.md` — the manifest grammar and a worked example.
- `assets/inbox-import-review.html` — the review view template. Its checks mirror the
  grammar.
