// Meeting recording for the mobile browser harness (local ADR 0048, scope Q3):
// getUserMedia plus MediaRecorder in audio/mp4 (AAC), which a note accepts, so
// the phone's note page records in the harness the way it does on Android.
//
// Not recorder.js, which is dictation's: a dictation turn is short and handed
// over whole, and a meeting is long, pausable and written to disk as it goes.
// Here MediaRecorder runs with a timeslice, and each slice is handed to .NET as
// a stream reference, in order, through one promise chain — the harness is
// Blazor Server, so the bytes reach the circuit in chunks and are appended to
// the part file there. A tick a few times a second while recording carries the
// input level from an AnalyserNode, where there is one; the .NET side's clock
// is the timer, and the tick is what redraws it.
//
// One recording at a time. `start` answers null once recording, or the error's
// name; `stop` resolves once the last slice has reached .NET. `abort` ends a
// recording without handing anything more over, for a circuit going away.

const recordingType = 'audio/mp4';
const levelEveryMs = 200;

let current = null;

export function isSupported() {
    return !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia)
        && typeof window.MediaRecorder !== 'undefined'
        && typeof MediaRecorder.isTypeSupported === 'function'
        && MediaRecorder.isTypeSupported(recordingType);
}

export async function start(handler, timesliceMs) {
    abort();

    if (!isSupported()) return 'not-supported';

    const session = {
        handler,
        recorder: null,
        stream: null,
        audio: null,
        levelTimer: null,
        chain: Promise.resolve(),
        stopped: null,
        done: false,
    };
    current = session;

    try {
        session.stream = await navigator.mediaDevices.getUserMedia({ audio: true });
    } catch (error) {
        if (current === session) current = null;
        return (error && error.name) || 'audio-capture';
    }

    // Abandoned while the permission prompt was up.
    if (current !== session) {
        release(session);
        return 'aborted';
    }

    try {
        session.recorder = new MediaRecorder(session.stream, { mimeType: recordingType });
    } catch {
        release(session);
        current = null;
        return 'not-supported';
    }

    session.recorder.ondataavailable = (event) => {
        if (!event.data || event.data.size === 0 || session.done) return;
        const blob = event.data;
        session.chain = session.chain.then(async () => {
            if (session.done) return;
            const bytes = new Uint8Array(await blob.arrayBuffer());
            await handler.invokeMethodAsync('OnChunkAsync', DotNet.createJSStreamReference(bytes));
        }).catch(() => { /* .NET is gone mid-recording; the file keeps what reached it. */ });
    };

    session.stopped = new Promise((resolve) => { session.recorder.onstop = resolve; });

    session.recorder.onerror = (event) => fault(session, (event && event.error && event.error.name) || 'audio-capture');
    session.stream.getAudioTracks().forEach((track) => {
        track.onended = () => fault(session, 'track-ended');
    });

    watchLevel(session);
    session.recorder.start(timesliceMs);
    return null;
}

export function pause() {
    if (current && current.recorder && current.recorder.state === 'recording') current.recorder.pause();
}

export function resume() {
    if (current && current.recorder && current.recorder.state === 'paused') current.recorder.resume();
}

export async function stop() {
    const session = current;
    if (session === null) return;
    current = null;

    if (session.recorder && session.recorder.state !== 'inactive') {
        try {
            session.recorder.stop();
        } catch {
            // Already stopping.
        }
        // The final slice comes through ondataavailable before onstop.
        await session.stopped;
    }

    await session.chain;
    session.done = true;
    release(session);
}

export function abort() {
    if (current === null) return;

    const session = current;
    current = null;
    session.done = true;
    try {
        if (session.recorder && session.recorder.state !== 'inactive') session.recorder.stop();
    } catch {
        // Nothing to abandon.
    }
    release(session);
}

function fault(session, name) {
    if (session.done) return;
    try {
        session.handler.invokeMethodAsync('OnFaultAsync', name).catch(() => { });
    } catch {
        // Disposed mid-recording.
    }
}

// The tick that moves the bar's timer, carrying the input level when there is
// an analyser to measure it and -1 when there is not: a browser with no
// AudioContext still shows the time running, and the .NET side tells "cannot
// measure" from silence, which would otherwise read as another app holding the
// microphone. Only while recording — a paused or stopping recording has nothing
// to report.
function watchLevel(session) {
    let measure = () => -1;

    try {
        const Context = window.AudioContext || window.webkitAudioContext;
        if (Context) {
            session.audio = new Context();
            const analyser = session.audio.createAnalyser();
            analyser.fftSize = 512;
            session.audio.createMediaStreamSource(session.stream).connect(analyser);
            const samples = new Float32Array(analyser.fftSize);

            measure = () => {
                analyser.getFloatTimeDomainData(samples);
                let sum = 0;
                for (let i = 0; i < samples.length; i++) sum += samples[i] * samples[i];
                // RMS, lifted so ordinary speech fills about half the meter.
                return Math.min(1, Math.sqrt(sum / samples.length) * 4);
            };
        }
    } catch {
        // No level, still a timer.
    }

    session.levelTimer = setInterval(() => {
        if (session.done || !session.recorder || session.recorder.state !== 'recording') return;
        try {
            session.handler.invokeMethodAsync('OnLevel', measure()).catch(() => { });
        } catch {
            // Disposed mid-recording.
        }
    }, levelEveryMs);
}

function release(session) {
    if (session.levelTimer !== null) clearInterval(session.levelTimer);
    session.levelTimer = null;
    if (session.audio) session.audio.close().catch(() => { });
    session.audio = null;
    if (session.stream) session.stream.getTracks().forEach((track) => track.stop());
    session.stream = null;
}
