// Web Speech recognition for the mobile browser harness.
//
// The constructor is looked up per call rather than captured when this module
// loads. A test — or a browser that only gains the API once a permission is
// granted — installs `window.SpeechRecognition` after the page is up, and a
// cached reference taken at import time would never see it.
//
// One turn at a time. Each turn owns its own state object so a late `onend`
// from an abandoned recogniser cannot resolve the turn that replaced it.

let current = null;

function recognitionConstructor() {
    return window.SpeechRecognition || window.webkitSpeechRecognition || null;
}

export function isSupported() {
    return recognitionConstructor() !== null;
}

export function start(handler) {
    abort();

    const Recognition = recognitionConstructor();
    if (Recognition === null) {
        report(handler, 'OnFailedAsync', 'not-supported');
        return;
    }

    const turn = { recognition: null, done: false, transcript: '' };
    current = turn;

    const settle = (method, payload) => {
        if (turn.done) return;
        turn.done = true;
        if (current === turn) current = null;
        report(handler, method, payload);
    };

    try {
        turn.recognition = new Recognition();
    } catch {
        settle('OnFailedAsync', 'audio-capture');
        return;
    }

    // No interim results in this increment: the screen shows what was heard
    // once, after the turn ends, rather than a sentence rewriting itself.
    turn.recognition.continuous = false;
    turn.recognition.interimResults = false;
    turn.recognition.lang = navigator.language || document.documentElement.lang || 'en-US';

    turn.recognition.onresult = (event) => {
        const results = event.results || [];
        for (let i = event.resultIndex || 0; i < results.length; i++) {
            const result = results[i];
            if (result && result.isFinal && result[0]) {
                turn.transcript += result[0].transcript;
            }
        }
    };

    turn.recognition.onerror = (event) => settle('OnFailedAsync', (event && event.error) || 'unknown');

    // `onend` runs for every turn, including one that already errored. `settle`
    // is first-wins, so a failed turn is never also reported as empty words.
    turn.recognition.onend = () => settle('OnRecognizedAsync', turn.transcript.trim());

    try {
        turn.recognition.start();
    } catch {
        // Chrome throws here when a recogniser is started twice in a row.
        settle('OnFailedAsync', 'aborted');
    }
}

export function stop() {
    if (current === null || current.recognition === null) return;

    try {
        // `stop` asks for the words so far; `abort` throws them away. The button
        // means "I have finished speaking", so this is the one that belongs here.
        current.recognition.stop();
    } catch {
        // Already stopped: the result is on its way through onend.
    }
}

function abort() {
    if (current !== null && current.recognition !== null) {
        try {
            current.recognition.abort();
        } catch {
            // Nothing to abandon.
        }
    }

    current = null;
}

function report(handler, method, payload) {
    try {
        handler.invokeMethodAsync(method, payload);
    } catch {
        // The .NET object reference is gone — the component that was listening
        // was disposed mid-turn. There is nobody left to tell.
    }
}
