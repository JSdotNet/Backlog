#!/usr/bin/env node
// Deterministic .NET package inventory for devbook:tech-update.

import { readdir, readFile, writeFile } from "node:fs/promises";
import path from "node:path";

const args = process.argv.slice(2);
const repoRoot = path.resolve(optionValue("--root") ?? process.cwd());
const outputPath = optionValue("--output");

const EXCLUDED_DIRECTORIES = new Set([
    ".git",
    ".vs",
    "bin",
    "obj",
    "node_modules",
    "packages",
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
        const fullPath = path.join(directory, entry.name);
        if (isDotnetManifest(entry.name, fullPath)) files.push(fullPath);
    }

    return files;
}

function isDotnetManifest(fileName, fullPath) {
    const normalized = fullPath.replace(/\\/g, "/");
    return (
        fileName.endsWith(".csproj") ||
        fileName === "Directory.Packages.props" ||
        fileName === "packages.config" ||
        fileName === "global.json" ||
        normalized.endsWith("/.config/dotnet-tools.json")
    );
}

function relativePath(fullPath) {
    return path.relative(repoRoot, fullPath).replace(/\\/g, "/");
}

function attributes(source) {
    const result = {};
    const pattern = /([A-Za-z_:][-A-Za-z0-9_:.]*)\s*=\s*"([^"]*)"/g;
    for (const match of source.matchAll(pattern)) {
        result[match[1]] = decodeXml(match[2]);
    }
    return result;
}

function decodeXml(value) {
    return value
        .replace(/&quot;/g, '"')
        .replace(/&apos;/g, "'")
        .replace(/&lt;/g, "<")
        .replace(/&gt;/g, ">")
        .replace(/&amp;/g, "&");
}

function innerText(xml, tagName) {
    const match = new RegExp(`<${tagName}[^>]*>([^<]*)<\\/${tagName}>`, "i").exec(xml);
    return match ? decodeXml(match[1].trim()) : null;
}

function splitTargetFrameworks(value) {
    if (!value) return [];
    return value
        .split(";")
        .map((entry) => entry.trim())
        .filter(Boolean)
        .sort((a, b) => a.localeCompare(b));
}

function parseProjectFile(content) {
    const targetFrameworks = new Set([
        ...splitTargetFrameworks(innerText(content, "TargetFramework")),
        ...splitTargetFrameworks(innerText(content, "TargetFrameworks")),
    ]);
    const packages = [];
    const projectReferences = [];

    const packagePattern = /<PackageReference\b([^>]*?)\/>|<PackageReference\b([^>]*?)>([\s\S]*?)<\/PackageReference>/gi;
    for (const match of content.matchAll(packagePattern)) {
        const attrs = attributes(match[1] ?? match[2] ?? "");
        const name = attrs.Include ?? attrs.Update;
        if (!name) continue;
        packages.push({
            name,
            version: attrs.Version ?? innerText(match[3] ?? "", "Version"),
            source: "PackageReference",
        });
    }

    const projectReferencePattern = /<ProjectReference\b([^>]*?)\/>|<ProjectReference\b([^>]*?)>[\s\S]*?<\/ProjectReference>/gi;
    for (const match of content.matchAll(projectReferencePattern)) {
        const attrs = attributes(match[1] ?? match[2] ?? "");
        if (attrs.Include) projectReferences.push(attrs.Include.replace(/\\/g, "/"));
    }

    return {
        type: "project",
        targetFrameworks: [...targetFrameworks],
        packages: sortPackages(packages),
        projectReferences: projectReferences.sort((a, b) => a.localeCompare(b)),
    };
}

function parseDirectoryPackages(content) {
    const packages = [];
    const packagePattern = /<(PackageVersion|GlobalPackageReference)\b([^>]*?)\/>|<(PackageVersion|GlobalPackageReference)\b([^>]*?)>([\s\S]*?)<\/\3>/gi;
    for (const match of content.matchAll(packagePattern)) {
        const tagName = match[1] ?? match[3];
        const attrs = attributes(match[2] ?? match[4] ?? "");
        const name = attrs.Include ?? attrs.Update;
        if (!name) continue;
        packages.push({
            name,
            version: attrs.Version ?? innerText(match[5] ?? "", "Version"),
            source: tagName,
        });
    }

    return { type: "central-package-management", packages: sortPackages(packages) };
}

function parsePackagesConfig(content) {
    const packages = [];
    for (const match of content.matchAll(/<package\b([^>]*?)\/>/gi)) {
        const attrs = attributes(match[1]);
        if (!attrs.id) continue;
        packages.push({
            name: attrs.id,
            version: attrs.version ?? null,
            targetFramework: attrs.targetFramework ?? null,
            source: "packages.config",
        });
    }

    return { type: "packages-config", packages: sortPackages(packages) };
}

function parseGlobalJson(content) {
    const parsed = JSON.parse(content);
    return {
        type: "global-json",
        sdkVersion: parsed.sdk?.version ?? null,
        rollForward: parsed.sdk?.rollForward ?? null,
    };
}

function parseDotnetTools(content) {
    const parsed = JSON.parse(content);
    const tools = Object.entries(parsed.tools ?? {}).map(([name, value]) => ({
        name,
        version: value.version ?? null,
        commands: [...(value.commands ?? [])].sort((a, b) => a.localeCompare(b)),
        source: "dotnet-tools",
    }));
    return { type: "dotnet-tools", packages: sortPackages(tools) };
}

function sortPackages(packages) {
    return packages.sort(
        (a, b) =>
            a.name.localeCompare(b.name) ||
            String(a.version ?? "").localeCompare(String(b.version ?? "")) ||
            String(a.source ?? "").localeCompare(String(b.source ?? ""))
    );
}

function summarizePackages(files) {
    const byName = new Map();
    for (const file of files) {
        for (const pkg of file.packages ?? []) {
            const entry = byName.get(pkg.name) ?? { name: pkg.name, versions: new Set(), files: new Set(), sources: new Set() };
            if (pkg.version) entry.versions.add(pkg.version);
            entry.files.add(file.path);
            if (pkg.source) entry.sources.add(pkg.source);
            byName.set(pkg.name, entry);
        }
    }

    return [...byName.values()]
        .map((entry) => ({
            name: entry.name,
            versions: [...entry.versions].sort((a, b) => a.localeCompare(b)),
            files: [...entry.files].sort((a, b) => a.localeCompare(b)),
            sources: [...entry.sources].sort((a, b) => a.localeCompare(b)),
        }))
        .sort((a, b) => a.name.localeCompare(b.name));
}

async function parseManifest(fullPath) {
    const content = await readFile(fullPath, "utf8");
    const fileName = path.basename(fullPath);
    const parsed = fileName.endsWith(".csproj")
        ? parseProjectFile(content)
        : fileName === "Directory.Packages.props"
          ? parseDirectoryPackages(content)
          : fileName === "packages.config"
            ? parsePackagesConfig(content)
            : fileName === "global.json"
              ? parseGlobalJson(content)
              : parseDotnetTools(content);

    return { path: relativePath(fullPath), ...parsed };
}

const files = [];
for (const fullPath of await walk(repoRoot)) {
    files.push(await parseManifest(fullPath));
}
files.sort((a, b) => a.path.localeCompare(b.path));
const packages = summarizePackages(files);

const document = {
    schemaVersion: 1,
    generatedBy: "tools/devbook-tech/dotnet-packages.mjs",
    ecosystem: "dotnet",
    files,
    packages,
    stats: {
        files: files.length,
        packages: packages.length,
    },
};

const json = `${JSON.stringify(document, null, 2)}\n`;
if (outputPath) {
    await writeFile(path.resolve(outputPath), json, "utf8");
} else {
    process.stdout.write(json);
}
