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
