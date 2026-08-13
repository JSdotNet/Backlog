// Blazor's DragEventArgs is read-only: a handler cannot touch dataTransfer.
// Chromium will happily start a drag without a payload but then refuses to fire
// drop, which is why a drag that looked fine did nothing when released. These
// listeners supply the payload and the move cursor; all the actual reordering
// still happens in C#.
//
// Capture phase, so this runs before Blazor's own handler for the same event.
document.addEventListener(
    'dragstart',
    (event) => {
        const grip = event.target instanceof Element ? event.target.closest('[data-drag-grip]') : null;
        if (!grip || !event.dataTransfer) return;

        event.dataTransfer.effectAllowed = 'move';
        // Some payload is required for the drag to be considered valid.
        event.dataTransfer.setData('text/plain', grip.getAttribute('data-drag-grip') ?? 'entry');

        // Drag the whole card, not the sliver of rail that was grabbed.
        const card = grip.closest('.subitem-card, .entry-doc');
        if (card && event.dataTransfer.setDragImage) {
            const bounds = card.getBoundingClientRect();
            event.dataTransfer.setDragImage(card, event.clientX - bounds.left, event.clientY - bounds.top);
        }
    },
    true
);

document.addEventListener(
    'dragenter',
    (event) => {
        const zone = event.target instanceof Element
            ? event.target.closest('[data-drag-grip], [data-drop-zone]')
            : null;
        if (!zone) return;

        event.preventDefault();
    },
    true
);

document.addEventListener(
    'dragover',
    (event) => {
        const grip = event.target instanceof Element
            ? event.target.closest('[data-drag-grip], [data-drop-zone]')
            : null;
        if (!grip || !event.dataTransfer) return;

        event.dataTransfer.dropEffect = 'move';

        // A drop only fires where dragover was cancelled. Blazor's
        // :preventDefault does this too, but the zones appear mid-drag and a
        // frame where the attribute is not yet attached is a dropped drop.
        event.preventDefault();
    },
    true
);

// Keyboard reordering has to carry the focus ring with the thing it moved;// after the list re-renders the element is a different node, so the caller
// names it by id.
window.backlogFocus = (id) => {
    const element = document.getElementById(id);
    if (element) element.focus();
};

// The side pane is resized by dragging its edge. Pointer capture and the live
// width both belong in the browser; C# only hears the settled value, so a drag
// costs one interop call instead of one per frame.
const BACKLOG_PANE_MIN_REM = 24;
const BACKLOG_PANE_ABSOLUTE_MAX_REM = 200;
let backlogPaneOwner = null;

function backlogRootFontSize() {
    return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
}

function backlogPaneLayout() {
    return document.querySelector('[data-testid="knowledge-layout"]');
}

// The pane may take everything the backlog does not strictly need, so the real
// ceiling is the window, not a fixed number of rem.
function backlogPaneMaxRem(layout) {
    const rem = backlogRootFontSize();
    const styles = getComputedStyle(layout);
    const workspaceMinRem = parseFloat(styles.getPropertyValue('--workspace-min-width')) || 22;
    const gapRem = ((parseFloat(styles.columnGap) || 0) * 2) / rem;
    const available = (layout.clientWidth / rem) - workspaceMinRem - gapRem - 1;

    return Math.min(BACKLOG_PANE_ABSOLUTE_MAX_REM, Math.max(BACKLOG_PANE_MIN_REM, Math.round(available * 2) / 2));
}

function backlogPaneWidthAt(layout, clientX) {
    const rem = (layout.getBoundingClientRect().right - clientX) / backlogRootFontSize();
    return Math.min(backlogPaneMaxRem(layout), Math.max(BACKLOG_PANE_MIN_REM, Math.round(rem * 2) / 2));
}

function backlogReportPaneBounds() {
    const layout = backlogPaneLayout();
    if (!layout || !backlogPaneOwner) return;

    backlogPaneOwner.invokeMethodAsync('SetSidePaneMaxWidthAsync', backlogPaneMaxRem(layout));
}

window.backlogPaneResizer = {
    initialize(owner) {
        backlogPaneOwner = owner;
        backlogReportPaneBounds();
    },
    dispose() {
        backlogPaneOwner = null;
    }
};

window.addEventListener('resize', backlogReportPaneBounds);

document.addEventListener('pointerdown', (event) => {
    if (event.button !== 0) return;

    const handle = event.target instanceof Element ? event.target.closest('[data-pane-resizer]') : null;
    if (!handle) return;

    const layout = handle.closest('[data-testid="knowledge-layout"]');
    if (!layout) return;

    event.preventDefault();
    handle.focus();
    document.body.classList.add('is-resizing-pane');

    let width = backlogPaneWidthAt(layout, event.clientX);

    const onMove = (move) => {
        width = backlogPaneWidthAt(layout, move.clientX);
        layout.style.setProperty('--knowledge-panel-width', `${width}rem`);
        handle.setAttribute('aria-valuenow', String(width));
    };

    const onUp = () => {
        document.removeEventListener('pointermove', onMove);
        document.removeEventListener('pointerup', onUp);
        document.removeEventListener('pointercancel', onUp);
        document.body.classList.remove('is-resizing-pane');
        backlogPaneOwner?.invokeMethodAsync('SetSidePaneWidthAsync', width);
    };

    document.addEventListener('pointermove', onMove);
    document.addEventListener('pointerup', onUp);
    document.addEventListener('pointercancel', onUp);
});

const backlogDiagramInstances = new Map();
const backlogDiagramLibrarySources = {
    mermaid: [
        '/vendor/mermaid/mermaid.esm.min.mjs',
        'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs'
    ],
    g6: [
        '/vendor/g6/g6.min.js',
        'https://unpkg.com/@antv/g6@5/dist/g6.min.js'
    ]
};

let backlogMermaidPromise;
let backlogG6Promise;

function backlogEscapeHtml(value) {
    return String(value ?? '')
        .replaceAll('&', '&amp;')
        .replaceAll('<', '&lt;')
        .replaceAll('>', '&gt;')
        .replaceAll('"', '&quot;')
        .replaceAll("'", '&#39;');
}

async function backlogLoadScript(url) {
    if (document.querySelector(`script[data-backlog-diagram-src="${url}"]`)) return;

    await new Promise((resolve, reject) => {
        const script = document.createElement('script');
        script.src = url;
        script.async = true;
        script.dataset.backlogDiagramSrc = url;
        script.onload = resolve;
        script.onerror = () => reject(new Error(`Could not load ${url}`));
        document.head.appendChild(script);
    });
}

async function backlogLoadMermaid() {
    if (window.mermaid) return window.mermaid;
    if (!backlogMermaidPromise) {
        backlogMermaidPromise = (async () => {
            for (const source of backlogDiagramLibrarySources.mermaid) {
                try {
                    const module = await import(source);
                    const mermaid = module.default ?? module.mermaid ?? window.mermaid;
                    if (mermaid) {
                        mermaid.initialize({
                            startOnLoad: false,
                            theme: 'dark',
                            securityLevel: 'strict',
                            deterministicIds: true
                        });
                        return mermaid;
                    }
                } catch {
                    // Try the next source; the UI shows source fallback if every source fails.
                }
            }

            throw new Error('Mermaid renderer unavailable.');
        })();
    }

    return backlogMermaidPromise;
}

async function backlogLoadG6() {
    if (window.G6?.Graph) return window.G6;
    if (!backlogG6Promise) {
        backlogG6Promise = (async () => {
            for (const source of backlogDiagramLibrarySources.g6) {
                try {
                    await backlogLoadScript(source);
                    if (window.G6?.Graph) return window.G6;
                } catch {
                    // Try the next source; SVG fallback keeps the graph usable offline.
                }
            }

            throw new Error('AntV G6 renderer unavailable.');
        })();
    }

    return backlogG6Promise;
}

function backlogRenderDiagramError(element, message) {
    element.innerHTML = `<div class="diagram-view__fallback" role="note">${backlogEscapeHtml(message)} Source is available below.</div>`;
}

function backlogStatusColor(status) {
    const normalized = String(status ?? '').toLowerCase();
    if (normalized === 'accepted' || normalized === 'active' || normalized === 'adopted') return '#22c55e';
    if (normalized === 'trial') return '#38bdf8';
    if (normalized === 'hold') return '#f59e0b';
    if (normalized === 'retired' || normalized === 'deprecated') return '#ef4444';
    if (normalized === 'proposed' || normalized === 'draft' || normalized === 'candidate') return '#f59e0b';
    return '#38bdf8';
}

function backlogGroupTechnologyLayers(nodes) {
    const layers = [];
    const byLayer = new Map();
    for (const node of nodes) {
        const layer = String(node.layer || 'Unassigned');
        if (!byLayer.has(layer)) {
            const group = { layer, nodes: [] };
            byLayer.set(layer, group);
            layers.push(group);
        }

        byLayer.get(layer).nodes.push(node);
    }

    return layers;
}

function backlogCreateRoadmapElement(tagName, className, text) {
    const element = document.createElement(tagName);
    if (className) element.className = className;
    if (text !== undefined) element.textContent = text;
    return element;
}

function backlogRenderTechnologyRoadmap(element, graph, id) {
    const nodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
    const edges = Array.isArray(graph?.edges) ? graph.edges : [];
    if (nodes.length === 0) {
        element.innerHTML = '<p class="tech-graph__status" role="status">No technology graph nodes are available.</p>';
        return;
    }

    const existing = backlogDiagramInstances.get(id);
    existing?.destroy?.();
    element.replaceChildren();

    const incoming = new Map();
    const outgoing = new Map();
    for (const node of nodes) {
        incoming.set(node.id, []);
        outgoing.set(node.id, []);
    }

    for (const edge of edges) {
        if (!incoming.has(edge.source) || !outgoing.has(edge.target)) continue;
        incoming.get(edge.source).push(edge.target);
        outgoing.get(edge.target).push(edge.source);
    }

    const roadmap = backlogCreateRoadmapElement('div', 'tech-roadmap');
    const legend = backlogCreateRoadmapElement('div', 'tech-roadmap__legend');
    for (const item of ['candidate', 'trial', 'adopted', 'hold', 'retired']) {
        const swatch = backlogCreateRoadmapElement('span', `tech-roadmap__legend-item tech-roadmap__legend-item--${item}`);
        swatch.textContent = item;
        legend.appendChild(swatch);
    }

    const hint = backlogCreateRoadmapElement('p', 'tech-roadmap__hint', 'Select a card to spotlight its direct dependencies and dependents.');
    const viewport = backlogCreateRoadmapElement('div', 'tech-roadmap__viewport');
    const canvas = backlogCreateRoadmapElement('div', 'tech-roadmap__canvas');
    const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'tech-roadmap__edges');
    svg.setAttribute('aria-hidden', 'true');
    const defs = document.createElementNS('http://www.w3.org/2000/svg', 'defs');
    const marker = document.createElementNS('http://www.w3.org/2000/svg', 'marker');
    marker.setAttribute('id', `${id}-arrow`);
    marker.setAttribute('viewBox', '0 0 10 10');
    marker.setAttribute('refX', '9');
    marker.setAttribute('refY', '5');
    marker.setAttribute('markerWidth', '7');
    marker.setAttribute('markerHeight', '7');
    marker.setAttribute('orient', 'auto-start-reverse');
    const markerPath = document.createElementNS('http://www.w3.org/2000/svg', 'path');
    markerPath.setAttribute('d', 'M 0 0 L 10 5 L 0 10 z');
    marker.appendChild(markerPath);
    defs.appendChild(marker);
    svg.appendChild(defs);
    canvas.appendChild(svg);

    const cardById = new Map();
    for (const group of backlogGroupTechnologyLayers(nodes)) {
        const lane = backlogCreateRoadmapElement('section', 'tech-roadmap__lane');
        lane.setAttribute('aria-label', group.layer);
        const header = backlogCreateRoadmapElement('header', 'tech-roadmap__lane-header');
        header.appendChild(backlogCreateRoadmapElement('h4', null, group.layer));
        header.appendChild(backlogCreateRoadmapElement('span', 'tech-roadmap__lane-count', String(group.nodes.length)));
        lane.appendChild(header);

        for (const node of group.nodes) {
            const dependencies = incoming.get(node.id) ?? [];
            const dependents = outgoing.get(node.id) ?? [];
            const card = backlogCreateRoadmapElement('button', `tech-roadmap__card tech-roadmap__card--${String(node.status || 'unknown').toLowerCase()}`);
            card.type = 'button';
            card.dataset.nodeId = node.id;
            card.style.setProperty('--tech-roadmap-status-color', backlogStatusColor(node.status));
            card.title = node.description || node.label;

            const title = backlogCreateRoadmapElement('span', 'tech-roadmap__card-title', node.label);
            const meta = backlogCreateRoadmapElement('span', 'tech-roadmap__card-meta');
            meta.appendChild(backlogCreateRoadmapElement('span', 'tech-roadmap__pill', node.kind || 'technology'));
            meta.appendChild(backlogCreateRoadmapElement('span', 'tech-roadmap__pill tech-roadmap__pill--status', node.status || 'unknown'));
            const summary = backlogCreateRoadmapElement('span', 'tech-roadmap__card-summary', node.description || 'No summary yet.');
            const relation = backlogCreateRoadmapElement('span', 'tech-roadmap__card-relations', `${dependencies.length} dependencies / ${dependents.length} dependents`);
            card.append(title, meta, summary, relation);
            lane.appendChild(card);
            cardById.set(node.id, card);
        }

        canvas.appendChild(lane);
    }

    viewport.appendChild(canvas);
    roadmap.append(legend, hint, viewport);
    element.appendChild(roadmap);

    let selectedId = null;
    const relatedIds = () => new Set([
        selectedId,
        ...(incoming.get(selectedId) ?? []),
        ...(outgoing.get(selectedId) ?? [])
    ].filter(Boolean));

    const drawEdges = () => {
        const width = Math.max(canvas.scrollWidth, viewport.clientWidth);
        const height = Math.max(canvas.scrollHeight, viewport.clientHeight);
        svg.setAttribute('width', String(width));
        svg.setAttribute('height', String(height));
        svg.setAttribute('viewBox', `0 0 ${width} ${height}`);
        for (const edgePath of [...svg.querySelectorAll('path.tech-roadmap__edge')]) edgePath.remove();

        const highlighted = selectedId ? relatedIds() : null;
        for (const edge of edges) {
            const source = cardById.get(edge.source);
            const target = cardById.get(edge.target);
            if (!source || !target) continue;

            const sourceRect = source.getBoundingClientRect();
            const targetRect = target.getBoundingClientRect();
            const canvasRect = canvas.getBoundingClientRect();
            const from = {
                x: targetRect.right - canvasRect.left,
                y: targetRect.top - canvasRect.top + targetRect.height / 2
            };
            const to = {
                x: sourceRect.left - canvasRect.left,
                y: sourceRect.top - canvasRect.top + sourceRect.height / 2
            };
            const direction = Math.max(80, Math.abs(to.x - from.x) * 0.45);
            const path = document.createElementNS('http://www.w3.org/2000/svg', 'path');
            path.setAttribute('class', 'tech-roadmap__edge');
            path.setAttribute('d', `M ${from.x} ${from.y} C ${from.x + direction} ${from.y}, ${to.x - direction} ${to.y}, ${to.x} ${to.y}`);
            path.setAttribute('marker-end', `url(#${id}-arrow)`);
            if (highlighted) {
                path.classList.toggle('tech-roadmap__edge--active', highlighted.has(edge.source) && highlighted.has(edge.target));
                path.classList.toggle('tech-roadmap__edge--muted', !(highlighted.has(edge.source) && highlighted.has(edge.target)));
            }
            svg.appendChild(path);
        }
    };

    const applySelection = (nodeId) => {
        selectedId = selectedId === nodeId ? null : nodeId;
        const highlighted = selectedId ? relatedIds() : null;
        for (const [cardId, card] of cardById) {
            card.classList.toggle('tech-roadmap__card--selected', cardId === selectedId);
            card.classList.toggle('tech-roadmap__card--muted', Boolean(highlighted) && !highlighted.has(cardId));
            card.setAttribute('aria-pressed', cardId === selectedId ? 'true' : 'false');
        }
        drawEdges();
    };

    const listeners = [];
    for (const [nodeId, card] of cardById) {
        const onClick = () => applySelection(nodeId);
        card.addEventListener('click', onClick);
        listeners.push(() => card.removeEventListener('click', onClick));
    }

    const onViewportChange = () => window.requestAnimationFrame(drawEdges);
    viewport.addEventListener('scroll', onViewportChange, { passive: true });
    window.addEventListener('resize', onViewportChange);
    listeners.push(() => viewport.removeEventListener('scroll', onViewportChange));
    listeners.push(() => window.removeEventListener('resize', onViewportChange));

    let resizeObserver;
    if (window.ResizeObserver) {
        resizeObserver = new ResizeObserver(onViewportChange);
        resizeObserver.observe(canvas);
        listeners.push(() => resizeObserver.disconnect());
    }

    backlogDiagramInstances.set(id, {
        destroy() {
            for (const remove of listeners) remove();
        }
    });

    window.requestAnimationFrame(drawEdges);
}
window.backlogDiagrams = {
    async render(element, id, language, source) {
        const normalized = String(language ?? '').trim().toLowerCase();
        if (normalized !== 'mermaid' && normalized !== 'mmd') {
            backlogRenderDiagramError(element, `${language ?? 'Diagram'} rendering is not configured yet.`);
            return;
        }

        element.innerHTML = '<p class="diagram-view__status" role="status">Rendering Mermaid diagram...</p>';

        try {
            const mermaid = await backlogLoadMermaid();
            const result = await mermaid.render(`${id}-svg`, source);
            element.innerHTML = result.svg;
            result.bindFunctions?.(element);
        } catch (error) {
            backlogRenderDiagramError(element, error instanceof Error ? error.message : 'Mermaid rendering failed.');
        }
    },

    async renderTechnologyGraph(element, id, graph) {
        element.innerHTML = '<p class="tech-graph__status" role="status">Rendering embedded technology roadmap...</p>';
        backlogRenderTechnologyRoadmap(element, graph, id);
    },

    dispose(id) {
        const instance = backlogDiagramInstances.get(id);
        instance?.destroy?.();
        backlogDiagramInstances.delete(id);
    }
};

window.backlogCaptureScreenshot = async () => {
    if (!navigator.mediaDevices?.getDisplayMedia) {
        throw new Error('Screenshot capture is not available in this WebView.');
    }

    const stream = await navigator.mediaDevices.getDisplayMedia({ video: true, audio: false });
    const video = document.createElement('video');

    try {
        video.srcObject = stream;
        video.muted = true;
        await video.play();

        await new Promise((resolve) => {
            if (video.videoWidth > 0 && video.videoHeight > 0) {
                resolve();
                return;
            }

            video.onloadedmetadata = resolve;
        });

        const maxSide = 900;
        const scale = Math.min(1, maxSide / Math.max(video.videoWidth, video.videoHeight));
        let width = Math.max(1, Math.round(video.videoWidth * scale));
        let height = Math.max(1, Math.round(video.videoHeight * scale));
        const canvas = document.createElement('canvas');
        const context = canvas.getContext('2d');
        const mediaType = 'image/jpeg';
        let quality = 0.72;
        let dataUrl;

        do {
            canvas.width = width;
            canvas.height = height;
            context.drawImage(video, 0, 0, width, height);
            dataUrl = canvas.toDataURL(mediaType, quality);

            if (dataUrl.length <= 56000) break;
            if (quality > 0.35) {
                quality -= 0.1;
            } else {
                width = Math.max(320, Math.round(width * 0.8));
                height = Math.max(240, Math.round(height * 0.8));
            }
        } while (dataUrl.length > 56000 && (width > 320 || height > 240));

        const base64Length = dataUrl.slice(dataUrl.indexOf(',') + 1).length;

        return {
            dataUrl,
            mediaType,
            width,
            height,
            sizeBytes: Math.ceil(base64Length * 3 / 4)
        };
    } finally {
        for (const track of stream.getTracks()) {
            track.stop();
        }
        video.srcObject = null;
    }
};
