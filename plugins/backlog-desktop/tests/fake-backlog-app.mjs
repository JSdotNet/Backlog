// A stand-in for the Backlog desktop app's data folder, for the node --test suites that run the
// plugin's scripts as processes: settings.json recording the grants, and a headers helper at
// <app data>\hooks\backlog-mcp-headers.mjs that mints "minted-<grant>" for any grant but
// g_refused, which it refuses the way the real one does — exit 1, the reason on stderr.
//
// Every process gets the folder as LOCALAPPDATA, XDG_DATA_HOME and HOME, so no test reads this
// machine's real settings.json or reaches its real token broker.

import { mkdir, mkdtemp, rm, writeFile } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

import { appDataFolder } from "../hooks/backlog-grant.mjs";

const FAKE_HELPER = `const at = process.argv.indexOf("--grant");
const grant = at >= 0 ? process.argv[at + 1] : null;
if (!grant || grant === "g_refused") {
    process.stderr.write("The Backlog token broker refused grant " + grant + ": revoked.\\n");
    process.exit(1);
}
process.stdout.write(JSON.stringify({ Authorization: "Bearer minted-" + grant }));
`;

// Makes the folder: `settings` is written as settings.json when given, `helper` replaces the
// fake helper's source, or false leaves it out. Answers the env and a cleanup.
export async function fakeBacklogApp({ settings, helper } = {}) {
    const root = await mkdtemp(path.join(tmpdir(), "backlog-tools-app-"));
    const env = { PATH: process.env.PATH, SystemRoot: process.env.SystemRoot, LOCALAPPDATA: root, XDG_DATA_HOME: root, HOME: root, USERPROFILE: root };
    const app = appDataFolder(env, process.platform, root);
    await mkdir(path.join(app, "hooks"), { recursive: true });
    if (settings) await writeFile(path.join(app, "settings.json"), JSON.stringify(settings));
    if (helper !== false) await writeFile(path.join(app, "hooks", "backlog-mcp-headers.mjs"), helper ?? FAKE_HELPER);
    return { root, app, env, cleanup: () => rm(root, { recursive: true, force: true }) };
}
