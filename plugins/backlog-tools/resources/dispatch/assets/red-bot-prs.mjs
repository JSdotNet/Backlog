#!/usr/bin/env node
// The dispatch-sessions skill's mend step (local ADR 0038 §6): prints, one JSON line each, the
// open pull requests a bot opened in the given clone folder whose checks settled red and that no
// session watches yet, with the fixer session's fixed title and brief.
//
//   node red-bot-prs.mjs <clone-folder> [--sessions <file>]
//
// `--sessions` names a file holding the session host's list() answer (archived included) as JSON —
// each row's `name`, `prNumber` and `archived`, or Claude Desktop's `title`, `prNumber` and `isArchived` — a file, since a title may hold a
// quote no shell argument survives. Without it nothing counts as watched. Lines:
//   {"repository","number","head","title","brief"}   a pull request to start a fixer for
//   {"repository","number","skip"}                    one held back: watched, fixing or gave-up
// Exits 1 with the reason on stderr when gh cannot read the folder's repository.
// The decisions are in mend-logic.mjs, which the plugin's tests drive.

import { spawnSync } from "node:child_process";
import { readFileSync } from "node:fs";

import { fixersFor } from "./mend-logic.mjs";

function gh(folder, args) {
    const run = spawnSync("gh", args, { cwd: folder, encoding: "utf8" });
    if (run.status !== 0) throw new Error((run.stderr || run.error?.message || `gh ${args[0]} failed`).trim());
    return JSON.parse(run.stdout);
}

const args = process.argv.slice(2);
const folder = args[0];
if (!folder || folder.startsWith("--")) {
    console.error("usage: node red-bot-prs.mjs <clone-folder> [--sessions <file>]");
    process.exit(2);
}
const at = args.indexOf("--sessions");
let sessions = [];
if (at >= 0) {
    try {
        sessions = JSON.parse(readFileSync(args[at + 1], "utf8"));
        if (!Array.isArray(sessions)) throw new Error();
    } catch {
        console.error("--sessions does not name a file holding a JSON array");
        process.exit(2);
    }
}

try {
    const repository = gh(folder, ["repo", "view", "--json", "nameWithOwner"]).nameWithOwner;
    const open = gh(folder, ["pr", "list", "--state", "open", "--limit", "100", "--json", "number,isDraft,isCrossRepository,author,headRefOid,statusCheckRollup"]);
    for (const line of fixersFor(repository, open, sessions)) console.log(JSON.stringify(line));
} catch (error) {
    console.error(error.message);
    process.exit(1);
}
