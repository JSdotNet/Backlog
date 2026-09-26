// Pull-to-refresh on a list: a downward drag that starts with the page already
// at the top, held past the threshold and let go. Touch only — a pointer has the
// refresh button beside the heading, which is also what a screen reader finds.
const threshold = 64;

export function attach(element, dotnet) {
    let startY = null;
    let armed = false;

    const atTop = () => {
        for (let node = element; node; node = node.parentElement) {
            if (node.scrollTop > 0) return false;
        }
        return (document.scrollingElement?.scrollTop ?? 0) <= 0;
    };

    const reset = () => {
        startY = null;
        armed = false;
        element.classList.remove('inbox--pulling');
    };

    const onStart = (event) => {
        startY = event.touches.length === 1 && atTop() ? event.touches[0].clientY : null;
    };

    const onMove = (event) => {
        if (startY === null) return;
        armed = event.touches[0].clientY - startY > threshold;
        element.classList.toggle('inbox--pulling', armed);
    };

    const onEnd = () => {
        if (armed) dotnet.invokeMethodAsync('OnPulled');
        reset();
    };

    element.addEventListener('touchstart', onStart, { passive: true });
    element.addEventListener('touchmove', onMove, { passive: true });
    element.addEventListener('touchend', onEnd);
    element.addEventListener('touchcancel', reset);

    return {
        dispose() {
            element.removeEventListener('touchstart', onStart);
            element.removeEventListener('touchmove', onMove);
            element.removeEventListener('touchend', onEnd);
            element.removeEventListener('touchcancel', reset);
        }
    };
}
