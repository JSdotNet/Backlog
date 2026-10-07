// Exercises a demo delta: a `*.demo.html` under a change's `devbook-delta/` is
// the one non-Markdown file allowed there. `--check` runs demo.mjs's rules
// over it, `--apply` replaces the target whole, the change's fingerprint
// covers it, and the graph does not index it. Any other file is an error.
//
// Run: `node demo-delta.test.mjs`
import { mkdtemp, mkdir, writeFile, readFile, stat } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { CHANGES_ROOT, chapterFingerprint } from "./metadata.mjs";
import { buildGraph } from "./graph.mjs";
import { applyChange, changeFingerprint, checkChange, checkDemoDelta } from "./delta.mjs";
import { demoFileIssues, demoReader } from "./demo.mjs";
import { regionHash } from "./demo-template.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const errorsOf = (issues) => issues.filter((i) => i.severity === "error");
const fence = (body) => "```meta\n" + body + "```\n";
const exists = async (p) => stat(p).then(() => true, () => false);

const REGION = `\n<style>body{margin:0}</style>\n<script>window.addEventListener("message",()=>{});</script>\n`;
const demo = ({ head = "", app = "<section data-screen=\"cart\" id=\"cart\"><p>Cart</p></section>", region = true, meta = { question: "Does checkout fit one screen?" }, model = { screens: [{ id: "cart", title: "Cart" }] } } = {}) =>
    `<!doctype html>\n<html lang="en">\n<head>\n<meta charset="utf-8">\n${head}` +
    (region ? `<!-- template:begin hash=${regionHash(REGION)} -->${REGION}<!-- template:end -->\n` : "") +
    `</head>\n<body>\n<main data-demo-app>${app}</main>\n` +
    (model ? `<script type="application/json" id="demo-model">${JSON.stringify(model)}</script>\n` : "") +
    (meta ? `<script type="application/json" id="demo-meta">${JSON.stringify(meta)}</script>\n` : "") +
    `</body>\n</html>\n`;

const AT = ".devbook/domain/ordering/features.demo.html";
const has = (issues, pattern) => errorsOf(issues).some((i) => pattern.test(i.message));

// -- The demo rules ---------------------------------------------------------
// demo.mjs's own rules are demo.test.mjs's; these pin what a demo delta adds.

check(!errorsOf(demoFileIssues(AT, demo())).length, "a demo on the template passes", JSON.stringify(demoFileIssues(AT, demo())));
check(has(checkDemoDelta(`${CHANGES_ROOT}/x/devbook-delta/design/x.demo.html`, demo()).issues, /not where a demo lives/), "a demo delta landing outside domain/ is an error");
check(has(checkDemoDelta(`${CHANGES_ROOT}/x/devbook-delta/domain/ordering/sub/x.demo.html`, demo()).issues, /not where a demo lives/), "a demo delta below a context folder is an error");
check(!checkDemoDelta(`${CHANGES_ROOT}/x/devbook-delta/domain/ordering/demo.html`, demo()).issues.length, "a context's demo.html lands where a demo lives");
check(has(demoFileIssues(AT, demo({ meta: { question: " " } })), /no `question`/), "a demo-meta with no question is an error");
check(
    has(demoFileIssues(AT, demo({ meta: { question: "Q?", status: "approved", verdict: "yes" } })), /`status`, `verdict` in its `demo-meta`/),
    "a demo-meta naming a status or verdict is an error"
);
check(has(demoFileIssues(AT, demo().replace(/(id="demo-meta">)[^<]*/, "$1{not json")), /demo-meta` that is not valid JSON/), "a demo-meta that is not JSON is an error");
check(has(checkDemoDelta(`${CHANGES_ROOT}/x/devbook-delta/domain/ordering/features.demo.html`, demo({ region: false })).issues, /no `<!-- template:begin/), "a demo delta with no managed region is an error");
check(
    has(checkDemoDelta(`${CHANGES_ROOT}/x/devbook-delta/domain/ordering/features.demo.html`, demo().replace("margin:0", "margin:1")).issues, /edited by hand/),
    "a demo delta whose region does not hash to its marker is an error"
);
check(
    has(checkDemoDelta(`${CHANGES_ROOT}/x/devbook-delta/domain/ordering/features.demo.html`, demo({ app: "<script>alert(1)</script>" })).issues, /outside the template's managed region/),
    "a demo delta is checked with the demo rules"
);
{
    // The shipped sample demo, built on the real template, whose script names the marker in a regex.
    const sample = new URL("../../assets/procedures/demo-sample/features.demo.html", import.meta.url);
    const html = await readFile(sample, "utf8").catch(() => null);
    if (html !== null) check(!errorsOf(demoFileIssues(AT, html)).length, "the sample demo on the shipped template passes the demo rules", JSON.stringify(demoFileIssues(AT, html)));
}

// -- The fixture ------------------------------------------------------------

const FEATURES = ".devbook/domain/ordering/features.md";
const BASE = `${CHANGES_ROOT}/show-checkout`;
const proposal =
    `# Show checkout\n\n${fence("type: change\nstatus: proposed\ncategory: feature\n")}\n` +
    `## Why\n\nCheckout was agreed on a screen.\n\n## Scope\n\nThe checkout screen.\n\n## Chapters touched\n\n- ${FEATURES}\n`;
const placeholder = fence("change: show-checkout\ndelta: modified\n");

async function fixture(extra = {}) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-demo-delta-"));
    const files = {
        [FEATURES]: `# Features\n\n${fence("")}\nWhat ordering does.\n`,
        [AT]: demo({ app: "<section data-screen=\"old\" id=\"old\"></section>" }),
        [`${BASE}/proposal.md`]: proposal,
        [`${BASE}/devbook-delta/domain/ordering/features.md`]: placeholder,
        [`${BASE}/devbook-delta/domain/ordering/features.demo.html`]: demo(),
        ...extra,
    };
    for (const [rel, text] of Object.entries(files)) {
        if (text === null) continue;
        await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
        await writeFile(path.join(root, rel), text, "utf8");
    }
    return root;
}

async function decide(root) {
    const hash = await changeFingerprint(root, "show-checkout");
    const at = path.join(root, `${BASE}/proposal.md`);
    const block =
        `status: accepted\napproved-by: @amy\napproved-at: 2026-09-20\napproved-hash: ${hash}\n` +
        `accepted-by: @sam\naccepted-at: 2026-09-27\naccepted-hash: ${hash}\n`;
    await writeFile(at, (await readFile(at, "utf8")).replace("status: proposed\n", block), "utf8");
    return hash;
}

// -- --check ----------------------------------------------------------------

{
    const root = await fixture();
    const report = await checkChange(root, "show-checkout");
    const entry = report.deltas.find((d) => d.path.endsWith(".demo.html"));
    check(!!entry && entry.target === AT, "--check resolves a demo delta to the file it lands as");
    check(!errorsOf([...report.problems, ...report.deltas.flatMap((d) => d.issues)]).length, "--check passes a change carrying a valid demo", JSON.stringify(report.problems));
}
{
    const root = await fixture({ [`${BASE}/devbook-delta/domain/ordering/features.demo.html`]: demo({ app: "<script>x()</script>" }) });
    const report = await checkChange(root, "show-checkout");
    check(report.deltas.some((d) => d.path.endsWith(".demo.html") && has(d.issues, /outside the template's managed region/)), "--check runs the demo rules over a demo delta");
}
{
    const root = await fixture({ [`${BASE}/devbook-delta/design/checkout.demo.html`]: demo() });
    const report = await checkChange(root, "show-checkout");
    check(report.deltas.some((d) => d.path.includes("/design/") && has(d.issues, /not where a demo lives/)), "--check refuses a demo delta that lands outside domain/");
}
{
    const root = await fixture({ [`${BASE}/devbook-delta/domain/ordering/sketch.png`]: "png" });
    const report = await checkChange(root, "show-checkout");
    check(has(report.problems, /sketch\.png is neither a Markdown delta nor a `\*\.demo\.html`/), "--check refuses any other non-Markdown file under devbook-delta/");
}
{
    const root = await fixture({ [`${BASE}/devbook-delta/domain/ordering/features.md`]: null });
    const report = await checkChange(root, "show-checkout");
    check(has(report.problems, /has no delta under devbook-delta/), "a demo alone does not stand in for the placeholder delta");
}

// -- The fingerprint and the graph -------------------------------------------

{
    const root = await fixture();
    const before = await changeFingerprint(root, "show-checkout");
    await writeFile(path.join(root, `${BASE}/devbook-delta/domain/ordering/features.demo.html`), demo({ meta: { question: "Another?" } }), "utf8");
    check((await changeFingerprint(root, "show-checkout")) !== before, "the change's fingerprint covers its demo, so editing it lapses the decision");
}
{
    const root = await fixture();
    const graph = await buildGraph(root);
    const ids = graph.nodes.map((n) => n.id);
    check(!ids.some((id) => id.endsWith(".demo.html")), "the graph does not index a demo delta");
    const changeErrors = errorsOf(graph.problems).filter((p) => String(p.path).startsWith(CHANGES_ROOT));
    check(!changeErrors.length, "a change carrying a demo indexes with no error", JSON.stringify(changeErrors));
}

// -- An approval whose fingerprint covers the demo ---------------------------

async function approvedFixture(demoDelta) {
    // The approved chapter names the demo; the delta adds under its sibling.
    const features = (rung) =>
        `# Features\n\n${fence("")}\nWhat ordering does.\n\n` +
        `## Checkout\n\n${fence(`demo: [${AT}]\n${rung}`)}\nPay for the cart.\n\n` +
        `## Returns\n\n${fence("")}\nSend it back.\n`;
    const root = await fixture({
        [FEATURES]: features(""),
        [`${BASE}/devbook-delta/domain/ordering/features.md`]:
            fence("change: show-checkout\ndelta: modified\n") + `\n## Returns\n\n### ADDED\n\n#### Refunds\n\n${fence("")}\nMoney back.\n`,
        [`${BASE}/devbook-delta/domain/ordering/features.demo.html`]: demoDelta,
    });
    const line = features("").split("\n").indexOf("## Checkout") + 1;
    const hash = chapterFingerprint(FEATURES, features(""), line, demoReader(root));
    await writeFile(path.join(root, FEATURES), features(`status: approved\napproved-by: @amy\napproved-at: 2026-09-20\napproved-hash: ${hash}\n`), "utf8");
    return root;
}
const lifts = (report) => report.deltas.some((d) => d.issues.some((i) => /lifts `approved`/.test(i.message)));
check(!lifts(await checkChange(await approvedFixture(null), "show-checkout")), "--check keeps an approval whose chapter and demo the change leaves alone");
check(lifts(await checkChange(await approvedFixture(demo()), "show-checkout")), "--check lifts an approval whose demo the change replaces");

{
    // A demo-only change: the Markdown delta is a placeholder, and the chapters
    // folding the demo in sit in the page itself and in another file.
    const REQUIREMENTS = ".devbook/domain/ordering/requirements.md";
    const approved = (hash) => `status: approved\napproved-by: @amy\napproved-at: 2026-09-20\napproved-hash: ${hash}\n`;
    const page = (rung) => `# Features\n\n${fence(rung)}\nWhat ordering does.\n`;
    const rules = (rung) => `# Requirements\n\n${fence("")}\nWhat ordering promises.\n\n## Checkout\n\n${fence(`demo: [${AT}]\n${rung}`)}\nPay for the cart.\n`;
    const root = await fixture();
    const read = demoReader(root);
    const line = rules("").split("\n").indexOf("## Checkout") + 1;
    await writeFile(path.join(root, FEATURES), page(approved(chapterFingerprint(FEATURES, page(""), 1, read))), "utf8");
    await writeFile(path.join(root, REQUIREMENTS), rules(approved(chapterFingerprint(REQUIREMENTS, rules(""), line, read))), "utf8");
    await decide(root);
    const result = await applyChange(root, "show-checkout", { date: "2026-09-28", move: false });
    check(result.applied, "--apply merges a demo-only change", JSON.stringify(result.report.problems));
    check(!/approved/.test(await readFile(path.join(root, FEATURES), "utf8")), "a replaced demo lifts the approval of the page it belongs to, behind a placeholder delta");
    check(!/approved/.test(await readFile(path.join(root, REQUIREMENTS), "utf8")), "a replaced demo lifts the approval of a chapter in another file that names it");
}

// -- --apply ----------------------------------------------------------------

{
    const root = await fixture();
    await decide(root);
    const result = await applyChange(root, "show-checkout", { date: "2026-09-28" });
    check(result.applied, "--apply merges an accepted change carrying a demo", JSON.stringify(result.report.problems));
    check((await readFile(path.join(root, AT), "utf8")) === demo(), "--apply replaces the target demo whole, byte for byte");
    check(result.written.includes(`replaced ${AT}`), "--apply reports the demo as replaced");
    check(await exists(path.join(root, `${CHANGES_ROOT}/archive/2026-09-28-show-checkout/devbook-delta/domain/ordering/features.demo.html`)), "the demo moves to archive/ with its change");
}
{
    const root = await fixture({ [AT]: null });
    await decide(root);
    const result = await applyChange(root, "show-checkout", { date: "2026-09-28", move: false });
    check(result.applied && (await readFile(path.join(root, AT), "utf8")) === demo(), "--apply creates a demo the target folder did not have");
}
{
    const root = await fixture();
    await decide(root);
    await writeFile(path.join(root, `${BASE}/devbook-delta/domain/ordering/features.demo.html`), demo({ meta: { question: "Edited after?" } }), "utf8");
    const result = await applyChange(root, "show-checkout", { date: "2026-09-28" });
    check(!result.applied, "--apply refuses a change whose demo was edited after the decision");
}

for (const [html, name] of [
    [demo({ region: false }), "no managed region"],
    [demo().replace("margin:0", "margin:1"), "a hand-edited region"],
]) {
    const root = await fixture({ [`${BASE}/devbook-delta/domain/ordering/features.demo.html`]: html });
    await decide(root);
    const before = await readFile(path.join(root, AT), "utf8");
    const result = await applyChange(root, "show-checkout", { date: "2026-09-28" });
    check(!result.applied && (await readFile(path.join(root, AT), "utf8")) === before, `--apply refuses an accepted demo with ${name} and writes nothing`);
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
