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

// The technology visualizer itself lives in the component library, as
// `backlogGraphExplorer` in components.js: tabs, zoom, panning, cards and the
// three layouts are drawing, not knowledge about technologies.
//
// What is left here is the only part that is about technologies: which lanes
// exist, what a status means, and which cluster a technology belongs to. That
// is turned into the explorer's model below and handed over.

const BACKLOG_TECHNOLOGY_STATUS_COLORS = {
    adopted: '#22c55e',
    accepted: '#22c55e',
    active: '#22c55e',
    trial: '#38bdf8',
    hold: '#f59e0b',
    candidate: '#f59e0b',
    proposed: '#f59e0b',
    draft: '#f59e0b',
    retired: '#ef4444',
    deprecated: '#ef4444'
};

// Lane order is the lifecycle order, and every lane is drawn even when empty:
// an empty "Trial" column is information about the portfolio.
const BACKLOG_TECHNOLOGY_STATUS_DEFINITIONS = [
    { key: 'candidate', label: 'Candidate' },
    { key: 'trial', label: 'Trial' },
    { key: 'adopted', label: 'Adopted' },
    { key: 'hold', label: 'Hold' },
    { key: 'retired', label: 'Retired' }
];

const BACKLOG_TECHNOLOGY_CLOUD_ORDER = [
    'Cloud platform',
    'Delivery',
    'Observability',
    'Data and storage',
    'Application surfaces',
    'Shared foundations'
];

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
    const groups = BACKLOG_TECHNOLOGY_STATUS_DEFINITIONS.map((status) => ({ ...status, nodes: [] }));
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

function backlogTechnologyCloudGroups(nodes) {
    const groups = backlogGroupTechnologyBy(nodes, backlogTechnologyCloudCluster);
    groups.sort((left, right) => BACKLOG_TECHNOLOGY_CLOUD_ORDER.indexOf(left.key) - BACKLOG_TECHNOLOGY_CLOUD_ORDER.indexOf(right.key));
    return groups;
}

function backlogTechnologyExplorerGroups(groups) {
    return groups.map((group) => ({
        key: group.key,
        label: group.label,
        nodeIds: group.nodes.map((node) => node.id)
    }));
}

// The graph as .tech metadata describes it, translated into the shape the
// explorer draws: statuses normalized once here, so the library never has to
// know that "accepted" and "adopted" are the same thing.
function backlogTechnologyExplorerModel(graph) {
    const nodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
    const edges = Array.isArray(graph?.edges) ? graph.edges : [];

    return {
        nodes: nodes.map((node) => ({
            id: node.id,
            label: node.label,
            kind: node.kind || 'technology',
            status: backlogNormalizeTechnologyStatus(node.status),
            description: node.description
        })),
        edges: edges.map((edge) => ({ source: edge.source, target: edge.target })),
        views: [
            {
                id: 'board',
                label: 'Board',
                hint: 'Lifecycle board: lanes are technology states from candidate to retired.',
                layout: 'lanes',
                groups: backlogTechnologyExplorerGroups(backlogTechnologyStatusGroups(nodes))
            },
            {
                id: 'roadmap',
                label: 'Roadmap',
                hint: 'Area spine: technology areas form the central vertical line, with technologies branching around each area.',
                layout: 'spine',
                groups: backlogTechnologyExplorerGroups(backlogGroupTechnologyLayers(nodes))
            },
            {
                id: 'cloud',
                label: 'Cloud',
                hint: 'Cloud view: clusters technologies by platform, delivery, observability, data, and application surface concerns.',
                layout: 'cluster',
                ariaLabel: 'Clustered technology cloud map with dependency links. Drag the map to pan, or focus it and use arrow keys.',
                groups: backlogTechnologyExplorerGroups(backlogTechnologyCloudGroups(nodes))
            }
        ],
        defaultViewId: 'roadmap',
        legend: [
            { key: 'candidate', label: 'candidate', color: '#f59e0b' },
            { key: 'trial', label: 'trial', color: '#38bdf8' },
            { key: 'adopted', label: 'adopted', color: '#22c55e' },
            { key: 'hold', label: 'hold', color: '#f59e0b' },
            { key: 'retired', label: 'retired', color: '#ef4444' }
        ],
        statusColors: BACKLOG_TECHNOLOGY_STATUS_COLORS,
        defaultStatusColor: '#38bdf8',
        itemNoun: 'technologies',
        emptyMessage: 'No technology graph nodes are available.',
        viewsLabel: 'Technology visualizer views',
        zoomLabel: 'Zoom technology visualizer',
        selectionHint: 'Select a card to spotlight direct dependencies and dependents.'
    };
}

// components.js owns the diagram host. The technology graph is this app's own
// reading of the data, so it is attached to the shared object rather than
// replacing it. The guard means app.js still parses if it is ever loaded alone.
window.backlogDiagrams = window.backlogDiagrams || {};

window.backlogDiagrams.renderTechnologyGraph = async (element, id, graph) => {
    window.backlogGraphExplorer.render(element, id, backlogTechnologyExplorerModel(graph));
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
