// Microphone recording for the mobile browser harness's cloud dictation engine
// (local ADR 0045 §6): getUserMedia plus MediaRecorder, in webm/opus where the
// browser has it.
//
// One turn at a time, each with its own state object, for the reason speech.js
// gives. A turn ends when `stop` is called or when `maxMs` passes, whichever is
// first, and hands the whole recording to .NET as a stream reference: the
// harness is Blazor Server, so the bytes travel to the circuit in chunks rather
// than as one SignalR message.
//
// A stop belongs to its turn: one that arrives while getUserMedia is still
// waiting, permission prompt included, means the microphone never starts, and
// the turn reports a recording with nothing in it. `abort` ends a turn without
// reporting at all — a cancelled turn, or a page torn down mid-turn.

const preferredTypes = ['audio/webm;codecs=opus', 'audio/webm'];

let current = null;

function recordingType() {
    if (typeof window.MediaRecorder === 'undefined') return null;
    if (typeof MediaRecorder.isTypeSupported !== 'function') return 'audio/webm';
    return preferredTypes.find((type) => MediaRecorder.isTypeSupported(type)) || null;
}

export function isSupported() {
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia) && recordingType() !== null;
}

export async function start(handler, maxMs) {
    abort();

    const type = recordingType();
    if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia || type === null) {
        report(handler, 'OnFailedAsync', 'not-supported');
        return;
    }

    const turn = { recorder: null, stream: null, chunks: [], timer: null, done: false, stopRequested: false };
    current = turn;

    const release = () => {
        if (turn.timer !== null) clearTimeout(turn.timer);
        turn.timer = null;
        if (turn.stream) turn.stream.getTracks().forEach((track) => track.stop());
        turn.stream = null;
        if (current === turn) current = null;
    };

    const fail = (error) => {
        if (turn.done) return;
        turn.done = true;
        release();
        report(handler, 'OnFailedAsync', error);
    };

    try {
        turn.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (error) {
        fail((error && error.name) || 'audio-capture');
        return;
    }

    // Abandoned while the permission prompt was up.
    if (current !== turn) {
        release();
        return;
    }

    // Stopped while the permission prompt was up: the person's answer for this
    // turn, so the microphone goes off again before anything is recorded.
    if (turn.stopRequested) {
        turn.done = true;
        release();
        report(handler, 'OnStoppedBeforeRecordingAsync', type);
        return;
    }

    try {
        turn.recorder = new MediaRecorder(turn.stream, { mimeType: type });
    } catch {
        fail('not-supported');
        return;
    }

    turn.recorder.ondataavailable = (event) => {
        if (event.data && event.data.size > 0) turn.chunks.push(event.data);
    };

    turn.recorder.onerror = () => fail('audio-capture');

    turn.recorder.onstop = async () => {
        if (turn.done) return;
        turn.done = true;
        release();

        const blob = new Blob(turn.chunks, { type: turn.recorder.mimeType || type });
        try {
            const bytes = new Uint8Array(await blob.arrayBuffer());
            handler.invokeMethodAsync('OnRecordedAsync', DotNet.createJSStreamReference(bytes), blob.type || type)
                .catch(() => { /* The .NET side is gone mid-turn. */ });
        } catch {
            // The .NET object reference is gone; nobody is left to hand it to.
        }
    };

    turn.recorder.start();

    // The cap: the recording ends as if stop were pressed.
    turn.timer = setTimeout(() => stopTurn(turn), maxMs);
}

export function stop() {
    if (current === null) return;

    current.stopRequested = true;
    stopTurn(current);
}

function stopTurn(turn) {
    if (turn.recorder === null || turn.recorder.state === 'inactive') return;

    try {
        turn.recorder.stop();
    } catch {
        // Already stopping: the recording is on its way through onstop.
    }
}

export function abort() {
    if (current !== null) {
        const turn = current;
        turn.done = true;
        current = null;
        if (turn.timer !== null) clearTimeout(turn.timer);
        try {
            if (turn.recorder !== null && turn.recorder.state !== 'inactive') turn.recorder.stop();
        } catch {
            // Nothing to abandon.
        }
        if (turn.stream) turn.stream.getTracks().forEach((track) => track.stop());
    }
}

function report(handler, method, payload) {
    try {
        handler.invokeMethodAsync(method, payload).catch(() => { /* Disposed mid-turn, as below. */ });
    } catch {
        // See speech.js: the component that was recording was disposed mid-turn.
    }
}
