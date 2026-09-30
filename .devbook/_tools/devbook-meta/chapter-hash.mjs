// chapter-hash.mjs — print the content fingerprint of one addressed chapter,
// the value that goes in `approved-hash` / `accepted-hash`.
//
// The gate writes the field; nobody types it. This is what the gate calls, and
// it is deliberately the same `chapterHash` the checker compares against, so a
// value written here can never disagree with the check that reads it.
//
// Dependency-free ESM against node built-ins, like everything else here.

import { readFile } from "node:fs/promises";
import { pathToFileURL } from "node:url";

import { CHANGES_ROOT, changePathParts, chapterHash, parseDocument, slugify } from "./metadata.mjs";
import { changeFingerprint } from "./delta.mjs";

const USAGE = `chapter-hash.mjs — the content fingerprint of an addressed chapter

  node chapter-hash.mjs <path>          the file block: the whole file
  node chapter-hash.mjs <path>#<slug>   one chapter: its heading and everything
                                        under it, to the next heading at the
                                        same or a higher level
  node chapter-hash.mjs openspec/changes/<name>
                                        a change, decided as one: its
                                        proposal.md and every delta together.
                                        The proposal's own path prints the same

Prints \`sha256:\` followed by eight lowercase hex characters. The \`meta\`
blocks and \`annotation\` fences are excluded and whitespace is normalised, so
a note or a reflowed paragraph does not change the value.`;

export async function main(argv) {
    const address = argv[0];
    if (!address || address === "--help" || address === "-h") {
        console.log(USAGE);
        return address ? 0 : 1;
    }

    const hashIndex = address.lastIndexOf("#");
    const filePath = hashIndex === -1 ? address : address.slice(0, hashIndex);
    const slug = hashIndex === -1 ? null : address.slice(hashIndex + 1);

    // A change is decided as one: its folder, or its proposal's file block,
    // fingerprints the proposal and every delta together.
    const folder = filePath.replace(/\\/g, "/").replace(/\/+$/, "");
    const change = changePathParts(folder.endsWith(".md") ? folder : `${folder}/proposal.md`);
    if (slug === null && change?.part === "proposal") {
        const hash = await changeFingerprint(process.cwd(), change.name);
        if (hash === null) {
            console.error(`No proposal.md in ${CHANGES_ROOT}/${change.name}/.`);
            return 1;
        }
        console.log(hash);
        return 0;
    }

    let markdown;
    try {
        markdown = await readFile(filePath, "utf8");
    } catch (error) {
        console.error(`Cannot read ${filePath}: ${error.message}`);
        return 1;
    }

    const { chapters } = parseDocument(markdown);
    if (slug === null) {
        const file = chapters.find((entry) => entry.level === 1);
        console.log(chapterHash(markdown, file ? file.line : 1));
        return 0;
    }

    const wanted = slugify(slug);
    const chapter = chapters.find((entry) => entry.slug === wanted);
    if (!chapter) {
        console.error(`No chapter "${slug}" in ${filePath}. Headings there: ${chapters.map((e) => e.slug).join(", ") || "(none)"}`);
        return 1;
    }

    console.log(chapterHash(markdown, chapter.line));
    return 0;
}

if (process.argv[1] && import.meta.url === pathToFileURL(process.argv[1]).href) {
    process.exit(await main(process.argv.slice(2)));
}
