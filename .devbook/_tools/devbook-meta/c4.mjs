// c4.mjs — reads an arc42 folder's Structurizr DSL workspace into plain data.
//
// The C4 model decision makes `.devbook/arc42/workspace.dsl` the one place an
// architecture element or view is defined. This module reads the subset of the
// DSL that decision supports and returns what a check, an index, or a renderer
// needs: the elements with their hierarchical identifiers, the relationships,
// the views, and the problems.
//
// It never guesses. A construct outside the subset is a problem naming its file
// and line, and reading continues after it; a workspace that reads clean is a
// workspace whose every statement was understood. A picture quietly missing
// what could not be read is the failure this guards against.
//
// The subset: `workspace`, `model`, `views`, `!identifiers hierarchical`,
// `!include` of a relative file under the workspace's `c4/` folder; `person`,
// `softwareSystem`, `container`, `component`, `group`, `tags`; relationships with
// description, technology, and tags; `deploymentEnvironment`, `deploymentNode`,
// `infrastructureNode`, `containerInstance`, `softwareSystemInstance`; and the
// six view kinds with key, scope, description, `title`, `include`, `exclude`,
// `autolayout`, and a dynamic view's steps.
//
// Zero dependencies beyond Node, like every file in this folder.
//
// Run: `node c4.mjs <workspace.dsl>` prints the reading as JSON and exits 1 on
// any problem.

import { readFileSync, realpathSync, statSync } from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

// ── lexer ───────────────────────────────────────────────────────────────────
//
// Line-oriented: a statement ends where its line ends unless it opens a brace,
// so a newline is a token. `#` and `//` open a comment only at the start of a
// line — mid-line they are text the parser reports — and `/* */` may sit
// anywhere outside a string.

const isBreak = (c) => c === " " || c === "\t" || c === "\n" || c === '"' || c === "{" || c === "}";

/** Tokens of one file: `{ kind, text, line, file }`, kind one of word, text, arrow, assign, open, close, end. */
export function tokenize(source, file = "workspace.dsl", problems = []) {
    const tokens = [];
    const text = String(source ?? "").replace(/\r\n?/g, "\n");
    let line = 1;
    let i = 0;
    let pending = false;
    let lineStart = true;
    const end = () => {
        if (pending) tokens.push({ kind: "end", text: "\n", line, file });
        pending = false;
    };
    const add = (kind, value) => {
        tokens.push({ kind, text: value, line, file });
        pending = true;
        lineStart = false;
    };

    while (i < text.length) {
        const c = text[i];
        if (c === "\n") { end(); line++; i++; lineStart = true; continue; }
        if (c === " " || c === "\t") { i++; continue; }
        if (lineStart && (c === "#" || (c === "/" && text[i + 1] === "/"))) {
            while (i < text.length && text[i] !== "\n") i++;
            continue;
        }
        if (c === "/" && text[i + 1] === "*") {
            const opened = line;
            i += 2;
            while (i < text.length && !(text[i] === "*" && text[i + 1] === "/")) {
                if (text[i] === "\n") { end(); line++; lineStart = true; }
                i++;
            }
            if (i >= text.length) problems.push({ file, line: opened, construct: "/*", message: "A `/*` comment is never closed; the rest of the file was read as comment." });
            i = Math.min(i + 2, text.length);
            continue;
        }
        if (c === '"') {
            let value = "";
            i++;
            while (i < text.length && text[i] !== '"' && text[i] !== "\n") {
                if (text[i] === "\\" && i + 1 < text.length && text[i + 1] !== "\n") {
                    const e = text[i + 1];
                    value += e === "n" ? "\n" : e === "t" ? "\t" : e;
                    i += 2;
                    continue;
                }
                value += text[i++];
            }
            if (text[i] === '"') i++;
            else problems.push({ file, line, construct: '"', message: "A string is never closed on its line; it was read to the line end." });
            add("text", value);
            continue;
        }
        if (c === "{") { add("open", "{"); end(); i++; continue; }
        if (c === "}") { end(); add("close", "}"); end(); i++; continue; }
        if (c === "-" && text[i + 1] === ">") { add("arrow", "->"); i += 2; continue; }
        if (c === "=" && text[i + 1] !== "=") { add("assign", "="); i++; continue; }
        const start = i;
        while (i < text.length && !isBreak(text[i])) i++;
        if (i === start) i++;
        add("word", text.slice(start, i));
    }
    end();
    return tokens;
}

// ── includes ────────────────────────────────────────────────────────────────
//
// `!include` splices the named file's statements in where it stands, the way
// Structurizr does, so an included file may hold any statement its place
// allows. Expansion happens on tokens, before parsing, which keeps every token's
// own file and line for the problems the parser reports.

function expandIncludes(tokens, include, problems, stack) {
    const out = [];
    let atStart = true;
    for (let i = 0; i < tokens.length; i++) {
        const t = tokens[i];
        if (atStart && t.kind === "word" && t.text.toLowerCase() === "!include") {
            const parts = [];
            let j = i + 1;
            while (j < tokens.length && tokens[j].kind !== "end" && tokens[j].kind !== "open" && tokens[j].kind !== "close") parts.push(tokens[j++]);
            if (tokens[j]?.kind === "open") {
                problems.push(problem(t, "`!include` cannot open a block."));
            }
            const target = parts.length === 1 && isValue(parts[0]) ? parts[0].text : null;
            if (!target) {
                problems.push(problem(t, "`!include` takes exactly one path."));
            } else {
                const found = include(target, t.file);
                if (found.problem) {
                    problems.push(problem(t, found.problem));
                } else if (stack.includes(found.file)) {
                    problems.push(problem(t, `\`!include ${target}\` includes \`${found.file}\` again, a cycle; it is read once.`));
                } else {
                    out.push(...expandIncludes(tokenize(found.text, found.file, problems), include, problems, [...stack, found.file]));
                }
            }
            i = tokens[j]?.kind === "end" ? j : j - 1;
            atStart = true;
            continue;
        }
        out.push(t);
        atStart = t.kind === "end" || t.kind === "open" || t.kind === "close";
    }
    return out;
}

// ── parser ──────────────────────────────────────────────────────────────────

const isValue = (t) => t?.kind === "word" || t?.kind === "text";
const lower = (t) => (t?.kind === "word" ? t.text.toLowerCase() : null);
const problem = (t, message) => ({ file: t.file, line: t.line, construct: t.text, message });
const splitTags = (value) => (value ? value.split(",").map((s) => s.trim()).filter(Boolean) : []);
const blank = (value) => (value == null || value.trim() === "" ? null : value);
const slug = (value) => String(value).toLowerCase().replace(/[^a-z0-9]+/g, "-").replace(/^-+|-+$/g, "") || "element";

// Positional arguments per declaring keyword, after the keyword itself.
const STATIC = {
    person: { kind: "person", args: ["name", "description", "tags"], inside: [null] },
    softwaresystem: { kind: "softwareSystem", args: ["name", "description", "tags"], inside: [null] },
    container: { kind: "container", args: ["name", "description", "technology", "tags"], inside: ["softwareSystem"] },
    component: { kind: "component", args: ["name", "description", "technology", "tags"], inside: ["container"] },
};
const DEPLOYMENT = {
    deploymentnode: { kind: "deploymentNode", args: ["name", "description", "technology", "tags", "instances"], inside: [null, "deploymentNode"] },
    infrastructurenode: { kind: "infrastructureNode", args: ["name", "description", "technology", "tags"], inside: ["deploymentNode"] },
};
const INSTANCES = {
    containerinstance: { kind: "containerInstance", of: "container" },
    softwaresysteminstance: { kind: "softwareSystemInstance", of: "softwareSystem" },
};
const VIEWS = {
    systemlandscape: { kind: "systemLandscape", scope: [] },
    systemcontext: { kind: "systemContext", scope: ["softwareSystem"] },
    container: { kind: "container", scope: ["softwareSystem"] },
    component: { kind: "component", scope: ["container"] },
    dynamic: { kind: "dynamic", scope: ["*", "softwareSystem", "container"] },
    deployment: { kind: "deployment", scope: ["*", "softwareSystem"], environment: true },
};
const DIRECTIONS = new Set(["tb", "bt", "lr", "rl"]);
const VIEW_KEY = /^[A-Za-z0-9_-]+$/;
const IDENTIFIER = /^[A-Za-z0-9_-]+$/;

class Parser {
    constructor(tokens, problems) {
        this.tokens = tokens;
        this.i = 0;
        this.problems = problems;
        this.hierarchical = false;
        this.identifiersSeen = false;
        this.elements = [];
        this.byId = new Map(); // lower-cased id → element
        this.anonymousIds = new Set();
        this.modelSeen = false;
        this.relationships = [];
        this.views = [];
        this.environments = [];
        this.pendingRelationships = [];
        this.pendingInstances = [];
    }

    get current() { return this.tokens[this.i]; }

    report(token, message) { this.problems.push(problem(token, message)); }

    statement() {
        const parts = [];
        while (this.i < this.tokens.length) {
            const t = this.current;
            if (t.kind === "end") { this.i++; return { parts, block: false }; }
            if (t.kind === "close") return { parts, block: false };
            if (t.kind === "open") { this.i++; return { parts, block: true }; }
            parts.push(t);
            this.i++;
        }
        return { parts, block: false };
    }

    // Every statement of the block just opened; leaves the cursor past its `}`.
    *block(opener) {
        while (this.i < this.tokens.length && this.current.kind !== "close") {
            if (this.current.kind === "end") { this.i++; continue; }
            const s = this.statement();
            if (s.parts.length === 0) {
                if (s.block) {
                    this.report(this.tokens[this.i - 1], "A block with no statement before it is outside the subset; it was skipped.");
                    this.skip();
                }
                continue;
            }
            yield s;
        }
        if (this.i < this.tokens.length) this.i++;
        else if (opener) this.report(opener, `\`${opener.text}\` opens a block that is never closed.`);
    }

    skip() {
        let depth = 1;
        while (this.i < this.tokens.length && depth > 0) {
            const k = this.current.kind;
            if (k === "open") depth++;
            else if (k === "close") depth--;
            this.i++;
        }
    }

    unknown(head, block, where) {
        if (block) this.skip();
        this.report(head, `\`${head.text}\` is outside the supported DSL subset inside \`${where}\`; it was skipped.`);
    }

    // The values from `from` on, reporting any token that is not one and any past `max`.
    values(parts, from, max, what) {
        const out = [];
        for (let k = from; k < parts.length; k++) {
            if (!isValue(parts[k])) { this.report(parts[k], `\`${parts[k].text}\` is not a value \`${what}\` takes.`); continue; }
            out.push(parts[k].text);
        }
        if (max != null && out.length > max) {
            this.report(parts[from], `\`${what}\` takes at most ${max} argument${max === 1 ? "" : "s"}; ${out.length - max} more were ignored.`);
        }
        return out;
    }

    workspace() {
        while (this.current?.kind === "end") this.i++;
        const first = this.current;
        if (!first) {
            this.problems.push({ file: null, line: 1, construct: "workspace", message: "The file is empty." });
            return;
        }
        if (lower(first) !== "workspace") {
            this.report(first, "A workspace file opens with `workspace`; nothing was read.");
            return;
        }
        const { parts, block } = this.statement();
        if (parts[1] && lower(parts[1]) === "extends") {
            this.report(parts[1], "`workspace extends` is outside the supported DSL subset; nothing was read.");
            return;
        }
        const [name, description] = this.values(parts, 1, 2, "workspace");
        this.name = blank(name);
        this.description = blank(description);
        if (!block) {
            this.report(first, "`workspace` has no block, so it defines nothing.");
            return;
        }
        let position = 0;
        for (const s of this.block(first)) {
            const head = s.parts[0];
            const word = lower(head);
            if (word === "!identifiers") this.identifiers(s.parts, position === 0);
            else if (word === "model" && s.block) { this.model(head, null, null); this.modelSeen = true; }
            else if (word === "views" && s.block) this.viewsBlock(head);
            else this.unknown(head, s.block, "workspace");
            position++;
        }
        if (!this.identifiersSeen) this.report(first, "The workspace does not declare `!identifiers hierarchical`; identifiers were read flat.");
    }

    leftover() {
        while (this.i < this.tokens.length && this.current.kind === "end") this.i++;
        const t = this.current;
        if (t) this.report(t, `\`${t.text}\` stands after the workspace closed; it and everything after it were not read.`);
    }

    // The rule starts the workspace with `!identifiers hierarchical`. Anywhere
    // else before the first element it is still honoured, and reported.
    identifiers(parts, first) {
        const [mode] = this.values(parts, 1, 1, "!identifiers");
        if (this.modelSeen) {
            this.report(parts[0], "`!identifiers` comes after the model began, so the elements before it were read flat; move it first.");
            return;
        }
        if (!first) this.report(parts[0], "`!identifiers` is not the workspace's first statement; the workspace starts with it, so move it first.");
        this.identifiersSeen = true;
        this.hierarchical = String(mode).toLowerCase() === "hierarchical";
        if (!this.hierarchical) this.report(parts[0], `\`!identifiers ${mode ?? ""}\`: only \`hierarchical\` is supported; identifiers were read flat.`);
    }

    // ── model ──

    // parent: the element whose block this is, or null at the top of the model.
    model(opener, parent, group) {
        for (const s of this.block(opener)) {
            const head = s.parts[0];
            const word = lower(head);
            const where = parent ? parent.kind : "model";
            if (word === "!identifiers" && !parent) { this.identifiers(s.parts, false); continue; }
            if (word === "tags" && parent && !s.block) { parent.tags.push(...splitTags(this.values(s.parts, 1, 1, "tags")[0])); continue; }
            if (word === "group" && [null, "softwareSystem", "container"].includes(parent?.kind ?? null)) { this.group(s, parent, group, (g) => this.model(head, parent, g)); continue; }
            if (this.relationship(s, parent, null)) continue;
            const keyword = this.keyword(s.parts);
            if (lower(keyword) === "deploymentenvironment" && !parent && !group) { this.environment(s); continue; }
            const spec = keyword && STATIC[lower(keyword)];
            if (spec) {
                if (!spec.inside.includes(parent?.kind ?? null)) {
                    this.report(keyword, `\`${keyword.text}\` cannot be declared inside \`${where}\`; it was skipped.`);
                    if (s.block) this.skip();
                    continue;
                }
                const element = this.declare(s.parts, spec, parent, group, null);
                if (s.block) {
                    if (element) this.model(keyword, element, null);
                    else this.skip();
                }
                continue;
            }
            this.unknown(keyword ?? head, s.block, where);
        }
    }

    group(s, parent, group, body) {
        const head = s.parts[0];
        const [name] = this.values(s.parts, 1, 1, "group");
        if (!s.block) { this.report(head, "`group` has no block, so it groups nothing."); return; }
        if (group) { this.report(head, "A group inside a group is outside the supported DSL subset; its elements were read into the outer group."); }
        if (!name) this.report(head, "`group` was declared with no name.");
        body(group ?? blank(name));
    }

    environment(s) {
        const head = this.keyword(s.parts);
        const assigned = head !== s.parts[0] ? s.parts[0].text : null;
        const [name] = this.values(s.parts, s.parts.indexOf(head) + 1, 1, "deploymentEnvironment");
        if (!name) { this.report(head, "`deploymentEnvironment` was declared with no name; it was skipped."); if (s.block) this.skip(); return; }
        const env = { id: assigned, name, file: head.file, line: head.line };
        if (this.environments.some((e) => e.name.toLowerCase() === name.toLowerCase())) this.report(head, `Deployment environment \`${name}\` is declared more than once.`);
        this.environments.push(env);
        if (s.block) this.deployment(head, env, null, null);
    }

    deployment(opener, env, parent, group) {
        for (const s of this.block(opener)) {
            const head = s.parts[0];
            const word = lower(head);
            const where = parent ? parent.kind : "deploymentEnvironment";
            if (word === "tags" && parent && !s.block) { parent.tags.push(...splitTags(this.values(s.parts, 1, 1, "tags")[0])); continue; }
            if (word === "group") { this.group(s, parent, group, (g) => this.deployment(head, env, parent, g)); continue; }
            if (this.relationship(s, parent, env)) continue;
            const keyword = this.keyword(s.parts);
            const name = lower(keyword);
            const spec = DEPLOYMENT[name];
            const instance = INSTANCES[name];
            if (spec || instance) {
                const allowed = spec ? spec.inside : ["deploymentNode"];
                if (!allowed.includes(parent?.kind ?? null)) {
                    this.report(keyword, `\`${keyword.text}\` cannot be declared inside \`${where}\`; it was skipped.`);
                    if (s.block) this.skip();
                    continue;
                }
                const element = spec ? this.declare(s.parts, spec, parent, group, env) : this.instance(s.parts, instance, parent, group, env);
                if (s.block) {
                    if (!element) this.skip();
                    else if (spec?.kind === "deploymentNode") this.deployment(keyword, env, element, null);
                    else this.leaf(keyword, element);
                }
                continue;
            }
            this.unknown(keyword ?? head, s.block, where);
        }
    }

    // The block of an element that holds no other element: tags and relationships.
    leaf(opener, element) {
        for (const s of this.block(opener)) {
            const head = s.parts[0];
            if (lower(head) === "tags" && !s.block) { element.tags.push(...splitTags(this.values(s.parts, 1, 1, "tags")[0])); continue; }
            if (this.relationship(s, element, null)) continue;
            this.unknown(head, s.block, element.kind);
        }
    }

    // The declaring keyword: after `id =` when there is one.
    keyword(parts) {
        if (parts.length > 2 && parts[1].kind === "assign") return parts[2];
        return parts[0];
    }

    idFor(local, parent, env) {
        if (local.startsWith("~") && parent) return `${parent.id}.${local}`;
        const prefix = parent ? parent.id : env?.id ?? null;
        return this.hierarchical && prefix ? `${prefix}.${local}` : local;
    }

    // An unnamed element has no identifier in Structurizr. It gets `~<slug>`, which
    // no reference can spell, so it can never shadow or collide with a named one.
    register(element, token, anonymous) {
        let id = element.id;
        if (anonymous) {
            let n = 1;
            while (this.anonymousIds.has(id.toLowerCase())) id = `${element.id}-${++n}`;
            element.id = id;
            this.anonymousIds.add(id.toLowerCase());
            this.elements.push(element);
            return element;
        }
        if (this.byId.has(id.toLowerCase())) {
            this.report(token, `\`${id}\` is declared more than once; the first declaration is the one references resolve to.`);
            this.elements.push(element);
            return element;
        }
        this.byId.set(id.toLowerCase(), element);
        this.elements.push(element);
        return element;
    }

    declare(parts, spec, parent, group, env) {
        const keyword = this.keyword(parts);
        const at = parts.indexOf(keyword);
        if (at === 2 && !isValue(parts[0])) this.report(parts[0], "An identifier is a bare word.");
        const assigned = at === 2 ? parts[0].text : null;
        const values = this.values(parts, at + 1, spec.args.length, keyword.text);
        const fields = Object.fromEntries(spec.args.map((a, k) => [a, blank(values[k])]));
        if (!fields.name) { this.report(keyword, `\`${keyword.text}\` was declared with no name; it was skipped.`); return null; }
        if (assigned && !IDENTIFIER.test(assigned)) this.report(parts[0], `\`${assigned}\` is not an identifier: letters, digits, \`_\`, and \`-\` only.`);
        const local = assigned ?? `~${slug(fields.name)}`;
        const element = {
            id: this.idFor(local, parent, env),
            kind: spec.kind,
            name: fields.name,
            description: fields.description,
            technology: spec.args.includes("technology") ? fields.technology : null,
            tags: splitTags(fields.tags),
            parent: parent?.id ?? null,
            group: group ?? null,
            environment: env?.name ?? null,
            file: keyword.file,
            line: keyword.line,
        };
        if (fields.instances) element.instances = fields.instances;
        if (!assigned) element.anonymous = true;
        return this.register(element, keyword, !assigned);
    }

    instance(parts, spec, parent, group, env) {
        const keyword = this.keyword(parts);
        const at = parts.indexOf(keyword);
        const assigned = at === 2 ? parts[0].text : null;
        const [target, deploymentGroups, tags] = this.values(parts, at + 1, 3, keyword.text);
        if (!target) { this.report(keyword, `\`${keyword.text}\` names nothing to instantiate; it was skipped.`); return null; }
        if (blank(deploymentGroups)) this.report(keyword, `Deployment groups on \`${keyword.text}\` are outside the supported DSL subset; they were ignored.`);
        if (assigned && !IDENTIFIER.test(assigned)) this.report(parts[0], `\`${assigned}\` is not an identifier: letters, digits, \`_\`, and \`-\` only.`);
        const local = assigned ?? `~${slug(target)}-instance`;
        const element = {
            id: this.idFor(local, parent, env),
            kind: spec.kind,
            name: null,
            description: null,
            technology: null,
            tags: splitTags(tags),
            parent: parent?.id ?? null,
            group: group ?? null,
            environment: env?.name ?? null,
            instanceOf: target,
            file: keyword.file,
            line: keyword.line,
        };
        if (!assigned) element.anonymous = true;
        this.pendingInstances.push({ element, of: spec.of, token: keyword, scope: parent?.id ?? env?.id ?? null });
        return this.register(element, keyword, !assigned);
    }

    // `[id =] source -> destination ["description"] ["technology"] ["tags"]`, the
    // source implicit inside an element block when the arrow leads or it is `this`.
    relationship(s, parent, env) {
        const parts = s.parts;
        const arrow = parts.findIndex((t) => t.kind === "arrow");
        if (arrow < 0) return false;
        const at = parts.length > 1 && parts[1].kind === "assign" ? 2 : 0;
        const assigned = at === 2 ? parts[0].text : null;
        const written = arrow > at ? parts[arrow - 1] : null;
        const scope = parent?.id ?? env?.id ?? null;
        const source = arrow === at ? parent?.id ?? null : lower(written) === "this" ? parent?.id ?? null : written.text;
        const destination = parts[arrow + 1];
        if (arrow - at > 1) this.report(parts[at], `\`${parts[at].text}\` before the relationship's source is outside the supported DSL subset.`);
        if (!source || !isValue(destination)) {
            this.report(parts[arrow], "A relationship needs an element on both sides of `->`; it was skipped.");
            if (s.block) this.skip();
            return true;
        }
        const [description, technology, tags] = this.values(parts, arrow + 2, 3, "->");
        const relationship = {
            id: assigned,
            source,
            destination: destination.text,
            description: blank(description),
            technology: blank(technology),
            tags: splitTags(tags),
            file: parts[arrow].file,
            line: parts[arrow].line,
        };
        if (s.block) {
            for (const b of this.block(parts[arrow])) {
                if (lower(b.parts[0]) === "tags" && !b.block) relationship.tags.push(...splitTags(this.values(b.parts, 1, 1, "tags")[0]));
                else this.unknown(b.parts[0], b.block, "relationship");
            }
        }
        this.pendingRelationships.push({ relationship, scope });
        return true;
    }

    // ── views ──

    viewsBlock(opener) {
        for (const s of this.block(opener)) {
            const head = s.parts[0];
            const spec = VIEWS[lower(head)];
            if (spec) this.view(spec, s);
            else this.unknown(head, s.block, "views");
        }
    }

    view(spec, s) {
        const head = s.parts[0];
        const max = spec.scope.length ? (spec.environment ? 4 : 3) : 2;
        const values = this.values(s.parts, 1, max, head.text);
        let k = 0;
        const scope = spec.scope.length ? blank(values[k++]) : null;
        const environment = spec.environment ? blank(values[k++]) : null;
        const key = blank(values[k++]);
        const description = blank(values[k]);
        const view = {
            key, kind: spec.kind, scope, environment, title: null, description,
            include: [], exclude: [], autolayout: null, steps: [], file: head.file, line: head.line,
        };
        if (spec.scope.length && !scope) this.report(head, `A \`${head.text}\` view needs a scope.`);
        if (spec.environment && !environment) this.report(head, "A `deployment` view needs an environment.");
        if (s.block) {
            for (const b of this.block(head)) {
                const word = lower(b.parts[0]);
                if (b.block) { this.unknown(b.parts[0], true, `${spec.kind} view`); continue; }
                if (word === "include" || word === "exclude") {
                    for (const t of b.parts.slice(1)) {
                        if (!isValue(t) || /==|->|^element\.|^relationship\.|^->|->$/i.test(t.text)) {
                            this.report(t, `\`${word} ${t.text}\` is an expression, outside the supported DSL subset; only \`*\` and identifiers are read.`);
                            continue;
                        }
                        view[word].push({ id: t.text, token: t });
                    }
                    continue;
                }
                if (word === "title") { view.title = blank(this.values(b.parts, 1, 1, "title")[0]); continue; }
                if (word === "autolayout") { view.autolayout = this.autolayout(b.parts); continue; }
                if (spec.kind === "dynamic" && b.parts.some((t) => t.kind === "arrow")) { this.step(b.parts, view); continue; }
                this.unknown(b.parts[0], false, `${spec.kind} view`);
            }
        }
        this.views.push(view);
    }

    autolayout(parts) {
        const [direction = "tb", rank, node] = this.values(parts, 1, 3, "autolayout");
        const layout = { direction: direction.toLowerCase(), rankSeparation: null, nodeSeparation: null };
        if (!DIRECTIONS.has(layout.direction)) this.report(parts[1], `\`autolayout ${direction}\`: the direction is one of tb, bt, lr, rl.`);
        for (const [field, value] of [["rankSeparation", rank], ["nodeSeparation", node]]) {
            if (value == null) continue;
            if (/^\d+$/.test(value)) layout[field] = Number(value);
            else this.report(parts[0], `\`autolayout\`: \`${value}\` is not a whole number of pixels.`);
        }
        return layout;
    }

    step(parts, view) {
        const arrow = parts.findIndex((t) => t.kind === "arrow");
        if (arrow !== 1 || !isValue(parts[0]) || !isValue(parts[2])) {
            this.report(parts[0], "A dynamic step is `source -> destination [\"description\"] [\"technology\"]`; numbered and parallel steps are outside the supported DSL subset.");
            return;
        }
        const [description, technology] = this.values(parts, 3, 2, "->");
        view.steps.push({
            order: view.steps.length + 1,
            source: parts[0].text,
            destination: parts[2].text,
            description: blank(description),
            technology: blank(technology),
            file: parts[1].file,
            line: parts[1].line,
            tokens: [parts[0], parts[2]],
        });
    }

    // ── resolution ──

    // An identifier as written, resolved from the innermost scope outward, then as written.
    resolve(name, scope) {
        if (!name) return null;
        let current = this.hierarchical ? scope : null;
        while (current) {
            const hit = this.byId.get(`${current}.${name}`.toLowerCase());
            if (hit) return hit;
            const cut = current.lastIndexOf(".");
            current = cut < 0 ? null : current.slice(0, cut);
        }
        return this.byId.get(String(name).toLowerCase()) ?? null;
    }

    // Whether a relationship runs from `a` or an element inside it to `b` or an
    // element inside it: Structurizr's implied relationships, which a dynamic step
    // between two systems rests on when only their containers are related.
    related(a, b) {
        const within = (id, outer) => id === outer || id.startsWith(`${outer}.`) || this.ancestors(id).includes(outer);
        return this.relationships.some((r) => within(r.source, a) && within(r.destination, b));
    }

    ancestors(id) {
        const out = [];
        let e = this.byId.get(id.toLowerCase());
        while (e?.parent) { out.push(e.parent); e = this.byId.get(e.parent.toLowerCase()); }
        return out;
    }

    finish() {
        for (const { element, of, token, scope } of this.pendingInstances) {
            const target = this.resolve(element.instanceOf, scope);
            if (!target) { this.report(token, `\`${element.instanceOf}\` is not an element in this workspace, so this instance names nothing.`); continue; }
            if (target.kind !== of) { this.report(token, `\`${token.text}\` instantiates a ${of}; \`${target.id}\` is a ${target.kind}.`); continue; }
            element.instanceOf = target.id;
            element.name = target.name;
            element.description = target.description;
            element.technology = target.technology;
        }
        const relationshipIds = new Map();
        for (const { relationship, scope } of this.pendingRelationships) {
            const source = this.resolve(relationship.source, scope);
            const destination = this.resolve(relationship.destination, scope);
            if (!source || !destination) {
                const missing = source ? relationship.destination : relationship.source;
                this.problems.push({ file: relationship.file, line: relationship.line, construct: missing, message: `\`${missing}\` is not an element in this workspace, so this relationship was dropped.` });
                continue;
            }
            relationship.source = source.id;
            relationship.destination = destination.id;
            if (relationship.id) {
                relationship.id = this.hierarchical && scope ? `${scope}.${relationship.id}` : relationship.id;
                const taken = relationshipIds.has(relationship.id.toLowerCase()) || this.byId.has(relationship.id.toLowerCase());
                if (taken) this.problems.push({ file: relationship.file, line: relationship.line, construct: relationship.id, message: `\`${relationship.id}\` is declared more than once; the first declaration is the one references resolve to.` });
                else relationshipIds.set(relationship.id.toLowerCase(), relationship);
            } else delete relationship.id;
            this.relationships.push(relationship);
        }

        const keys = new Map();
        for (const view of this.views) {
            const at = { file: view.file, line: view.line };
            const spec = Object.values(VIEWS).find((v) => v.kind === view.kind);
            if (!view.key) this.problems.push({ ...at, construct: view.kind, message: `This \`${view.kind}\` view has no key, so no chapter can embed it.` });
            else if (!VIEW_KEY.test(view.key)) this.problems.push({ ...at, construct: view.key, message: `View key \`${view.key}\` holds a character other than a letter, digit, \`_\`, or \`-\`.` });
            else if (keys.has(view.key.toLowerCase())) this.problems.push({ ...at, construct: view.key, message: `View key \`${view.key}\` is used by more than one view.` });
            else keys.set(view.key.toLowerCase(), view);

            if (view.scope && view.scope !== "*") {
                const scope = this.resolve(view.scope, null);
                if (!scope) this.problems.push({ ...at, construct: view.scope, message: `\`${view.scope}\` is not an element in this workspace, so the view has no scope.` });
                else if (!spec.scope.includes(scope.kind)) this.problems.push({ ...at, construct: view.scope, message: `A \`${view.kind}\` view is scoped to ${spec.scope.filter((s) => s !== "*").join(" or ")}; \`${scope.id}\` is a ${scope.kind}.` });
                else view.scope = scope.id;
            } else if (view.scope === "*" && !spec.scope.includes("*")) {
                this.problems.push({ ...at, construct: "*", message: `A \`${view.kind}\` view cannot be scoped to \`*\`.` });
            }
            if (view.environment && !this.environments.some((e) => e.name.toLowerCase() === view.environment.toLowerCase() || e.id?.toLowerCase() === view.environment.toLowerCase())) {
                this.problems.push({ ...at, construct: view.environment, message: `\`${view.environment}\` is not a deployment environment in this workspace.` });
            } else if (view.environment) {
                view.environment = this.environments.find((e) => e.name.toLowerCase() === view.environment.toLowerCase() || e.id?.toLowerCase() === view.environment.toLowerCase()).name;
            }
            for (const list of ["include", "exclude"]) {
                view[list] = view[list].map(({ id, token }) => {
                    if (id === "*") return id;
                    const hit = this.resolve(id, null) ?? relationshipIds.get(id.toLowerCase());
                    if (!hit) { this.report(token, `\`${list} ${id}\` names nothing in this workspace.`); return id; }
                    return hit.id;
                });
            }
            for (const step of view.steps) {
                const [s, d] = step.tokens;
                delete step.tokens;
                for (const [field, token] of [["source", s], ["destination", d]]) {
                    const hit = this.resolve(token.text, null);
                    if (!hit) this.report(token, `\`${token.text}\` is not an element in this workspace, so this step is not drawn.`);
                    else step[field] = hit.id;
                }
                const s1 = this.byId.get(step.source.toLowerCase());
                const d1 = this.byId.get(step.destination.toLowerCase());
                if (s1 && d1) {
                    if (this.related(s1.id, d1.id)) continue;
                    if (this.related(d1.id, s1.id)) { step.response = true; continue; }
                    this.report(s, `No relationship from \`${s1.id}\` to \`${d1.id}\`, or below them, backs this dynamic step.`);
                }
            }
        }
    }
}

/**
 * Reads workspace text. `include(path, fromFile)` resolves an `!include` and
 * returns `{ file, text }` or `{ problem }`; without one, every include is a
 * problem.
 */
export function parseWorkspace(source, { file = "workspace.dsl", include } = {}) {
    const problems = [];
    const resolver = include ?? (() => ({ problem: "`!include` cannot be read here: no folder to read it from." }));
    const tokens = expandIncludes(tokenize(source, file, problems), resolver, problems, [file]);
    const parser = new Parser(tokens, problems);
    parser.workspace();
    parser.leftover();
    parser.finish();
    for (const e of parser.elements) if (e.anonymous !== true) delete e.anonymous;
    return {
        name: parser.name ?? null,
        description: parser.description ?? null,
        hierarchical: parser.hierarchical,
        elements: parser.elements,
        relationships: parser.relationships,
        views: parser.views,
        environments: parser.environments,
        problems: problems.sort((a, b) => (a.file ?? "").localeCompare(b.file ?? "") || a.line - b.line),
    };
}

/**
 * Reads a workspace file from disk. An `!include` is a relative path from the
 * including file and must land on a file under the workspace's own `c4/`
 * folder. File names in the result are relative to `root`, with `/`.
 */
export function readWorkspace(workspacePath, { root = process.cwd() } = {}) {
    const absolute = path.resolve(root, workspacePath);
    const label = (p) => path.relative(root, p).split(path.sep).join("/");
    const allowed = path.join(path.dirname(absolute), "c4");
    const fold = (p) => (process.platform === "win32" ? p.toLowerCase() : p);
    const inside = (folder, p) => {
        const r = path.relative(fold(folder), fold(p));
        return r === "" || (!r.startsWith("..") && !path.isAbsolute(r));
    };
    const include = (target, fromFile) => {
        if (/^[a-z][a-z0-9+.-]*:/i.test(target) || path.isAbsolute(target) || target.startsWith("/") || target.startsWith("\\")) {
            return { problem: `\`!include ${target}\`: only a relative path is supported.` };
        }
        const resolved = path.resolve(path.dirname(path.resolve(root, fromFile)), target);
        if (!inside(allowed, resolved)) return { problem: `\`!include ${target}\` lands outside \`${label(allowed)}\`, the only folder a workspace includes from.` };
        let stat;
        try { stat = statSync(resolved); } catch { return { problem: `\`!include ${target}\`: no such file.` }; }
        if (!stat.isFile()) return { problem: `\`!include ${target}\` is not a file; including a folder is outside the supported DSL subset.` };
        if (!inside(realpathSync(allowed), realpathSync(resolved))) return { problem: `\`!include ${target}\` is a link to a file outside \`${label(allowed)}\`; it was not read.` };
        return { file: label(resolved), text: readFileSync(resolved, "utf8") };
    };
    return parseWorkspace(readFileSync(absolute, "utf8"), { file: label(absolute), include });
}

if (process.argv[1] && path.resolve(process.argv[1]) === fileURLToPath(import.meta.url)) {
    const target = process.argv[2];
    if (!target) {
        console.error("usage: node c4.mjs <workspace.dsl>");
        process.exit(2);
    }
    const result = readWorkspace(target);
    console.log(JSON.stringify(result, null, 2));
    process.exit(result.problems.length ? 1 : 0);
}
