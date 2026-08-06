// render.mjs — HTML shell served by the knowledge-canvas local server.
// Client-side rendering uses CDN-hosted `marked` (Markdown -> HTML) and
// `mermaid` (diagram rendering) so the extension itself stays dependency-free.

export function renderPage() {
    return `<!doctype html>
<html>
<head>
<meta charset="utf-8" />
<title>Knowledge canvas</title>
<script src="https://cdn.jsdelivr.net/npm/marked/marked.min.js"></script>
<script src="https://cdn.jsdelivr.net/npm/mermaid@10/dist/mermaid.min.js"></script>
<style>
  :root { color-scheme: light dark; }
  body { font-family: system-ui, sans-serif; margin: 0; display: flex; height: 100vh; }
  #doc { flex: 3; overflow-y: auto; padding: 1.5rem 2rem; }
  #panel { flex: 1; min-width: 260px; max-width: 360px; overflow-y: auto; padding: 1.5rem 1rem; border-left: 1px solid #8884; }
  #path-bar { font-family: monospace; font-size: 0.85rem; padding: 0.5rem 1rem; border-bottom: 1px solid #8884; opacity: 0.8; }
  #layout { display: flex; flex-direction: column; width: 100%; }
  #body { display: flex; flex: 1; min-height: 0; }
  .chapter-card { border: 1px solid #8884; border-radius: 8px; padding: 0.6rem 0.8rem; margin-bottom: 0.75rem; }
  .chapter-card h4 { margin: 0 0 0.35rem 0; font-size: 0.85rem; }
  .status { display: inline-block; padding: 0.1rem 0.5rem; border-radius: 999px; font-size: 0.7rem; font-weight: 600; text-transform: uppercase; }
  .status-active, .status-done { background: #1a7f37; color: white; }
  .status-draft { background: #8884; }
  .status-proposed, .status-ready { background: #9a6700; color: white; }
  .status-deprecated, .status-blocked { background: #cf222e; color: white; }
  .status-in-progress { background: #0969da; color: white; }
  .field { font-size: 0.78rem; margin: 0.25rem 0; word-break: break-word; }
  .field b { display: block; opacity: 0.7; font-weight: 600; }
  .issues { font-size: 0.8rem; }
  .issue-error { color: #cf222e; }
  .issue-warning { color: #9a6700; }
  .issue-info { opacity: 0.7; }
  pre.mermaid { background: transparent; }
  #empty { padding: 2rem; opacity: 0.7; }
</style>
</head>
<body>
<div id="layout">
  <div id="path-bar">No document open. Call <code>set_document</code> with a path under .domain/, .arc42/, .backlog/, .tech/, or .design/.</div>
  <div id="body">
    <div id="doc"><div id="empty">Waiting for a document…</div></div>
    <div id="panel"><h3>Metadata</h3><div id="meta-list"></div><h3>Lint</h3><div id="issue-list" class="issues"></div></div>
  </div>
</div>
<script>
mermaid.initialize({ startOnLoad: false, securityLevel: "loose" });

function statusClass(status) {
  return "status status-" + String(status || "unknown").toLowerCase();
}

function renderField(key, value) {
  if (value === null || value === undefined) return "";
  const display = Array.isArray(value) ? value.join(", ") : value;
  if (Array.isArray(value) && value.length === 0) return "";
  return '<div class="field"><b>' + key + '</b>' + escapeHtml(String(display)) + '</div>';
}

function escapeHtml(s) {
  const map = { "&": "&amp;", "<": "&lt;", ">": "&gt;", '"': "&quot;", "'": "&#39;" };
  return s.replace(/[&<>"']/g, (c) => map[c]);
}

async function loadDocument() {
  const res = await fetch("/api/document");
  if (res.status === 404) {
    document.getElementById("path-bar").textContent = "No document open. Call set_document with a path under .domain/, .arc42/, .backlog/, .tech/, or .design/.";
    document.getElementById("doc").innerHTML = '<div id="empty">Waiting for a document…</div>';
    document.getElementById("meta-list").innerHTML = "";
    document.getElementById("issue-list").innerHTML = "";
    return;
  }
  const data = await res.json();
  document.getElementById("path-bar").textContent = data.path;

  const html = marked.parse(data.raw);
  document.getElementById("doc").innerHTML = html;

  // Re-render any \`\`\`mermaid fences marked's default handling emits as <code class="language-mermaid">.
  const blocks = document.querySelectorAll("code.language-mermaid");
  let i = 0;
  for (const block of blocks) {
    const pre = document.createElement("pre");
    pre.className = "mermaid";
    pre.textContent = block.textContent;
    block.parentElement.replaceWith(pre);
    i++;
  }
  if (i > 0) {
    try { await mermaid.run({ querySelector: ".mermaid" }); } catch (e) { /* leave raw text on failure */ }
  }

  const metaList = document.getElementById("meta-list");
  metaList.innerHTML = "";
  if (data.fileMeta) {
    metaList.innerHTML += '<div class="chapter-card"><h4>File: ' + escapeHtml(data.fileTitle || "") + ' <span class="' + statusClass(data.fileMeta.status) + '">' + escapeHtml(data.fileMeta.status || "?") + '</span></h4>' +
      Object.entries(data.fileMeta).filter(([k]) => k !== "status").map(([k, v]) => renderField(k, v)).join("") + '</div>';
  }
  for (const chapter of data.chapters) {
    if (!chapter.meta || chapter.level === 1) continue;
    metaList.innerHTML += '<div class="chapter-card"><h4>' + "#".repeat(chapter.level) + ' ' + escapeHtml(chapter.text) + ' <span class="' + statusClass(chapter.meta.status) + '">' + escapeHtml(chapter.meta.status || "?") + '</span></h4>' +
      Object.entries(chapter.meta).filter(([k]) => k !== "status").map(([k, v]) => renderField(k, v)).join("") + '</div>';
  }

  const issueList = document.getElementById("issue-list");
  issueList.innerHTML = data.issues.length
    ? data.issues.map((i) => '<div class="issue-' + i.severity + '">' + escapeHtml(i.severity.toUpperCase()) + ": " + escapeHtml(i.message) + '</div>').join("")
    : '<div style="opacity:0.6">No lint issues found.</div>';
}

loadDocument();
setInterval(loadDocument, 4000);
</script>
</body>
</html>`;
}

