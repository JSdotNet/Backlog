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

function backlogGroupTechnologyBy(nodes, keySelector, labelSelector) {
    const groups = [];
    const byKey = new Map();
    for (const node of nodes) {
        const key = keySelector(node);
        if (!byKey.has(key)) {
            const group = { key, label: labelSelector?.(node, key) ?? key, nodes: [] };
            byKey.set(key, group);
            groups.push(group);
        }

        byKey.get(key).nodes.push(node);
    }

    return groups;
}

function backlogGroupTechnologyLayers(nodes) {
    return backlogGroupTechnologyBy(nodes, (node) => String(node.layer || 'Unassigned'));
}

function backlogNormalizeTechnologyStatus(status) {
    const normalized = String(status ?? 'unknown').trim().toLowerCase();
    if (normalized === 'accepted' || normalized === 'active') return 'adopted';
    if (normalized === 'proposed' || normalized === 'draft') return 'candidate';
    if (normalized === 'deprecated') return 'retired';
    return normalized || 'unknown';
}

function backlogTechnologyStatusGroups(nodes) {
    const statusDefinitions = [
        { key: 'candidate', label: 'Candidate' },
        { key: 'trial', label: 'Trial' },
        { key: 'adopted', label: 'Adopted' },
        { key: 'hold', label: 'Hold' },
        { key: 'retired', label: 'Retired' }
    ];
    const groups = statusDefinitions.map((status) => ({ ...status, nodes: [] }));
    const byKey = new Map(groups.map((group) => [group.key, group]));

    for (const node of nodes) {
        const key = backlogNormalizeTechnologyStatus(node.status);
        if (!byKey.has(key)) {
            const label = key ? key.replace(/(^|[-_\s])\w/g, (match) => match.toUpperCase()) : 'Unknown';
            const group = { key, label, nodes: [] };
            byKey.set(key, group);
            groups.push(group);
        }

        byKey.get(key).nodes.push(node);
    }

    return groups;
}

function backlogTechnologyCloudCluster(node) {
    const haystack = `${node.label ?? ''} ${node.layer ?? ''} ${node.kind ?? ''} ${node.description ?? ''}`.toLowerCase();
    if (/azure|aws|cloud|container|docker|kubernetes|hosting|foundry|service bus|cosmos|storage/.test(haystack)) return 'Cloud platform';
    if (/github|actions|repository|source|git|devops|deployment|ci|cd/.test(haystack)) return 'Delivery';
    if (/telemetry|monitor|observability|logging|otel|application insights|metrics|traces/.test(haystack)) return 'Observability';
    if (/database|data|sqlite|sql|markdown|file|storage/.test(haystack)) return 'Data and storage';
    if (/desktop|maui|blazor|razor|webview|mobile|ide|ui|app/.test(haystack)) return 'Application surfaces';
    return 'Shared foundations';
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

    const visualizer = backlogCreateRoadmapElement('div', 'tech-roadmap');
    const toolbar = backlogCreateRoadmapElement('div', 'tech-roadmap__toolbar');
    const tabs = backlogCreateRoadmapElement('div', 'tech-roadmap__tabs');
    tabs.setAttribute('role', 'tablist');
    tabs.setAttribute('aria-label', 'Technology visualizer views');
    const legend = backlogCreateRoadmapElement('div', 'tech-roadmap__legend');
    for (const item of ['candidate', 'trial', 'adopted', 'hold', 'retired']) {
        const swatch = backlogCreateRoadmapElement('span', `tech-roadmap__legend-item tech-roadmap__legend-item--${item}`);
        swatch.textContent = item;
        legend.appendChild(swatch);
    }

    const viewDefinitions = [
        { id: 'board', label: 'Board', hint: 'Lifecycle board: lanes are technology states from candidate to retired.' },
        { id: 'roadmap', label: 'Roadmap', hint: 'Area spine: technology areas form the central vertical line, with technologies branching around each area.' },
        { id: 'cloud', label: 'Cloud', hint: 'Cloud view: clusters technologies by platform, delivery, observability, data, and application surface concerns.' }
    ];
    let activeView = 'roadmap';
    let selectedId = null;

    const hint = backlogCreateRoadmapElement('p', 'tech-roadmap__hint');
    const viewport = backlogCreateRoadmapElement('div', 'tech-roadmap__viewport');
    const content = backlogCreateRoadmapElement('div', 'tech-roadmap__content');
    viewport.appendChild(content);
    toolbar.append(tabs, legend);
    visualizer.append(toolbar, hint, viewport);
    element.appendChild(visualizer);

    const listeners = [];
    const cardById = new Map();
    const relatedIds = () => new Set([
        selectedId,
        ...(incoming.get(selectedId) ?? []),
        ...(outgoing.get(selectedId) ?? [])
    ].filter(Boolean));

    const applySelectionState = () => {
        const highlighted = selectedId ? relatedIds() : null;
        for (const [cardId, card] of cardById) {
            card.classList.toggle('tech-roadmap__card--selected', cardId === selectedId);
            card.classList.toggle('tech-roadmap__card--muted', Boolean(highlighted) && !highlighted.has(cardId));
            card.setAttribute('aria-pressed', cardId === selectedId ? 'true' : 'false');
        }
    };

    const applySelection = (nodeId) => {
        selectedId = selectedId === nodeId ? null : nodeId;
        applySelectionState();
    };

    const makeCard = (node, density = 'normal') => {
        const dependencies = incoming.get(node.id) ?? [];
        const dependents = outgoing.get(node.id) ?? [];
        const status = backlogNormalizeTechnologyStatus(node.status);
        const card = backlogCreateRoadmapElement('button', `tech-roadmap__card tech-roadmap__card--${status} tech-roadmap__card--${density}`);
        card.type = 'button';
        card.dataset.nodeId = node.id;
        card.style.setProperty('--tech-roadmap-status-color', backlogStatusColor(status));
        card.title = node.description || node.label;
        card.setAttribute('aria-pressed', node.id === selectedId ? 'true' : 'false');

        const title = backlogCreateRoadmapElement('span', 'tech-roadmap__card-title', node.label);
        const meta = backlogCreateRoadmapElement('span', 'tech-roadmap__card-meta');
        meta.appendChild(backlogCreateRoadmapElement('span', 'tech-roadmap__pill', node.kind || 'technology'));
        meta.appendChild(backlogCreateRoadmapElement('span', 'tech-roadmap__pill tech-roadmap__pill--status', status));
        const summary = backlogCreateRoadmapElement('span', 'tech-roadmap__card-summary', node.description || 'No summary yet.');
        const relation = backlogCreateRoadmapElement('span', 'tech-roadmap__card-relations', `${dependencies.length} dependencies / ${dependents.length} dependents`);
        card.append(title, meta, summary, relation);

        const onClick = () => applySelection(node.id);
        card.addEventListener('click', onClick);
        listeners.push(() => card.removeEventListener('click', onClick));
        cardById.set(node.id, card);
        return card;
    };

    const makeGroupHeader = (title, count) => {
        const header = backlogCreateRoadmapElement('header', 'tech-roadmap__lane-header');
        header.appendChild(backlogCreateRoadmapElement('h4', null, title));
        header.appendChild(backlogCreateRoadmapElement('span', 'tech-roadmap__lane-count', String(count)));
        return header;
    };

    const renderBoardView = () => {
        const canvas = backlogCreateRoadmapElement('div', 'tech-roadmap__canvas tech-roadmap__canvas--board');
        for (const group of backlogTechnologyStatusGroups(nodes)) {
            const lane = backlogCreateRoadmapElement('section', 'tech-roadmap__lane');
            lane.setAttribute('aria-label', `${group.label} technologies`);
            lane.appendChild(makeGroupHeader(group.label, group.nodes.length));
            for (const node of group.nodes) lane.appendChild(makeCard(node));
            canvas.appendChild(lane);
        }

        return canvas;
    };

    const renderRoadmapView = () => {
        const spine = backlogCreateRoadmapElement('div', 'tech-roadmap__spine');
        const groups = backlogGroupTechnologyLayers(nodes);
        groups.forEach((group, index) => {
            const section = backlogCreateRoadmapElement('section', 'tech-roadmap__spine-section');
            section.setAttribute('aria-label', group.label);
            const left = backlogCreateRoadmapElement('div', 'tech-roadmap__branch tech-roadmap__branch--left');
            const center = backlogCreateRoadmapElement('div', 'tech-roadmap__area-node');
            center.style.setProperty('--tech-roadmap-status-color', backlogStatusColor(group.nodes[0]?.status));
            center.appendChild(backlogCreateRoadmapElement('span', 'tech-roadmap__area-index', String(index + 1)));
            center.appendChild(backlogCreateRoadmapElement('strong', null, group.label));
            center.appendChild(backlogCreateRoadmapElement('span', null, `${group.nodes.length} technologies`));
            const right = backlogCreateRoadmapElement('div', 'tech-roadmap__branch tech-roadmap__branch--right');

            group.nodes.forEach((node, nodeIndex) => {
                const card = makeCard(node, 'compact');
                const branch = nodeIndex % 2 === 0 ? left : right;
                branch.appendChild(card);
            });

            section.append(left, center, right);
            spine.appendChild(section);
        });

        return spine;
    };

    const renderCloudView = () => {
        const cloud = backlogCreateRoadmapElement('div', 'tech-roadmap__cloud');
        const groups = backlogGroupTechnologyBy(nodes, backlogTechnologyCloudCluster);
        const order = ['Cloud platform', 'Delivery', 'Observability', 'Data and storage', 'Application surfaces', 'Shared foundations'];
        groups.sort((left, right) => order.indexOf(left.key) - order.indexOf(right.key));

        for (const group of groups) {
            const cluster = backlogCreateRoadmapElement('section', 'tech-roadmap__cloud-cluster');
            cluster.setAttribute('aria-label', group.label);
            cluster.appendChild(makeGroupHeader(group.label, group.nodes.length));
            const cards = backlogCreateRoadmapElement('div', 'tech-roadmap__cloud-cards');
            for (const node of group.nodes) cards.appendChild(makeCard(node, 'compact'));
            cluster.appendChild(cards);
            cloud.appendChild(cluster);
        }

        return cloud;
    };

    const removeCardListeners = () => {
        while (listeners.length > viewDefinitions.length) listeners.pop()?.();
    };

    const renderActiveView = () => {
        removeCardListeners();
        cardById.clear();
        content.replaceChildren();
        const definition = viewDefinitions.find((view) => view.id === activeView) ?? viewDefinitions[0];
        hint.textContent = `${definition.hint} Select a card to spotlight direct dependencies and dependents.`;
        content.id = `${id}-${definition.id}-view`;
        content.dataset.view = definition.id;
        content.appendChild(
            definition.id === 'board'
                ? renderBoardView()
                : definition.id === 'cloud'
                    ? renderCloudView()
                    : renderRoadmapView()
        );
        applySelectionState();
    };

    for (const view of viewDefinitions) {
        const tab = backlogCreateRoadmapElement('button', 'tech-roadmap__tab', view.label);
        tab.type = 'button';
        tab.dataset.view = view.id;
        tab.setAttribute('role', 'tab');
        tab.setAttribute('aria-controls', `${id}-${view.id}-view`);
        const onClick = () => {
            activeView = view.id;
            for (const button of tabs.querySelectorAll('.tech-roadmap__tab')) {
                const isSelected = button.dataset.view === activeView;
                button.classList.toggle('tech-roadmap__tab--active', isSelected);
                button.setAttribute('aria-selected', isSelected ? 'true' : 'false');
            }
            renderActiveView();
        };
        tab.addEventListener('click', onClick);
        listeners.push(() => tab.removeEventListener('click', onClick));
        tabs.appendChild(tab);
    }

    const selectedTab = tabs.querySelector(`[data-view="${activeView}"]`);
    selectedTab?.classList.add('tech-roadmap__tab--active');
    selectedTab?.setAttribute('aria-selected', 'true');
    for (const tab of tabs.querySelectorAll('.tech-roadmap__tab:not(.tech-roadmap__tab--active)')) tab.setAttribute('aria-selected', 'false');

    backlogDiagramInstances.set(id, {
        destroy() {
            for (const remove of listeners) remove();
        }
    });

    renderActiveView();
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
