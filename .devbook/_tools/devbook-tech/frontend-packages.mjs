#!/usr/bin/env node
// Deterministic frontend package inventory for devbook:tech-update.

import { readdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";

const args = process.argv.slice(2);
const repoRoot = path.resolve(optionValue("--root") ?? process.cwd());
const outputPath = optionValue("--output");

const DEPENDENCY_SECTIONS = [
    "dependencies",
    "devDependencies",
    "peerDependencies",
    "optionalDependencies",
    "bundledDependencies",
];
const LOCK_FILES = new Set([
    "package-lock.json",
    "npm-shrinkwrap.json",
    "pnpm-lock.yaml",
    "yarn.lock",
    "bun.lockb",
    "bun.lock",
]);
const EXCLUDED_DIRECTORIES = new Set([
    ".git",
    ".next",
    ".nuxt",
    ".turbo",
    "bin",
    "build",
    "coverage",
    "dist",
    "node_modules",
    "obj",
    "out",
    "TestResults",
    "_meta",
]);

function optionValue(name) {
    const index = args.indexOf(name);
    return index === -1 ? null : args[index + 1];
}

async function walk(directory) {
    const entries = await readdir(directory, { withFileTypes: true });
    const files = [];

    for (const entry of entries.sort((a, b) => a.name.localeCompare(b.name))) {
        if (entry.isDirectory()) {
            if (EXCLUDED_DIRECTORIES.has(entry.name)) continue;
            files.push(...(await walk(path.join(directory, entry.name))));
            continue;
        }

        if (!entry.isFile()) continue;
        if (entry.name === "package.json" || LOCK_FILES.has(entry.name)) {
            files.push(path.join(directory, entry.name));
        }
    }

    return files;
}

function relativePath(fullPath) {
    return path.relative(repoRoot, fullPath).replace(/\\/g, "/");
}

function sortedObjectEntries(value) {
    return Object.entries(value ?? {}).sort(([a], [b]) => a.localeCompare(b));
}

function normalizeArray(value) {
    if (!Array.isArray(value)) return [];
    return [...value].map(String).sort((a, b) => a.localeCompare(b));
}

function dependenciesFromPackageJson(parsed) {
    const dependencies = [];

    for (const section of DEPENDENCY_SECTIONS) {
        const rawSection = parsed[section];
        if (Array.isArray(rawSection)) {
            for (const name of normalizeArray(rawSection)) {
                dependencies.push({ section, name, version: null });
            }
            continue;
        }

        for (const [name, version] of sortedObjectEntries(rawSection)) {
            dependencies.push({ section, name, version: String(version) });
        }
    }

    return dependencies.sort(
        (a, b) =>
            a.section.localeCompare(b.section) ||
            a.name.localeCompare(b.name) ||
            String(a.version ?? "").localeCompare(String(b.version ?? ""))
    );
}

function scriptsFromPackageJson(parsed) {
    return sortedObjectEntries(parsed.scripts).map(([name, command]) => ({
        name,
        command: String(command),
    }));
}

function enginesFromPackageJson(parsed) {
    return sortedObjectEntries(parsed.engines).map(([name, version]) => ({
        name,
        version: String(version),
    }));
}

async function parsePackageJson(fullPath) {
    const parsed = JSON.parse(await readFile(fullPath, "utf8"));
    return {
        path: relativePath(fullPath),
        type: "package-json",
        name: parsed.name ?? null,
        version: parsed.version ?? null,
        private: parsed.private ?? null,
        packageManager: parsed.packageManager ?? null,
        workspaces: normalizeWorkspaces(parsed.workspaces),
        engines: enginesFromPackageJson(parsed),
        scripts: scriptsFromPackageJson(parsed),
        dependencies: dependenciesFromPackageJson(parsed),
    };
}

function normalizeWorkspaces(workspaces) {
    if (Array.isArray(workspaces)) return normalizeArray(workspaces);
    if (Array.isArray(workspaces?.packages)) return normalizeArray(workspaces.packages);
    return [];
}

function parseLockFile(fullPath) {
    return {
        path: relativePath(fullPath),
        type: "lock-file",
        lockFile: path.basename(fullPath),
    };
}

function summarizePackages(files) {
    const byName = new Map();

    for (const file of files) {
        for (const dependency of file.dependencies ?? []) {
            const entry = byName.get(dependency.name) ?? {
                name: dependency.name,
                versions: new Set(),
                sections: new Set(),
                files: new Set(),
            };
            if (dependency.version) entry.versions.add(dependency.version);
            entry.sections.add(dependency.section);
            entry.files.add(file.path);
            byName.set(dependency.name, entry);
        }
    }

    return [...byName.values()]
        .map((entry) => ({
            name: entry.name,
            versions: [...entry.versions].sort((a, b) => a.localeCompare(b)),
            sections: [...entry.sections].sort((a, b) => a.localeCompare(b)),
            files: [...entry.files].sort((a, b) => a.localeCompare(b)),
        }))
        .sort((a, b) => a.name.localeCompare(b.name));
}

const files = [];
for (const fullPath of await walk(repoRoot)) {
    if (path.basename(fullPath) === "package.json") {
        files.push(await parsePackageJson(fullPath));
    } else {
        files.push(parseLockFile(fullPath));
    }
}
files.sort((a, b) => a.path.localeCompare(b.path));
const packageJsonFiles = files.filter((file) => file.type === "package-json");
const packages = summarizePackages(packageJsonFiles);

const document = {
    schemaVersion: 1,
    generatedBy: "tools/devbook-tech/frontend-packages.mjs",
    ecosystem: "frontend",
    files,
    packages,
    stats: {
        files: files.length,
        packageJsonFiles: packageJsonFiles.length,
        packages: packages.length,
        lockFiles: files.filter((file) => file.type === "lock-file").length,
    },
};

const json = `${JSON.stringify(document, null, 2)}\n`;
if (outputPath) {
    await writeFile(path.resolve(outputPath), json, "utf8");
} else {
    process.stdout.write(json);
}
