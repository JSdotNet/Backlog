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


// An attached image is evidence for a bug report, not a photograph: it is
// committed to the repository and embedded in the issue, so it is held to a
// budget rather than sent at whatever size it arrived. Named because two
// readers meet it now — the screen capture below and the clipboard after it —
// and a second copy of the loop is how the two would stop agreeing.
const backlogScreenshotMaxSide = 900;
const backlogScreenshotMaxDataUrlLength = 56000;

// `toDataURL` takes a quality only for these two, and quality is what the
// budget is met with; anything else — the PNG the clipboard usually holds, a
// GIF, a BMP — has no quality to give up and would be downscaled to a thumbnail
// to fit instead. So it is re-encoded as the JPEG a capture already produces.
function backlogScreenshotMediaType(mediaType) {
    return mediaType === 'image/jpeg' || mediaType === 'image/webp' ? mediaType : 'image/jpeg';
}

// What the clipboard may hand over, in the order it is preferred. A whitelist
// rather than an `image/*` test because `createImageBitmap` cannot decode
// image/svg+xml: matching it would turn a copied SVG into a raw decode error
// instead of the friendly "no image" answer below. Preference order also
// settles the clipboard item that offers both an SVG and a PNG.
const backlogDecodableImageTypes = ['image/png', 'image/jpeg', 'image/webp', 'image/gif', 'image/bmp'];

// Anything `drawImage` accepts and its own size: a playing video for the
// capture, a decoded bitmap for the clipboard. Quality first, then dimensions,
// because a smaller picture of the whole screen says less than a softer one.
function backlogEncodeScreenshot(source, sourceWidth, sourceHeight, requestedMediaType) {
    const scale = Math.min(1, backlogScreenshotMaxSide / Math.max(sourceWidth, sourceHeight));
    let width = Math.max(1, Math.round(sourceWidth * scale));
    let height = Math.max(1, Math.round(sourceHeight * scale));
    const canvas = document.createElement('canvas');
    const context = canvas.getContext('2d');
    const mediaType = backlogScreenshotMediaType(requestedMediaType);
    let quality = 0.72;
    let dataUrl;

    do {
        canvas.width = width;
        canvas.height = height;

        // JPEG has no alpha channel, and a fresh canvas is transparent, so a
        // pasted PNG's transparent regions would encode as black. The capture
        // path never had an alpha channel to lose; the clipboard does.
        if (mediaType === 'image/jpeg') {
            context.fillStyle = '#ffffff';
            context.fillRect(0, 0, width, height);
        }

        context.drawImage(source, 0, 0, width, height);
        dataUrl = canvas.toDataURL(mediaType, quality);

        if (dataUrl.length <= backlogScreenshotMaxDataUrlLength) break;
        if (quality > 0.35) {
            quality -= 0.1;
        } else {
            width = Math.max(320, Math.round(width * 0.8));
            height = Math.max(240, Math.round(height * 0.8));
        }
    } while (dataUrl.length > backlogScreenshotMaxDataUrlLength && (width > 320 || height > 240));

    const base64Length = dataUrl.slice(dataUrl.indexOf(',') + 1).length;

    return {
        dataUrl,
        mediaType,
        width,
        height,
        sizeBytes: Math.ceil(base64Length * 3 / 4)
    };
}

window.backlogCaptureScreenshot = async () => {
    if (!navigator.mediaDevices?.getDisplayMedia) {
        throw new Error('This WebView does not offer screen capture.');
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

        return backlogEncodeScreenshot(video, video.videoWidth, video.videoHeight, 'image/jpeg');
    } finally {
        for (const track of stream.getTracks()) {
            track.stop();
        }
        video.srcObject = null;
    }
};

// The other way an image reaches a bug report: whatever was already copied.
// Same return shape as the capture above, and the same budget, so the dialog's
// preview, its retake and remove controls and the upload path do not know or
// care which of the two produced what they are holding.
//
// Nothing here is caught. A WebView that refuses the clipboard — no permission,
// no secure context — is an answer the reporter has to see, and swallowing it
// would leave the dialog saying an image was pasted while attaching nothing.
// The one thing that is not a failure is a clipboard with no image in it, and
// that is reported by returning null rather than by throwing: it is what a
// clipboard holding text does, which is most clipboards.
window.backlogReadClipboardImage = async () => {
    if (!navigator.clipboard?.read) {
        throw new Error('This WebView does not offer clipboard reading.');
    }

    const items = await navigator.clipboard.read();

    for (const item of items) {
        const mediaType = backlogDecodableImageTypes.find((type) => item.types.includes(type));
        if (!mediaType) continue;

        const bitmap = await createImageBitmap(await item.getType(mediaType));

        try {
            return backlogEncodeScreenshot(bitmap, bitmap.width, bitmap.height, mediaType);
        } finally {
            bitmap.close();
        }
    }

    return null;
};
