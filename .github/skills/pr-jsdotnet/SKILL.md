---
name: pr-jsdotnet
description: 'Create a GitHub Pull Request in any JSdotNet repository through the `gh` CLI using JSdotNet account credentials for that command only. Use this skill when Copilot runs on a different account but the PR must be created as JSdotNet with a stable, repeatable workflow.'
---

# Create PR in JSdotNet Repositories

Create a GitHub Pull Request in any JSdotNet organization repository through the `gh` CLI using JSdotNet credentials for the PR command instead of the built-in Copilot App PR tool.

## Agent Requirement

This skill executes shell commands and requires access to `powershell` or `bash` tools.

**Switch to the default Copilot CLI agent first.** Specialized agents such as `architecture:architect`, `domain-design:domain-architect`, and `product-owner:product-owner` do not have shell tool access and cannot run `gh` commands. Before executing any step in this skill, ensure you are operating as the default Copilot CLI agent.

How to switch:
1. If you are currently under a specialized agent, close or exit that agent context.
2. Return to the standard Copilot CLI session where `powershell` and `bash` tools are available.
3. Then proceed with the steps below.

If switching is not possible in the current session, stop and ask the user to re-invoke the skill from the default Copilot CLI agent instead of attempting to run shell commands in a restricted context.

## Prerequisites

- `gh` CLI must be available in the shell (`gh --version`).
- JSdotNet credentials must be available through one of these sources:
  1. `JSDOTNET_GH_TOKEN`
  2. `COPILOT_GH_ACCOUNT_GITHUB_2E_COM_JSDOTNET`
  3. an existing `gh` keyring login for account `JSdotNet`
- The feature branch must already exist locally with at least one commit ahead of the base branch.
- Target repository can be any repo in the `JSdotNet` organization, such as `JSdotNet/Copilot`.

## Key Features

- **Deterministic PR creation** through `gh pr create` with JSdotNet credentials.
- **No App account switching required** for normal Copilot usage.
- **Branch naming** follows kebab-case conventions such as `add-github-copilot-integration`.
- **Title generation** follows clear, descriptive patterns.
- **Labels support** for categorization such as bug, feature, enhancement, and documentation.
- **Draft PR option** for early feedback.
- **Maintainer modification** flag for collaboration.
- **Clear fallback behavior** when the JSdotNet token or required CLI tools are unavailable.

## Required Workflow

When this skill is invoked, follow these steps in order:

### Step 0 — Switch to default agent

Confirm you are operating as the default Copilot CLI agent with `powershell` or `bash` tool access before proceeding (see **Agent Requirement** above).

### Step 1 — Verify token availability

Resolve JSdotNet credentials in this order:

1. `JSDOTNET_GH_TOKEN`
2. `COPILOT_GH_ACCOUNT_GITHUB_2E_COM_JSDOTNET`
3. existing `gh` keyring auth for `JSdotNet`

If neither environment variable is set, do **not** fail yet; continue to Step 2 and verify
whether `gh` already has a working `JSdotNet` keyring login.

```powershell
if (-not [string]::IsNullOrWhiteSpace($env:JSDOTNET_GH_TOKEN)) {
    $resolvedToken = $env:JSDOTNET_GH_TOKEN
} elseif (-not [string]::IsNullOrWhiteSpace($env:COPILOT_GH_ACCOUNT_GITHUB_2E_COM_JSDOTNET)) {
    $resolvedToken = $env:COPILOT_GH_ACCOUNT_GITHUB_2E_COM_JSDOTNET
}
```

### Step 2 — Verify auth and permissions

If a token was resolved, set `GH_TOKEN` from that token and confirm it works.
If no token was resolved, remove `GH_TOKEN`, switch `gh` to the stored `JSdotNet` keyring
account, and confirm that account works.

```powershell
if (-not [string]::IsNullOrWhiteSpace($resolvedToken)) {
    $env:GH_TOKEN = $resolvedToken
} else {
    Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
    gh auth switch -u JSdotNet
}
gh auth status
```

If `gh auth status` reports an error, or the account shown is not `JSdotNet`, stop and surface the exact error. Do not proceed to PR creation.
If neither a resolved token nor a working `JSdotNet` keyring login is available, stop with a
clear message explaining which credential sources were checked.

### Step 3 — Push branch if needed

Ensure the feature branch is pushed to the remote before calling `gh pr create`. A missing remote branch is the most common reason `gh pr create` fails silently.

```powershell
git push --set-upstream origin HEAD
```

If the push fails due to authentication, ensure the remote URL uses HTTPS and the token has write access to the repository.

### Step 4 — Create the PR

Create the PR with JSdotNet credentials, then immediately unset `GH_TOKEN` when a token source
was used.

```powershell
if (-not [string]::IsNullOrWhiteSpace($resolvedToken)) {
    $env:GH_TOKEN = $resolvedToken
} else {
    Remove-Item Env:GH_TOKEN -ErrorAction SilentlyContinue
    gh auth switch -u JSdotNet
}

gh pr create `
  --repo JSdotNet/Copilot `
  --base main `
  --head <branch> `
  --title "<title>" `
  --body "<body>"

Remove-Item Env:GH_TOKEN
```

Only set `GH_TOKEN` for this command block when a token source is being used. Remove it
immediately after to avoid contaminating subsequent `gh` calls with JSdotNet credentials.

### Step 5 — Confirm and sync

After successful creation, output the PR URL. Do not call the built-in `create_pull_request` tool after a successful `gh pr create`; the session context is already updated.

## Usage Patterns

### Create a feature PR

```text
Create a PR for my feature branch with:
- Title: "Add GitHub Copilot App integration"
- Description: Comprehensive summary of changes
- Labels: feature, copilot-app
- Use the JSdotNet account for `gh pr create`
```

### Create a draft PR for review

```text
Create a draft PR to get early feedback:
- Title: "WIP: Refactor plugin architecture"
- Labels: enhancement, work-in-progress
- Use JSdotNet credentials for the PR command
```

### Create a bug fix PR

```text
Create a PR to fix the issue:
- Title: "Fix plugin loading timeout error"
- Description: Root cause analysis and fix summary
- Labels: bug, high-priority
- Create the PR via `gh`, not the built-in PR tool
```

## Integration Points

- **GitHub Copilot App** keeps the normal Copilot account active for chat and coding.
- **`gh` CLI** creates the PR with JSdotNet credentials for that command only.
- **Copilot CLI** syncs PR information to session context after successful creation.
- **Product Owner plugin** can be used with GitHub Issues workflows.
- **Any JSdotNet repository** can use the same PR creation pattern.

## Guardrails

- Do not execute any step of this workflow from a specialized agent that lacks shell tool access; switch to the default agent first.
- Do not rely on prompt-only account switching for PR creation.
- Do not fall back to the built-in `create_pull_request` tool when the PR must be authored as `JSdotNet`.
- Prefer `JSDOTNET_GH_TOKEN` when present, then `COPILOT_GH_ACCOUNT_GITHUB_2E_COM_JSDOTNET`, then a stored `gh` keyring login for `JSdotNet`.
- If no JSdotNet credential source works, stop and surface exactly which sources were checked.
- If organization authorization or SSO is missing for the JSdotNet token, surface the exact `gh` error and stop.
- Always unset `GH_TOKEN` after the PR creation command to avoid credential leakage.

## Reference

Source skill location: `plugins/copilot-app/skills/pr-jsdotnet/SKILL.md`
