// The Structurizr DSL reader, `c4.mjs`, reads the subset the C4 model decision
// supports and returns elements, relationships, views, and problems. Each
// construct in the subset has a fixture here, so does an `!include` from disk,
// and so does every problem the reader reports: anything outside the subset is
// a problem with its line, never dropped silently.
//
// Run: `node c4.test.mjs`
import { mkdtemp, mkdir, writeFile, rm, symlink } from "node:fs/promises";
import { tmpdir } from "node:os";
import path from "node:path";
import { parseWorkspace, readWorkspace, tokenize } from "./c4.mjs";

let failed = 0;
const check = (ok, name, detail) => {
    if (!ok) failed++;
    console.log(`${ok ? "PASS" : "FAIL"}  ${name}${ok || !detail ? "" : `\n        ${detail}`}`);
};
const eq = (a, b) => JSON.stringify(a) === JSON.stringify(b);
const show = (v) => JSON.stringify(v);
const el = (r, id) => r.elements.find((e) => e.id === id);
const view = (r, key) => r.views.find((v) => v.key === key);
// One problem whose message matches, on the given line.
const reports = (r, pattern, line) => r.problems.some((p) => pattern.test(p.message) && (line == null || p.line === line));
const wrap = (model, views = "") => `workspace "W" {\n!identifiers hierarchical\nmodel {\n${model}\n}\nviews {\n${views}\n}\n}\n`;

// ── the whole subset, clean ─────────────────────────────────────────────────

const FULL = `workspace "Shop" "An online shop" {
    !identifiers hierarchical

    # a line comment
    // another
    /* a block
       comment */
    model {
        customer = person "Customer" "Buys things" "External,Human"
        group "Internal" {
            shop = softwareSystem "Shop" "Sells things" {
                web = container "Web" "Serves pages" "ASP.NET Core" "UI"
                db = container "Database" "Holds orders" "PostgreSQL" {
                    tags "Database"
                }
                group "Backend" {
                    api = container "API" "Takes orders" "C#" {
                        orders = component "Orders" "Order logic" "C# class"
                        -> db "Reads and writes" "EF Core"
                    }
                }
                web -> api "Calls" "HTTPS/JSON"
            }
        }
        mail = softwareSystem "Mail" {
            tags "External"
        }
        customer -> shop.web "Browses" "HTTPS" "Primary"
        rel = shop.api -> mail "Sends mail" {
            tags "Async"
        }
        live = deploymentEnvironment "Live" {
            aws = deploymentNode "AWS" "Cloud" "Amazon Web Services" "Cloud" {
                lb = infrastructureNode "Load balancer" "Spreads load" "ELB"
                app = deploymentNode "App server" "" "Ubuntu" "" "3" {
                    webi = containerInstance shop.web
                    apii = containerInstance shop.api "" "Hot"
                }
                ssi = softwareSystemInstance mail
                lb -> app.webi "Forwards" "HTTPS"
            }
        }
    }

    views {
        systemLandscape landscape "Everything" {
            include *
            autolayout lr 200 100
        }
        systemContext shop context {
            include *
            exclude mail
            title "Shop in context"
        }
        container shop containers {
            include *
            autoLayout
        }
        component shop.api components {
            include *
        }
        dynamic shop checkout "Placing an order" {
            title "Checkout"
            customer -> shop.web "Places an order"
            shop.web -> shop.api "Submits" "HTTPS"
            shop.api -> shop.web "Returns the order"
            customer -> shop "Comes back later"
        }
        deployment * "Live" live-deployment {
            include *
        }
    }
}
`;

{
    const r = parseWorkspace(FULL);
    check(r.problems.length === 0, "the full subset reads with no problem", show(r.problems));
    check(r.name === "Shop" && r.description === "An online shop" && r.hierarchical, "workspace name, description, and hierarchical identifiers", show([r.name, r.description, r.hierarchical]));

    check(eq(el(r, "customer"), {
        id: "customer", kind: "person", name: "Customer", description: "Buys things", technology: null,
        tags: ["External", "Human"], parent: null, group: null, environment: null, file: "workspace.dsl", line: 9,
    }), "person: name, description, tags, and its line", show(el(r, "customer")));
    check(el(r, "shop")?.kind === "softwareSystem" && el(r, "shop").group === "Internal" && el(r, "shop").technology === null,
        "softwareSystem inside a group carries the group and no technology", show(el(r, "shop")));
    check(eq([el(r, "shop.web")?.kind, el(r, "shop.web")?.parent, el(r, "shop.web")?.technology, el(r, "shop.web")?.tags], ["container", "shop", "ASP.NET Core", ["UI"]]),
        "container: hierarchical id, parent, technology, tags", show(el(r, "shop.web")));
    check(eq(el(r, "shop.db")?.tags, ["Database"]), "a `tags` statement in an element block adds tags", show(el(r, "shop.db")));
    check(el(r, "shop.api")?.group === "Backend", "a group inside a softwareSystem groups its containers", show(el(r, "shop.api")));
    check(eq([el(r, "shop.api.orders")?.kind, el(r, "shop.api.orders")?.parent, el(r, "shop.api.orders")?.group], ["component", "shop.api", null]),
        "component: hierarchical id three deep, not in the container's group", show(el(r, "shop.api.orders")));
    check(eq(el(r, "mail")?.tags, ["External"]), "an element with a block and no positional tags", show(el(r, "mail")));

    const rel = (s, d) => r.relationships.find((x) => x.source === s && x.destination === d);
    check(eq(rel("shop.api", "shop.db") && [rel("shop.api", "shop.db").description, rel("shop.api", "shop.db").technology], ["Reads and writes", "EF Core"]),
        "a leading arrow in an element block takes that element as the source, a sibling resolved by its local name", show(r.relationships));
    check(!!rel("shop.web", "shop.api"), "a relationship inside a system names its containers by local name");
    check(eq(rel("customer", "shop.web")?.tags, ["Primary"]), "relationship: description, technology, tags", show(rel("customer", "shop.web")));
    check(rel("shop.api", "mail")?.id === "rel" && eq(rel("shop.api", "mail").tags, ["Async"]) && rel("shop.api", "mail").technology === null,
        "an assigned relationship with a tags block", show(rel("shop.api", "mail")));

    check(eq(r.environments.map((e) => [e.id, e.name]), [["live", "Live"]]), "deploymentEnvironment is listed", show(r.environments));
    check(eq([el(r, "live.aws")?.kind, el(r, "live.aws")?.environment, el(r, "live.aws")?.technology, el(r, "live.aws")?.parent], ["deploymentNode", "Live", "Amazon Web Services", null]),
        "deploymentNode: id under the environment, environment name", show(el(r, "live.aws")));
    check(el(r, "live.aws.app")?.instances === "3" && el(r, "live.aws.app").description === null, "a nested deploymentNode keeps its instance count; an empty argument is null", show(el(r, "live.aws.app")));
    check(el(r, "live.aws.lb")?.kind === "infrastructureNode" && el(r, "live.aws.lb").technology === "ELB", "infrastructureNode", show(el(r, "live.aws.lb")));
    check(eq([el(r, "live.aws.app.webi")?.kind, el(r, "live.aws.app.webi")?.instanceOf, el(r, "live.aws.app.webi")?.name], ["containerInstance", "shop.web", "Web"]),
        "containerInstance resolves what it instantiates and takes its name", show(el(r, "live.aws.app.webi")));
    check(eq(el(r, "live.aws.app.apii")?.tags, ["Hot"]), "containerInstance tags are its third argument", show(el(r, "live.aws.app.apii")));
    check(el(r, "live.aws.ssi")?.kind === "softwareSystemInstance" && el(r, "live.aws.ssi").instanceOf === "mail", "softwareSystemInstance", show(el(r, "live.aws.ssi")));
    check(!!rel("live.aws.lb", "live.aws.app.webi"), "a relationship between deployment elements, resolved from its scope", show(r.relationships));

    check(eq(r.views.map((v) => [v.kind, v.key]), [["systemLandscape", "landscape"], ["systemContext", "context"], ["container", "containers"], ["component", "components"], ["dynamic", "checkout"], ["deployment", "live-deployment"]]),
        "the six view kinds, each with its key", show(r.views.map((v) => [v.kind, v.key])));
    check(eq(view(r, "landscape")?.autolayout, { direction: "lr", rankSeparation: 200, nodeSeparation: 100 }) && view(r, "landscape").description === "Everything" && view(r, "landscape").scope === null,
        "systemLandscape: no scope, description, autolayout with separations", show(view(r, "landscape")));
    check(eq([view(r, "context")?.scope, view(r, "context")?.include, view(r, "context")?.exclude, view(r, "context")?.title], ["shop", ["*"], ["mail"], "Shop in context"]),
        "systemContext: scope, include, exclude, title", show(view(r, "context")));
    check(eq(view(r, "containers")?.autolayout, { direction: "tb", rankSeparation: null, nodeSeparation: null }), "a bare `autoLayout` defaults to top-bottom", show(view(r, "containers")));
    check(view(r, "components")?.scope === "shop.api", "component view scoped to a container by hierarchical id", show(view(r, "components")));
    check(eq(view(r, "checkout")?.steps.map((s) => [s.order, s.source, s.destination, s.description, s.technology]), [[1, "customer", "shop.web", "Places an order", null], [2, "shop.web", "shop.api", "Submits", "HTTPS"], [3, "shop.api", "shop.web", "Returns the order", null], [4, "customer", "shop", "Comes back later", null]]) && view(r, "checkout").title === "Checkout",
        "dynamic: ordered steps by identifier, title", show(view(r, "checkout")));
    check(view(r, "checkout")?.steps[2].response === true && !("response" in view(r, "checkout").steps[0]), "a step against a relationship's direction is a response", show(view(r, "checkout")?.steps));
    check(!("response" in (view(r, "checkout")?.steps[3] ?? {})), "a step between systems rests on their containers' relationship, implied");
    check(view(r, "live-deployment")?.scope === "*" && view(r, "live-deployment").environment === "Live", "deployment: `*` scope and environment", show(view(r, "live-deployment")));
}

// ── flat identifiers and anonymous elements ─────────────────────────────────

{
    const r = parseWorkspace(`workspace {\nmodel {\nsoftwareSystem "Billing Hub" {\napi = container "API"\n}\n}\n}\n`);
    check(reports(r, /does not declare `!identifiers hierarchical`/, 1), "a workspace without `!identifiers hierarchical` is a problem on its first line", show(r.problems));
    check(el(r, "api")?.parent === "~billing-hub" && el(r, "~billing-hub")?.anonymous === true, "flat ids; an unnamed element gets a `~` slug id and `anonymous`", show(r.elements));
    check(!("anonymous" in el(r, "api")), "an assigned element carries no `anonymous` flag");
}
{
    const r = parseWorkspace(`workspace {\n!identifiers flat\nmodel {\n}\n}\n`);
    check(reports(r, /only `hierarchical` is supported/, 2), "`!identifiers flat` is a problem", show(r.problems));
}
{
    const r = parseWorkspace(`workspace {\nmodel {\n!identifiers hierarchical\na = softwareSystem "A" {\nb = container "B"\n}\n}\n}\n`);
    check(!!el(r, "a.b"), "`!identifiers` inside `model` is still honoured", show(r.elements));
    check(reports(r, /not the workspace's first statement/, 3), "`!identifiers` inside `model` is reported: the workspace starts with it", show(r.problems));
}
{
    const r = parseWorkspace(`workspace {
views {
}
!identifiers hierarchical
model {
a = softwareSystem "A" {
b = container "B"
}
}
}
`);
    check(!!el(r, "a.b") && reports(r, /not the workspace's first statement/, 4), "`!identifiers` after another statement is honoured and reported", show(r.problems));
}
{
    const r = parseWorkspace(`workspace {
# a comment first
!identifiers hierarchical
model {
}
}
`);
    check(!reports(r, /first statement/), "a comment before `!identifiers` is no statement", show(r.problems));
}

{
    const r = parseWorkspace(wrap("softwareSystem \"Api\"\napi = person \"P\"\nx = person \"X\"\nx -> api", "systemLandscape k {\ninclude ~api\n}"));
    check(el(r, "api")?.kind === "person" && r.relationships[0]?.destination === "api", "an unnamed element never shadows a named one", show(r));
    check(reports(r, /`include ~api` names nothing/, 11), "an unnamed element cannot be named in a view", show(r.problems));
}
{
    const r = parseWorkspace(wrap("live = deploymentEnvironment \"Live\" {\na = deploymentNode \"A\"\nb = deploymentNode \"B\"\na -> b \"Replicates\"\n}"));
    check(r.problems.length === 0 && r.relationships[0]?.source === "live.a" && r.relationships[0]?.destination === "live.b", "a relationship directly in an environment resolves within it", show(r));
}
{
    const r = parseWorkspace(wrap("a = person \"A\" {\nthis -> b \"Uses\"\n}\nb = softwareSystem \"B\"\nr1 = a -> b", "systemLandscape k {\ninclude r1\n}"));
    check(r.problems.length === 0 && r.relationships.length === 2 && r.relationships.every((x) => x.source === "a"), "`this ->` takes the element as the source", show(r));
    check(eq(r.views[0].include, ["r1"]), "a view includes a relationship by its identifier", show(r.views[0]));
}

// ── includes ────────────────────────────────────────────────────────────────

{
    const root = await mkdtemp(path.join(tmpdir(), "c4-"));
    try {
        const arc = path.join(root, ".devbook", "arc42");
        await mkdir(path.join(arc, "c4", "blocks"), { recursive: true });
        await writeFile(path.join(arc, "workspace.dsl"), [
            "workspace {",
            "!identifiers hierarchical",
            "model {",
            "!include c4/people.dsl",
            "!include c4/blocks/shop.dsl",
            "!include ../outside.dsl",
            "!include c4/missing.dsl",
            "!include c4/blocks",
            "!include https://example.com/x.dsl",
            "!include /abs.dsl",
            "!include c4/loop.dsl",
            "!include c4/../c4x/sib.dsl",
            "}",
            "}",
            "",
        ].join("\n"));
        await writeFile(path.join(arc, "c4", "people.dsl"), "u = person \"User\"\n");
        await writeFile(path.join(arc, "c4", "blocks", "shop.dsl"), "s = softwareSystem \"Shop\" {\n!include api.dsl\n}\nu -> s \"Uses\"\nbogus here\n");
        await writeFile(path.join(arc, "c4", "blocks", "api.dsl"), "api = container \"API\"\n");
        await writeFile(path.join(arc, "c4", "loop.dsl"), "!include loop.dsl\n");
        await writeFile(path.join(arc, "outside.dsl"), "x = person \"X\"\n");
        await mkdir(path.join(arc, "c4x"), { recursive: true });
        await writeFile(path.join(arc, "c4x", "sib.dsl"), "y = person \"Y\"\n");

        const r = readWorkspace(".devbook/arc42/workspace.dsl", { root });
        const files = (p) => r.problems.filter((x) => p.test(x.message)).map((x) => `${x.file}:${x.line}`);
        check(!!el(r, "u") && !!el(r, "s") && el(r, "s.api")?.parent === "s", "an include splices its statements in place, nested includes relative to the including file", show(r.elements.map((e) => e.id)));
        check(el(r, "s.api")?.file === ".devbook/arc42/c4/blocks/api.dsl" && el(r, "s.api").line === 1, "an included element keeps its own file and line", show(el(r, "s.api")));
        check(r.relationships.length === 1 && r.relationships[0].source === "u", "a relationship in an included file resolves across files", show(r.relationships));
        check(eq(files(/outside the supported DSL subset inside `model`/), [".devbook/arc42/c4/blocks/shop.dsl:5"]), "a problem in an included file names that file and line", show(r.problems));
        check(eq(files(/lands outside/), [".devbook/arc42/workspace.dsl:6", ".devbook/arc42/workspace.dsl:12"]) && !el(r, "x") && !el(r, "y"), "an include outside `c4/`, a sibling `c4x/` included, is a problem and is not read", show(r.problems));
        if (process.platform === "win32") {
            const flipped = root.replace(/^[A-Za-z]:/, (d) => (d === d.toUpperCase() ? d.toLowerCase() : d.toUpperCase()));
            const other = readWorkspace(path.join(arc, "workspace.dsl"), { root: flipped });
            check(!!el(other, "s.api"), "on Windows, a root whose drive letter differs in case still includes", show(other.problems));
        }
        check(eq(files(/no such file/), [".devbook/arc42/workspace.dsl:7"]), "a missing include is a problem");
        check(eq(files(/is not a file/), [".devbook/arc42/workspace.dsl:8"]), "including a folder is a problem");
        check(eq(files(/only a relative path/), [".devbook/arc42/workspace.dsl:9", ".devbook/arc42/workspace.dsl:10"]), "a URL or an absolute include is a problem", show(r.problems));
        check(eq(files(/a cycle/), [".devbook/arc42/c4/loop.dsl:1"]), "an include cycle is a problem, read once", show(r.problems));
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}
// A link under `c4/` passes the path check, so only the realpath comparison
// stands between it and a file outside the folder.
{
    const root = await mkdtemp(path.join(tmpdir(), "c4-link-"));
    try {
        const arc = path.join(root, ".devbook", "arc42");
        await mkdir(path.join(arc, "c4"), { recursive: true });
        await writeFile(path.join(arc, "workspace.dsl"), "workspace {\n!identifiers hierarchical\nmodel {\n!include c4/escape.dsl\n!include c4/inner.dsl\n}\n}\n");
        await writeFile(path.join(arc, "outside.dsl"), "x = person \"X\"\n");
        await writeFile(path.join(arc, "c4", "real.dsl"), "y = person \"Y\"\n");
        let linked = true;
        try {
            await symlink(path.join(arc, "outside.dsl"), path.join(arc, "c4", "escape.dsl"), "file");
            await symlink(path.join(arc, "c4", "real.dsl"), path.join(arc, "c4", "inner.dsl"), "file");
        } catch (e) {
            if (!["EPERM", "EACCES", "ENOTSUP", "ENOSYS"].includes(e.code)) throw e;
            linked = false;
            console.log(`SKIP  an include through a link to a file outside \`c4/\` is refused — this platform refused to create a symlink (${e.code}); on Windows that takes developer mode or an elevated shell`);
        }
        if (linked) {
            const r = readWorkspace(".devbook/arc42/workspace.dsl", { root });
            const at = r.problems.filter((x) => /is a link to a file outside/.test(x.message)).map((x) => `${x.file}:${x.line}`);
            check(eq(at, [".devbook/arc42/workspace.dsl:4"]) && !el(r, "x"), "an include through a link to a file outside `c4/` is a problem and is not read", show(r.problems));
            check(!!el(r, "y") && !r.problems.some((x) => x.line === 5), "an include through a link that stays inside `c4/` is read", show(r.problems));
        }
    } finally {
        await rm(root, { recursive: true, force: true });
    }
}
{
    const r = parseWorkspace(wrap("!include c4/x.dsl"));
    check(reports(r, /cannot be read here/, 4), "parseWorkspace without a resolver reports every include", show(r.problems));
    const r2 = parseWorkspace(wrap("!include"));
    check(reports(r2, /takes exactly one path/, 4), "`!include` with no path is a problem", show(r2.problems));
}

// ── every problem ───────────────────────────────────────────────────────────

const cases = [
    ["an empty file", "", /The file is empty/, 1],
    ["a file not opening with `workspace`", "model {\n}\n", /opens with `workspace`/, 1],
    ["`workspace extends`", "workspace extends other.dsl {\n}\n", /`workspace extends`/, 1],
    ["a workspace with no block", "workspace \"W\"\n", /has no block/, 1],
    ["an unclosed block", "workspace {\n!identifiers hierarchical\nmodel {\n", /never closed/, 3],
    ["an unknown workspace statement", wrap("").replace("model {", "configuration {\n}\nmodel {"), /`configuration` is outside the supported DSL subset inside `workspace`/, 3],
    ["an unknown model statement", wrap("!constant X 1"), /`!constant` is outside the supported DSL subset inside `model`/, 4],
    ["`properties` in an element block", wrap("a = softwareSystem \"A\" {\nproperties {\n\"k\" \"v\"\n}\n}"), /`properties` is outside the supported DSL subset inside `softwareSystem`/, 5],
    ["`description` in an element block", wrap("a = softwareSystem \"A\" {\ndescription \"d\"\n}"), /`description` is outside the supported DSL subset inside `softwareSystem`/, 5],
    ["`styles` in views", wrap("", "styles {\nelement \"Person\" {\nshape person\n}\n}"), /`styles` is outside the supported DSL subset inside `views`/, 7],
    ["an unknown view statement", wrap("a = softwareSystem \"A\"", "systemContext a k {\nanimation {\n}\n}"), /`animation` is outside the supported DSL subset inside `systemContext view`/, 8],
    ["a bare block", wrap("{\n}"), /A block with no statement before it/, 4],
    ["a container outside a system", wrap("c = container \"C\""), /`container` cannot be declared inside `model`/, 4],
    ["a component inside a system", wrap("a = softwareSystem \"A\" {\nc = component \"C\"\n}"), /`component` cannot be declared inside `softwareSystem`/, 5],
    ["a deploymentNode outside an environment", wrap("n = deploymentNode \"N\""), /`deploymentNode` is outside the supported DSL subset inside `model`/, 4],
    ["an instance directly in an environment", wrap("a = softwareSystem \"A\"\ndeploymentEnvironment \"E\" {\nsoftwareSystemInstance a\n}"), /`softwareSystemInstance` cannot be declared inside `deploymentEnvironment`/, 6],
    ["an infrastructureNode directly in an environment", wrap("deploymentEnvironment \"E\" {\ninfrastructureNode \"I\"\n}"), /`infrastructureNode` cannot be declared inside `deploymentEnvironment`/, 5],
    ["an unknown deployment statement", wrap("deploymentEnvironment \"E\" {\nhealthCheck \"x\" \"y\"\n}"), /`healthCheck` is outside the supported DSL subset inside `deploymentEnvironment`/, 5],
    ["an unknown statement in an infrastructureNode block", wrap("deploymentEnvironment \"E\" {\nn = deploymentNode \"N\" {\ni = infrastructureNode \"I\" {\nurl https://x\n}\n}\n}"), /`url` is outside the supported DSL subset inside `infrastructureNode`/, 7],
    ["an element with no name", wrap("a = softwareSystem"), /was declared with no name/, 4],
    ["too many arguments", wrap("a = person \"A\" \"d\" \"t\" \"extra\""), /takes at most 3 arguments/, 4],
    ["a duplicate identifier", wrap("a = person \"A\"\na = person \"B\""), /`a` is declared more than once/, 5],
    ["a nested group", wrap("group \"G\" {\ngroup \"H\" {\na = person \"A\"\n}\n}"), /A group inside a group/, 5],
    ["a group with no block", wrap("group \"G\""), /`group` has no block/, 4],
    ["a group with no name", wrap("group {\n}"), /`group` was declared with no name/, 4],
    ["an environment with no name", wrap("deploymentEnvironment {\n}"), /`deploymentEnvironment` was declared with no name/, 4],
    ["a duplicate environment", wrap("deploymentEnvironment \"E\"\ndeploymentEnvironment \"E\""), /`E` is declared more than once/, 5],
    ["an instance naming nothing", wrap("deploymentEnvironment \"E\" {\nn = deploymentNode \"N\" {\ncontainerInstance\n}\n}"), /names nothing to instantiate/, 6],
    ["an instance of a missing element", wrap("deploymentEnvironment \"E\" {\nn = deploymentNode \"N\" {\ncontainerInstance nope\n}\n}"), /`nope` is not an element/, 6],
    ["an instance of the wrong kind", wrap("p = person \"P\"\ndeploymentEnvironment \"E\" {\nn = deploymentNode \"N\" {\ncontainerInstance p\n}\n}"), /instantiates a container; `p` is a person/, 7],
    ["deployment groups on an instance", wrap("a = softwareSystem \"A\"\ndeploymentEnvironment \"E\" {\nn = deploymentNode \"N\" {\nsoftwareSystemInstance a \"g1\"\n}\n}"), /Deployment groups/, 7],
    ["a relationship with no source", wrap("-> a \"x\""), /needs an element on both sides/, 4],
    ["a relationship to a missing element", wrap("a = person \"A\"\na -> nope"), /`nope` is not an element in this workspace, so this relationship was dropped/, 5],
    ["an unknown statement in a relationship block", wrap("a = person \"A\"\na -> a {\nurl x\n}"), /`url` is outside the supported DSL subset inside `relationship`/, 6],
    ["a token before a relationship's source", wrap("a = person \"A\"\nb = person \"B\"\nx a -> b"), /before the relationship's source/, 6],
    ["a view with no key", wrap("", "systemLandscape {\n}"), /has no key, so no chapter can embed it/, 7],
    ["a view key with a bad character", wrap("", "systemLandscape \"bad key\" {\n}"), /holds a character other than/, 7],
    ["a duplicate view key", wrap("", "systemLandscape k\nsystemLandscape k"), /used by more than one view/, 8],
    ["a scoped view with no scope", wrap("", "systemContext"), /needs a scope/, 7],
    ["a scope that names nothing", wrap("", "container nope k"), /`nope` is not an element in this workspace, so the view has no scope/, 7],
    ["a scope of the wrong kind", wrap("p = person \"P\"", "container p k"), /scoped to softwareSystem; `p` is a person/, 7],
    ["a `*` scope on a container view", wrap("", "container * k"), /cannot be scoped to `\*`/, 7],
    ["a deployment view with no environment", wrap("", "deployment *"), /needs an environment/, 7],
    ["a deployment view of a missing environment", wrap("", "deployment * Nope k"), /`Nope` is not a deployment environment/, 7],
    ["an include expression", wrap("a = person \"A\"", "systemLandscape k {\ninclude element.tag==X\n}"), /is an expression/, 8],
    ["an include naming nothing", wrap("", "systemLandscape k {\ninclude nope\n}"), /`include nope` names nothing/, 8],
    ["an exclude naming nothing", wrap("", "systemLandscape k {\nexclude nope\n}"), /`exclude nope` names nothing/, 8],
    ["a bad autolayout direction", wrap("", "systemLandscape k {\nautolayout sideways\n}"), /the direction is one of/, 8],
    ["a bad autolayout separation", wrap("", "systemLandscape k {\nautolayout lr wide\n}"), /not a whole number/, 8],
    ["a numbered dynamic step", wrap("a = person \"A\"", "dynamic * k {\n1: a -> a\n}"), /numbered and parallel steps/, 8],
    ["a parallel dynamic block", wrap("a = person \"A\"", "dynamic * k {\n{\na -> a\n}\n}"), /A block with no statement before it/, 8],
    ["a dynamic step naming nothing", wrap("a = person \"A\"", "dynamic * k {\na -> nope\n}"), /`nope` is not an element in this workspace, so this step is not drawn/, 8],
    ["a stray closing brace", wrap("a = person \"A\"\n}"), /stands after the workspace closed/, 7],
    ["text after the workspace", "workspace {\n!identifiers hierarchical\n}\nmore", /`more` stands after the workspace closed/, 4],
    ["`!identifiers` after the model", "workspace {\nmodel {\n}\n!identifiers hierarchical\n}\n", /comes after the model began/, 4],
    ["an unterminated string", wrap("a = person \"A"), /never closed on its line/, 4],
    ["an unterminated block comment", "workspace {\n}\n/* open", /comment is never closed/, 3],
    ["a dynamic step no relationship backs", wrap("a = person \"A\"\nb = person \"B\"", "dynamic * k {\na -> b\n}"), /No relationship from `a` to `b`/, 9],
    ["a duplicate relationship identifier", wrap("a = person \"A\"\nr = a -> a\nr = a -> a"), /`r` is declared more than once/, 6],
    ["a relationship identifier an element holds", wrap("a = person \"A\"\na = a -> a"), /`a` is declared more than once/, 5],
    ["a group in a component", wrap("s = softwareSystem \"S\" {\nc = container \"C\" {\nk = component \"K\" {\ngroup \"G\" {\n}\n}\n}\n}"), /`group` is outside the supported DSL subset inside `component`/, 7],
    ["an environment inside a group", wrap("group \"G\" {\ndeploymentEnvironment \"E\"\n}"), /`deploymentEnvironment` is outside the supported DSL subset inside `model`/, 5],
    ["an identifier with a bad character", wrap("a.b = person \"A\""), /is not an identifier/, 4],
    ["a non-value token where a value belongs", wrap("a = person \"A\" ="), /`=` is not a value/, 4],
];
for (const [name, source, pattern, line] of cases) {
    const r = parseWorkspace(source);
    check(reports(r, pattern, line), `problem: ${name} (line ${line})`, show(r.problems));
}

// Nothing outside the subset is dropped silently: each unknown statement is one problem.
{
    const r = parseWorkspace(wrap("a = person \"A\"\nfoo\nbar baz", "systemLandscape k {\nqux\n}"));
    check(eq(r.problems.map((p) => [p.line, p.construct]), [[5, "foo"], [6, "bar"], [10, "qux"]]), "every unknown statement is reported once, in line order", show(r.problems));
    check(!!el(r, "a") && r.views.length === 1, "reading continues past each problem");
}

// ── the lexer ───────────────────────────────────────────────────────────────

{
    const t = tokenize('a = b "x \\"y\\" \\n"  # not a comment mid-line\n/* x\ny */ c', "f.dsl");
    check(eq(t.filter((x) => x.kind === "text").map((x) => x.text), ['x "y" \n']), "a quoted string resolves its escapes", show(t));
    check(t.some((x) => x.text === "#" && x.line === 1), "`#` mid-line is text, not a comment", show(t));
    check(t.find((x) => x.text === "c")?.line === 3, "a block comment counts its lines");
    const u = tokenize('a "unterminated\nb', "f.dsl");
    check(u.find((x) => x.text === "b")?.line === 2, "an unterminated string stops at the line end");
    check(tokenize("a\r\nb\r\n").filter((x) => x.kind === "word").map((x) => x.line).join() === "1,2", "CRLF reads as LF");
}

console.log(failed ? `\n${failed} case(s) failed.` : "\nAll cases passed.");
process.exit(failed ? 1 : 0);
