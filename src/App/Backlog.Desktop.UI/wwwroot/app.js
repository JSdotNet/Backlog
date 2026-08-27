// The three drag listeners that used to be here moved into the component
// library's components.js, along with the drag itself. They existed for the
// backlog pane's hand-rolled grab rails and drop zones, keyed on
// `data-drag-grip` and `data-drop-zone`; the pane's entries and steps are
// TaskListView now, so the payload Chromium insists on belongs beside the
// component that starts the drag rather than in whichever app happens to render
// it.

// The technology atlas itself lives in the component library, as
// `backlogGraphAtlas` in components.js: the projection, the clustering, the
// camera and the picking are drawing, not knowledge about technologies.
//
// What is left here is the only part that is about technologies: which statuses
// this project's ladder has, and which of them mean the same thing. That is
// turned into the atlas's model below and handed over.
//
// No colours. They used to be here, four raw hex values that matched nothing in
// the palette and that `DesignTokenTests` could not see because they were in
// JavaScript. A node's colour now comes from its tone — which C# reads off the
// same `KnowledgeStatus` vocabulary the badges use — and the renderer resolves a
// tone to a token. Neither this file nor that one holds a colour any more.

// `.tech` writes one word and `.arc42` sometimes writes another for the same
// state. Normalising once, here, is what keeps the renderer from having to know
// that "accepted" and "adopted" are the same thing.
function backlogNormalizeTechnologyStatus(status) {
    const normalized = String(status ?? 'unknown').trim().toLowerCase();
    if (normalized === 'accepted' || normalized === 'active') return 'adopted';
    if (normalized === 'proposed' || normalized === 'draft') return 'candidate';
    if (normalized === 'deprecated') return 'retired';
    return normalized || 'unknown';
}

// The graph as `.tech` describes it, in the shape the atlas draws. Everything
// here is a rename or a pass-through: the degrees, the ordinals and the tone are
// computed in C#, where they can be tested against the Markdown they came from.
function backlogTechnologyAtlasModel(graph) {
    const nodes = Array.isArray(graph?.nodes) ? graph.nodes : [];
    const edges = Array.isArray(graph?.edges) ? graph.edges : [];

    return {
        nodes: nodes.map((node) => ({
            id: node.id,
            label: node.label,
            kind: node.kind || 'technology',
            status: backlogNormalizeTechnologyStatus(node.status),
            toneSlug: node.toneSlug || '',
            group: node.layer || 'Unassigned',
            groupIndex: typeof node.layerIndex === 'number' ? node.layerIndex : 0,
            ordinal: typeof node.ordinalInLayer === 'number' ? node.ordinalInLayer : 0,
            inDegree: node.inDegree || 0,
            outDegree: node.outDegree || 0,
            isFoundation: node.isFoundation === true,
            isBoundary: node.isBoundary === true
        })),
        edges: edges.map((edge) => ({ source: edge.source, target: edge.target })),
        emptyMessage: 'No technology graph nodes are available.'
    };
}

// components.js owns the renderers. The technology atlas is this app's own
// reading of the data, so it is attached to the shared object rather than
// replacing it. The guard means app.js still parses if it is ever loaded alone.
window.backlogDiagrams = window.backlogDiagrams || {};

window.backlogDiagrams.renderTechnologyAtlas = async (element, id, graph, dotnet) => {
    window.backlogGraphAtlas.render(element, id, backlogTechnologyAtlasModel(graph), dotnet);
};

// The knowledge atlas hands over a model that is already in the renderer's shape:
// the folders' own graphs carry groups, degrees and tones, and C# reads them. So
// there is nothing for this app to translate — it passes the model through and is
// here only so the knowledge atlas has a renderer name of its own, which is what
// lets the two atlases be told apart in a test and in a trace.
window.backlogDiagrams.renderKnowledgeAtlas = async (element, id, graph, dotnet) => {
    window.backlogGraphAtlas.render(element, id, graph, dotnet);
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
