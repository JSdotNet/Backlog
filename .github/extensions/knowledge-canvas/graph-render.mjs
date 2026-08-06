// graph-render.mjs — HTML shell for the knowledge-graph canvas.
//
// An Obsidian-style force-directed view over _meta/knowledge-graph.json.
// Rendering uses CDN-hosted Cytoscape.js (core only, built-in `cose` layout)
// so the extension itself stays dependency-free, matching render.mjs.

export function renderGraphPage({ scopes = ["."], scope = "." } = {}) {
    const scopeOptions = scopes
        .map(
            (value) =>
                `<option value="${value}"${value === scope ? " selected" : ""}>${
                    value === "." ? "All folders" : value
                }</option>`
        )
        .join("");

    return `<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<title>Knowledge graph</title>
<script src="https://cdn.jsdelivr.net/npm/cytoscape@3/dist/cytoscape.min.js"></script>
<style>
  :root { color-scheme: light dark; }
  body { font-family: system-ui, sans-serif; margin: 0; height: 100vh; overflow: hidden; }
  #layout { display: flex; height: 100vh; }
  #cy { flex: 1; min-width: 0; }
  #panel { width: 300px; min-width: 260px; overflow-y: auto; padding: 1rem; border-left: 1px solid #8884; font-size: 0.82rem; }
  #panel h3 { margin: 1rem 0 0.4rem; font-size: 0.8rem; text-transform: uppercase; opacity: 0.65; letter-spacing: 0.04em; }
  #panel h3:first-child { margin-top: 0; }
  #toolbar { position: absolute; top: 0.6rem; left: 0.6rem; display: flex; gap: 0.4rem; align-items: center; flex-wrap: wrap; z-index: 5; }
  #toolbar input, #toolbar select, #toolbar button { font: inherit; font-size: 0.78rem; padding: 0.25rem 0.45rem; border-radius: 6px; border: 1px solid #8886; background: Canvas; color: CanvasText; }
  label.toggle { font-size: 0.78rem; display: flex; align-items: center; gap: 0.25rem; background: Canvas; padding: 0.2rem 0.45rem; border-radius: 6px; border: 1px solid #8886; }
  .legend-row { display: flex; align-items: center; gap: 0.45rem; margin: 0.2rem 0; }
  .swatch { width: 0.75rem; height: 0.75rem; border-radius: 50%; flex: none; }
  .field { margin: 0.3rem 0; word-break: break-word; }
  .field b { display: block; opacity: 0.65; font-weight: 600; font-size: 0.72rem; text-transform: uppercase; }
  .ref { display: block; font-family: monospace; font-size: 0.72rem; cursor: pointer; color: #0969da; margin: 0.1rem 0; }
  .ref:hover { text-decoration: underline; }
  .status { display: inline-block; padding: 0.1rem 0.5rem; border-radius: 999px; font-size: 0.68rem; font-weight: 600; text-transform: uppercase; }
  .status-active, .status-done, .status-adopted { background: #1a7f37; color: white; }
  .status-draft, .status-candidate { background: #8884; }
  .status-proposed, .status-ready, .status-trial, .status-hold { background: #9a6700; color: white; }
  .status-deprecated, .status-blocked, .status-retired { background: #cf222e; color: white; }
  .status-in-progress { background: #0969da; color: white; }
  .stat { display: flex; justify-content: space-between; gap: 1rem; font-size: 0.76rem; padding: 0.08rem 0; }
  .stat span:last-child { font-variant-numeric: tabular-nums; opacity: 0.75; }
  .muted { opacity: 0.6; }
  #problems { color: #9a6700; font-size: 0.75rem; }
</style>
</head>
<body>
<div id="layout">
  <div id="cy"></div>
  <div id="toolbar">
    <input id="search" type="search" placeholder="Search nodes…" />
    <select id="scope" title="Graph scope">${scopeOptions}</select>
    <select id="folder"><option value="">All folders</option></select>
    <label class="toggle"><input id="show-contains" type="checkbox" /> containment edges</label>
    <label class="toggle"><input id="show-headings" type="checkbox" /> plain headings</label>
    <button id="relayout">Re-layout</button>
    <button id="fit">Fit</button>
  </div>
  <div id="panel">
    <h3>Selection</h3>
    <div id="details" class="muted">Click a node to inspect it.</div>
    <h3>Legend</h3>
    <div id="legend"></div>
    <h3>Stats</h3>
    <div id="stats"></div>
    <h3>Problems</h3>
    <div id="problems"></div>
  </div>
</div>
<script>
const FOLDER_COLORS = {
  ".arc42": "#0969da",
  ".domain": "#8250df",
  ".backlog": "#1a7f37",
  ".tech": "#bc4c00",
};
const EXTERNAL_COLOR = "#57606a";
const EDGE_COLORS = {
  "depends-on": "#bc4c00",
  related: "#8886",
  implements: "#1a7f37",
  contains: "#88888833",
};

let cy;
let graph;

function colorFor(node) {
  return FOLDER_COLORS["." + node.folder] || EXTERNAL_COLOR;
}

function escapeHtml(value) {
  return String(value).replace(/[&<>"]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;" }[c]));
}

async function load() {
  const scope = document.getElementById("scope").value;
  const res = await fetch("/api/graph?scope=" + encodeURIComponent(scope));
  graph = await res.json();

  if (cy) cy.destroy();

  // Folder is stored as the bare kind ("arc42"); colour and filter by it.
  const elements = [
    ...graph.elements.nodes.map((n) => ({
      data: { ...n.data, color: colorFor(n.data) },
    })),
    ...graph.elements.edges.map((e) => ({ data: e.data })),
  ];

  cy = cytoscape({
    container: document.getElementById("cy"),
    elements,
    style: [
      {
        selector: "node",
        style: {
          "background-color": "data(color)",
          label: "data(label)",
          "font-size": 7,
          color: "#888",
          "text-valign": "center",
          "text-halign": "right",
          "text-margin-x": 3,
          width: 10,
          height: 10,
        },
      },
      { selector: 'node[type = "file"]', style: { width: 20, height: 20, "font-size": 9, shape: "round-rectangle", color: "#aaa" } },
      { selector: 'node[type = "heading"]', style: { width: 6, height: 6, "background-opacity": 0.5 } },
      { selector: 'node[type = "external"]', style: { shape: "diamond", "background-opacity": 0.6 } },
      { selector: "node[?outOfScope]", style: { "background-opacity": 0.35, "border-width": 1, "border-style": "dashed", "border-color": "#8888", "font-size": 6 } },
      { selector: 'node[status = "deprecated"], node[status = "retired"], node[status = "blocked"]', style: { "border-width": 2, "border-color": "#cf222e" } },
      {
        selector: "edge",
        style: {
          width: 1,
          "line-color": "#8886",
          "curve-style": "bezier",
          "target-arrow-shape": "triangle",
          "target-arrow-color": "#8886",
          "arrow-scale": 0.5,
          opacity: 0.75,
        },
      },
      { selector: 'edge[type = "depends-on"]', style: { "line-color": EDGE_COLORS["depends-on"], "target-arrow-color": EDGE_COLORS["depends-on"], width: 1.4 } },
      { selector: 'edge[type = "implements"]', style: { "line-color": EDGE_COLORS.implements, "target-arrow-color": EDGE_COLORS.implements } },
      { selector: 'edge[type = "contains"]', style: { "line-style": "dotted", opacity: 0.35, "target-arrow-shape": "none" } },
      { selector: ".dimmed", style: { opacity: 0.08 } },
      { selector: ".highlight", style: { "border-width": 3, "border-color": "#f0b400" } },
    ],
    layout: { name: "grid" },
    wheelSensitivity: 0.25,
  });

  cy.on("tap", "node", (evt) => showDetails(evt.target));
  cy.on("tap", (evt) => {
    if (evt.target === cy) clearHighlight();
  });

  populateFolders();
  renderLegend();
  renderStats();
  renderProblems();
  applyFilters();
  runLayout();
}

function runLayout() {
  cy.layout({
    name: "cose",
    animate: false,
    nodeRepulsion: 9000,
    idealEdgeLength: 60,
    nestingFactor: 0.8,
    gravity: 0.4,
    numIter: 1200,
    fit: true,
    padding: 40,
  }).run();
}

function applyFilters() {
  const folder = document.getElementById("folder").value;
  const showContains = document.getElementById("show-contains").checked;
  const showHeadings = document.getElementById("show-headings").checked;
  const term = document.getElementById("search").value.trim().toLowerCase();

  cy.batch(() => {
    cy.nodes().forEach((n) => {
      const d = n.data();
      let visible = true;
      if (folder && d.folder !== folder) visible = false;
      if (!showHeadings && d.type === "heading") visible = false;
      n.style("display", visible ? "element" : "none");
    });
    cy.edges().forEach((e) => {
      const visible = showContains || e.data("type") !== "contains";
      e.style("display", visible ? "element" : "none");
    });
    cy.nodes().removeClass("highlight dimmed");
    if (term) {
      const matches = cy.nodes().filter((n) => String(n.data("label")).toLowerCase().includes(term) || String(n.data("id")).toLowerCase().includes(term));
      cy.nodes().addClass("dimmed");
      matches.removeClass("dimmed").addClass("highlight");
    }
  });
}

function clearHighlight() {
  cy.elements().removeClass("dimmed highlight");
  document.getElementById("details").className = "muted";
  document.getElementById("details").textContent = "Click a node to inspect it.";
}

function showDetails(node) {
  const d = node.data();
  const neighborhood = node.closedNeighborhood();
  cy.elements().addClass("dimmed");
  neighborhood.removeClass("dimmed");
  node.addClass("highlight");

  const outgoing = node.outgoers("edge").filter((e) => e.data("type") !== "contains");
  const incoming = node.incomers("edge").filter((e) => e.data("type") !== "contains");

  const refList = (edges, dir) =>
    edges
      .map((e) => {
        const other = dir === "out" ? e.target() : e.source();
        return '<span class="ref" data-id="' + escapeHtml(other.id()) + '">' + escapeHtml(e.data("type")) + " → " + escapeHtml(other.data("label")) + "</span>";
      })
      .join("") || '<span class="muted">none</span>';

  const parts = [
    "<div class=\\"field\\"><b>label</b>" + escapeHtml(d.label) + "</div>",
    d.status ? '<div class="field"><b>status</b><span class="status status-' + escapeHtml(String(d.status).toLowerCase()) + '">' + escapeHtml(d.status) + "</span></div>" : "",
    d.kind ? "<div class=\\"field\\"><b>kind</b>" + escapeHtml(d.kind) + "</div>" : "",
    d.version ? "<div class=\\"field\\"><b>version</b>" + escapeHtml(d.version) + "</div>" : "",
    "<div class=\\"field\\"><b>id</b><code>" + escapeHtml(d.id) + "</code></div>",
    "<div class=\\"field\\"><b>type</b>" + escapeHtml(d.type) + "</div>",
    d.alternatives ? "<div class=\\"field\\"><b>alternatives</b>" + escapeHtml([].concat(d.alternatives).join(", ")) + "</div>" : "",
    d.issue ? "<div class=\\"field\\"><b>issue</b>" + escapeHtml(d.issue) + "</div>" : "",
    "<div class=\\"field\\"><b>outgoing</b>" + refList(outgoing, "out") + "</div>",
    "<div class=\\"field\\"><b>incoming</b>" + refList(incoming, "in") + "</div>",
  ];

  const details = document.getElementById("details");
  details.className = "";
  details.innerHTML = parts.join("");
  details.querySelectorAll(".ref").forEach((el) => {
    el.addEventListener("click", () => {
      const target = cy.getElementById(el.dataset.id);
      if (target.length) {
        cy.animate({ center: { eles: target }, zoom: 1.6 }, { duration: 250 });
        showDetails(target);
      }
    });
  });
}

function populateFolders() {
  const select = document.getElementById("folder");
  select.innerHTML = '<option value="">All folders</option>';
  const folders = [...new Set(cy.nodes().map((n) => n.data("folder")).filter(Boolean))].sort();
  for (const folder of folders) {
    const option = document.createElement("option");
    option.value = folder;
    option.textContent = "." + folder;
    select.appendChild(option);
  }
}

function renderLegend() {
  const rows = Object.entries(FOLDER_COLORS).map(
    ([name, color]) => '<div class="legend-row"><span class="swatch" style="background:' + color + '"></span>' + name + "</div>"
  );
  rows.push('<div class="legend-row"><span class="swatch" style="background:' + EXTERNAL_COLOR + '"></span>external</div>');
  rows.push('<div class="legend-row muted">large = file · small = chapter · diamond = external</div>');
  rows.push('<div class="legend-row muted">dashed = outside the current scope</div>');
  document.getElementById("legend").innerHTML = rows.join("");
}

function renderStats() {
  const s = graph.stats;
  const group = (title, obj) =>
    "<div class=\\"field\\"><b>" + title + "</b>" +
    Object.entries(obj)
      .sort((a, b) => b[1] - a[1])
      .map(([k, v]) => '<div class="stat"><span>' + escapeHtml(k) + "</span><span>" + v + "</span></div>")
      .join("") +
    "</div>";
  document.getElementById("stats").innerHTML =
    '<div class="stat"><span>nodes</span><span>' + s.nodes + "</span></div>" +
    '<div class="stat"><span>edges</span><span>' + s.edges + "</span></div>" +
    group("by folder", s.nodesByFolder) +
    group("by type", s.nodesByType) +
    group("by status", s.nodesByStatus) +
    group("edges by type", s.edgesByType);
}

function renderProblems() {
  const el = document.getElementById("problems");
  if (!graph.problems.length) {
    el.innerHTML = '<span class="muted">No broken references.</span>';
    return;
  }
  el.innerHTML = graph.problems.map((p) => "<div>[" + escapeHtml(p.severity) + "] " + escapeHtml(p.message) + "</div>").join("");
}

for (const id of ["folder", "show-contains", "show-headings"]) {
  document.getElementById(id).addEventListener("change", applyFilters);
}
document.getElementById("scope").addEventListener("change", load);
document.getElementById("search").addEventListener("input", applyFilters);
document.getElementById("relayout").addEventListener("click", runLayout);
document.getElementById("fit").addEventListener("click", () => cy.fit(undefined, 40));

load();
</script>
</body>
</html>`;
}
