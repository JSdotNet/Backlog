// The page came back into view, or the network came back: either is a reason
// for the outbox to try now. Both heads load this; the MAUI head also hears the
// same things natively, and a second report costs a flush that finds nothing due.
export function watch(dotnet) {
    const onVisibility = () => {
        if (document.visibilityState === 'visible') dotnet.invokeMethodAsync('OnResumed');
    };
    const onOnline = () => dotnet.invokeMethodAsync('OnResumed');

    document.addEventListener('visibilitychange', onVisibility);
    window.addEventListener('online', onOnline);

    return {
        dispose() {
            document.removeEventListener('visibilitychange', onVisibility);
            window.removeEventListener('online', onOnline);
        }
    };
}
