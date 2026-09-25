// Exercises the five write operations against a throwaway fixture repository:
// where `add` lands a note, that `reply` and `resolve` splice into the fence
// already there rather than reserializing it, that a swept note leaves no
// trace behind, that `sweep` takes every resolved fence and no open one, and
// that an ordinal means the same thing to all of them once a chapter has
// subchapters. Every case re-lints the written file, because a writer that
// produces something `--check` rejects is the failure that matters.
import { mkdtemp, mkdir, writeFile, readFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { add, reply, resolve, sweep, list } from "./annotations.mjs";
import { validateDocument } from "./metadata.mjs";

const FENCE = "```";
const REL = ".devbook/arc42/05-building-block-view.md";
const ADDRESS = `${REL}#devbook-meta`;

const SOURCE = [
    "# Building Block View",
    "",
    FENCE + "meta",
    "status: draft",
    FENCE,
    "",
    "## Devbook Meta",
    "",
    FENCE + "meta",
    "status: draft",
    FENCE,
    "",
    "The outline is one indexed range read.",
    "",
    FENCE + "annotation",
    "kind: question",
    "author: jobsc",
    "date: 2026-09-02",
    "quote: one indexed range read",
    "body: An existing thread.",
    "ext:",
    "  your-plugin:",
    "    raised-in: 2026-09-02",
    FENCE,
    "",
].join("\n");

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};

async function fixture(source) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-annotations-"));
    await mkdir(path.join(root, ".devbook", "arc42"), { recursive: true });
    await writeFile(path.join(root, REL), source, "utf8");
    return root;
}

async function lint(root) {
    const markdown = await readFile(path.join(root, REL), "utf8");
    return validateDocument(REL, markdown).filter((issue) => issue.severity === "error");
}

async function run(name, body, source = SOURCE) {
    const root = await fixture(source);
    try {
        await body(root);
        const errors = await lint(root);
        check(errors.length === 0, `${name}: the written file still lints clean`, errors.map((e) => e.message).join(" | "));
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}

await run("add --after", async (root) => {
    await add(root, ADDRESS, {
        after: "one indexed range read",
        author: "claude",
        date: "2026-09-03",
        body: "A second thread on the same passage.",
    });
    const threads = await list(root, ADDRESS);
    check(threads.length === 2, "add --after: the chapter now has two threads", String(threads.length));
    check(
        threads[1].body === "A second thread on the same passage.",
        "add --after: it lands under the notes already on that block, not above them",
        threads[1].body
    );
    check(threads[1].target === "block", "add --after: it annotates the passage", threads[1].target);
});

await run("add chapter-level", async (root) => {
    await add(root, ADDRESS, { author: "jobsc", date: "2026-09-03", body: "Whole-chapter note." });
    const threads = await list(root, ADDRESS);
    check(threads[0].target === "chapter", "add: with no --after it annotates the chapter", threads[0].target);
    check(threads[0].index === 1, "add: chapter-level lands first in document order", String(threads[0].index));
});

await run("add --after with no match", async (root) => {
    let threw = false;
    try {
        await add(root, ADDRESS, { after: "a phrase that is not there", author: "a", body: "b" });
    } catch {
        threw = true;
    }
    check(threw, "add: an unmatched --after refuses rather than guessing a position");
});

await run("reply", async (root) => {
    await reply(root, ADDRESS, 1, { author: "claude", date: "2026-09-03", body: "An answer." });
    const [thread] = await list(root, ADDRESS);
    check(thread.replies?.length === 1, "reply: the thread gained one reply", JSON.stringify(thread.replies));
    check(
        thread.ext?.["your-plugin"]?.["raised-in"] === "2026-09-02",
        "reply: splicing left the ext namespace untouched",
        JSON.stringify(thread.ext)
    );
    const markdown = await readFile(path.join(root, REL), "utf8");
    check(
        markdown.indexOf("replies:") < markdown.indexOf("ext:"),
        "reply: a new replies list goes above ext, not after it"
    );
});

await run("reply twice", async (root) => {
    await reply(root, ADDRESS, 1, { author: "a", date: "2026-09-03", body: "First." });
    await reply(root, ADDRESS, 1, { author: "b", date: "2026-09-03", body: "Second." });
    const [thread] = await list(root, ADDRESS);
    check(
        thread.replies?.length === 2 && thread.replies[1].body === "Second.",
        "reply: a second reply appends to the existing list in order",
        JSON.stringify(thread.replies)
    );
});

await run("reply with a multi-line body", async (root) => {
    await reply(root, ADDRESS, 1, { author: "a", date: "2026-09-03", body: "One.\nTwo." });
    const [thread] = await list(root, ADDRESS);
    check(
        thread.replies?.[0].body === "One.\nTwo.",
        "reply: a multi-line reply body round-trips as a block scalar",
        JSON.stringify(thread.replies?.[0].body)
    );
});

await run("resolve", async (root) => {
    await resolve(root, ADDRESS, 1);
    const [thread] = await list(root, ADDRESS);
    check(thread.status === "resolved", "resolve: the thread is resolved", thread.status);
    check(thread.kind === "question", "resolve: it left every other field alone", thread.kind);
    const open = await list(root, ADDRESS, { status: "open" });
    check(open.length === 0, "resolve: it drops out of the open list", String(open.length));
});

await run("resolve --delete", async (root) => {
    await resolve(root, ADDRESS, 1, { delete: true });
    const threads = await list(root, ADDRESS);
    check(threads.length === 0, "sweep: the note is gone", String(threads.length));
    const markdown = await readFile(path.join(root, REL), "utf8");
    check(!markdown.includes("annotation"), "sweep: no fence left behind");
    check(
        markdown.trimEnd().endsWith("The outline is one indexed range read."),
        "sweep: it took the blank line that separated the note from its passage",
        JSON.stringify(markdown.slice(-60))
    );
});

await run("resolve a missing index", async (root) => {
    let threw = false;
    try {
        await resolve(root, ADDRESS, 9);
    } catch {
        threw = true;
    }
    check(threw, "resolve: an index that is not there refuses rather than editing something else");
});

// --- Nesting ---------------------------------------------------------------
//
// An ordinal counts within one heading, never across the parent's line range:
// a fence under `### Bar` is Bar's first note, not `## Foo`'s second. `list`
// has always read it that way, so every write operation must too.

/** A parent chapter and a subchapter, each with a note on its own passage. */
const nested = ({ parentNote = true } = {}) =>
    [
        "# Building Block View",
        "",
        FENCE + "meta",
        "status: draft",
        FENCE,
        "",
        "## Foo",
        "",
        FENCE + "meta",
        "status: draft",
        FENCE,
        "",
        "The parent passage.",
        "",
        ...(parentNote
            ? [
                  FENCE + "annotation",
                  "author: jobsc",
                  "date: 2026-09-02",
                  "quote: The parent passage",
                  "body: A note on Foo.",
                  FENCE,
                  "",
              ]
            : []),
        "### Bar",
        "",
        FENCE + "meta",
        "status: draft",
        FENCE,
        "",
        "The subchapter passage.",
        "",
        FENCE + "annotation",
        "author: jobsc",
        "date: 2026-09-02",
        "quote: The subchapter passage",
        "body: A note on Bar.",
        FENCE,
        "",
    ].join("\n");

const FOO = `${REL}#foo`;
const BAR = `${REL}#bar`;

await run(
    "nested list",
    async (root) => {
        const foo = await list(root, FOO);
        check(
            foo.length === 1 && foo[0].body === "A note on Foo.",
            "list: a parent chapter shows its own note and not the subchapter's",
            JSON.stringify(foo.map((thread) => thread.body))
        );
        const bar = await list(root, BAR);
        check(
            bar.length === 1 && bar[0].index === 1 && bar[0].body === "A note on Bar.",
            "list: a subchapter numbers its notes from one",
            JSON.stringify(bar.map((thread) => [thread.index, thread.body]))
        );
    },
    nested()
);

await run(
    "nested resolve --delete",
    async (root) => {
        await resolve(root, FOO, 1, { delete: true });
        const markdown = await readFile(path.join(root, REL), "utf8");
        check(
            !markdown.includes("A note on Foo."),
            "sweep: index 1 on the parent took the parent's own note"
        );
        check(markdown.includes("A note on Bar."), "sweep: it left the subchapter's note where it was");
    },
    nested()
);

await run(
    "nested resolve past the end",
    async (root) => {
        let threw = false;
        try {
            await resolve(root, FOO, 2);
        } catch {
            threw = true;
        }
        check(threw, "resolve: index 2 on the parent refuses rather than reaching into the subchapter");
        const [bar] = await list(root, BAR);
        check(bar.status === "open", "resolve: the subchapter's note is untouched", bar.status);
    },
    nested()
);

// The reported failure: one note, under a subheading only. `list` on the
// parent printed nothing while `resolve --delete` on it swept the subchapter's
// note and exited 0.
await run(
    "nested resolve on an empty parent",
    async (root) => {
        const foo = await list(root, FOO);
        check(foo.length === 0, "list: a parent with no notes of its own has none", String(foo.length));
        let threw = false;
        try {
            await resolve(root, FOO, 1, { delete: true });
        } catch {
            threw = true;
        }
        check(threw, "sweep: index 1 on an empty parent refuses rather than deleting the subchapter's note");
        const [bar] = await list(root, BAR);
        check(bar?.body === "A note on Bar.", "sweep: the subchapter's note survives", JSON.stringify(bar));
    },
    nested({ parentNote: false })
);

await run(
    "nested reply",
    async (root) => {
        await reply(root, BAR, 1, { author: "claude", date: "2026-09-03", body: "An answer." });
        const [bar] = await list(root, BAR);
        check(
            bar.replies?.length === 1,
            "reply: index 1 on the subchapter lands in the subchapter's own fence",
            JSON.stringify(bar.replies)
        );
        const [foo] = await list(root, FOO);
        check(!foo.replies, "reply: the parent's note gained nothing", JSON.stringify(foo.replies));
    },
    nested()
);

await run(
    "nested resolve on a file address",
    async (root) => {
        let message = "";
        try {
            await resolve(root, REL, 1, { delete: true });
        } catch (error) {
            message = error.message;
        }
        check(
            message.includes("ambiguous"),
            "resolve: a file address where two chapters each have a note 1 refuses to guess",
            message
        );
        const markdown = await readFile(path.join(root, REL), "utf8");
        check(
            markdown.includes("A note on Foo.") && markdown.includes("A note on Bar."),
            "resolve: nothing was swept"
        );
    },
    nested()
);

// --- Sweep -----------------------------------------------------------------
//
// `resolve --delete` closes one loop by ordinal; the sweep closes the branch's.
// It must take every resolved fence in one pass and leave every open one where
// it was — which only holds if it deletes bottom-up, since removing a fence
// moves every line after it.

/** Three notes on one passage: resolved, open, resolved. */
const mixed = [
    "# Building Block View",
    "",
    FENCE + "meta",
    "status: draft",
    FENCE,
    "",
    "## Devbook Meta",
    "",
    FENCE + "meta",
    "status: draft",
    FENCE,
    "",
    "The outline is one indexed range read.",
    "",
    FENCE + "annotation",
    "status: resolved",
    "author: jobsc",
    "date: 2026-09-02",
    "body: First, answered.",
    FENCE,
    "",
    FENCE + "annotation",
    "kind: question",
    "author: jobsc",
    "date: 2026-09-02",
    "body: Second, still open.",
    FENCE,
    "",
    FENCE + "annotation",
    "status: resolved",
    "author: claude",
    "date: 2026-09-03",
    "body: Third, answered.",
    FENCE,
    "",
].join("\n");

await run(
    "sweep",
    async (root) => {
        const result = await sweep(root, ADDRESS);
        check(result.swept.length === 2, "sweep: it took both resolved notes", String(result.swept.length));
        check(result.remaining === 1, "sweep: it reports what it left", String(result.remaining));
        const threads = await list(root, ADDRESS);
        check(
            threads.length === 1 && threads[0].body === "Second, still open.",
            "sweep: the open note between two resolved ones survives intact",
            JSON.stringify(threads.map((thread) => thread.body))
        );
        check(threads[0].index === 1, "sweep: what is left renumbers from one", String(threads[0].index));
        const markdown = await readFile(path.join(root, REL), "utf8");
        check(!markdown.includes("answered."), "sweep: no resolved fence left behind");
        check(
            markdown.includes("The outline is one indexed range read."),
            "sweep: it took no prose with it"
        );
    },
    mixed
);

await run(
    "sweep twice",
    async (root) => {
        await sweep(root, ADDRESS);
        const again = await sweep(root, ADDRESS);
        check(
            again.swept.length === 0 && again.remaining === 1,
            "sweep: a second pass is a no-op",
            JSON.stringify(again)
        );
    },
    mixed
);

await run("sweep with nothing resolved", async (root) => {
    const result = await sweep(root, ADDRESS);
    check(result.swept.length === 0, "sweep: an open-only chapter loses nothing", String(result.swept.length));
    const threads = await list(root, ADDRESS);
    check(threads.length === 1, "sweep: the open note is still there", String(threads.length));
});

await run("sweep an unknown status", async (root) => {
    let threw = false;
    try {
        await sweep(root, ADDRESS, { status: "closed" });
    } catch {
        threw = true;
    }
    check(threw, "sweep: a status outside the closed set refuses rather than deleting nothing quietly");
});

await run(
    "sweep is chapter-scoped",
    async (root) => {
        await resolve(root, FOO, 1);
        await resolve(root, BAR, 1);
        const result = await sweep(root, FOO);
        check(result.swept.length === 1, "sweep: the parent address took one note", String(result.swept.length));
        const markdown = await readFile(path.join(root, REL), "utf8");
        check(!markdown.includes("A note on Foo."), "sweep: the parent's own resolved note is gone");
        check(
            markdown.includes("A note on Bar."),
            "sweep: a resolved note under a subheading is the subchapter's to sweep"
        );
    },
    nested()
);

await run(
    "sweep a file address",
    async (root) => {
        await resolve(root, FOO, 1);
        await resolve(root, BAR, 1);
        const result = await sweep(root, REL);
        check(result.swept.length === 2, "sweep: a file address sweeps every chapter in it", String(result.swept.length));
        const markdown = await readFile(path.join(root, REL), "utf8");
        check(
            !markdown.includes("A note on Foo.") && !markdown.includes("A note on Bar."),
            "sweep: both notes are gone"
        );
    },
    nested()
);

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
