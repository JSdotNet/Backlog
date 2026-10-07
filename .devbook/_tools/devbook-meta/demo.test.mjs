// demo.test.mjs — the click demos: every rule the checker holds a
// `*.demo.html` and a `demo` address to, and the fingerprint that folds a demo
// into the page it belongs to.
//
// Each case writes a small repository to a temporary folder — one bounded
// context with a page-named demo — breaks it one way, and runs the graph build
// over it, which is what `build.mjs --check` runs.
//
// Run: node plugins/devbook/tools/devbook-meta/demo.test.mjs

import { mkdtemp, mkdir, writeFile, rm } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";

import { buildGraph } from "./graph.mjs";
import { chapterFingerprint, chapterHash, isPageNamedDemo, validateDocument } from "./metadata.mjs";
import { changeFingerprint, mergeDelta, parseDelta } from "./delta.mjs";
import { demoReader, DEMO_SIZE_TARGET } from "./demo.mjs";
import { main as hashMain } from "./chapter-hash.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const fence = (body) => "```meta\n" + body + "```\n";

const CONTEXT = ".devbook/domain/ordering";
const DEMO = `${CONTEXT}/features.demo.html`;

// The template's shape: screens carry their anchors, a panel switch is a key,
// a walkthrough step is an object.
const screen = (id, anchors = []) => ({ id, title: id, anchors });
const step = (screen, anchor) => (anchor ? { screen, anchor, text: screen } : { screen, text: screen });
const MODEL = {
    screens: [screen("checkout", ["pay"]), screen("declined")],
    walkthroughs: [{ id: "card-declined", title: "Card declined", steps: [step("checkout", "pay"), step("declined")] }],
    roles: [{ key: "buyer", label: "Buyer" }],
    flags: [{ key: "express", label: "Express" }],
    viewports: [{ key: "mobile", label: "Mobile" }],
};
const BODY =
    `<section id="checkout" data-screen><h1>Checkout</h1><button data-anchor="pay">Pay</button></section>\n` +
    `<section id="declined" data-screen><p>Declined</p></section>\n`;

const demoHtml = ({ model = MODEL, body = BODY, head = "" } = {}) =>
    `<!doctype html>\n<html><head><title>Ordering</title>\n` +
    `<!-- template:begin -->\n<style>body{margin:0}</style>\n` +
    `<script>window.addEventListener("message", () => {}); /* <section id="quoted" data-screen> */</script>\n` +
    `<!-- template:end -->\n` +
    `<script type="application/json" id="demo-meta">{"question":"Does checkout read on one screen?"}</script>\n` +
    `<script type="application/json" id="demo-model">${JSON.stringify(model)}</script>\n` +
    `${head}</head><body>\n${body}</body></html>\n`;

const contextMd = `# Ordering\n\n${fence("type: context\n")}\nOrders.\n`;
const featuresMd = (meta = `demo: [${DEMO}#checkout]\n`) =>
    `# Features\n\n${fence("type: features\n")}\n## Checkout\n\n${fence(`type: feature\n${meta}`)}\nPaying.\n`;
const requirementsMd = (demo = `${DEMO}#walkthrough/card-declined`) =>
    `# Requirements\n\n${fence("type: requirements\n")}\n` +
    `## Checkout\n\n${fence(`type: requirements\nrelated: [${CONTEXT}/features.md#checkout]\n`)}\n` +
    `### Requirement: Pay by card\n\n${fence(`type: requirement\ndemo: [${demo}]\n`)}\nThe buyer SHALL pay by card.\n\n` +
    `#### Scenario: Card declined\n\nWhen the card is declined, the buyer sees why.\n`;

const BASE = {
    [`${CONTEXT}/context.md`]: contextMd,
    [`${CONTEXT}/features.md`]: featuresMd(),
    [`${CONTEXT}/requirements.md`]: requirementsMd(),
    [DEMO]: demoHtml(),
};

const roots = [];
async function repo(overrides = {}) {
    const root = await mkdtemp(path.join(tmpdir(), "devbook-demo-"));
    roots.push(root);
    const files = { ...BASE, ...overrides };
    for (const [rel, text] of Object.entries(files)) {
        if (text == null) continue;
        await mkdir(path.dirname(path.join(root, rel)), { recursive: true });
        await writeFile(path.join(root, rel), text, "utf8");
    }
    return root;
}

/** The demo problems a build of `overrides` over the base reports. */
async function demoProblemsOf(overrides) {
    const { problems } = await buildGraph(await repo(overrides));
    return problems.filter((p) => /demo/i.test(p.message));
}
const has = (problems, severity, needle) => problems.some((p) => p.severity === severity && p.message.includes(needle));
const dump = (problems) => problems.map((p) => `[${p.severity}] ${p.message}`).join("\n        ");

// -- The base corpus is clean ---------------------------------------------

{
    const problems = await demoProblemsOf({});
    check(problems.length === 0, "a demo beside its page, every address resolving, reports nothing", dump(problems));
}

// -- Names and pairing ----------------------------------------------------

check(isPageNamedDemo(`${CONTEXT}/demo.html`), "demo.html is page-named, for context.md");
check(isPageNamedDemo(`${CONTEXT}/features.checkout.demo.html`), "a split page's demo is page-named");
check(!isPageNamedDemo(`${CONTEXT}/checkout-journey.demo.html`), "a name no page prescribes is not page-named");

{
    const problems = await demoProblemsOf({ [`${CONTEXT}/flow.demo.html`]: demoHtml() });
    check(has(problems, "error", "is named for .devbook/domain/ordering/flow.md, which does not exist"), "a page-named demo without its page errors", dump(problems));
}

{
    const problems = await demoProblemsOf({ [`${CONTEXT}/context.md`]: null, [`${CONTEXT}/demo.html`]: demoHtml() });
    check(has(problems, "error", "is named for .devbook/domain/ordering/context.md"), "demo.html without context.md errors", dump(problems));
}

{
    const problems = await demoProblemsOf({ [`${CONTEXT}/checkout-journey.demo.html`]: demoHtml() });
    check(has(problems, "error", "no chapter's `demo` field names it"), "a demo with no page-style name that no chapter names errors", dump(problems));
}

{
    const journey = `${CONTEXT}/checkout-journey.demo.html`;
    const problems = await demoProblemsOf({ [journey]: demoHtml(), [`${CONTEXT}/features.md`]: featuresMd(`demo: [${journey}#checkout]\n`) });
    check(problems.length === 0, "the same demo named from a chapter is placed by it", dump(problems));
}

// -- References -----------------------------------------------------------

{
    const problems = await demoProblemsOf({ [`${CONTEXT}/features.md`]: featuresMd(`demo: [${CONTEXT}/gone.demo.html#checkout]\n`) });
    check(has(problems, "error", "gone.demo.html does not exist"), "a `demo` reference that does not resolve errors", dump(problems));
}

{
    const problems = await demoProblemsOf({ [`${CONTEXT}/features.md`]: featuresMd(`demo: [${CONTEXT}/features.md#checkout]\n`) });
    check(has(problems, "error", "does not name a demo"), "a `demo` reference to a Markdown file errors", dump(problems));
}

for (const [address, needle, name] of [
    ["nowhere", 'screen "nowhere"', "an unlisted screen"],
    ["checkout/ghost", 'anchor "ghost" on screen "checkout"', "an unlisted anchor"],
    ["walkthrough/express", 'walkthrough "express"', "an unlisted walkthrough"],
    ["walkthrough/card-declined/3", "step 3", "a step past the walkthrough's last"],
    ["checkout?role=admin", 'role "admin"', "an unlisted role"],
    ["checkout?flags=express,beta", 'flag "beta"', "an unlisted flag"],
    ["checkout?vp=tv", 'viewport "tv"', "an unlisted viewport"],
]) {
    const problems = await demoProblemsOf({ [`${CONTEXT}/features.md`]: featuresMd(`demo: ["${DEMO}#${address}"]\n`) });
    check(has(problems, "error", needle), `a \`demo\` address naming ${name} errors`, dump(problems));
}

{
    const fine = ["", "#checkout/pay", "#walkthrough/card-declined/2", "#declined?role=buyer&vp=mobile"].map((f) => `"${DEMO}${f}"`).join(", ");
    const problems = await demoProblemsOf({ [`${CONTEXT}/features.md`]: featuresMd(`demo: [${fine}]\n`) });
    check(problems.length === 0, "a whole demo, an anchor, a step, and panel keys all resolve", dump(problems));
}

// -- A requirement's walkthrough ------------------------------------------

{
    const model = { ...MODEL, walkthroughs: [...MODEL.walkthroughs, { id: "express", steps: [step("checkout")] }] };
    const problems = await demoProblemsOf({
        [DEMO]: demoHtml({ model }),
        [`${CONTEXT}/requirements.md`]: requirementsMd(`${DEMO}#walkthrough/express`),
    });
    check(has(problems, "error", 'walkthrough "express" matches no `#### Scenario:`'), "a requirement's walkthrough matching none of its scenarios errors", dump(problems));
}

{
    const model = { ...MODEL, walkthroughs: [{ id: "scenario-card-declined", steps: [step("declined")] }] };
    const problems = await demoProblemsOf({
        [DEMO]: demoHtml({ model }),
        [`${CONTEXT}/requirements.md`]: requirementsMd(`${DEMO}#walkthrough/scenario-card-declined`),
    });
    check(problems.length === 0, "the scenario's full heading slug matches too", dump(problems));
}

// -- The file's own rules -------------------------------------------------

{
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body: BODY + `<section id="extra" data-screen></section>\n` }) });
    check(has(problems, "error", 'has screen "extra", which `demo-model` does not list'), "a screen missing from demo-model errors", dump(problems));
}

{
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body: BODY + `<section id="declined" data-screen></section>\n` }) });
    check(has(problems, "error", 'repeats screen id "declined"'), "a repeated screen id errors", dump(problems));
}

{
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ model: { ...MODEL, screens: [...MODEL.screens, screen("checkout")] } }) });
    check(has(problems, "error", 'lists screen "checkout" twice'), "a screen listed twice in demo-model errors", dump(problems));
}

{
    const body = BODY.replace("</section>", `<a data-anchor="help">Help</a></section>`);
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body }) });
    check(has(problems, "error", '`data-anchor` "help" on screen "checkout", which `demo-model` does not list'), "a data-anchor missing from demo-model errors", dump(problems));
}

{
    const body = BODY.replace("</section>", `<button data-anchor="pay">Pay again</button></section>`);
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body }) });
    check(has(problems, "error", 'repeats `data-anchor` "pay" on screen "checkout"'), "a data-anchor repeated on one screen errors", dump(problems));
}

{
    const body = BODY.replace("<p>Declined</p>", `<button data-anchor="pay">Pay</button>`);
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body }) });
    check(!has(problems, "error", "repeats `data-anchor`"), "the same anchor on two screens is two addresses", dump(problems));
}

{
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ head: `<script>document.title = "x";</script>\n` }) });
    check(has(problems, "error", "outside the template's managed region"), "a script outside the managed region errors", dump(problems));
}

for (const [html, name] of [
    [demoHtml({ head: `<script id="demo-meta">alert(1)</script>\n` }), "a second demo-meta script"],
    [demoHtml().replace(`<script type="application/json" id="demo-meta">`, `<script id="demo-meta">`), "a demo-meta not typed as JSON"],
]) {
    const problems = await demoProblemsOf({ [DEMO]: html });
    check(has(problems, "error", "outside the template's managed region"), `${name} is not exempt from the managed region`, dump(problems));
}

{
    const html = demoHtml().replace("<!-- template:begin -->", "").replace("<!-- template:end -->", "");
    const problems = await demoProblemsOf({ [DEMO]: html });
    check(has(problems, "error", "outside the template's managed region"), "with no managed region, the template's own script is outside it", dump(problems));
}

for (const [head, needle, name] of [
    [`<img src="https://cdn.example.com/logo.png">`, "https://cdn.example.com/logo.png", "an image URL"],
    [`<link rel="stylesheet" href="//fonts.example.com/a.css">`, "//fonts.example.com/a.css", "a protocol-relative stylesheet"],
    [`<style>body{background:url("https://x.example/bg.png")}</style>`, "url(https://x.example/bg.png)", "a CSS url()"],
    [`<style>@import "https://x.example/a.css";</style>`, "@import", "a CSS @import"],
]) {
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ head }) });
    check(has(problems, "error", needle) && has(problems, "error", "fetches from the network"), `${name} is reported as fetched from the network`, dump(problems));
}

{
    const html = demoHtml().replace("window.addEventListener", `fetch("/api"); window.addEventListener`);
    const problems = await demoProblemsOf({ [DEMO]: html });
    check(has(problems, "error", "fetch()"), "a fetch() call, even inside the managed region, is reported", dump(problems));
}

{
    const html = demoHtml({ body: BODY + `<a href="https://example.com/terms">Terms</a>\n` });
    const problems = await demoProblemsOf({ [DEMO]: html });
    check(!has(problems, "error", "fetches"), "a link a person follows is not a fetch", dump(problems));
}

{
    const padding = `<!-- ${"x".repeat(DEMO_SIZE_TARGET)} -->\n`;
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body: BODY + padding }) });
    check(has(problems, "warning", "past the 500 KB target"), "a demo over 500 KB warns", dump(problems));
    check(!problems.some((p) => p.severity === "error"), "and the size is never an error", dump(problems));
}

{
    const body = BODY.replace(`<section id="checkout" data-screen>`, `<section id="checkout" data-screen data-variant="list">`) +
        `<section id="checkout--board" data-screen data-variant="board"></section>\n`;
    const model = { ...MODEL, screens: [...MODEL.screens, screen("checkout--board")] };
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body, model }) });
    check(has(problems, "error", "carries 2 variants"), "a demo under domain/ with two variants errors", dump(problems));
}

{
    const body = BODY.replace(`<section id="checkout" data-screen>`, `<section id="checkout" data-screen data-variant="list">`);
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ body, model: { ...MODEL, variants: [{ key: "list", label: "List" }] } }) });
    check(!has(problems, "error", "variants"), "one variant is the agreed one", dump(problems));
}

{
    const model = { ...MODEL, walkthroughs: [{ id: "card-declined", steps: [step("checkout", "pay"), step("receipt"), step("checkout", "ghost")] }] };
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ model }) });
    check(has(problems, "error", 'walkthrough "card-declined" step 2 on screen "receipt"'), "a walkthrough step naming a missing screen errors", dump(problems));
    check(has(problems, "error", 'walkthrough "card-declined" step 3 on anchor "ghost"'), "a walkthrough step naming a missing anchor errors", dump(problems));
}

{
    // The template drops a walkthrough whose step plays an unlisted role or
    // switches an unlisted flag, and opens no home screen it does not list.
    const playing = (extra) => ({ ...MODEL, walkthroughs: [{ id: "card-declined", steps: [{ ...step("checkout"), ...extra }] }] });
    const role = await demoProblemsOf({ [DEMO]: demoHtml({ model: playing({ role: "admin" }) }) });
    check(has(role, "error", 'step 1 playing role "admin"'), "a walkthrough step playing an unlisted role errors", dump(role));
    const listed = await demoProblemsOf({ [DEMO]: demoHtml({ model: playing({ role: "buyer", flags: ["express"] }) }) });
    check(!has(listed, "error", "card-declined"), "a step playing a listed role and flag resolves", dump(listed));
    const flag = await demoProblemsOf({ [DEMO]: demoHtml({ model: playing({ flags: ["ghost"] }) }) });
    check(has(flag, "error", 'step 1 switching flag "ghost"'), "a walkthrough step switching an unlisted flag errors", dump(flag));
    const notList = await demoProblemsOf({ [DEMO]: demoHtml({ model: playing({ flags: "express" }) }) });
    check(has(notList, "error", "has `flags` that is not a list of keys"), "a step's flags that are not a list error", dump(notList));
    const home = await demoProblemsOf({ [DEMO]: demoHtml({ model: { ...MODEL, app: { name: "Ordering", home: "ghost" } } }) });
    check(has(home, "error", '`app.home` "ghost"'), "an app.home naming no screen errors", dump(home));
    const fine = await demoProblemsOf({ [DEMO]: demoHtml({ model: { ...MODEL, app: { name: "Ordering", home: "checkout" } } }) });
    check(!has(fine, "error", "app"), "an app.home naming a listed screen resolves", dump(fine));
}

{
    const html = demoHtml().replace(/<script type="application\/json" id="demo-model">.*<\/script>\n/, "");
    const problems = await demoProblemsOf({ [DEMO]: html });
    check(has(problems, "error", "has no `<script type=\"application/json\" id=\"demo-model\">`"), "a demo without demo-model errors", dump(problems));
}

{
    // The shapes the checker once read tolerantly: the template reads none of them.
    const model = {
        screens: ["checkout", screen("declined")],
        anchors: ["pay"],
        actors: [{ key: "buyer" }],
        flags: ["express"],
        walkthroughs: { "card-declined": ["checkout/pay"] },
    };
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ model }) });
    const cannot = (detail) => has(problems, "error", `the template cannot read: ${detail}`);
    check(cannot("`screens` entry 1 has no `id`"), "a screen given as a bare id errors", dump(problems));
    check(cannot("`anchors` is not a key the template reads") && cannot("`actors` is not a key the template reads"), "a top-level anchors list and an actors list error", dump(problems));
    check(cannot("`flags` entry 1 has no `key`"), "a panel switch given as a bare key errors", dump(problems));
    check(cannot("`walkthroughs` is not a list"), "walkthroughs keyed by id error", dump(problems));
}

{
    const model = { ...MODEL, walkthroughs: [{ id: "card-declined", steps: ["checkout/pay"] }] };
    const problems = await demoProblemsOf({ [DEMO]: demoHtml({ model }) });
    check(has(problems, "error", 'walkthrough "card-declined" step 1 is not `{ screen, anchor?, text }`'), "a walkthrough step given as an address string errors", dump(problems));
}

// -- A change's proposal.md and solution.md -------------------------------

const CHANGE = "openspec/changes/add-express";
const proposalMd = (demo) =>
    `# Add express checkout\n\n${fence(`type: change\nstatus: proposed\ncategory: feature\ndemo: [${demo}]\n`)}\n` +
    `## Why\n\nFaster.\n\n## Scope\n\nCheckout.\n\n## Chapters touched\n\n- ${CONTEXT}/features.md#checkout\n`;
const solutionMd = (demo) => `# Solution\n\n${fence(`demo: [${demo}]\n`)}\nShow express on the checkout screen.\n`;

{
    const problems = await demoProblemsOf({ [`${CHANGE}/proposal.md`]: proposalMd(`${DEMO}#express`) });
    check(has(problems, "error", `${CHANGE}/proposal.md has \`demo\``) && has(problems, "error", 'screen "express"'), "a proposal's address missing from demo-model errors", dump(problems));
}

{
    const problems = await demoProblemsOf({ [`${CHANGE}/proposal.md`]: proposalMd(`${DEMO}#checkout`), [`${CHANGE}/solution.md`]: solutionMd(`${DEMO}#checkout/express`) });
    check(has(problems, "error", `${CHANGE}/solution.md has \`demo\``) && has(problems, "error", 'anchor "express"'), "a solution's address missing from demo-model errors", dump(problems));
}

{
    // The change lands a revised demo with an express screen; its proposal
    // addresses the screen the revision adds, which the live demo lacks.
    const model = { ...MODEL, screens: [...MODEL.screens, screen("express")] };
    const revised = demoHtml({ model, body: BODY + `<section id="express" data-screen></section>\n` });
    const problems = await demoProblemsOf({
        [`${CHANGE}/proposal.md`]: proposalMd(`${DEMO}#express`),
        [`${CHANGE}/solution.md`]: solutionMd(`${DEMO}#express`),
        [`${CHANGE}/devbook-delta/domain/ordering/features.demo.html`]: revised,
    });
    check(problems.length === 0, "a change's addresses resolve against the demo it lands", dump(problems));
}

{
    const problems = await demoProblemsOf({
        [`${CHANGE}/proposal.md`]: proposalMd(`${DEMO}#checkout`),
        [`${CHANGE}/devbook-delta/domain/ordering/flow.demo.html`]: demoHtml(),
    });
    check(has(problems, "error", "is named for .devbook/domain/ordering/flow.md"), "a proposed page-named demo without its page errors", dump(problems));
}

{
    const body = BODY + `<section id="checkout--board" data-screen data-variant="board"></section><div data-variant="list"></div>\n`;
    const problems = await demoProblemsOf({
        [`${CHANGE}/proposal.md`]: proposalMd(`${DEMO}#checkout`),
        [`${CHANGE}/devbook-delta/domain/ordering/features.demo.html`]: demoHtml({ body, model: { ...MODEL, screens: [...MODEL.screens, screen("checkout--board")] } }),
    });
    check(has(problems, "error", "carries 2 variants"), "a proposed demo for domain/ is held to one variant too", dump(problems));
}

// -- The fingerprint ------------------------------------------------------

{
    const approved = (hash) => `type: feature\ndemo: [${DEMO}#checkout]\nstatus: approved\napproved-by: Job\napproved-at: 2026-10-02\napproved-hash: ${hash}\n`;
    const root = await repo();
    const read = demoReader(root);
    const draft = featuresMd(approved("sha256:00000000"));
    const line = draft.split("\n").indexOf("## Checkout") + 1;
    const hash = chapterFingerprint(`${CONTEXT}/features.md`, draft, line, read);
    check(hash !== chapterHash(draft, line), "a chapter naming a demo fingerprints the demo with it");

    const stamped = featuresMd(approved(hash));
    const rel = `${CONTEXT}/features.md`;
    const clean = validateDocument(rel, stamped, { demoText: read });
    check(!clean.some((i) => i.message.includes("content that has changed")), "the recorded fingerprint matches while the demo is unchanged", dump(clean));

    const edited = demoReader(await repo({ [DEMO]: demoHtml({ body: BODY.replace("Pay", "Pay now") }) }));
    const stale = validateDocument(rel, stamped, { demoText: edited });
    check(stale.some((i) => i.severity === "error" && i.message.includes("content that has changed")), "editing the demo lifts the chapter's approval", dump(stale));

    const { problems } = await buildGraph(await repo({ [rel]: stamped, [DEMO]: demoHtml({ body: BODY.replace("Pay", "Pay now") }) }));
    check(has(problems, "error", "content that has changed"), "the check reports it through the build", dump(problems));

    // A template refresh rewrites the managed region only: the approval holds.
    const refreshed = demoReader(await repo({ [DEMO]: demoHtml().replace("<!-- template:begin -->", "<!-- template:begin hash=sha256:feedface -->").replace("body{margin:0}", "body{margin:0;padding:0}") }));
    const held = validateDocument(rel, stamped, { demoText: refreshed });
    check(!held.some((i) => i.message.includes("content that has changed")), "rewriting the managed region keeps the chapter's approval", dump(held));
}

{
    const root = await repo({ [`${CONTEXT}/demo.html`]: demoHtml() });
    const cwd = process.cwd();
    const printed = async (address) => {
        const lines = [];
        const log = console.log;
        console.log = (text) => lines.push(String(text));
        process.chdir(root);
        try {
            await hashMain([address]);
        } finally {
            process.chdir(cwd);
            console.log = log;
        }
        return lines.at(-1);
    };
    const before = await printed(`${CONTEXT}/context.md`);
    check(before !== chapterHash(contextMd, 1), "chapter-hash counts the context's demo.html toward context.md");
    await writeFile(path.join(root, CONTEXT, "demo.html"), demoHtml({ body: BODY.replace("Declined", "Card declined") }), "utf8");
    check((await printed(`${CONTEXT}/context.md`)) !== before, "editing demo.html changes context.md's fingerprint");

    const features = await printed(`${CONTEXT}/features.md`);
    await writeFile(path.join(root, DEMO), demoHtml({ body: BODY.replace("Checkout", "Check out") }), "utf8");
    check((await printed(`${CONTEXT}/features.md`)) !== features, "editing <page>.demo.html changes <page>.md's file fingerprint");

    const requirements = await printed(`${CONTEXT}/requirements.md#requirement-pay-by-card`);
    await writeFile(path.join(root, DEMO), demoHtml({ body: BODY.replace("Pay", "Pay by card") }), "utf8");
    check((await printed(`${CONTEXT}/requirements.md#requirement-pay-by-card`)) !== requirements, "editing a demo changes the fingerprint of a chapter whose `demo` names it");

    const plain = `${CONTEXT}/model.md`;
    const text = `# Model\n\n${fence("type: model\n")}\nShapes.\n`;
    await writeFile(path.join(root, plain), text, "utf8");
    check((await printed(plain)) === chapterHash(text, 1), "a page with no demo fingerprints as before");
}

{
    const root = await repo({
        [`${CHANGE}/proposal.md`]: proposalMd(`${DEMO}#checkout`),
        [`${CHANGE}/devbook-delta/domain/ordering/features.demo.html`]: demoHtml(),
    });
    const before = await changeFingerprint(root, "add-express");
    await writeFile(path.join(root, CHANGE, "devbook-delta/domain/ordering/features.demo.html"), demoHtml({ body: BODY.replace("Pay", "Pay now") }), "utf8");
    check((await changeFingerprint(root, "add-express")) !== before, "editing a proposed demo changes the change's fingerprint");

    const edited = await changeFingerprint(root, "add-express");
    await writeFile(path.join(root, CHANGE, "devbook-delta/domain/ordering/features.demo.html"), demoHtml({ body: BODY.replace("Pay", "Pay now") }).replace("body{margin:0}", "body{margin:1px}"), "utf8");
    check((await changeFingerprint(root, "add-express")) === edited, "rewriting a proposed demo's managed region keeps the change's fingerprint");
}

{
    // A merge that leaves a demo-backed chapter alone keeps its approval: the
    // merge compares against the same fingerprint the gate recorded.
    const root = await repo();
    const read = demoReader(root);
    const rel = `${CONTEXT}/features.md`;
    const meta = (hash) => `type: feature\ndemo: [${DEMO}#checkout]\nstatus: approved\napproved-by: Job\napproved-at: 2026-10-02\napproved-hash: ${hash}\n`;
    const draft = featuresMd(meta("sha256:00000000")) + `\n## Refunds\n\n${fence("type: feature\n")}\nRefunding.\n`;
    const line = draft.split("\n").indexOf("## Checkout") + 1;
    const original = draft.replace("sha256:00000000", chapterFingerprint(rel, draft, line, read));
    const delta = parseDelta(
        fence("change: add-express\ndelta: modified\n") + `\n## Refunds\n\n### MODIFIED\n\n${fence("")}\n#### Partial refunds\n\n${fence("")}\nPart of an order.\n`
    );
    const { merged, issues } = mergeDelta(delta, original, "add-express", { target: rel, demoText: read });
    check(merged?.includes("status: approved") && !issues.some((i) => i.message.includes("lifts")), "a merge elsewhere in the page keeps a demo-backed approval", JSON.stringify(issues));
}

for (const root of roots) await rm(root, { recursive: true, force: true });
console.log(`\n${failed ? `${failed} failed` : "all passed"}`);
process.exit(failed ? 1 : 0);
