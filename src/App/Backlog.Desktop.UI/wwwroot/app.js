// The three drag listeners that used to be here moved into the component
// library's components.js, along with the drag itself. They existed for the
// backlog pane's hand-rolled grab rails and drop zones, keyed on
// `data-drag-grip` and `data-drop-zone`; the pane's entries and steps are
// TaskListView now, so the payload Chromium insists on belongs beside the
// component that starts the drag rather than in whichever app happens to render
// it.

// components.js owns the renderers. The Devbook atlas is this app's own
// reading of the data, so it is attached to the shared object rather than
// replacing it. The guard means app.js still parses if it is ever loaded alone.
// The Technology area no longer draws an atlas: its views are Blazor components
// (.devbook/domain/devbook/features.md#technology-view).
window.backlogDiagrams = window.backlogDiagrams || {};

// The Devbook atlas hands over a model that is already in the renderer's shape:
// the folders' own graphs carry groups, degrees and tones, and C# reads them. So
// there is nothing for this app to translate — it passes the model through and is
// here only so the Devbook atlas has a renderer name of its own, which is what
// lets it be found by name in a test and in a trace.
window.backlogDiagrams.renderDevbookAtlas = async (element, id, graph, dotnet) => {
    window.backlogGraphAtlas.render(element, id, graph, dotnet);
};

// A surface served as a web page (local ADR 0041 §7) is a browser tab rather than
// an OS window, so BrowserTabShellWindows retitles and closes the tab through
// these. A browser closes only a tab a script opened; any other stays open.
// open answers whether the browser opened the tab: null from window.open is a
// pop-up the browser blocked.
window.backlogShellWindow = {
    open: path => window.open(path, '_blank') !== null,
    setTitle: title => { document.title = title; },
    close: () => window.close(),
};


// Escape on a takeover whose focus has fallen to <body>. Each surface's <main>
// closes on Escape through its own @onkeydown, which hears only the keys pressed
// inside it. A focused control that becomes disabled — a Refresh button while
// its read runs — drops focus to <body>, outside the Blazor root, and the next
// Escape went nowhere. This catches exactly that press and nothing else:
//
// - only with focus on <body>, so a key pressed inside the surface stays the
//   surface's own handler's, and one pressed in the header stays the header's;
// - only when a surface that closes on Escape is open — the Roadmap is a view in
//   the workspace rather than a surface, and Escape means something else there;
// - never when something already took the key (defaultPrevented) or a modal is
//   open, since Escape belongs to the dialog first.
window.backlogTakeoverEscape = (() => {
    let owner = null;

    document.addEventListener('keydown', (event) => {
        if (event.key !== 'Escape' || event.defaultPrevented || !owner) return;
        if (event.target !== document.body && event.target !== document.documentElement) return;
        if (!document.querySelector('main[data-escape-closes]')) return;
        if (document.querySelector('[aria-modal="true"], dialog[open]')) return;

        event.preventDefault();
        owner.invokeMethodAsync('CloseSurfaceOnEscapeAsync');
    });

    // Keyless, and one owner per WebView: this script runs once per document, and
    // every window — the main one and each surface popped out beside it (local ADR
    // 0041 §4) — has a WebView, and so a document, of its own. Only the main
    // window's shell registers; a popped-out window registers nothing and marks no
    // <main> to close, so Escape there closes nothing — in it or in another window,
    // whose document this listener cannot see. A web page the desktop serves is a
    // browser tab, a document of its own again.
    return {
        register: (dotnet) => { owner = dotnet; },
        unregister: () => { owner = null; }
    };
})();

// The shell header stays one row. When it runs out of room the navigation
// options give up their words in tiers — `app-header--fold-1` hides the
// Dashboard's, `--fold-2` the work in progress's as well, `--fold-3` the
// Inbox's, `--fold-4` the view switch's — and this picks the fewest tiers that
// fit. Measured rather than set at fixed breakpoints, because what the row holds
// varies: how many repositories the scope shows, which flags are on.
//
// Every fit starts unfolded and folds one tier at a time, so a wider window
// unfolds again. When even the last tier overflows, `app-header--wrap` hands the
// row back to wrapping rather than clipping a control.
//
// The regions are observed as well as the header: with the row unwrapped the
// header's own box does not change when a region grows, only the region's does.
// The fit runs on the next frame rather than inside the observer's callback, so
// the sizes it changes are not reported back into the same delivery.
window.backlogHeaderFit = (() => {
    const tiers = 4;
    let header = null;
    let observer = null;
    let frame = 0;

    const overflows = () => header.scrollWidth > header.clientWidth + 1;

    const apply = (folded, wrap) => {
        for (let tier = 1; tier <= tiers; tier++) {
            header.classList.toggle(`app-header--fold-${tier}`, tier <= folded);
        }
        header.classList.toggle('app-header--wrap', wrap);
    };

    const fit = () => {
        if (!header || !header.isConnected) return;
        let folded = 0;
        apply(0, false);
        while (overflows() && folded < tiers) {
            folded++;
            apply(folded, false);
        }
        if (overflows()) apply(tiers, true);
    };

    const schedule = () => {
        if (frame) return;
        frame = requestAnimationFrame(() => {
            frame = 0;
            fit();
        });
    };

    const stop = () => {
        if (frame) cancelAnimationFrame(frame);
        frame = 0;
        observer?.disconnect();
        observer = null;
        header = null;
    };

    // Keyed on the element, unlike the takeover Escape above: a remounted shell's
    // observe can reach here before the outgoing one's disconnect, and that late
    // disconnect must not switch off the new header.
    return {
        observe: (element) => {
            stop();
            if (!element) return;
            header = element;
            observer = new ResizeObserver(schedule);
            observer.observe(header);
            for (const region of header.children) observer.observe(region);
            fit();
        },
        disconnect: (element) => {
            if (element === header) stop();
        }
    };
})();

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
