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

// Keyboard reordering has to carry the focus ring with the thing it moved;
// after the list re-renders the element is a different node, so the caller
// names it by id.
window.backlogFocus = (id) => {
    const element = document.getElementById(id);
    if (element) element.focus();
};

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
    if (normalized === 'accepted' || normalized === 'active') return '#22c55e';
    if (normalized === 'proposed' || normalized === 'draft') return '#f59e0b';
    if (normalized === 'deprecated') return '#ef4444';
    return '#38bdf8';
}

function backlogRenderGraphFallback(element, graph) {
    const nodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
    const edges = Array.isArray(graph?.edges) ? graph.edges : [];
    if (nodes.length === 0) {
        element.innerHTML = '<p class="tech-graph__status" role="status">No technology graph nodes are available.</p>';
        return;
    }

    const width = Math.max(720, nodes.length * 120);
    const height = Math.max(360, Math.ceil(nodes.length / 6) * 140);
    const radius = Math.min(width, height) * 0.34;
    const centerX = width / 2;
    const centerY = height / 2;
    const positions = new Map(nodes.map((node, index) => {
        const angle = (Math.PI * 2 * index) / nodes.length - Math.PI / 2;
        return [node.id, {
            x: centerX + Math.cos(angle) * radius,
            y: centerY + Math.sin(angle) * radius
        }];
    }));

    const edgeMarkup = edges.map(edge => {
        const source = positions.get(edge.source);
        const target = positions.get(edge.target);
        if (!source || !target) return '';
        return `<line class="tech-graph__edge" x1="${source.x}" y1="${source.y}" x2="${target.x}" y2="${target.y}" />`;
    }).join('');

    const nodeMarkup = nodes.map(node => {
        const position = positions.get(node.id);
        const label = backlogEscapeHtml(node.label);
        const layer = backlogEscapeHtml(node.layer);
        const color = backlogStatusColor(node.status);
        return `<g class="tech-graph__node" transform="translate(${position.x} ${position.y})">
            <circle r="34" fill="${color}" />
            <text y="-4" text-anchor="middle">${label}</text>
            <text y="14" text-anchor="middle" class="tech-graph__node-layer">${layer}</text>
        </g>`;
    }).join('');

    element.innerHTML = `<svg class="tech-graph__fallback-svg" viewBox="0 0 ${width} ${height}" role="img" aria-label="Technology dependency graph">${edgeMarkup}${nodeMarkup}</svg>`;
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
        element.innerHTML = '<p class="tech-graph__status" role="status">Rendering AntV G6 technology graph...</p>';

        try {
            const g6 = await backlogLoadG6();
            const existing = backlogDiagramInstances.get(id);
            existing?.destroy?.();

            const data = {
                nodes: (graph?.nodes ?? []).map(node => ({
                    id: node.id,
                    data: node,
                    style: {
                        labelText: node.label,
                        fill: backlogStatusColor(node.status),
                        stroke: '#e2e8f0'
                    }
                })),
                edges: (graph?.edges ?? []).map(edge => ({
                    id: edge.id,
                    source: edge.source,
                    target: edge.target,
                    data: edge,
                    style: {
                        labelText: edge.label,
                        stroke: '#64748b',
                        endArrow: true
                    }
                }))
            };

            const instance = new g6.Graph({
                container: element,
                data,
                autoFit: 'view',
                layout: { type: 'force', preventOverlap: true, linkDistance: 160 },
                behaviors: ['drag-canvas', 'zoom-canvas', 'drag-element'],
                node: { type: 'circle', style: { size: 48, labelFill: '#f8fafc', labelPlacement: 'bottom' } },
                edge: { type: 'line', style: { labelFill: '#cbd5e1', labelBackground: true } }
            });

            backlogDiagramInstances.set(id, instance);
            await instance.render();
        } catch {
            backlogRenderGraphFallback(element, graph);
        }
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
