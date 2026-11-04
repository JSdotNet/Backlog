// A horizontal swipe on the Notifications pager: one finger, mostly sideways, past
// the threshold, and let go. Left is the next item, right the previous one. It only
// moves the pager — an answer is always a tap — so a touch that starts on a control
// is the control's, and a mostly vertical drag is the page scrolling. A touch that
// starts inside something that scrolls sideways — a wide code block or table in the
// body — is that scroller's, so the wide part can be read. Touch only: a pointer and a
// screen reader have the Previous and Next buttons beside it.
const threshold = 64;
const dominance = 1.5;
const controls = 'button, input, textarea, select, label';

export function attach(element, dotnet) {
    let start = null;

    const scrollsSideways = (target) => {
        for (let node = target; node && node !== element; node = node.parentElement) {
            if (node.scrollWidth > node.clientWidth) {
                const overflow = getComputedStyle(node).overflowX;
                if (overflow === 'auto' || overflow === 'scroll') return true;
            }
        }
        return false;
    };

    const onStart = (event) => {
        start = event.touches.length === 1 && !event.target.closest(controls) && !scrollsSideways(event.target)
            ? { x: event.touches[0].clientX, y: event.touches[0].clientY }
            : null;
    };

    const onEnd = (event) => {
        if (start === null) return;

        const touch = event.changedTouches[0];
        const dx = touch.clientX - start.x;
        const dy = touch.clientY - start.y;
        start = null;

        if (Math.abs(dx) >= threshold && Math.abs(dx) > Math.abs(dy) * dominance) {
            dotnet.invokeMethodAsync('OnSwiped', dx < 0 ? 'next' : 'previous');
        }
    };

    const reset = () => {
        start = null;
    };

    element.addEventListener('touchstart', onStart, { passive: true });
    element.addEventListener('touchend', onEnd, { passive: true });
    element.addEventListener('touchcancel', reset);

    return {
        dispose() {
            element.removeEventListener('touchstart', onStart);
            element.removeEventListener('touchend', onEnd);
            element.removeEventListener('touchcancel', reset);
        }
    };
}
