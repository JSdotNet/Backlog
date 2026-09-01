/*
    Browser behaviour owned by the shared component library, served from
    _content/Backlog.UI.Components/components.js. A host scripts this file before
    its own, so `window.backlogDiagrams` and friends already exist by the time an
    app extends them.

    Everything lives inside one IIFE: only the handful of `window.*` entry points
    below are shared, so a host script can declare whatever top-level names it
    likes without colliding with this file.
*/
(() => {
    'use strict';

    // Keyboard reordering has to carry the focus ring with the thing it moved;
    // after the list re-renders the element is a different node, so the caller
    // names it by id.
    //
    // Arriving in a field should be arriving ready to type, so a caller may ask
    // for the value to be selected as well: the first keystroke then replaces
    // what is there instead of being appended to it, which is what renaming means
    // everywhere else. Optional and off unless asked for, because the other
    // callers here focus buttons — select is something an input has and a button
    // does not, which is why it is called for rather than assumed.
    window.backlogFocus = (id, select) => {
        const element = document.getElementById(id);
        if (!element) return;

        element.focus();
        if (select) element.select?.();
    };

    // Tab inside a quick edit belongs to the list, not to the browser: it commits
    // the rename and opens the field on the next row, and a browser that also
    // moved the focus ring on would land it one control past the field that just
    // opened. Blazor's @onkeydown:preventDefault cannot do this — it is read when
    // the field renders, which is before anyone has pressed anything, so it would
    // have to be a constant, and a constant true swallows every keystroke typed
    // into the field. So the field is told here instead, by id, because it is a
    // new element every time the editor opens.
    //
    // Which is not the same answer for every field, hence the mode. A row's rename
    // owns Tab outright, both ways: forward hands the editor to the row below and
    // Shift+Tab to the row above, so neither may reach the browser. The list's add
    // field owns far less than that — it is a permanent control sitting in the tab
    // order, and Tab out of it while it is empty is how a reader leaves the list at
    // all. Suppressing that would strand them, so 'filled' takes only a forward Tab
    // and only while there is something to add.
    //
    // Deciding it here rather than per keystroke in C# is the whole point: by the
    // time a keydown has crossed to .NET the browser has already moved the focus
    // ring, so anything the component says afterwards is too late. The predicate is
    // the trimmed value because that is the same test the component applies to
    // decide whether it will handle the key — the two have to agree, or a field of
    // spaces would be one the browser was stopped from leaving and the component
    // declined to act on.
    //
    // The listener dies with the element, which is the whole reason there is no
    // matching release. Re-arming an element it is already on changes its mode
    // rather than stacking a second listener on it: the mode is read at keydown,
    // not captured at arm time.
    window.backlogGuardTab = (id, mode) => {
        const element = document.getElementById(id);
        if (!element) return;

        const armed = element.dataset.backlogTabGuard !== undefined;
        element.dataset.backlogTabGuard = mode === 'filled' ? 'filled' : 'always';

        if (armed) return;

        element.addEventListener('keydown', (event) => {
            if (event.key !== 'Tab') return;

            if (element.dataset.backlogTabGuard === 'filled'
                && (event.shiftKey || (element.value ?? '').trim().length === 0)) {
                return;
            }

            event.preventDefault();
        });
    };

    /*
        Hold the focus inside a region while it is open, and give it back when it
        closes.

        Both halves are one primitive because they are one promise: a reader sent
        into a drawer has to be able to get out of it the way they came, and a trap
        that forgets where the focus was is a trap in the unkind sense. The element
        that had focus when the trap arms is the element it is returned to, unless
        the caller names one — a host that knows the row a sheet was opened from can
        say so, and that survives the row being re-rendered underneath.

        Tab cycles rather than being merely blocked. `backlogGuardTab` above stops a
        Tab from leaving a field; this one wraps it to the other end of the region,
        which is what a dialog does and what a guard cannot express.

        Tabbables are read at keydown, not at arm time. The sheet's contents change
        as the reader pages through records, and a list captured on open would send
        Tab to an element that is no longer there.
    */
    const backlogFocusTrapListeners = new Map();
    const backlogFocusTrapReturns = new Map();

    const backlogFocusTrapSelector = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled]):not([type="hidden"])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(',');

    function backlogTabbablesIn(element) {
        return [...element.querySelectorAll(backlogFocusTrapSelector)].filter((candidate) => {
            if (candidate.hasAttribute('inert') || candidate.closest('[inert]')) return false;
            if (candidate.getAttribute('aria-hidden') === 'true') return false;

            // `visibility: hidden` still has boxes, so the rect test below cannot see
            // it — and focus() on one does nothing. Ask directly.
            if (getComputedStyle(candidate).visibility === 'hidden') return false;

            // offsetParent is null for display:none, and for position:fixed — which a
            // sheet is — so fall back to the box before believing it.
            return candidate.offsetParent !== null || candidate.getClientRects().length > 0;
        });
    }

    window.backlogFocusTrap = (id, restoreToId) => {
        const element = document.getElementById(id);
        if (!element) return;
        if (element.dataset.backlogFocusTrap !== undefined) return;

        element.dataset.backlogFocusTrap = 'armed';
        element.dataset.backlogFocusReturn = restoreToId ?? '';

        const previous = document.activeElement;
        if (previous instanceof HTMLElement) {
            backlogFocusTrapReturns.set(id, previous);
        }

        const onKeyDown = (event) => {
            if (event.key !== 'Tab') return;

            const tabbables = backlogTabbablesIn(element);
            if (tabbables.length === 0) {
                event.preventDefault();
                element.focus();
                return;
            }

            const first = tabbables[0];
            const last = tabbables[tabbables.length - 1];
            const active = document.activeElement;

            // Focus sitting on the region itself counts as before the first: that is
            // where it lands when the sheet opens, and Tab from there must go in.
            if (event.shiftKey && (active === first || active === element)) {
                event.preventDefault();
                last.focus();
            } else if (!event.shiftKey && active === last) {
                event.preventDefault();
                first.focus();
            }
        };

        element.addEventListener('keydown', onKeyDown);
        backlogFocusTrapListeners.set(id, () => element.removeEventListener('keydown', onKeyDown));

        // Move the focus in only if it is not already inside — a host that focused
        // its own control first should keep it.
        //
        // Two frames late, and that is not superstition. A sheet is `visibility:
        // hidden` until the render that opens it, and `focus()` on a hidden element
        // does nothing at all — silently, with no error. Arming happens in the same
        // tick as the class flip, so focusing immediately lands nowhere and the
        // reader is left outside a dialog that has already trapped the Tab key.
        // One frame gets the style recalculated; the second is the paint.
        if (!element.contains(document.activeElement)) {
            requestAnimationFrame(() => requestAnimationFrame(() => {
                if (element.dataset.backlogFocusTrap === undefined) return;
                if (element.contains(document.activeElement)) return;

                const tabbables = backlogTabbablesIn(element);
                (tabbables[0] ?? element).focus();
            }));
        }
    };

    window.backlogReleaseFocusTrap = (id) => {
        const element = document.getElementById(id);
        if (element) {
            delete element.dataset.backlogFocusTrap;
            delete element.dataset.backlogFocusReturn;
        }

        backlogFocusTrapListeners.get(id)?.();
        backlogFocusTrapListeners.delete(id);

        const named = element?.dataset?.backlogFocusReturn;
        const target = (named && document.getElementById(named)) || backlogFocusTrapReturns.get(id);
        backlogFocusTrapReturns.delete(id);

        // Only take the focus back if the region still holds it. The reader may have
        // clicked somewhere else entirely, and yanking them back would be the trap
        // outliving the thing it was trapping for.
        if (target && target.isConnected && (!element || element.contains(document.activeElement) || document.activeElement === document.body)) {
            target.focus();
        }
    };

    /*
        Whether the focus has landed outside every one of several named regions.

        Several rather than one, because "outside" is not always one element's
        worth of DOM: a resizable split's own separator sits between its two
        halves rather than inside either, and a reader dragging it — or tabbing
        onto it — has not left the pane on the other side of it. The caller
        passes every element a focus landing there should still count as staying
        put, as CSS selectors rather than plain ids so a stable data-testid can
        be reused instead of minting a matching id for every element this needs
        to name.

        The question a `focusout` handler actually has is "did the reader leave
        this region, or only move about inside it", and Blazor's FocusEventArgs
        cannot answer it: it carries `Type` and nothing else, so the
        `relatedTarget` the DOM event holds never reaches C#. Rather than smuggle
        the target across, the caller asks afterwards where the focus ended up.

        Afterwards is the point. A `focusout` fires before the next element is
        focused, so `activeElement` mid-transfer is the body; by the time this
        interop call runs the browser has finished the transfer in the task that
        raised the event, so the answer is the settled one rather than the
        transitional one — which also makes it right for a focus the handler
        itself moved.

        Nowhere is deliberately not somewhere else. The focus is on the body
        after the element holding it was removed — closing a date picker does
        exactly that — and after the window itself loses focus. Neither is the
        reader moving on, and a caller acting on it would tear down the surface
        they are in the middle of using.
    */
    window.backlogFocusOutside = (...selectors) => {
        const elements = selectors.map((selector) => document.querySelector(selector)).filter(Boolean);
        if (elements.length === 0) return false;

        const focused = document.activeElement;
        if (!focused || focused === document.body || focused === document.documentElement) return false;

        return !elements.some((element) => element.contains(focused));
    };

    // Copying is the browser's job, and the browser is allowed to refuse: the
    // async clipboard needs a secure context and a permission the host WebView
    // may not have granted. The execCommand path is the fallback for exactly
    // that case — deprecated, but it is what still works in a WebView2 without
    // clipboard-write. Either way the caller is told whether it worked, so the
    // UI can say so instead of claiming a copy that never happened.
    /*
        Markdown editing that acts on the selection.

        This is the "rich text" half of a markdown editor, and it stops exactly
        where a WYSIWYG would begin: the text stays markdown, the textarea stays
        the truth, and a toolbar button is a text edit you could have typed. That
        keeps every promise the read view already makes — the source is what is
        saved, what is diffed, and what the parser sees — and it keeps the one
        thing a contenteditable surface cannot: a body half-typed in a syntax
        nobody has closed yet still round-trips exactly.

        Selection lives in the browser and nowhere else, so all of it is here.
        C# hears the finished text.
    */
    const backlogMarkdownSurface = (container) =>
        container instanceof HTMLTextAreaElement ? container : container?.querySelector('textarea');

    // Wrapping is a toggle: pressing bold on text that is already bold takes it
    // off, because the alternative is `****text****` and a reader who has to
    // count asterisks.
    const backlogWrapSelection = (value, start, end, marker) => {
        const selected = value.slice(start, end);
        const before = value.slice(0, start);
        const after = value.slice(end);
        const width = marker.length;

        if (selected.startsWith(marker) && selected.endsWith(marker) && selected.length >= width * 2) {
            const inner = selected.slice(width, -width);
            return { value: before + inner + after, start, end: start + inner.length };
        }

        if (before.endsWith(marker) && after.startsWith(marker)) {
            return {
                value: before.slice(0, -width) + selected + after.slice(width),
                start: start - width,
                end: end - width
            };
        }

        return {
            value: `${before}${marker}${selected}${marker}${after}`,
            start: start + width,
            end: end + width
        };
    };

    // A line prefix applies to every line the selection touches, including the
    // one the caret merely sits on. Applied to all of them or removed from all
    // of them — a half-marked block is nobody's intent.
    const backlogPrefixLines = (value, start, end, prefix) => {
        const lineStart = value.lastIndexOf('\n', start - 1) + 1;
        const lineEndIndex = value.indexOf('\n', end);
        const lineEnd = lineEndIndex === -1 ? value.length : lineEndIndex;

        const block = value.slice(lineStart, lineEnd);
        const lines = block.split('\n');
        // An ordered list renumbers rather than repeating "1." down the block.
        const ordered = prefix === '1. ';
        const marked = (line) => (ordered ? /^\d+[.)]\s/.test(line) : line.startsWith(prefix));
        const allMarked = lines.every((line) => line.trim().length === 0 || marked(line));

        const next = lines
            .map((line, index) => {
                if (line.trim().length === 0) return line;
                if (allMarked) return line.replace(ordered ? /^\d+[.)]\s/ : prefix, '');
                return (ordered ? `${index + 1}. ` : prefix) + line;
            })
            .join('\n');

        return {
            value: value.slice(0, lineStart) + next + value.slice(lineEnd),
            start: lineStart,
            end: lineStart + next.length
        };
    };

    // One entry per live editor, so the scroll listener can be taken off again
    // when the component goes away.
    const backlogMarkdownWatchers = new Map();

    window.backlogMarkdownEditor = {
        /*
            Keeps the highlight layer showing the same part of the text the
            textarea is showing. The layer never scrolls itself — it is told
            where the textarea got to — because two boxes scrolling on their own
            drift apart by exactly as much as the reader scrolls.
        */
        watch(container, id) {
            const textarea = backlogMarkdownSurface(container);
            const layer = container?.querySelector('.markdown-editor__highlight');
            if (!textarea || !layer) return;

            this.unwatch(id);

            const sync = () => {
                layer.scrollTop = textarea.scrollTop;
                layer.scrollLeft = textarea.scrollLeft;
            };

            textarea.addEventListener('scroll', sync, { passive: true });
            // Typing at the bottom scrolls the textarea without a scroll event
            // in every browser, so the input is worth listening to as well.
            textarea.addEventListener('input', sync);
            backlogMarkdownWatchers.set(id, () => {
                textarea.removeEventListener('scroll', sync);
                textarea.removeEventListener('input', sync);
            });

            sync();
        },

        unwatch(id) {
            const remove = backlogMarkdownWatchers.get(id);
            remove?.();
            backlogMarkdownWatchers.delete(id);
        },

        /*
            Applies one action to the textarea inside `container` and returns the
            text afterwards. The element is mutated here rather than waiting for a
            round trip, so the caret never visibly jumps to the end and back; C#
            stores the same string, which means Blazor's diff finds nothing to
            write and leaves the selection alone.
        */
        apply(container, action, argument) {
            const textarea = backlogMarkdownSurface(container);
            if (!textarea) return null;

            const value = textarea.value ?? '';
            const start = textarea.selectionStart ?? value.length;
            const end = textarea.selectionEnd ?? start;

            let result;
            switch (action) {
                case 'bold': result = backlogWrapSelection(value, start, end, '**'); break;
                case 'italic': result = backlogWrapSelection(value, start, end, '*'); break;
                case 'strike': result = backlogWrapSelection(value, start, end, '~~'); break;
                case 'code': result = backlogWrapSelection(value, start, end, '`'); break;
                case 'heading': result = backlogPrefixLines(value, start, end, '# '); break;
                case 'quote': result = backlogPrefixLines(value, start, end, '> '); break;
                case 'bullet': result = backlogPrefixLines(value, start, end, '- '); break;
                case 'ordered': result = backlogPrefixLines(value, start, end, '1. '); break;
                case 'task': result = backlogPrefixLines(value, start, end, '- [ ] '); break;
                case 'link': {
                    const text = value.slice(start, end) || 'link text';
                    const url = argument || 'https://';
                    const replacement = `[${text}](${url})`;
                    result = {
                        value: value.slice(0, start) + replacement + value.slice(end),
                        // Land on the URL: the text was already chosen, the URL
                        // never is.
                        start: start + text.length + 3,
                        end: start + text.length + 3 + url.length
                    };
                    break;
                }
                default:
                    return null;
            }

            textarea.value = result.value;
            textarea.setSelectionRange(result.start, result.end);
            textarea.focus();

            return result.value;
        }
    };

    window.backlogClipboard = {
        copy: async (text) => {
            const value = text ?? '';

            if (navigator.clipboard && window.isSecureContext) {
                try {
                    await navigator.clipboard.writeText(value);
                    return true;
                } catch {
                    // Fall through to the legacy path.
                }
            }

            const staging = document.createElement('textarea');
            staging.value = value;
            // Off-screen rather than hidden: the selection has to be real, and
            // display:none elements cannot be selected.
            staging.setAttribute('readonly', '');
            staging.setAttribute('aria-hidden', 'true');
            staging.style.position = 'fixed';
            staging.style.top = '-1000px';
            staging.style.opacity = '0';
            document.body.appendChild(staging);

            try {
                staging.select();
                return document.execCommand('copy');
            } catch {
                return false;
            } finally {
                staging.remove();
            }
        }
    };

    // Reordering a task row is a pointer gesture, not an HTML5 drag.
    //
    // Native drag was the obvious implementation and it is unusable in one of the
    // heads this library ships to. The desktop head renders these components in a
    // WebView2 hosted by WinUI3 (MAUI's BlazorWebView on Windows), and there the
    // native drag session is aborted by the platform about five milliseconds after
    // it opens: `dragstart` fires, `pointercancel` follows, `dragend` arrives
    // immediately, and no `dragover` or `drop` is ever delivered. Measured on the
    // running app over its WebView2 debugging port, ten gestures out of ten.
    //
    // That leaves nothing for a native-drag implementation to hook: the events that
    // say where the row is going never happen, and the pointer stream that could
    // have answered instead is cancelled by the drag that is about to die. Both
    // halves of the gesture have to come from pointer events, so the row is no
    // longer `draggable` at all and this drives the same C# state machine —
    // PointerDragStart, PointerDragOver, PointerDragEnd — that the drag events used
    // to. One implementation for every host, because a second one kept only for the
    // hosts where native drag happens to work would be a second one to get wrong.
    //
    // Here rather than in a host's own script, because TaskListView owns the whole
    // gesture: a host that had to supply this would be a host that has to know the
    // list reorders at all.
    const taskListRefs = new Map();

    window.taskListDrag = {
        register(ownerId, dotNetRef) {
            taskListRefs.set(ownerId, dotNetRef);
        },
        unregister(ownerId) {
            taskListRefs.delete(ownerId);
        }
    };

    // How far the pointer travels before a press becomes a drag. Without a
    // threshold every click on a row would open and close a drag, and a click is
    // how a row is selected — so the gesture has to prove it is a move first.
    const TASK_DRAG_THRESHOLD_PX = 4;

    // How long after a drag a click is swallowed. The pointerup that ends a drag is
    // followed by a click on whatever is under it, and that click would select a
    // row the reader was only dropping onto.
    const TASK_DRAG_CLICK_GRACE_MS = 300;

    // Everything inside a row that owns its own press. The row's title button is
    // deliberately absent: dragging a row by its title is the gesture people
    // actually make, and it stays a click when the pointer does not travel.
    const TASK_DRAG_EXCLUDED =
        '.task-item__check, .task-item__edit, .task-item__delete, .task-item__copy,' +
        '.task-item__actions, .task-item__fold, .task-item__rename,' +
        'input, textarea, select, a[href]';

    let taskDrag = null;
    let taskDragClickBlockedUntil = 0;

    function endTaskDrag() {
        taskDrag = null;
    }

    function taskRowFromPoint(x, y) {
        const element = document.elementFromPoint(x, y);
        return element instanceof Element ? element.closest('.task-item[data-task-id]') : null;
    }

    document.addEventListener('pointerdown', (event) => {
        // The primary button only. A right-click opens a menu and a middle-click is
        // not a gesture this list claims.
        if (event.button !== 0 || !event.isPrimary) return;

        const target = event.target instanceof Element ? event.target : null;
        const row = target?.closest('.task-item[data-draggable="true"]');
        if (!row) return;

        const onGrip = !!target.closest('.task-item__grip');

        // A control inside the row keeps its own press, unless the press is on the
        // grip — which is nothing but a handle and sits inside the row with them.
        if (!onGrip && target.closest(TASK_DRAG_EXCLUDED)) return;

        // Touch and pen drag from the grip only. A finger pressing anywhere else on
        // a row is how the list is scrolled, and taking that over would make a long
        // list unreadable to get a row moved.
        if (event.pointerType !== 'mouse' && !onGrip) return;

        const ownerId = row.closest('[data-list-owner]')?.getAttribute('data-list-owner');
        const ref = ownerId ? taskListRefs.get(ownerId) ?? null : null;
        const taskId = row.getAttribute('data-task-id');
        if (!ref || !taskId) return;

        taskDrag = {
            ref,
            taskId,
            startX: event.clientX,
            startY: event.clientY,
            pointerId: event.pointerId,
            active: false,
            lastOverId: null
        };
    });

    document.addEventListener('pointermove', (event) => {
        if (!taskDrag || event.pointerId !== taskDrag.pointerId) return;

        if (!taskDrag.active) {
            const travelled = Math.hypot(event.clientX - taskDrag.startX, event.clientY - taskDrag.startY);
            if (travelled < TASK_DRAG_THRESHOLD_PX) return;

            taskDrag.active = true;
            taskDrag.ref.invokeMethodAsync('PointerDragStart', taskDrag.taskId).catch(() => {
                // The circuit can already be gone (navigation, disposal).
                endTaskDrag();
            });
        }

        // Reported only when the answer changes. A pointer crossing a list produces
        // a move event per frame, and each one is a round trip to C# — the row under
        // the pointer is what the drop needs, not how often it was asked.
        const overId = taskRowFromPoint(event.clientX, event.clientY)?.getAttribute('data-task-id');
        if (!overId || overId === taskDrag.lastOverId) return;

        taskDrag.lastOverId = overId;
        taskDrag.ref.invokeMethodAsync('PointerDragOver', overId).catch(() => {
        });
    });

    document.addEventListener('pointerup', (event) => {
        if (!taskDrag || event.pointerId !== taskDrag.pointerId) return;

        const { ref, active } = taskDrag;
        endTaskDrag();

        // Never travelled, so it was a click: the row keeps it, and nothing was
        // started that needs settling.
        if (!active) return;

        taskDragClickBlockedUntil = performance.now() + TASK_DRAG_CLICK_GRACE_MS;

        ref.invokeMethodAsync('PointerDragEnd').catch(() => {
        });
    });

    // The gesture was taken away rather than finished — the platform cancelling the
    // pointer, or the window losing it. The row goes back where it was, because a
    // drag nobody released is not a drop.
    const cancelTaskDrag = () => {
        if (!taskDrag) return;

        const { ref, active } = taskDrag;
        endTaskDrag();

        if (active) {
            ref.invokeMethodAsync('PointerDragCancel').catch(() => {
            });
        }
    };

    document.addEventListener('pointercancel', cancelTaskDrag);
    window.addEventListener('blur', cancelTaskDrag);

    // Escape abandons a drag in flight, the way it abandons every other thing in
    // this product that can be put down.
    document.addEventListener('keydown', (event) => {
        if (event.key === 'Escape') cancelTaskDrag();
    });

    // The click that follows the pointerup that ended a drag. It would land on the
    // row the drop was aimed at and select it, which is a second thing happening
    // because of one gesture.
    document.addEventListener(
        'click',
        (event) => {
            if (performance.now() >= taskDragClickBlockedUntil) return;

            taskDragClickBlockedUntil = 0;
            event.preventDefault();
            event.stopPropagation();
        },
        true
    );

    // The side pane is resized by dragging its edge. Pointer capture and the live
    // width both belong in the browser; C# only hears the settled value, so a drag
    // costs one interop call instead of one per frame.
    const BACKLOG_PANE_MIN_REM = 24;
    const BACKLOG_PANE_ABSOLUTE_MAX_REM = 200;
    const BACKLOG_SINGLE_PANE_MAX_REM = 72;
    const BACKLOG_THREE_PANE_MIN_REM = 96;
    // The app's own knowledge layout, or any SplitPane the library renders.
    const BACKLOG_PANE_LAYOUT_SELECTOR = '[data-testid="knowledge-layout"], [data-pane-split]';

    /**
     * Who to tell when a drag settles, one entry per resizable layout.
     *
     * This was a single owner, and a single owner is why a page could only ever
     * have one draggable pane: the second layout to register replaced the first,
     * and every drag reported its width to whichever component happened to be in
     * the variable. The desktop shell registers for its knowledge layout on
     * startup, so a SplitPane inside that shell had to opt out of the pointer
     * drag entirely and keep only its keyboard resize — a separator you could
     * tab to and not drag.
     *
     * Keyed by the layout's `data-pane-owner`, which the component mints, so the
     * drag settles to the layout it was actually performed on. The empty key is
     * the app's own layout, which has no attribute because it was here first.
     */
    const backlogPaneOwners = new Map();

    function backlogOwnerKey(layout) {
        return layout.getAttribute('data-pane-owner') ?? '';
    }

    /**
     * The owner for one layout, and never a fallback to somebody else's.
     *
     * A layout that names an owner and has not registered one is mid-render or
     * disposed, and reporting its width to the default owner is the exact
     * cross-talk the key was added to stop.
     */
    function backlogPaneOwnerFor(layout) {
        return backlogPaneOwners.get(backlogOwnerKey(layout)) ?? null;
    }

    function backlogLayoutForKey(key) {
        return key
            ? document.querySelector(`[data-pane-owner="${key}"]`)
            : document.querySelector(BACKLOG_PANE_LAYOUT_SELECTOR);
    }

    function backlogRootFontSize() {
        return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    }

    // The pane may take everything the backlog does not strictly need, so the real
    // ceiling is the window, not a fixed number of rem.
    function backlogPaneMaxRem(layout) {
        const rem = backlogRootFontSize();
        const styles = getComputedStyle(layout);
        const companionMinRem = parseFloat(styles.getPropertyValue('--pane-min-width')) || 22;
        const gapRem = ((parseFloat(styles.columnGap) || 0) * 2) / rem;
        const available = (layout.clientWidth / rem) - companionMinRem - gapRem - 1;

        return Math.min(BACKLOG_PANE_ABSOLUTE_MAX_REM, Math.max(BACKLOG_PANE_MIN_REM, Math.round(available * 2) / 2));
    }

    /**
     * Which edge the resized pane is anchored to.
     *
     * The app's knowledge panel sits on the right, so its width is the distance
     * from the pointer to the layout's right edge. A SplitPane's bound value is the
     * width of whichever pane its Anchor names, so a start-anchored one measures
     * from the left edge instead. Measuring both the same way made the library's
     * separator run backwards: dragging right narrowed the pane it was supposed to
     * widen. The default stays 'end' because a layout that says nothing is the app's
     * own, which was here first.
     */
    function backlogPaneAnchor(layout) {
        return layout.getAttribute('data-pane-anchor') === 'start' ? 'start' : 'end';
    }

    function backlogPaneWidthAt(layout, clientX) {
        const box = layout.getBoundingClientRect();
        const distance = backlogPaneAnchor(layout) === 'start' ? clientX - box.left : box.right - clientX;
        const rem = distance / backlogRootFontSize();

        return Math.min(backlogPaneMaxRem(layout), Math.max(BACKLOG_PANE_MIN_REM, Math.round(rem * 2) / 2));
    }

    function backlogViewportWidthRem() {
        return (document.documentElement.clientWidth || window.innerWidth || 0) / backlogRootFontSize();
    }

    function backlogPaneCapacity() {
        const viewportWidthRem = backlogViewportWidthRem();
        if (viewportWidthRem <= BACKLOG_SINGLE_PANE_MAX_REM) return 1;
        if (viewportWidthRem >= BACKLOG_THREE_PANE_MIN_REM) return 3;
        return 2;
    }

    // The window decides how many panes fit; the layout element only decides how
    // wide the side pane may get. They are reported separately because the two
    // questions do not need the same answer to be on screen: capacity reads the
    // viewport and can always be answered, while a width can only be measured
    // from a layout that is actually mounted. Guarding both on the layout meant a
    // window resized while a full-screen surface was open reported neither, so
    // the panes came back sized for a window that was gone.
    function backlogReportPaneBounds() {
        const capacity = backlogPaneCapacity();

        for (const [key, owner] of backlogPaneOwners) {
            owner.invokeMethodAsync('SetGlobalPaneCapacityAsync', capacity);

            // Measured per owner, because two layouts on one page do not have the
            // same room: the shell's knowledge panel may take the window, while a
            // split nested inside it may only take what its own box has left.
            const layout = backlogLayoutForKey(key);
            if (layout) owner.invokeMethodAsync('SetSidePaneMaxWidthAsync', backlogPaneMaxRem(layout));
        }
    }

    window.backlogPaneResizer = {
        /**
         * @param owner the .NET object to report settled widths to.
         * @param key the layout's `data-pane-owner`, or omitted for the app's own
         *        layout — which carries no attribute because it predates the key.
         */
        initialize(owner, key) {
            backlogPaneOwners.set(key ?? '', owner);
            backlogReportPaneBounds();
        },
        /**
         * Re-measure now, without waiting for a resize.
         *
         * The bounds are reported from the layout element, so while the shell is
         * showing a full-screen surface instead of the panes there is nothing to
         * measure and the resize handler above no-ops. A window resized during a
         * takeover would therefore be reported only at the *next* resize, leaving
         * the pane capacity describing a window that is gone. The host calls this
         * on the first render after the layout comes back, which measures the
         * window it actually returned to.
         */
        refresh() {
            backlogReportPaneBounds();
        },
        dispose(key) {
            backlogPaneOwners.delete(key ?? '');
        }
    };

    window.addEventListener('resize', backlogReportPaneBounds);

    document.addEventListener('pointerdown', (event) => {
        if (event.button !== 0) return;

        const handle = event.target instanceof Element ? event.target.closest('[data-pane-resizer]') : null;
        if (!handle) return;

        const layout = handle.closest(BACKLOG_PANE_LAYOUT_SELECTOR);
        if (!layout) return;

        // A layout with nobody listening is not this drag's to settle. Looked up
        // per layout rather than globally, which is what lets two resizable panes
        // share a document: the width goes to the component that drew this
        // separator, not to whichever one registered first.
        const owner = backlogPaneOwnerFor(layout);
        if (!owner) return;

        event.preventDefault();
        handle.focus();
        document.body.classList.add('is-resizing-pane');

        let width = backlogPaneWidthAt(layout, event.clientX);

        const onMove = (move) => {
            width = backlogPaneWidthAt(layout, move.clientX);
            // Both names are set so the app's knowledge layout and the library's
            // SplitPane each read the one their stylesheet knows.
            layout.style.setProperty('--knowledge-panel-width', `${width}rem`);
            layout.style.setProperty('--split-pane-fixed', `${width}rem`);
            handle.setAttribute('aria-valuenow', String(width));
        };

        const onUp = () => {
            document.removeEventListener('pointermove', onMove);
            document.removeEventListener('pointerup', onUp);
            document.removeEventListener('pointercancel', onUp);
            document.body.classList.remove('is-resizing-pane');
            owner.invokeMethodAsync('SetSidePaneWidthAsync', width);
        };

        document.addEventListener('pointermove', onMove);
        document.addEventListener('pointerup', onUp);
        document.addEventListener('pointercancel', onUp);
    });

    const backlogDiagramInstances = new Map();

    // The drawing libraries are large and are not needed until a diagram is on
    // screen, so they load from a CDN on demand rather than shipping with the
    // library. A host that must work offline sets window.backlogDiagramLibrarySources
    // before components.js runs, pointing each name at a local copy it serves
    // itself; the entries below are the fallback, tried in order.
    //
    // Earlier versions listed a '/vendor/...' path first. No host has ever served
    // that path, so every diagram cost a guaranteed 404 before reaching the CDN.
    // The hook is now opt-in and silent when unused.
    const backlogDiagramLibrarySources = {
        mermaid: ['https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs'],
        ...(window.backlogDiagramLibrarySources ?? {})
    };

    let backlogMermaidPromise;

    function backlogEscapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#39;');
    }

    async function backlogLoadScript(url) {
        if (document.querySelector(`script[data-backlog-diagram-src="${url}"]`)) return;

        await new Promise((resolve, reject) => {
            const script = document.createElement('script');
            script.src = url;
            script.async = true;
            script.dataset.backlogDiagramSrc = url;
            script.onload = resolve;
            script.onerror = () => reject(new Error(`Could not load ${url}`));
            document.head.appendChild(script);
        });
    }

    /**
     * Mermaid's palette, expressed in the product's tokens.
     *
     * Read off the live document rather than hard-coded, so a change to
     * components.css moves the diagrams with it and there is no second copy of
     * the palette to keep in step. Mermaid needs concrete colours — it cannot
     * take var() — so the values are resolved once, at initialize time.
     */
    function backlogMermaidTheme() {
        const styles = getComputedStyle(document.documentElement);
        const token = (name, fallback) => styles.getPropertyValue(name).trim() || fallback;

        const primary = token('--color-primary', '#F2C14E');
        const surface = token('--color-background-alt', '#202023');
        const raised = token('--color-background-raised', '#353539');
        const ink = token('--color-text-primary', '#F8F9FA');
        const inverse = token('--color-text-inverse', '#212529');
        const line = token('--color-border-strong', '#737379');

        return {
            darkMode: true,
            background: token('--color-background', '#121214'),
            fontFamily: token('--font-family-base', 'sans-serif'),

            // Nodes carry the brand colour rather than mermaid's washed lavender,
            // which is what made the diagrams recede into the page.
            primaryColor: primary,
            primaryTextColor: inverse,
            primaryBorderColor: token('--color-primary-dark', '#D4A72C'),

            secondaryColor: raised,
            secondaryTextColor: ink,
            secondaryBorderColor: line,
            tertiaryColor: surface,
            tertiaryTextColor: ink,
            tertiaryBorderColor: line,

            mainBkg: primary,
            secondBkg: raised,
            lineColor: line,
            textColor: ink,
            nodeBorder: token('--color-primary-dark', '#D4A72C'),
            clusterBkg: surface,
            clusterBorder: token('--color-border', '#545459'),
            titleColor: ink,
            edgeLabelBackground: surface,

            // Sequence diagrams name almost everything separately.
            actorBkg: primary,
            actorBorder: token('--color-primary-dark', '#D4A72C'),
            actorTextColor: inverse,
            actorLineColor: line,
            signalColor: ink,
            signalTextColor: ink,
            labelBoxBkgColor: raised,
            labelBoxBorderColor: line,
            labelTextColor: ink,
            loopTextColor: ink,
            noteBkgColor: token('--color-info', '#0A2C31'),
            noteTextColor: ink,
            noteBorderColor: token('--color-info', '#38BDF8'),
            sequenceNumberColor: inverse
        };
    }

    async function backlogLoadMermaid() {
        if (window.mermaid) return window.mermaid;
        if (!backlogMermaidPromise) {
            backlogMermaidPromise = (async () => {
                for (const source of backlogDiagramLibrarySources.mermaid) {
                    try {
                        const module = await import(source);
                        const mermaid = module.default ?? module.mermaid ?? window.mermaid;
                        if (mermaid) {
                            mermaid.initialize({
                                startOnLoad: false,
                                // 'base' plus themeVariables, not 'dark'. The dark
                                // theme is mermaid's own grey-and-lavender palette,
                                // which made every diagram look like a screenshot
                                // from another product dropped into the page. Base
                                // is the only theme that takes overrides.
                                theme: 'base',
                                themeVariables: backlogMermaidTheme(),
                                securityLevel: 'strict',
                                deterministicIds: true,
                                // We report parse failures ourselves through the
                                // source fallback; mermaid's own error graphic would
                                // be a second, uglier answer to the same question.
                                suppressErrorRendering: true
                            });
                            return mermaid;
                        }
                    } catch {
                        // Try the next source; the UI shows source fallback if every source fails.
                    }
                }

                throw new Error('Mermaid renderer unavailable.');
            })();
        }

        return backlogMermaidPromise;
    }

    // The message and nothing else. It used to end with "Source is available
    // below.", which was true while a diagram carried a source disclosure; there is
    // no disclosure now, and a fallback that points a reader at something that is
    // not there is worse than one that just says what went wrong.
    function backlogRenderDiagramError(element, message) {
        element.innerHTML = `<div class="diagram-view__fallback" role="note">${backlogEscapeHtml(message)}</div>`;
    }

    // mermaid.render() works in a scratch element it appends to <body>. It removes
    // that element on success, but on a parse error it draws its own "Syntax error"
    // graphic there and leaves it behind. Without this cleanup a half-typed diagram
    // in an entry parks a bomb icon at the bottom of the page, next to our own
    // fallback that already reported the error properly.
    //
    // The rendered SVG carries `${id}-svg` too, so anything still inside our own
    // container is the real diagram and must survive; only strays outside it go.
    function backlogRemoveMermaidScratchNodes(element, id) {
        for (const candidate of [`d${id}-svg`, `${id}-svg`]) {
            const node = document.getElementById(candidate);
            if (node && !element.contains(node)) {
                node.remove();
            }
        }
    }

    // GraphView's default renderer. It knows nothing about any particular graph:
    // nodes and edges in, a readable list out. A host that wants a real layout
    // registers its own function and points GraphView's JsFunction at it.
    function backlogRenderGenericGraph(element, id, data) {
        const nodes = Array.isArray(data?.nodes) ? data.nodes : [];
        const edges = Array.isArray(data?.edges) ? data.edges : [];

        backlogDiagramInstances.get(id)?.destroy?.();
        backlogDiagramInstances.delete(id);

        if (nodes.length === 0) {
            element.innerHTML = '<p class="tech-graph__status" role="status">No graph nodes are available.</p>';
            return;
        }

        const labelOf = (node) => backlogEscapeHtml(node?.label ?? node?.name ?? node?.id ?? '');
        const nodeMarkup = nodes
            .map((node) => `<li class="diagram-view__node">${labelOf(node)}</li>`)
            .join('');
        const edgeMarkup = edges
            .map((edge) => {
                const from = backlogEscapeHtml(edge?.source ?? edge?.from ?? '');
                const to = backlogEscapeHtml(edge?.target ?? edge?.to ?? '');
                return `<li>${from} &rarr; ${to}</li>`;
            })
            .join('');

        element.innerHTML =
            `<ul class="diagram-view__nodes">${nodeMarkup}</ul>` +
            (edgeMarkup ? `<ul class="diagram-view__nodes">${edgeMarkup}</ul>` : '');
    }

    /*
        Graph explorer: a switchable, zoomable, pannable view over node/edge data.

        Everything it knows arrives as data. It draws groups, it does not decide
        what a group means: the caller names the views, orders the groups and
        picks the colours, and the three layouts below only say where boxes go.
        `lanes` is a column per group, `spine` is a central column of group nodes
        with their members branching off it, and `cluster` scatters each group
        around an anchor bubble and draws the edges between them.
    */
    const BACKLOG_GRAPH_ZOOM_LEVELS = [0.5, 0.67, 0.85, 1, 1.25, 1.5];
    const BACKLOG_GRAPH_DEFAULT_ZOOM_INDEX = 3;
    // Anchor positions for `cluster`, in percent of the map. A caller with more
    // groups than anchors wraps around them, which is why they are spread out.
    const BACKLOG_GRAPH_CLUSTER_ANCHORS = [
        { x: 23, y: 30 },
        { x: 56, y: 24 },
        { x: 79, y: 54 },
        { x: 50, y: 70 },
        { x: 25, y: 70 },
        { x: 44, y: 45 }
    ];

    function backlogGraphElement(tagName, className, text) {
        const element = document.createElement(tagName);
        if (className) element.className = className;
        if (text !== undefined) element.textContent = text;
        return element;
    }

    // Statuses are the caller's words, so they can be anything; a class name
    // cannot. Only the modifier is slugged — the visible text stays as given.
    function backlogGraphSlug(value) {
        const slug = String(value ?? '').trim().toLowerCase().replace(/[^a-z0-9]+/g, '-').replace(/^-+|-+$/g, '');
        return slug || 'unknown';
    }

    function backlogRenderGraphExplorer(element, id, model) {
        const nodes = Array.isArray(model?.nodes) ? model.nodes : [];
        const edges = Array.isArray(model?.edges) ? model.edges : [];

        backlogDiagramInstances.get(id)?.destroy?.();
        backlogDiagramInstances.delete(id);

        if (nodes.length === 0) {
            const message = model?.emptyMessage ?? 'No graph nodes are available.';
            element.innerHTML = `<p class="tech-graph__status" role="status">${backlogEscapeHtml(message)}</p>`;
            return;
        }

        element.replaceChildren();

        const itemNoun = model?.itemNoun ?? 'items';
        const noSummaryText = model?.noSummaryText ?? 'No summary yet.';
        const selectionHint = model?.selectionHint ?? 'Select a card to spotlight direct neighbours.';
        const statusColors = model?.statusColors ?? {};
        const defaultStatusColor = model?.defaultStatusColor ?? null;
        const statusColor = (status) => statusColors[String(status ?? '').trim().toLowerCase()] ?? defaultStatusColor;

        const nodeById = new Map(nodes.map((node) => [node.id, node]));
        const incoming = new Map();
        const outgoing = new Map();
        for (const node of nodes) {
            incoming.set(node.id, []);
            outgoing.set(node.id, []);
        }

        for (const edge of edges) {
            if (!incoming.has(edge.source) || !outgoing.has(edge.target)) continue;
            incoming.get(edge.source).push(edge.target);
            outgoing.get(edge.target).push(edge.source);
        }

        // A caller that says nothing about views still gets one: every node in a
        // single lane, which is the least surprising thing to draw.
        const views = Array.isArray(model?.views) && model.views.length > 0
            ? model.views
            : [{ id: 'all', label: 'All', layout: 'lanes', groups: [{ key: 'all', label: 'All', nodeIds: nodes.map((node) => node.id) }] }];

        const groupsOf = (view) => (Array.isArray(view?.groups) ? view.groups : []).map((group) => ({
            ...group,
            nodes: (Array.isArray(group.nodeIds) ? group.nodeIds : []).map((nodeId) => nodeById.get(nodeId)).filter(Boolean)
        }));

        const visualizer = backlogGraphElement('div', 'graph-explorer');
        const toolbar = backlogGraphElement('div', 'graph-explorer__toolbar');
        const tabs = backlogGraphElement('div', 'graph-explorer__tabs');
        tabs.setAttribute('role', 'tablist');
        tabs.setAttribute('aria-label', model?.viewsLabel ?? 'Graph views');
        let zoomIndex = BACKLOG_GRAPH_DEFAULT_ZOOM_INDEX;

        const legend = backlogGraphElement('div', 'graph-explorer__legend');
        for (const item of Array.isArray(model?.legend) ? model.legend : []) {
            const swatch = backlogGraphElement('span', `graph-explorer__legend-item graph-explorer__legend-item--${backlogGraphSlug(item.key)}`);
            swatch.textContent = item.label ?? item.key ?? '';
            const color = item.color ?? statusColor(item.key);
            if (color) swatch.style.setProperty('--graph-explorer-legend-color', color);
            legend.appendChild(swatch);
        }

        let activeView = views.some((view) => view.id === model?.defaultViewId) ? model.defaultViewId : views[0].id;
        let selectedId = null;

        const hint = backlogGraphElement('p', 'graph-explorer__hint');
        const viewport = backlogGraphElement('div', 'graph-explorer__viewport');
        const content = backlogGraphElement('div', 'graph-explorer__content');
        const zoomControls = backlogGraphElement('div', 'graph-explorer__zoom');
        zoomControls.setAttribute('aria-label', model?.zoomLabel ?? 'Zoom graph');
        viewport.appendChild(content);
        toolbar.append(tabs, zoomControls, legend);
        visualizer.append(toolbar, hint, viewport);
        element.appendChild(visualizer);

        const listeners = [];
        const viewListeners = [];
        const cardById = new Map();
        const relatedIds = () => new Set([
            selectedId,
            ...(incoming.get(selectedId) ?? []),
            ...(outgoing.get(selectedId) ?? [])
        ].filter(Boolean));

        const applySelectionState = () => {
            const highlighted = selectedId ? relatedIds() : null;
            for (const [cardId, card] of cardById) {
                card.classList.toggle('graph-explorer__card--selected', cardId === selectedId);
                card.classList.toggle('graph-explorer__card--muted', Boolean(highlighted) && !highlighted.has(cardId));
                card.setAttribute('aria-pressed', cardId === selectedId ? 'true' : 'false');
            }
        };

        const applySelection = (nodeId) => {
            selectedId = selectedId === nodeId ? null : nodeId;
            applySelectionState();
        };

        const relationText = (node) => {
            const dependencies = typeof node.dependencies === 'number' ? node.dependencies : (incoming.get(node.id) ?? []).length;
            const dependents = typeof node.dependents === 'number' ? node.dependents : (outgoing.get(node.id) ?? []).length;
            return `${dependencies} dependencies / ${dependents} dependents`;
        };

        const makeCard = (node, density = 'normal') => {
            const status = node.status ?? '';
            const card = backlogGraphElement('button', `graph-explorer__card graph-explorer__card--${backlogGraphSlug(status)} graph-explorer__card--${density}`);
            card.type = 'button';
            card.dataset.nodeId = node.id;
            const color = statusColor(status);
            if (color) card.style.setProperty('--graph-explorer-status-color', color);
            card.title = node.description || node.label;
            card.setAttribute('aria-pressed', node.id === selectedId ? 'true' : 'false');

            const title = backlogGraphElement('span', 'graph-explorer__card-title', node.label);
            const meta = backlogGraphElement('span', 'graph-explorer__card-meta');
            if (node.kind) meta.appendChild(backlogGraphElement('span', 'graph-explorer__pill', node.kind));
            if (status) meta.appendChild(backlogGraphElement('span', 'graph-explorer__pill graph-explorer__pill--status', status));
            const summary = backlogGraphElement('span', 'graph-explorer__card-summary', node.description || noSummaryText);
            const relation = backlogGraphElement('span', 'graph-explorer__card-relations', relationText(node));
            card.append(title, meta, summary, relation);

            const onClick = () => applySelection(node.id);
            card.addEventListener('click', onClick);
            viewListeners.push(() => card.removeEventListener('click', onClick));
            cardById.set(node.id, card);
            return card;
        };

        const makeGroupHeader = (title, count) => {
            const header = backlogGraphElement('header', 'graph-explorer__lane-header');
            header.appendChild(backlogGraphElement('h4', null, title));
            header.appendChild(backlogGraphElement('span', 'graph-explorer__lane-count', String(count)));
            return header;
        };

        const renderLanesLayout = (view) => {
            const canvas = backlogGraphElement('div', 'graph-explorer__canvas graph-explorer__canvas--lanes');
            for (const group of groupsOf(view)) {
                const lane = backlogGraphElement('section', 'graph-explorer__lane');
                lane.setAttribute('aria-label', `${group.label} ${itemNoun}`);
                lane.appendChild(makeGroupHeader(group.label, group.nodes.length));
                for (const node of group.nodes) lane.appendChild(makeCard(node));
                canvas.appendChild(lane);
            }

            return canvas;
        };

        const renderSpineLayout = (view) => {
            const spine = backlogGraphElement('div', 'graph-explorer__spine');
            groupsOf(view).forEach((group, index) => {
                const section = backlogGraphElement('section', 'graph-explorer__spine-section');
                section.setAttribute('aria-label', group.label);
                const left = backlogGraphElement('div', 'graph-explorer__branch graph-explorer__branch--left');
                const center = backlogGraphElement('div', 'graph-explorer__area-node');
                const color = statusColor(group.nodes[0]?.status);
                if (color) center.style.setProperty('--graph-explorer-status-color', color);
                center.appendChild(backlogGraphElement('span', 'graph-explorer__area-index', String(index + 1)));
                center.appendChild(backlogGraphElement('strong', null, group.label));
                center.appendChild(backlogGraphElement('span', null, `${group.nodes.length} ${itemNoun}`));
                const right = backlogGraphElement('div', 'graph-explorer__branch graph-explorer__branch--right');

                group.nodes.forEach((node, nodeIndex) => {
                    const card = makeCard(node, 'compact');
                    const branch = nodeIndex % 2 === 0 ? left : right;
                    branch.appendChild(card);
                });

                section.append(left, center, right);
                spine.appendChild(section);
            });

            return spine;
        };

        const makeClusterNode = (node, x, y) => {
            const status = node.status ?? '';
            const nodeButton = backlogGraphElement('button', `graph-explorer__cloud-node graph-explorer__card--${backlogGraphSlug(status)}`);
            nodeButton.type = 'button';
            nodeButton.dataset.nodeId = node.id;
            nodeButton.style.left = `${x}%`;
            nodeButton.style.top = `${y}%`;
            const color = statusColor(status);
            if (color) nodeButton.style.setProperty('--graph-explorer-status-color', color);
            nodeButton.title = `${node.label}: ${node.description || noSummaryText}`;
            nodeButton.setAttribute('aria-label', [node.label, status, node.kind].filter(Boolean).join(', '));
            nodeButton.setAttribute('aria-pressed', node.id === selectedId ? 'true' : 'false');
            nodeButton.appendChild(backlogGraphElement('span', null, node.label));

            const onClick = () => applySelection(node.id);
            nodeButton.addEventListener('click', onClick);
            viewListeners.push(() => nodeButton.removeEventListener('click', onClick));
            cardById.set(node.id, nodeButton);
            return nodeButton;
        };

        const renderClusterLayout = (view) => {
            const cloud = backlogGraphElement('div', 'graph-explorer__cloud-map');
            cloud.setAttribute('role', 'group');
            cloud.setAttribute('aria-label', view.ariaLabel ?? 'Clustered map with links. Drag the map to pan, or focus it and use arrow keys.');
            cloud.tabIndex = 0;
            const scene = backlogGraphElement('div', 'graph-explorer__cloud-scene');
            const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
            svg.setAttribute('class', 'graph-explorer__cloud-links');
            svg.setAttribute('viewBox', '0 0 100 100');
            svg.setAttribute('preserveAspectRatio', 'none');
            const nodeLayer = backlogGraphElement('div', 'graph-explorer__cloud-nodes');
            const pan = { x: 0, y: 0, dragging: false, pointerId: null, startX: 0, startY: 0, originX: 0, originY: 0 };
            const applyCloudPan = () => {
                scene.style.transform = `translate(${pan.x}px, ${pan.y}px)`;
            };
            const moveCloudPan = (deltaX, deltaY) => {
                pan.x += deltaX;
                pan.y += deltaY;
                applyCloudPan();
            };
            const groups = groupsOf(view);
            const anchors = Array.isArray(view.anchors) && view.anchors.length > 0 ? view.anchors : BACKLOG_GRAPH_CLUSTER_ANCHORS;
            const positions = new Map();

            const addLine = (className, x1, y1, x2, y2) => {
                const line = document.createElementNS('http://www.w3.org/2000/svg', 'line');
                line.setAttribute('class', className);
                line.setAttribute('x1', x1.toFixed(2));
                line.setAttribute('y1', y1.toFixed(2));
                line.setAttribute('x2', x2.toFixed(2));
                line.setAttribute('y2', y2.toFixed(2));
                svg.appendChild(line);
            };

            groups.forEach((group, groupIndex) => {
                const center = anchors[groupIndex % anchors.length];
                const hub = backlogGraphElement('div', 'graph-explorer__cloud-hub');
                hub.style.left = `${center.x}%`;
                hub.style.top = `${center.y}%`;
                hub.textContent = group.label;
                nodeLayer.appendChild(hub);

                group.nodes.forEach((node, nodeIndex) => {
                    const angle = (Math.PI * 2 * nodeIndex / Math.max(group.nodes.length, 1)) + (groupIndex * 0.45);
                    const radius = 9 + Math.min(12, group.nodes.length * 0.42) + ((nodeIndex % 3) * 2.2);
                    const x = Math.min(96, Math.max(4, center.x + Math.cos(angle) * radius));
                    const y = Math.min(94, Math.max(6, center.y + Math.sin(angle) * radius));
                    positions.set(node.id, { x, y });
                    addLine('graph-explorer__cloud-link graph-explorer__cloud-link--spoke', center.x, center.y, x, y);
                    nodeLayer.appendChild(makeClusterNode(node, x, y));
                });
            });

            for (const edge of edges) {
                const source = positions.get(edge.source);
                const target = positions.get(edge.target);
                if (!source || !target) continue;
                addLine('graph-explorer__cloud-link graph-explorer__cloud-link--dependency', source.x, source.y, target.x, target.y);
            }

            const onPointerDown = (event) => {
                if (event.button !== 0 || event.target.closest('.graph-explorer__cloud-node')) return;
                pan.dragging = true;
                pan.pointerId = event.pointerId;
                pan.startX = event.clientX;
                pan.startY = event.clientY;
                pan.originX = pan.x;
                pan.originY = pan.y;
                cloud.classList.add('graph-explorer__cloud-map--dragging');
                cloud.setPointerCapture(event.pointerId);
                event.preventDefault();
            };
            const onPointerMove = (event) => {
                if (!pan.dragging || event.pointerId !== pan.pointerId) return;
                pan.x = pan.originX + event.clientX - pan.startX;
                pan.y = pan.originY + event.clientY - pan.startY;
                applyCloudPan();
            };
            const endDrag = (event) => {
                if (!pan.dragging || event.pointerId !== pan.pointerId) return;
                pan.dragging = false;
                pan.pointerId = null;
                cloud.classList.remove('graph-explorer__cloud-map--dragging');
                if (cloud.hasPointerCapture(event.pointerId)) cloud.releasePointerCapture(event.pointerId);
            };
            const onKeyDown = (event) => {
                const panStep = event.shiftKey ? 72 : 24;
                if (event.key === 'ArrowLeft') moveCloudPan(panStep, 0);
                else if (event.key === 'ArrowRight') moveCloudPan(-panStep, 0);
                else if (event.key === 'ArrowUp') moveCloudPan(0, panStep);
                else if (event.key === 'ArrowDown') moveCloudPan(0, -panStep);
                else if (event.key === 'Home') {
                    pan.x = 0;
                    pan.y = 0;
                    applyCloudPan();
                }
                else return;
                event.preventDefault();
            };
            for (const [eventName, handler] of [['pointerdown', onPointerDown], ['pointermove', onPointerMove], ['pointerup', endDrag], ['pointercancel', endDrag], ['keydown', onKeyDown]]) {
                cloud.addEventListener(eventName, handler);
                viewListeners.push(() => cloud.removeEventListener(eventName, handler));
            }

            scene.append(svg, nodeLayer);
            cloud.appendChild(scene);
            return cloud;
        };

        const removeCardListeners = () => {
            while (viewListeners.length > 0) viewListeners.pop()?.();
        };

        const applyZoomState = () => {
            const zoom = BACKLOG_GRAPH_ZOOM_LEVELS[zoomIndex];
            content.style.transform = `scale(${zoom})`;
            content.style.transformOrigin = 'top left';
            content.style.marginRight = `${Math.round((zoom - 1) * 100)}%`;
            content.style.marginBottom = `${Math.round((zoom - 1) * 100)}%`;
            for (const button of zoomControls.querySelectorAll('[data-zoom-action]')) {
                button.disabled = (button.dataset.zoomAction === 'out' && zoomIndex === 0) || (button.dataset.zoomAction === 'in' && zoomIndex === BACKLOG_GRAPH_ZOOM_LEVELS.length - 1);
            }
            const value = zoomControls.querySelector('.graph-explorer__zoom-value');
            if (value) value.textContent = `${Math.round(zoom * 100)}%`;
        };

        const changeZoom = (action) => {
            if (action === 'out') zoomIndex = Math.max(0, zoomIndex - 1);
            if (action === 'in') zoomIndex = Math.min(BACKLOG_GRAPH_ZOOM_LEVELS.length - 1, zoomIndex + 1);
            if (action === 'reset') zoomIndex = BACKLOG_GRAPH_DEFAULT_ZOOM_INDEX;
            applyZoomState();
        };

        const renderActiveView = () => {
            removeCardListeners();
            cardById.clear();
            content.replaceChildren();
            const definition = views.find((view) => view.id === activeView) ?? views[0];
            hint.textContent = [definition.hint, selectionHint].filter(Boolean).join(' ');
            content.id = `${id}-${definition.id}-view`;
            content.dataset.view = definition.id;
            content.dataset.layout = definition.layout ?? 'lanes';
            content.appendChild(
                definition.layout === 'cluster'
                    ? renderClusterLayout(definition)
                    : definition.layout === 'spine'
                        ? renderSpineLayout(definition)
                        : renderLanesLayout(definition)
            );
            applySelectionState();
            applyZoomState();
        };

        const zoomOut = backlogGraphElement('button', 'graph-explorer__zoom-button', '-');
        zoomOut.type = 'button';
        zoomOut.dataset.zoomAction = 'out';
        zoomOut.setAttribute('aria-label', 'Zoom out');
        const zoomValue = backlogGraphElement('span', 'graph-explorer__zoom-value', '100%');
        const zoomReset = backlogGraphElement('button', 'graph-explorer__zoom-button', 'Reset');
        zoomReset.type = 'button';
        zoomReset.dataset.zoomAction = 'reset';
        zoomReset.setAttribute('aria-label', 'Reset zoom');
        const zoomIn = backlogGraphElement('button', 'graph-explorer__zoom-button', '+');
        zoomIn.type = 'button';
        zoomIn.dataset.zoomAction = 'in';
        zoomIn.setAttribute('aria-label', 'Zoom in');
        zoomControls.append(zoomOut, zoomValue, zoomReset, zoomIn);
        for (const button of [zoomOut, zoomReset, zoomIn]) {
            const onClick = () => changeZoom(button.dataset.zoomAction);
            button.addEventListener('click', onClick);
            listeners.push(() => button.removeEventListener('click', onClick));
        }

        for (const view of views) {
            const tab = backlogGraphElement('button', 'graph-explorer__tab', view.label ?? view.id);
            tab.type = 'button';
            tab.dataset.view = view.id;
            tab.setAttribute('role', 'tab');
            tab.setAttribute('aria-controls', `${id}-${view.id}-view`);
            const onClick = () => {
                activeView = view.id;
                for (const button of tabs.querySelectorAll('.graph-explorer__tab')) {
                    const isSelected = button.dataset.view === activeView;
                    button.classList.toggle('graph-explorer__tab--active', isSelected);
                    button.setAttribute('aria-selected', isSelected ? 'true' : 'false');
                }
                renderActiveView();
            };
            tab.addEventListener('click', onClick);
            listeners.push(() => tab.removeEventListener('click', onClick));
            tabs.appendChild(tab);
        }

        const selectedTab = tabs.querySelector(`[data-view="${activeView}"]`);
        selectedTab?.classList.add('graph-explorer__tab--active');
        selectedTab?.setAttribute('aria-selected', 'true');
        for (const tab of tabs.querySelectorAll('.graph-explorer__tab:not(.graph-explorer__tab--active)')) tab.setAttribute('aria-selected', 'false');

        // Registered in the diagram registry so `backlogDiagrams.dispose` — the one
        // teardown call every diagram component makes — finds this too.
        backlogDiagramInstances.set(id, {
            destroy() {
                removeCardListeners();
                for (const remove of listeners) remove();
            }
        });

        renderActiveView();
    }


    /*
        The knowledge atlas: a graph drawn as a place rather than a chart.

        Nodes sit in three dimensions, clustered by group, and the picture is a
        perspective projection of that onto a canvas. Depth is the point — it is
        what lets sixty-odd nodes and a hundred and forty edges read as a shape
        instead of a hairball, and it is why the layout is 3D even though the
        surface is 2D.

        No WebGL and no graph library. Sixty nodes painted with gradients and
        quadratic curves is nothing for a 2D context, and a local-first desktop app
        should not need a CDN or a bundled engine to draw its own technology stack.
        The trade is real — no shaders, so the glow is a radial gradient — and it
        buys a renderer that cannot fail to load.

        The layout is deterministic. Every position comes from the node's ordinal
        and its group's index through a Fibonacci distribution, never from a random
        number, so the same graph draws the same picture twice. That is what makes
        a screenshot of it worth comparing.

        Selection is not decided here. A pick is reported to .NET and the highlight
        is set when .NET says so, because the sheet, the list and this canvas have
        to agree and only one of them can be the one that knows.
    */

    // Reference geometry. The radius curve is log so that the difference between
    // one dependent and four is visible, which is where most of the graph lives —
    // a linear scale spends its whole range on the two or three hubs.
    const BACKLOG_ATLAS_FOV = 50;
    const BACKLOG_ATLAS_MIN_RADIUS = 2.2;
    const BACKLOG_ATLAS_RADIUS_SPAN = 6.5;
    const BACKLOG_ATLAS_WORLD = 150;
    const BACKLOG_ATLAS_MIN_DISTANCE = 180;
    const BACKLOG_ATLAS_MAX_DISTANCE = 620;
    const BACKLOG_ATLAS_GOLDEN = Math.PI * (3 - Math.sqrt(5));

    // Every colour is read off the root at render time rather than written here.
    // The fallbacks are the token values as `.design/color-scheme.md` states them,
    // and exist only for a context with no stylesheet attached yet.
    const BACKLOG_ATLAS_TOKENS = {
        ready: ['--chart-ramp-1', '#6B5A2B'],
        draft: ['--chart-ramp-2', '#8C7433'],
        blocked: ['--chart-ramp-3', '#C39B3F'],
        active: ['--chart-ramp-4', '#F2C14E'],
        done: ['--chart-ramp-4', '#F2C14E'],
        archived: ['--chart-track', '#3A3527'],
        unknown: ['--chart-track', '#3A3527'],
        edge: ['--chart-grid', '#545459'],
        ink: ['--chart-ink', '#F8F9FA'],
        inkMuted: ['--chart-ink-muted', '#CED4DA'],
        surface: ['--chart-surface', '#202023'],
        focus: ['--color-border-focus', '#F2C14E']
    };

    function backlogAtlasPalette() {
        const styles = getComputedStyle(document.documentElement);
        const palette = {};

        for (const [name, pair] of Object.entries(BACKLOG_ATLAS_TOKENS)) {
            palette[name] = (styles.getPropertyValue(pair[0]) || '').trim() || pair[1];
        }

        return palette;
    }

    function backlogAtlasTone(palette, node) {
        return palette[node.toneSlug] ?? palette.unknown;
    }

    /*
        Points spread evenly over a sphere, by index.

        The golden angle is what makes this even without being regular: a lattice
        would put nodes in visible rows that mean nothing, and a random scatter
        would clump and would not survive a reload. Deterministic and even is
        exactly what a layout wants.
    */
    function backlogAtlasSpherePoint(index, count, radius) {
        if (count <= 1) return { x: 0, y: 0, z: 0 };

        const y = 1 - (index / (count - 1)) * 2;
        const ring = Math.sqrt(Math.max(0, 1 - y * y));
        const theta = BACKLOG_ATLAS_GOLDEN * index;

        return {
            x: Math.cos(theta) * ring * radius,
            y: y * radius,
            z: Math.sin(theta) * ring * radius
        };
    }

    /*
        Where each group sits, and how big a ball its members fill.

        Groups are placed on a sphere of their own so no cluster hides behind
        another, and a cluster's radius grows with the square root of its size so a
        layer with thirty technologies is denser than one with four rather than
        swallowing the map.

        Boundary nodes — chapters in other knowledge folders — go to the middle and
        to the back. They are not part of the stack; they are what it leans on from
        outside, and putting them behind everything says that without a legend.
    */
    function backlogAtlasClusters(model) {
        const groups = new Map();

        for (const node of model.nodes) {
            const key = node.group || 'Unassigned';
            if (!groups.has(key)) {
                groups.set(key, { key, label: key, index: node.groupIndex ?? groups.size, nodes: [] });
            }

            groups.get(key).nodes.push(node);
        }

        const ordered = [...groups.values()].sort((left, right) => left.index - right.index);
        const placed = ordered.filter((group) => group.index >= 0);

        for (const group of ordered) {
            group.radius = 26 + Math.sqrt(group.nodes.length) * 13;

            if (group.index < 0) {
                group.center = { x: 0, y: 0, z: -BACKLOG_ATLAS_WORLD * 1.15 };
                continue;
            }

            const seat = placed.indexOf(group);
            group.center = backlogAtlasSpherePoint(seat, Math.max(placed.length, 2), BACKLOG_ATLAS_WORLD);
        }

        return ordered;
    }

    function backlogAtlasLayout(model) {
        const clusters = backlogAtlasClusters(model);
        const maxInDegree = model.nodes.reduce((most, node) => Math.max(most, node.inDegree || 0), 0);
        const scale = Math.log1p(Math.max(maxInDegree, 1));
        const points = [];

        for (const cluster of clusters) {
            // Ordinal, not array position: document order is a fact about the
            // knowledge and survives a node being added above this one.
            const members = [...cluster.nodes].sort((left, right) => (left.ordinal ?? 0) - (right.ordinal ?? 0));

            members.forEach((node, seat) => {
                const offset = backlogAtlasSpherePoint(seat, Math.max(members.length, 2), cluster.radius);
                const radius = BACKLOG_ATLAS_MIN_RADIUS
                    + (scale > 0 ? Math.log1p(node.inDegree || 0) / scale : 0) * BACKLOG_ATLAS_RADIUS_SPAN;

                points.push({
                    node,
                    cluster,
                    radius,
                    x: cluster.center.x + offset.x,
                    y: cluster.center.y + offset.y,
                    z: cluster.center.z + offset.z
                });
            });
        }

        const byId = new Map(points.map((point) => [point.node.id, point]));
        const links = [];

        for (const edge of model.edges ?? []) {
            const from = byId.get(edge.source);
            const to = byId.get(edge.target);
            if (!from || !to) continue;

            // A curve, not a chord. Straight lines between sixty points in a ball
            // all cross the middle and the middle becomes a smear; bowing each one
            // outward keeps them readable as separate edges. The bow is derived
            // from the endpoints, so it never moves on its own.
            const midX = (from.x + to.x) / 2;
            const midY = (from.y + to.y) / 2;
            const midZ = (from.z + to.z) / 2;
            const span = Math.hypot(to.x - from.x, to.y - from.y, to.z - from.z);
            const lift = 1 + (span / (BACKLOG_ATLAS_WORLD * 5));

            links.push({
                from,
                to,
                control: { x: midX * lift, y: midY * lift, z: midZ * lift }
            });
        }

        return { clusters, points, byId, links };
    }

    function backlogAtlasProject(point, view) {
        const cosYaw = Math.cos(view.yaw);
        const sinYaw = Math.sin(view.yaw);
        const cosPitch = Math.cos(view.pitch);
        const sinPitch = Math.sin(view.pitch);

        const dx = point.x - view.target.x;
        const dy = point.y - view.target.y;
        const dz = point.z - view.target.z;

        const rx = dx * cosYaw - dz * sinYaw;
        const rz = dx * sinYaw + dz * cosYaw;
        const ry = dy * cosPitch - rz * sinPitch;
        const depth = dy * sinPitch + rz * cosPitch + view.distance;

        // Behind the eye, or on it. Reported rather than clamped: a caller that
        // drew it anyway would get a point mirrored through the origin.
        if (depth <= 1) return null;

        const k = view.focal / depth;
        return { x: view.width / 2 + rx * k, y: view.height / 2 - ry * k, depth, scale: k };
    }

    function backlogAtlasRender(element, id, model, dotnet) {
        backlogDiagramInstances.get(id)?.destroy?.();
        element.replaceChildren();

        const nodes = Array.isArray(model?.nodes) ? model.nodes : [];
        if (nodes.length === 0) {
            const empty = backlogGraphElement('p', 'graph-atlas__status', model?.emptyMessage ?? 'No atlas nodes are available.');
            empty.setAttribute('role', 'status');
            element.append(empty);
            return;
        }

        const canvas = document.createElement('canvas');
        canvas.className = 'graph-atlas__surface';
        // The picture is not the control. The list beside it is, so this is kept
        // off the accessibility tree entirely rather than given a label that would
        // announce sixty technologies as one unnavigable blob.
        canvas.setAttribute('aria-hidden', 'true');
        canvas.tabIndex = -1;
        element.append(canvas);

        const context = canvas.getContext('2d');
        if (!context) {
            const failed = backlogGraphElement('p', 'graph-atlas__status', 'The atlas could not be drawn here.');
            failed.setAttribute('role', 'status');
            element.append(failed);
            return;
        }

        const layout = backlogAtlasLayout(model);
        let palette = backlogAtlasPalette();
        const reduceMotion = window.matchMedia ? window.matchMedia('(prefers-reduced-motion: reduce)') : null;

        const view = {
            yaw: 0.6,
            pitch: 0.25,
            distance: 420,
            target: { x: 0, y: 0, z: 0 },
            desired: { x: 0, y: 0, z: 0 },
            width: 0,
            height: 0,
            focal: 0
        };

        let selectedId = null;
        let hoveredId = null;
        let neighbours = new Set();
        let frame = 0;
        let painted = [];

        const adjacency = new Map();
        for (const link of layout.links) {
            if (!adjacency.has(link.from.node.id)) adjacency.set(link.from.node.id, new Set());
            if (!adjacency.has(link.to.node.id)) adjacency.set(link.to.node.id, new Set());
            adjacency.get(link.from.node.id).add(link.to.node.id);
            adjacency.get(link.to.node.id).add(link.from.node.id);
        }

        function resize() {
            const ratio = window.devicePixelRatio || 1;
            const box = canvas.getBoundingClientRect();
            const width = Math.max(1, Math.round(box.width));
            const height = Math.max(1, Math.round(box.height));

            // The backing store is sized in device pixels and the context scaled to
            // match, or the whole atlas is soft at the 125% and 150% Windows uses by
            // default — which is most of the machines this runs on.
            canvas.width = Math.round(width * ratio);
            canvas.height = Math.round(height * ratio);
            context.setTransform(ratio, 0, 0, ratio, 0, 0);

            view.width = width;
            view.height = height;
            view.focal = (height / 2) / Math.tan((BACKLOG_ATLAS_FOV * Math.PI / 180) / 2);
            schedule();
        }

        function schedule() {
            if (frame) return;
            frame = requestAnimationFrame(() => {
                frame = 0;
                draw();
            });
        }

        function settleTarget() {
            const dx = view.desired.x - view.target.x;
            const dy = view.desired.y - view.target.y;
            const dz = view.desired.z - view.target.z;

            if (Math.hypot(dx, dy, dz) < 0.35) {
                view.target.x = view.desired.x;
                view.target.y = view.desired.y;
                view.target.z = view.desired.z;
                return false;
            }

            // Reduced motion moves the camera without animating it. The rule is that
            // the motion goes, not that the function does — the reader still arrives.
            if (reduceMotion && reduceMotion.matches) {
                view.target.x = view.desired.x;
                view.target.y = view.desired.y;
                view.target.z = view.desired.z;
                return false;
            }

            view.target.x += dx * 0.14;
            view.target.y += dy * 0.14;
            view.target.z += dz * 0.14;
            return true;
        }

        // A node's radius is a world quantity, so it is projected like every other
        // world quantity — scale already carries the perspective divide. Applying a
        // second constant factor on top of it, as an early draft did, paints every
        // node hundreds of pixels across and the map is one flat blob.
        function nodeSize(point) {
            return Math.max(1.6, point.radius * point.projected.scale);
        }

        function draw() {
            const moving = settleTarget();
            context.clearRect(0, 0, view.width, view.height);

            for (const point of layout.points) {
                point.projected = backlogAtlasProject(point, view);
            }

            drawLinks();

            painted = layout.points
                .filter((point) => point.projected)
                .sort((left, right) => right.projected.depth - left.projected.depth);

            for (const point of painted) {
                drawNode(point);
            }

            drawLabels();

            if (moving) schedule();
        }

        function emphasis(nodeId) {
            if (!selectedId) return 1;
            if (nodeId === selectedId) return 1;
            return neighbours.has(nodeId) ? 0.72 : 0.16;
        }

        function drawLinks() {
            context.lineCap = 'round';

            for (const link of layout.links) {
                const from = link.from.projected;
                const to = link.to.projected;
                if (!from || !to) continue;

                const control = backlogAtlasProject(link.control, view);
                if (!control) continue;

                const lit = selectedId
                    && (link.from.node.id === selectedId || link.to.node.id === selectedId);
                const strength = selectedId ? (lit ? 0.85 : 0.07) : 0.26;

                context.globalAlpha = strength;
                context.strokeStyle = lit ? palette.focus : palette.edge;
                context.lineWidth = Math.max(0.5, (lit ? 2.2 : 1.2) * ((from.scale + to.scale) / 2));
                context.beginPath();
                context.moveTo(from.x, from.y);
                context.quadraticCurveTo(control.x, control.y, to.x, to.y);
                context.stroke();
            }

            context.globalAlpha = 1;
        }

        function drawNode(point) {
            const projected = point.projected;
            const size = nodeSize(point);
            const tone = backlogAtlasTone(palette, point.node);
            const alpha = emphasis(point.node.id);
            const selected = point.node.id === selectedId;
            const hovered = point.node.id === hoveredId;

            // The glow is what gives a flat context depth: a node near the eye
            // spills further than one behind it, so the eye reads the order before
            // it reads the sizes.
            const halo = context.createRadialGradient(projected.x, projected.y, size * 0.2, projected.x, projected.y, size * 2.6);
            halo.addColorStop(0, tone);
            halo.addColorStop(1, 'transparent');
            // Hover lifts the glow and nothing else — enough to say "this is the one
            // under the pointer", not enough to be mistaken for a state.
            context.globalAlpha = alpha * (selected ? 0.5 : hovered ? 0.34 : 0.22);
            context.fillStyle = halo;
            context.beginPath();
            context.arc(projected.x, projected.y, size * 2.6, 0, Math.PI * 2);
            context.fill();

            context.globalAlpha = alpha;

            // Shape carries what one hue cannot. `hold` is a square and a retired or
            // unrecognised status is a hollow ring, so the ladder stays legible
            // without relying on where a tone sits on the ramp.
            const slug = point.node.toneSlug;
            context.fillStyle = tone;
            context.strokeStyle = tone;
            context.lineWidth = Math.max(1, size * 0.34);
            context.beginPath();

            if (slug === 'blocked') {
                context.rect(projected.x - size, projected.y - size, size * 2, size * 2);
                context.fill();
            } else if (slug === 'archived' || !slug) {
                context.arc(projected.x, projected.y, size, 0, Math.PI * 2);
                context.stroke();
            } else {
                context.arc(projected.x, projected.y, size, 0, Math.PI * 2);
                context.fill();
            }

            // The ring is what selection looks like, and only selection. Hover wore
            // the same ring, so moving the pointer across the map read as picking
            // everything it passed over — and the one node that actually was
            // selected stopped standing out.
            if (selected) {
                context.globalAlpha = 1;
                context.strokeStyle = palette.focus;
                context.lineWidth = Math.max(1.2, size * 0.24);
                context.beginPath();
                context.arc(projected.x, projected.y, size * 1.9, 0, Math.PI * 2);
                context.stroke();
            }

            context.globalAlpha = 1;
        }

        /*
            Labels for the few nodes big enough to earn one, plus whatever is
            selected or hovered.

            Labelling everything is how a graph becomes unreadable — sixty
            overlapping words say less than none. The threshold is on the projected
            size, so what is named changes as the reader moves closer, which is the
            behaviour a map has.
        */
        function drawLabels() {
            const font = (getComputedStyle(document.documentElement).getPropertyValue('--font-family-base') || '').trim()
                || 'system-ui, sans-serif';
            const drawn = [];

            for (let index = painted.length - 1; index >= 0; index--) {
                const point = painted[index];
                const projected = point.projected;
                const size = nodeSize(point);
                const selected = point.node.id === selectedId;
                const hovered = point.node.id === hoveredId;

                if (!selected && !hovered && size < 7) continue;
                if (selectedId && !selected && !neighbours.has(point.node.id)) continue;

                const y = projected.y - size - 7;
                let clash = false;

                for (const seat of drawn) {
                    if (Math.abs(seat.y - y) < 13 && Math.abs(seat.x - projected.x) < 78) {
                        clash = true;
                        break;
                    }
                }

                if (clash) continue;
                drawn.push({ x: projected.x, y });

                context.font = (selected ? '600 13px ' : '400 12px ') + font;
                context.textAlign = 'center';
                context.textBaseline = 'bottom';

                // A pill behind the word rather than a stroke around it: a wash of
                // the surface colour reads as the label sitting on the map, and an
                // outline reads as a mistake.
                const width = context.measureText(point.node.label).width;
                context.fillStyle = palette.surface;
                context.globalAlpha = selected ? 0.92 : 0.72;
                context.beginPath();
                context.roundRect(projected.x - width / 2 - 6, y - 15, width + 12, 18, 4);
                context.fill();

                context.globalAlpha = 1;
                context.fillStyle = selected ? palette.ink : palette.inkMuted;
                context.fillText(point.node.label, projected.x, y);
            }

            context.globalAlpha = 1;
        }

        function pick(clientX, clientY) {
            const box = canvas.getBoundingClientRect();
            const x = clientX - box.left;
            const y = clientY - box.top;

            // `painted` runs far to near, so the last match is the one nearest the
            // eye — which is the one the reader believes they clicked.
            let hit = null;

            for (const point of painted) {
                if (!point.projected) continue;

                // Generously: the drawn node is small, and a pointer target that
                // matches the ink exactly is one nobody can hit.
                const reach = Math.max(nodeSize(point) * 1.8, 11);
                if (Math.hypot(point.projected.x - x, point.projected.y - y) <= reach) hit = point;
            }

            return hit;
        }

        function applySelection(nodeId) {
            selectedId = nodeId || null;
            neighbours = selectedId ? (adjacency.get(selectedId) ?? new Set()) : new Set();

            const point = selectedId ? layout.byId.get(selectedId) : null;
            view.desired = point ? { x: point.x, y: point.y, z: point.z } : { x: 0, y: 0, z: 0 };
            schedule();
        }

        const listeners = [];

        function on(target, type, handler, options) {
            target.addEventListener(type, handler, options);
            listeners.push(() => target.removeEventListener(type, handler, options));
        }

        let dragging = false;
        let dragged = false;
        let lastX = 0;
        let lastY = 0;

        on(canvas, 'pointerdown', (event) => {
            if (event.button !== 0) return;
            dragging = true;
            dragged = false;
            lastX = event.clientX;
            lastY = event.clientY;
            canvas.setPointerCapture(event.pointerId);
            canvas.classList.add('graph-atlas__surface--dragging');
        });

        on(canvas, 'pointermove', (event) => {
            if (!dragging) {
                const hit = pick(event.clientX, event.clientY);
                const next = hit ? hit.node.id : null;

                // Only on an actual change. Repainting per pointermove is how a
                // cheap scene becomes an expensive one.
                if (next !== hoveredId) {
                    hoveredId = next;
                    canvas.style.cursor = next ? 'pointer' : '';
                    schedule();
                }

                return;
            }

            const dx = event.clientX - lastX;
            const dy = event.clientY - lastY;
            if (Math.abs(dx) + Math.abs(dy) > 3) dragged = true;
            lastX = event.clientX;
            lastY = event.clientY;

            view.yaw += dx * 0.006;
            // Stopped short of the poles, where the projection degenerates and the
            // map appears to flip.
            view.pitch = Math.max(-1.35, Math.min(1.35, view.pitch + dy * 0.006));
            schedule();
        });

        function endDrag(event) {
            if (!dragging) return;
            dragging = false;
            canvas.classList.remove('graph-atlas__surface--dragging');
            if (canvas.hasPointerCapture && canvas.hasPointerCapture(event.pointerId)) {
                canvas.releasePointerCapture(event.pointerId);
            }
        }

        on(canvas, 'pointerup', (event) => {
            const wasDragging = dragging;
            const moved = dragged;
            endDrag(event);

            if (!wasDragging || moved) return;

            const hit = pick(event.clientX, event.clientY);
            // Clicking the selected node clears it, which is how a reader closes the
            // sheet without going looking for the button.
            const next = hit ? (hit.node.id === selectedId ? null : hit.node.id) : null;

            if (dotnet && dotnet.invokeMethodAsync) dotnet.invokeMethodAsync('NodePicked', next);
        });

        on(canvas, 'pointercancel', endDrag);

        /*
            Zoom is a ratio, not a subtraction.

            A fixed step in world units moves the camera by a constant distance,
            which is a huge jump when you are already close and barely anything when
            you are far out — so the same notch of the wheel does two different
            things depending on where you happen to be. Multiplying keeps every
            notch the same *apparent* amount of movement.

            The delta is normalised first because a wheel reports in three different
            units: pixels, lines and pages. Trusting deltaY raw makes a mouse that
            reports lines zoom about forty times slower than a trackpad.
        */
        on(canvas, 'wheel', (event) => {
            event.preventDefault();

            const unit = event.deltaMode === 1 ? 16 : event.deltaMode === 2 ? view.height : 1;
            // Clamped so one flick of a high-resolution trackpad cannot cross the
            // whole range in a single event.
            const notches = Math.max(-4, Math.min(4, (event.deltaY * unit) / 100));

            view.distance = Math.max(
                BACKLOG_ATLAS_MIN_DISTANCE,
                Math.min(BACKLOG_ATLAS_MAX_DISTANCE, view.distance * Math.pow(1.18, notches)));
            schedule();
        }, { passive: false });

        // WebView2 drops the drawing context on resume and on a display change, with
        // no error anywhere. Telling .NET is what gets the model handed over again.
        on(canvas, 'contextlost', (event) => {
            event.preventDefault();
            if (dotnet && dotnet.invokeMethodAsync) dotnet.invokeMethodAsync('RendererLost');
        });

        const observer = new ResizeObserver(() => resize());
        observer.observe(canvas);
        listeners.push(() => observer.disconnect());

        // The palette is read off the root, so a stylesheet arriving late would
        // otherwise leave the picture painted in the fallbacks until something asked
        // it to look again.
        const repaint = () => {
            palette = backlogAtlasPalette();
            schedule();
        };

        on(window, 'focus', repaint);

        backlogDiagramInstances.set(id, {
            select(nodeId) {
                applySelection(nodeId);
            },
            destroy() {
                if (frame) cancelAnimationFrame(frame);
                frame = 0;
                for (const remove of listeners) remove();
                listeners.length = 0;
            }
        });

        resize();
    }

    window.backlogGraphAtlas = {
        render(element, id, model, dotnet) {
            backlogAtlasRender(element, id, model, dotnet);
        },
        select(id, nodeId) {
            const instance = backlogDiagramInstances.get(id);
            if (instance && instance.select) instance.select(nodeId || null);
        }
    };

    window.backlogGraphExplorer = {
        render(element, id, model) {
            backlogRenderGraphExplorer(element, id, model);
        },

        dispose(id) {
            const instance = backlogDiagramInstances.get(id);
            instance?.destroy?.();
            backlogDiagramInstances.delete(id);
        }
    };

    /*
        Gives an artifact frame the height of the artifact inside it.

        The frame cannot work this out for itself and neither can its stylesheet:
        an Archify document is a single `width: 100%` SVG over its own viewBox, so
        its height is a function of the frame's width, and it differs per diagram -
        1440x700 for one runtime view, 1200x2458 for a building block view. A fixed
        height fits neither, and whatever did not fit used to be simply gone.

        So the measurement is made where the answer is known and sent out. This is
        the receiving half.
    */
    const backlogWatchArtifactHeight = (element, id) => {
        const onMessage = (event) => {
            /*
                The window reference is the identity check, not the origin.
                `sandbox="allow-scripts"` without `allow-same-origin` puts the frame
                in an opaque origin, so `event.origin` arrives as the string "null"
                for every artifact frame on the page and can tell none of them
                apart. `event.source` can, and it is not forgeable by anything
                inside the frame.
            */
            if (event.source !== element.contentWindow) return;

            const message = event.data;
            if (!message || message.channel !== 'backlog-artifact-height' || message.id !== id) return;

            const height = Number(message.height);
            if (!Number.isFinite(height) || height <= 0) return;

            // Not while the frame is the whole screen. There the height is the
            // screen's and the browser owns it; writing a measured pixel value onto
            // a fullscreen element is how you get a diagram in a letterbox.
            if (document.fullscreenElement === element) return;

            // Bounded at both ends. The floor stops a frame that reports something
            // absurd from collapsing to a sliver, and the ceiling sits far above the
            // tallest artifact in this repository, so it only ever catches a
            // runaway rather than trimming a real diagram.
            element.style.height = `${Math.min(Math.max(Math.ceil(height), 120), 20000)}px`;
        };

        /*
            Fullscreen is a fact about the frame that the document inside cannot
            see, and it changes what `100dvh` means for it - the screen, rather than
            a height this host chose. So it is told, both ways.

            Listening on the document rather than the element because that is where
            `fullscreenchange` is dispatched, and checking identity rather than
            assuming: several artifact frames share this page and only one of them
            is ever the screen.
        */
        const onFullscreenChange = () => {
            const mine = document.fullscreenElement === element;

            // The inline height goes while fullscreen so the browser can size the
            // element, and comes back on exit from the frame's next measurement.
            if (mine) element.style.removeProperty('height');

            try {
                element.contentWindow?.postMessage(
                    { channel: 'backlog-artifact-fullscreen', on: mine },
                    '*');
            } catch {
                // A frame that has already gone is not an error worth reporting.
            }
        };

        window.addEventListener('message', onMessage);
        document.addEventListener('fullscreenchange', onFullscreenChange);

        return () => {
            window.removeEventListener('message', onMessage);
            document.removeEventListener('fullscreenchange', onFullscreenChange);
        };
    };

    // Merged, not assigned. app.js attaches its own renderers to this object, and
    // an outright assignment here would drop them if the load order ever flipped.
    window.backlogDiagrams = Object.assign(window.backlogDiagrams ?? {}, {
        // Exposed so a host's own renderer can register a teardown against the
        // same id `dispose` is called with, and reuse the loaders and escaping.
        instances: backlogDiagramInstances,
        escapeHtml: backlogEscapeHtml,
        renderError: backlogRenderDiagramError,

        async render(element, id, language, source) {
            const normalized = String(language ?? '').trim().toLowerCase();
            if (normalized !== 'mermaid' && normalized !== 'mmd') {
                backlogRenderDiagramError(element, `${language ?? 'Diagram'} rendering is not configured yet.`);
                return;
            }

            element.innerHTML = '<p class="diagram-view__status" role="status">Rendering Mermaid diagram...</p>';

            try {
                const mermaid = await backlogLoadMermaid();
                const result = await mermaid.render(`${id}-svg`, source);
                element.innerHTML = result.svg;
                result.bindFunctions?.(element);
            } catch (error) {
                backlogRenderDiagramError(element, error instanceof Error ? error.message : 'Mermaid rendering failed.');
            } finally {
                backlogRemoveMermaidScratchNodes(element, id);
            }
        },

        /*
            Shows a generated Archify artifact in place of a drawn mermaid diagram.

            `srcdoc` rather than `src`, because the artifact is not one of the app's
            own assets: it sits in whichever repository clone the reader configured,
            and the app reads it off disk. There is no URL to point at, in either
            host - the desktop is a WebView over local files and the harness serves
            its own wwwroot.

            `sandbox="allow-scripts"` is set in the markup, which puts the frame in
            an opaque origin. Archify's viewer survives that: every access it makes
            to localStorage is wrapped, naming sandboxed iframes as the reason.
            Scripts have to stay allowed or the document renders as an unstyled
            skeleton.

            The appended script is what `?theme=dark&embed=1` does for the storybook,
            which a srcdoc document cannot be given: the artifact resolves its theme
            before first paint from the query string, then from localStorage, then
            from `prefers-color-scheme` - and with the first two unavailable here, a
            reader whose system prefers light would get a light diagram inside an app
            `.design/design-principles.md` makes dark-only. So the theme is pinned
            from out here, through the same `data-theme` attribute the artifact
            publishes, and re-pinned if anything inside changes it. Nothing in the
            generated file is touched, so a regeneration cannot undo this.

            The cost is that the artifact's own theme toggle does nothing in-app,
            so it is the one control hidden from the viewer below.

            The other thing the frame has to be told is how tall to be. An artifact
            is one `width: 100%` SVG over its own viewBox, so its height follows the
            frame's WIDTH - a number only the frame knows. So it reports it: the
            script below watches its own layout and posts the height out, and
            `backlogWatchArtifactHeight` writes it onto the frame. Without that the
            frame kept the fixed height its stylesheet gives it and everything past
            that was cut off, which for a portrait diagram like
            `05-building-block-view.2` (1200x2458) is most of the picture.
        */
        renderArtifact(element, id, html) {
            /*
                `matchMedia` is lied to, and that is the part that does the work.

                The artifact resolves its own theme in an inline script in its head:
                query string, then localStorage, then
                `matchMedia('(prefers-color-scheme: light)')`. The first two are
                unreachable in an opaque-origin sandbox, so on a machine set to
                light it used to resolve light, write `data-theme="light"`, and
                commit the light background — and the observer below then corrected
                the attribute a microtask later, which the artifact's own
                `transition: background 0.2s` turned into a visible light-to-dark
                fade of about 170ms.

                Correcting the answer after the fact cannot win that race; the
                resolver has to be given the right answer in the first place. So the
                frame's `prefers-color-scheme` is answered as dark before the
                artifact's script ever asks, and every other media query is passed
                through to the real implementation untouched.

                The observer stays, for the theme toggle inside the artifact and for
                anything else that writes the attribute later. `color-scheme` stays
                too, but for what it actually governs — the UA's canvas and
                scrollbars — rather than for the flash, which was never its doing.
            */
            const pin = `<script>(function(){try{var h=document.documentElement;`
                /*
                    Presentation mode, on and staying on. It is the reading mode:
                    the diagram takes the whole frame and the info cards step out of
                    the way. It used to be a button, and the button did nothing worth
                    seeing - present mode sizes the diagram to the viewport, and in a
                    frame this host has already sized to the content, that only moves
                    the same box around. Fullscreen is where it earns its keep, and
                    fullscreen is now a host control rather than an artifact one.
                */
                + `h.setAttribute('data-present','true');`
                /*
                    And the one thing the artifact cannot work out for itself:
                    whether this frame is currently the whole screen. It changes what
                    `100dvh` means - the screen, rather than a height the host chose -
                    so the host says so, and the stylesheet below keys off it.
                */
                + `addEventListener('message',function(e){try{`
                + `if(!e.data||e.data.channel!=='backlog-artifact-fullscreen')return;`
                + `if(e.data.on)h.setAttribute('data-host-fullscreen','true');`
                + `else h.removeAttribute('data-host-fullscreen');`
                + `if(typeof schedule==='function')schedule();`
                + `}catch(_){}});`
                + `var real=window.matchMedia&&window.matchMedia.bind(window);`
                + `if(real){window.matchMedia=function(q){var r=real(q);`
                + `if(typeof q==='string'&&q.indexOf('prefers-color-scheme')>=0){`
                + `var dark=q.indexOf('light')<0;`
                + `return{media:r.media,matches:dark,onchange:null,`
                + `addListener:function(){},removeListener:function(){},`
                + `addEventListener:function(){},removeEventListener:function(){},`
                + `dispatchEvent:function(){return false;}};}`
                + `return r;};}`
                + `var pin=function(){if(h.getAttribute('data-theme')!=='dark')h.setAttribute('data-theme','dark');};`
                + `pin();new MutationObserver(pin).observe(h,{attributes:true,attributeFilter:['data-theme']});`
                /*
                    And the height, measured off the root box rather than off
                    `scrollHeight`, which is the whole trick. `scrollHeight` never
                    reports less than the viewport, and here the viewport IS the
                    frame we are about to size - so a frame that started at 28rem
                    would report 28rem forever and could never shrink to fit a small
                    diagram. The root element's border box is the content's real
                    height and has no such floor.

                    It can feed back on itself, which is what the guard below is
                    for: the artifact trims its own padding on a short viewport, and
                    the viewport is the answer this host just gave.
                */
                + `var last=0,prev=0,settled=false;`
                + `var post=function(){try{if(settled)return;var b=document.body;`
                + `var m=Math.max(b?b.getBoundingClientRect().height:0,h.getBoundingClientRect().height);`
                + `if(!(m>0))return;m=Math.ceil(m);if(Math.abs(m-last)<2)return;`
                /*
                    Two-cycle guard, and it is what makes measuring a document that
                    is no longer in embed mode safe.

                    The artifact's stylesheet has `@media (max-height: ...)` rules
                    that trim its padding on a short viewport. Inside a frame this
                    host sizes from the content, the viewport IS the answer we just
                    gave - so a diagram whose height lands near 920px or 1100px can
                    ask for a taller frame, get trimmed by the media query, ask for
                    a shorter one, and flip between the two for ever. Seeing the
                    height from two reports ago come back is exactly that, and the
                    larger of the pair is the one that leaves nothing cut off.
                */
                + `if(Math.abs(m-prev)<2){settled=true;m=Math.max(m,last);}`
                + `prev=last;last=m;`
                + `parent.postMessage({channel:'backlog-artifact-height',id:'${id}',height:m},'*');`
                + `}catch(_){}};`
                /*
                    `setTimeout` rather than `requestAnimationFrame`, which is the
                    difference between this working and not. A cross-origin frame
                    the browser is not currently painting - one scrolled out of a
                    chapter, which on a page with six diagrams is most of them - has
                    its animation frames throttled to nothing, so a measurement
                    posted from a rAF callback never left. Layout is still computed
                    for such a frame, so a timer measures it perfectly well.
                */
                + `var schedule=function(){setTimeout(post,0);};`
                // The artifact settles in stages - fonts, then its own chrome layout
                // - so one measurement is not enough. The observer covers a pane
                // resize; the two timers cover the settling of a frame too far off
                // screen for the observer to be delivered promptly.
                + `var watch=function(){if(window.ResizeObserver){var o=new ResizeObserver(schedule);`
                + `o.observe(h);if(document.body)o.observe(document.body);`
                + `var c=document.querySelector('.diagram-container');if(c)o.observe(c);}`
                + `post();setTimeout(post,150);setTimeout(post,600);};`
                + `if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',watch);else watch();`
                + `window.addEventListener('load',schedule);window.addEventListener('resize',schedule);`
                + `}catch(_){}})();<\/script>`;

            /*
                `color-scheme: normal`, and it is the one line that decides whether
                the frame is transparent at all.

                A frame whose root declares a `color-scheme` gets an opaque base
                canvas painted behind its document in that scheme - so with `dark`
                here, clearing every background inside the artifact only ever
                revealed the browser's dark slab instead of Archify's navy one, and
                a colour put behind the frame never came through. `normal` leaves
                the base transparent, and the drawing composites onto whatever the
                chapter puts behind it.

                It was set to `dark` for a flash that turned out to be something
                else: the artifact resolving its theme from `prefers-color-scheme`
                before the observer could correct it, which the `matchMedia` lie
                below fixes at the source. Nothing here regressed by dropping it -
                the artifact never reads `color-scheme`, it reads `data-theme` and
                the media query, and both are answered.

                Injected into the artifact's own <head> rather than appended after
                the document, because the script that follows only works if it runs
                before the artifact's theme resolver does. Appended at the end it
                was always a frame too late.
            */
            const suppressFlash = `<style>:root{color-scheme:normal}</style>${pin}`;
            const source = String(html ?? '');
            const head = source.search(/<head[^>]*>/i);
            const injected = head < 0
                // No <head> to aim at. Appending is the old behaviour and still
                // correct, just a frame late.
                ? `${source}${suppressFlash}`
                : source.slice(0, source.indexOf('>', head) + 1)
                    + suppressFlash
                    + source.slice(source.indexOf('>', head) + 1);

            /*
                What this host asks of the artifact, now that it is no longer asking
                it to be a thumbnail.

                `data-embed` used to be set here, and unsetting it is most of this
                feature. It is not a stylesheet: the artifact enforces it in
                twenty-four JavaScript guards, and every one of them is a plain
                `if (html.getAttribute('data-embed') === 'true') return false;` at
                the top of something a reader would want. The visual style menu will
                not open under it. Neither will the node finder, the semantic lens,
                the route probe, a guided view's journey, or presentation mode; the
                relationship overlays never install, and focus-from-hash never
                resolves. No amount of CSS from out here reaches any of that, which
                is what the first attempt at this discovered by unhiding a Style
                button that then refused to do anything.

                So the artifact renders as its full self, and what stays is only what
                genuinely cannot work inside a frame the host controls.

                Appended after the document rather than spliced into its head,
                because two of these have to beat rules in the artifact's own
                stylesheet at identical specificity, and parse order is the only
                thing that can separate them. Nothing in the generated file is
                edited, so a regeneration cannot undo any of it.
            */
            const chrome = '<style>'
                /*
                    `min-height: 100vh` off the body, which is the one rule that
                    would break the sizing outright. The frame's viewport height is
                    the height this host just gave it from the content, so a body
                    that insists on filling the viewport can never report less than
                    the frame already is - it would latch at its opening 28rem and
                    stay there for every diagram.
                */
                + 'body{min-height:0}'

                /*
                    The theme toggle, which is the only control here with nothing
                    behind it. This host pins `data-theme` to dark from outside and
                    re-pins it through a MutationObserver, so pressing it would snap
                    straight back - a switch that visibly refuses is worse than one
                    that is not offered. `.design/design-principles.md` makes the
                    product dark-only; a reader who wants the light artifact opens
                    the file.
                */
                + '#btn-theme{display:none!important}'

                /*
                    The Present button, gone. Presentation is not a mode to toggle
                    here - it is always on - so a control that claims to turn it on
                    is a control that lies about the state it is in.
                */
                + '#btn-present{display:none!important}'

                /*
                    Presentation mode's viewport sizing, neutralised while the frame
                    is in the chapter - and this is the rule that keeps the whole
                    feature standing up.

                    Present mode pins the document to `100dvh`. Inside a frame this
                    host sizes from the document's own height, `100dvh` IS the answer
                    the host just gave, so the measurement would report back exactly
                    what it was told and every frame would latch at its opening 28rem
                    for ever. Letting the document be its own height again breaks that
                    circle, and present mode's real effects - the cards away, the
                    diagram filling its container - are untouched.

                    In fullscreen the opposite is true: `100dvh` means the screen,
                    which is a number the host did not choose and cannot feed back
                    into. So the override lifts exactly there.
                */
                + 'html[data-present="true"]:not([data-host-fullscreen]) body'
                + '{height:auto!important;min-height:0!important;overflow:visible!important}'
                + 'html[data-present="true"]:not([data-host-fullscreen]) .container{height:auto!important}'

                /*
                    And Style only where it is a choice. A picker offering one thing
                    is a control that cannot change anything, which is the same
                    objection as the theme toggle above.

                    Written as "unless it holds two options that are not hidden"
                    rather than as a count, because the sibling combinator inside
                    `:has()` is exactly that question and needs no JavaScript to ask
                    it. Every artifact in this repository currently offers four
                    presets, so this shows the picker today; it earns its place the
                    moment a generated artifact offers fewer.
                */
                + '.preset-wrap'
                + ':not(:has(.preset-option:not([hidden]) ~ .preset-option:not([hidden])))'
                + '{display:none!important}'

                /*
                    And the background out. The artifact paints a near-black navy
                    slab three ways - `--bg` on the body, `--panel` on the diagram
                    container and a grid rect filling the SVG - which inside a
                    chapter reads as a card the diagram is sitting on rather than as
                    part of the page. All three go, and the frame element's own
                    background goes with them in components.css, so what is behind
                    the drawing is the knowledge pane.

                    The grid is the one that cannot be reached through a class,
                    because it has none: it is
                    `<rect width="100%" height="100%" fill="url(#grid)"/>` inside the
                    SVG, so it is addressed as exactly that.
                */
                + 'html,body,.container,.diagram-container'
                + '{background:transparent!important;background-image:none!important;box-shadow:none!important}'
                + '.diagram-container>svg>rect[fill="url(#grid)"]{display:none}'
                + '</style>';

            element.srcdoc = injected + chrome;

            const unwatch = backlogWatchArtifactHeight(element, id);

            backlogDiagramInstances.set(id, {
                destroy() {
                    unwatch();
                    // Dropping the document releases the frame's own runtime, its
                    // observer and the roughly 675 KB behind it. A closed panel that
                    // kept all three would be the difference between a knowledge
                    // pane that can be browsed and one that cannot.
                    element.srcdoc = '';
                    element.style.removeProperty('height');
                }
            });
        },

        /*
            Takes an artifact frame to the whole screen, and back.

            The native Fullscreen API rather than a pop-out of this app's own: the
            artifact is already a self-contained document with its own viewer, so
            what it needs is room, not a second frame around it. Requested on the
            iframe element by this page - the frame itself is sandboxed and asks for
            nothing.

            Presentation mode is what makes the room count. It is always on inside
            the artifact, and the stylesheet injected with it lets present mode's own
            `100dvh` sizing apply exactly here, where the viewport really is a screen
            rather than a height this host measured.
        */
        async toggleArtifactFullscreen(element) {
            if (!element) return false;

            if (document.fullscreenElement === element) {
                await document.exitFullscreen();
                return false;
            }

            // `navigationUI: 'hide'` asks for the diagram and nothing else; a browser
            // that will not honour it still goes fullscreen, which is the part that
            // matters.
            await element.requestFullscreen({ navigationUI: 'hide' });
            return true;
        },

        renderGraph(element, id, data) {
            backlogRenderGenericGraph(element, id, data);
        },

        dispose(id) {
            const instance = backlogDiagramInstances.get(id);
            instance?.destroy?.();
            backlogDiagramInstances.delete(id);
        }
    });
    /*
        Roadmap timeline drag.

        The bars are Blazor's; only the pointer is JS's. This listens on the
        timeline once and works out, from the distance travelled, how many whole
        weeks and how many rows the gesture amounts to — then tells .NET, but
        only when that answer changes.

        That "only when it changes" is the whole design. A pointermove fires per
        frame, and forwarding every one of them over a server circuit would make
        the bar lag the pointer by a round trip each. Rounding to the week the
        drag has actually reached collapses a hundred moves into three or four
        calls, and the preview .NET draws is the snapped position the drop will
        commit to — so what the reader sees while dragging is what they get.

        Nothing here knows what a week is worth in pixels. That arrives from
        .NET in rem, because the rem is what the geometry is stated in, and it
        is converted here at the moment of the drag so a reader who has zoomed
        their text still moves a bar one week per week's width on their screen.
    */
    const backlogRoadmapTimelines = new Map();

    window.backlogRoadmapTimeline = {
        attach(element, id, reference, options) {
            if (!element) return;

            this.dispose(id);

            const weekRem = Number(options?.weekRem) || 1;
            const rowRem = Number(options?.rowRem) || 1;
            const drag = { active: false, pointerId: null, target: null, startX: 0, startY: 0, steps: 0, rows: 0 };

            const reset = () => {
                if (drag.target && drag.pointerId !== null && drag.target.hasPointerCapture?.(drag.pointerId)) {
                    drag.target.releasePointerCapture(drag.pointerId);
                }
                drag.active = false;
                drag.pointerId = null;
                drag.target = null;
                element.classList.remove('roadmap-timeline--dragging');
            };

            const onPointerDown = (event) => {
                // Secondary buttons open menus; a drag started on one would run
                // under a context menu the reader is trying to read.
                if (event.button !== 0 || drag.active) return;

                const grip = event.target.closest('[data-roadmap-grip]');
                if (!grip || !element.contains(grip)) return;

                const bar = grip.closest('[data-roadmap-bar]');
                if (!bar || bar.dataset.roadmapLocked === 'true') return;

                drag.active = true;
                drag.pointerId = event.pointerId;
                drag.target = grip;
                drag.startX = event.clientX;
                drag.startY = event.clientY;
                drag.steps = 0;
                drag.rows = 0;

                grip.setPointerCapture?.(event.pointerId);
                element.classList.add('roadmap-timeline--dragging');

                // The browser's own text selection and native image drag both
                // fight a pointer drag, and both leave the reader holding a
                // ghost of the label instead of the bar.
                event.preventDefault();

                reference.invokeMethodAsync('DragBegin', bar.dataset.roadmapBar, grip.dataset.roadmapGrip);
            };

            const onPointerMove = (event) => {
                if (!drag.active || event.pointerId !== drag.pointerId) return;

                const rem = backlogRootFontSize();
                const steps = Math.round((event.clientX - drag.startX) / rem / weekRem);

                // An edge has no row to land on, so vertical travel while
                // resizing is a wobble in the reader's hand, not an instruction.
                const rows = drag.target.dataset.roadmapGrip === 'move'
                    ? Math.round((event.clientY - drag.startY) / rem / rowRem)
                    : 0;

                if (steps === drag.steps && rows === drag.rows) return;

                drag.steps = steps;
                drag.rows = rows;

                reference.invokeMethodAsync('DragPreview', steps, rows);
            };

            const onPointerUp = (event) => {
                if (!drag.active || event.pointerId !== drag.pointerId) return;

                reset();
                reference.invokeMethodAsync('DragCommit');
            };

            const onPointerCancel = (event) => {
                if (!drag.active || event.pointerId !== drag.pointerId) return;

                reset();
                reference.invokeMethodAsync('DragCancel');
            };

            // Escape abandons a pointer drag as well as a keyboard one. The two
            // gestures are different but the reader's "no, put it back" is the
            // same key, and a drag that could only be cancelled by dropping it
            // somewhere would have no way out at all.
            const onKeyDown = (event) => {
                if (!drag.active || event.key !== 'Escape') return;

                reset();
                reference.invokeMethodAsync('DragCancel');
            };

            element.addEventListener('pointerdown', onPointerDown);
            element.addEventListener('pointermove', onPointerMove);
            element.addEventListener('pointerup', onPointerUp);
            element.addEventListener('pointercancel', onPointerCancel);
            document.addEventListener('keydown', onKeyDown);

            backlogRoadmapTimelines.set(id, () => {
                reset();
                element.removeEventListener('pointerdown', onPointerDown);
                element.removeEventListener('pointermove', onPointerMove);
                element.removeEventListener('pointerup', onPointerUp);
                element.removeEventListener('pointercancel', onPointerCancel);
                document.removeEventListener('keydown', onKeyDown);
            });
        },

        dispose(id) {
            const detach = backlogRoadmapTimelines.get(id);
            detach?.();
            backlogRoadmapTimelines.delete(id);
        }
    };
})();

(() => {
    /*
        The C4 explorer's viewer.

        Everything here is about a diagram somebody else drew. Mermaid renders a C4
        view into a static SVG and hands it over; this adds the exploration layer over
        the top — pan and zoom, a minimap, presentation mode, a click target on every
        element, and a dimming pass for the Highlighter.

        It works on the SVG's own `viewBox` rather than on a CSS transform. The viewBox
        is what mermaid already sizes the picture with, so zoom-to-fit is a copy of the
        original numbers, panning is arithmetic on them, and the minimap only has to
        draw the same SVG at its full extent with a rectangle where the viewBox now
        sits. A CSS transform would have needed all of that translated back out of a
        matrix, and would have blurred the text on the way.

        The bridge back to the model is the node id. Mermaid writes each element as
        `<g class="node ..." id="<svg id>-<alias>">`, and the alias is the one this
        repository's own writer produced — so stripping the svg id off the front is the
        whole of the lookup, and no text matching is involved anywhere.
    */

    const explorers = new Map();

    const MIN_SCALE = 0.2;
    const MAX_SCALE = 8;

    function nodeAlias(svg, group) {
        const prefix = svg.id ? svg.id + '-' : '';
        const id = group.id || '';
        return prefix && id.startsWith(prefix) ? id.slice(prefix.length) : id;
    }

    function readViewBox(svg) {
        const raw = (svg.getAttribute('viewBox') || '').trim().split(/[\s,]+/).map(Number);
        if (raw.length !== 4 || raw.some(Number.isNaN)) {
            // A mermaid SVG always carries one; this is the honest fallback rather
            // than NaN arithmetic that silently blanks the picture.
            const box = svg.getBBox ? svg.getBBox() : { x: 0, y: 0, width: 100, height: 100 };
            return { x: box.x, y: box.y, w: box.width || 100, h: box.height || 100 };
        }

        return { x: raw[0], y: raw[1], w: raw[2], h: raw[3] };
    }

    function writeViewBox(svg, box) {
        svg.setAttribute('viewBox', `${box.x} ${box.y} ${box.w} ${box.h}`);
    }

    window.backlogC4Explorer = {
        /**
         * Takes over a rendered diagram. Safe to call again for the same id — the
         * previous attachment is torn down first, which is what makes it correct to
         * call on every re-render rather than having to track whether one is live.
         */
        attach(id, frameSelector, reference, viewKey, drillable) {
            this.dispose(id);

            const frame = document.querySelector(frameSelector);
            const svg = frame?.querySelector('svg');
            if (!frame || !svg) return false;

            // Mermaid caps its own width so the picture never fills a tall frame.
            // The explorer owns the box now.
            svg.removeAttribute('style');
            svg.setAttribute('width', '100%');
            svg.setAttribute('height', '100%');
            svg.setAttribute('preserveAspectRatio', 'xMidYMid meet');

            // Only the boxes that lead somewhere are marked, and only those get the
            // hand cursor and the hover outline. Marking all of them was a promise
            // the click could not keep: most elements have no deeper view — a
            // component is a leaf, and a container without a component view has
            // nothing declared — so the pointer invited a click that did nothing and
            // made the whole gesture read as broken.
            const drills = new Set(Array.isArray(drillable) ? drillable : []);
            svg.querySelectorAll('g.node').forEach(group => {
                group.classList.toggle('c4-drillable', drills.has(nodeAlias(svg, group)));
            });

            const home = readViewBox(svg);
            let box = { ...home };

            const minimap = frame.closest('.c4-explorer__stage')?.querySelector('[data-c4-minimap]') ?? null;
            let lens = null;

            if (minimap) {
                // The minimap is the same SVG again, frozen at full extent, with a
                // rectangle for where the reader is. A clone rather than a <use>,
                // because <use> would inherit the live viewBox and show nothing but
                // itself.
                const copy = svg.cloneNode(true);
                copy.removeAttribute('id');
                copy.querySelectorAll('[id]').forEach(n => n.removeAttribute('id'));
                writeViewBox(copy, home);
                copy.setAttribute('width', '100%');
                copy.setAttribute('height', '100%');

                lens = document.createElementNS('http://www.w3.org/2000/svg', 'rect');
                lens.setAttribute('class', 'c4-minimap__lens');
                copy.appendChild(lens);

                minimap.replaceChildren(copy);
            }

            function drawLens() {
                if (!lens) return;
                lens.setAttribute('x', box.x);
                lens.setAttribute('y', box.y);
                lens.setAttribute('width', Math.max(box.w, 1));
                lens.setAttribute('height', Math.max(box.h, 1));
            }

            function apply() {
                writeViewBox(svg, box);
                drawLens();
                reference?.invokeMethodAsync('ZoomChanged', Math.round((home.w / box.w) * 100));
            }

            function clampScale(next) {
                const scale = home.w / next.w;
                if (scale < MIN_SCALE || scale > MAX_SCALE) return false;
                return true;
            }

            // ---- zoom ----------------------------------------------------------

            function zoomAt(factor, clientX, clientY) {
                const rect = svg.getBoundingClientRect();
                if (!rect.width || !rect.height) return;

                // Zoom about the pointer: the point under the cursor is the one that
                // must not move, which is what makes wheel-zoom feel like a map.
                const fx = (clientX - rect.left) / rect.width;
                const fy = (clientY - rect.top) / rect.height;

                const next = {
                    w: box.w / factor,
                    h: box.h / factor,
                    x: box.x + (box.w - box.w / factor) * fx,
                    y: box.y + (box.h - box.h / factor) * fy
                };

                if (!clampScale(next)) return;
                box = next;
                apply();
            }

            function zoomBy(factor) {
                const rect = svg.getBoundingClientRect();
                zoomAt(factor, rect.left + rect.width / 2, rect.top + rect.height / 2);
            }

            const onWheel = (event) => {
                if (!event.ctrlKey && Math.abs(event.deltaY) < 1) return;
                event.preventDefault();
                zoomAt(event.deltaY < 0 ? 1.15 : 1 / 1.15, event.clientX, event.clientY);
            };

            // ---- pan -----------------------------------------------------------

            // `node` is the shape the gesture started on, and it is the whole reason
            // activation happens on pointerup rather than on click.
            //
            // Capturing the pointer is what makes panning survive the cursor leaving
            // the frame — and it also redirects the subsequent `click` to the capture
            // target. So the click arrives on the frame div, `closest('g.node')` finds
            // nothing, and clicking a card did nothing at all. Remembering what was
            // under the pointer when it went down is immune to that, and it is more
            // correct anyway: a press that starts on a card and ends on one is a click
            // on the card the reader aimed at.
            const drag = { active: false, x: 0, y: 0, moved: 0, node: null };

            const onPointerDown = (event) => {
                if (event.button !== 0) return;
                drag.active = true;
                drag.x = event.clientX;
                drag.y = event.clientY;
                drag.moved = 0;
                drag.node = event.target.closest?.('g.node') ?? null;
                frame.setPointerCapture?.(event.pointerId);
                frame.classList.add('is-panning');
            };

            const onPointerMove = (event) => {
                if (!drag.active) return;

                const rect = svg.getBoundingClientRect();
                if (!rect.width || !rect.height) return;

                const dx = event.clientX - drag.x;
                const dy = event.clientY - drag.y;
                drag.moved += Math.abs(dx) + Math.abs(dy);
                drag.x = event.clientX;
                drag.y = event.clientY;

                box = {
                    ...box,
                    x: box.x - dx * (box.w / rect.width),
                    y: box.y - dy * (box.h / rect.height)
                };

                apply();
            };

            const endDrag = (event) => {
                if (!drag.active) return;

                drag.active = false;
                frame.releasePointerCapture?.(event.pointerId);
                frame.classList.remove('is-panning');

                // A press that moved is a pan, not a click. Without this, dragging
                // across the picture opens whatever happened to be under the finger
                // when it went down.
                if (event.type !== 'pointerup' || drag.moved > 4) { drag.node = null; return; }

                const group = drag.node;
                drag.node = null;
                if (!group) return;

                const now = performance.now();
                if (now - activatedAt < 500) return;

                const alias = nodeAlias(svg, group);
                if (!alias) return;

                activatedAt = now;

                // The view is named as well as the node. What the reader pressed is a
                // box on *this* picture, and by the time the message lands the explorer
                // may have moved on — in which case the press meant nothing and is
                // dropped rather than applied to whatever is showing now.
                reference?.invokeMethodAsync('NodeActivated', alias, viewKey ?? null);
            };

            // ---- drill in ------------------------------------------------------

            // The last activation, so a second one on its heels is ignored.
            //
            // Drilling is one press here and a double-click in c4hero, and a reader who
            // brings that habit would otherwise descend two levels at once — the second
            // press landing on the newly drawn diagram before they have seen the first.
            // The same window covers the render race: while the next view is being
            // drawn the old SVG is still on screen and still listening.
            let activatedAt = 0;

            frame.addEventListener('wheel', onWheel, { passive: false });
            frame.addEventListener('pointerdown', onPointerDown);
            frame.addEventListener('pointermove', onPointerMove);
            frame.addEventListener('pointerup', endDrag);
            frame.addEventListener('pointercancel', endDrag);

            explorers.set(id, {
                svg,
                frame,
                home,
                get box() { return box; },
                set box(value) { box = value; },
                apply,
                zoomBy,
                detach() {
                    frame.removeEventListener('wheel', onWheel);
                    frame.removeEventListener('pointerdown', onPointerDown);
                    frame.removeEventListener('pointermove', onPointerMove);
                    frame.removeEventListener('pointerup', endDrag);
                    frame.removeEventListener('pointercancel', endDrag);
                }
            });

            apply();
            return true;
        },

        /** Back to the whole picture. Mermaid's own viewBox is the definition of
         *  "fits", so this is a copy rather than a measurement. */
        fit(id) {
            const state = explorers.get(id);
            if (!state) return;
            state.box = { ...state.home };
            state.apply();
        },

        zoom(id, factor) {
            explorers.get(id)?.zoomBy(factor);
        },

        /**
         * Marks which nodes the Highlighter matched and which the search found.
         * Classes rather than inline style, so the dimming is themeable and one
         * selector turns the whole effect off.
         */
        highlight(id, matched, focused) {
            const state = explorers.get(id);
            if (!state) return;

            const dim = Array.isArray(matched);
            const wanted = new Set(dim ? matched : []);
            const focus = new Set(Array.isArray(focused) ? focused : []);

            state.svg.querySelectorAll('g.node').forEach(group => {
                const alias = nodeAlias(state.svg, group);
                group.classList.toggle('c4-dimmed', dim && !wanted.has(alias));
                group.classList.toggle('c4-focused', focus.has(alias));
            });
        },

        /** Centres one element and leaves the zoom alone — a search hit should move
         *  the reader to the box, not decide how close they stand to it. */
        reveal(id, alias) {
            const state = explorers.get(id);
            if (!state || !alias) return;

            const prefix = state.svg.id ? state.svg.id + '-' : '';
            const group = state.svg.querySelector(`g.node[id="${CSS.escape(prefix + alias)}"]`);
            if (!group || !group.getBBox) return;

            const box = group.getBBox();
            state.box = {
                ...state.box,
                x: box.x + box.width / 2 - state.box.w / 2,
                y: box.y + box.height / 2 - state.box.h / 2
            };
            state.apply();
        },

        dispose(id) {
            const state = explorers.get(id);
            state?.detach();
            explorers.delete(id);
        }
    };
})();
