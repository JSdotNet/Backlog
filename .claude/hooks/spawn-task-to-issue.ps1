#!/usr/bin/env pwsh
<#
.SYNOPSIS
    PostToolUse hook: turns a suggested-task chip into a GitHub issue.

.DESCRIPTION
    Fires on mcp__ccd_session__spawn_task. Reads the hook payload from stdin,
    maps the suggestion onto `gh issue create`, and skips creation when an open
    issue with the same title already exists so repeated sessions do not stack
    duplicates. Always exits 0 on the happy path; failures are reported on
    stderr without blocking the tool call.
#>

$ErrorActionPreference = 'Stop'

$raw = [Console]::In.ReadToEnd()
if ([string]::IsNullOrWhiteSpace($raw)) { exit 0 }

try {
    $payload = $raw | ConvertFrom-Json
} catch {
    Write-Error "spawn-task-to-issue: could not parse hook payload: $_"
    exit 1
}

$task = $payload.tool_input
$title = $task.title
if ([string]::IsNullOrWhiteSpace($title)) { exit 0 }

# The chip is advisory; a failed lookup must never break the session.
try {
    $open = gh issue list --state open --limit 200 --json title | ConvertFrom-Json
    if ($open -and ($open.title -contains $title)) {
        Write-Output "spawn-task-to-issue: open issue '$title' already exists, skipped."
        exit 0
    }
} catch {
    Write-Error "spawn-task-to-issue: could not list existing issues: $_"
    exit 1
}

# spawn_task carries no type field, so classify from the wording and fall back
# to 'enhancement' — the label is a starting point for triage, not a verdict.
$text = "$title $($task.tldr)"
$label = switch -Regex ($text) {
    '(?i)\b(bug|broken|fails?|failing|incorrect|wrong|crash|regression|unreachable)\b' { 'bug'; break }
    '(?i)\b(docs?|documentation|readme|comment)\b' { 'documentation'; break }
    default { 'enhancement' }
}

$body = @()
if ($task.tldr) { $body += $task.tldr; $body += '' }
if ($task.prompt) { $body += '## Context'; $body += ''; $body += $task.prompt; $body += '' }
$body += '---'
$body += ''
$body += '_Filed automatically from a Claude Code suggested task._'
$bodyText = ($body -join "`n")

try {
    $url = gh issue create --title $title --body $bodyText --label $label
    Write-Output "spawn-task-to-issue: created $url"
} catch {
    Write-Error "spawn-task-to-issue: gh issue create failed: $_"
    exit 1
}

exit 0
