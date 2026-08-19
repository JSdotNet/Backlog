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
        Whether the focus has landed on something outside a named region.

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
    window.backlogFocusOutside = (id) => {
        const element = document.getElementById(id);
        if (!element) return false;

        const focused = document.activeElement;
        if (!focused || focused === document.body || focused === document.documentElement) return false;

        return !element.contains(focused);
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

    // A task row's drag needs a payload, and Blazor's DragEventArgs is read-only so
    // no C# handler can supply one. Chromium starts a drag without one happily
    // enough and then refuses to fire drop, which reads as a drag that works and
    // does nothing when released.
    //
    // Here rather than in a host's own script, because TaskListView owns the whole
    // gesture: a host that had to supply this would be a host that has to know the
    // list drags at all. Capture phase, so it runs before Blazor's handler for the
    // same event.
    document.addEventListener(
        'dragstart',
        (event) => {
            const row = event.target instanceof Element ? event.target.closest('.task-item[draggable="true"]') : null;
            if (!row || !event.dataTransfer) return;

            event.dataTransfer.effectAllowed = 'move';
            // Some payload is required for the drag to be considered valid.
            event.dataTransfer.setData('text/plain', row.getAttribute('data-testid') ?? 'task');

            if (event.dataTransfer.setDragImage) {
                const bounds = row.getBoundingClientRect();
                event.dataTransfer.setDragImage(row, event.clientX - bounds.left, event.clientY - bounds.top);
            }
        },
        true
    );

    // A drop only fires where dragover was cancelled. The row's own
    // `:preventDefault` does that too; this is the frame before Blazor has attached
    // it, which is otherwise a dropped drop.
    document.addEventListener(
        'dragover',
        (event) => {
            const row = event.target instanceof Element ? event.target.closest('.task-item') : null;
            if (!row || !event.dataTransfer) return;

            event.dataTransfer.dropEffect = 'move';
            event.preventDefault();
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
    let backlogPaneOwner = null;

    function backlogRootFontSize() {
        return parseFloat(getComputedStyle(document.documentElement).fontSize) || 16;
    }

    function backlogPaneLayout() {
        return document.querySelector(BACKLOG_PANE_LAYOUT_SELECTOR);
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
        if (!backlogPaneOwner) return;

        backlogPaneOwner.invokeMethodAsync('SetGlobalPaneCapacityAsync', backlogPaneCapacity());

        const layout = backlogPaneLayout();
        if (!layout) return;

        backlogPaneOwner.invokeMethodAsync('SetSidePaneMaxWidthAsync', backlogPaneMaxRem(layout));
    }

    window.backlogPaneResizer = {
        initialize(owner) {
            backlogPaneOwner = owner;
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
        dispose() {
            backlogPaneOwner = null;
        }
    };

    window.addEventListener('resize', backlogReportPaneBounds);

    document.addEventListener('pointerdown', (event) => {
        if (event.button !== 0) return;

        const handle = event.target instanceof Element ? event.target.closest('[data-pane-resizer]') : null;
        if (!handle) return;

        const layout = handle.closest(BACKLOG_PANE_LAYOUT_SELECTOR);
        if (!layout) return;

        // A split that did not register an owner is not this drag's to settle.
        // There is one owner for the whole document, so honouring a second layout
        // would hand its width to whichever component registered first.
        if (layout.getAttribute('data-pane-drag') === 'off') return;

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
            backlogPaneOwner?.invokeMethodAsync('SetSidePaneWidthAsync', width);
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
        g6: ['https://unpkg.com/@antv/g6@5/dist/g6.min.js'],
        ...(window.backlogDiagramLibrarySources ?? {})
    };

    let backlogMermaidPromise;
    let backlogG6Promise;

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

    async function backlogLoadG6() {
        if (window.G6?.Graph) return window.G6;
        if (!backlogG6Promise) {
            backlogG6Promise = (async () => {
                for (const source of backlogDiagramLibrarySources.g6) {
                    try {
                        await backlogLoadScript(source);
                        if (window.G6?.Graph) return window.G6;
                    } catch {
                        // Try the next source; SVG fallback keeps the graph usable offline.
                    }
                }

                throw new Error('AntV G6 renderer unavailable.');
            })();
        }

        return backlogG6Promise;
    }

    // `sourceShown` is whether the component kept its source disclosure. A host that
    // prints the fence itself turns that off, and the fallback must not then point a
    // reader at something that is not there — the promise is the part of this message
    // that has to be true.
    function backlogRenderDiagramError(element, message, sourceShown = true) {
        const promise = sourceShown ? ' Source is available below.' : '';
        element.innerHTML = `<div class="diagram-view__fallback" role="note">${backlogEscapeHtml(message)}${promise}</div>`;
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

    window.backlogDiagrams = {
        // Exposed so a host's own renderer can register a teardown against the
        // same id `dispose` is called with, and reuse the loaders and escaping.
        instances: backlogDiagramInstances,
        loadG6: backlogLoadG6,
        escapeHtml: backlogEscapeHtml,
        renderError: backlogRenderDiagramError,

        async render(element, id, language, source, sourceShown = true) {
            const normalized = String(language ?? '').trim().toLowerCase();
            if (normalized !== 'mermaid' && normalized !== 'mmd') {
                backlogRenderDiagramError(element, `${language ?? 'Diagram'} rendering is not configured yet.`, sourceShown);
                return;
            }

            element.innerHTML = '<p class="diagram-view__status" role="status">Rendering Mermaid diagram...</p>';

            try {
                const mermaid = await backlogLoadMermaid();
                const result = await mermaid.render(`${id}-svg`, source);
                element.innerHTML = result.svg;
                result.bindFunctions?.(element);
            } catch (error) {
                backlogRenderDiagramError(element, error instanceof Error ? error.message : 'Mermaid rendering failed.', sourceShown);
            } finally {
                backlogRemoveMermaidScratchNodes(element, id);
            }
        },

        renderGraph(element, id, data) {
            backlogRenderGenericGraph(element, id, data);
        },

        dispose(id) {
            const instance = backlogDiagramInstances.get(id);
            instance?.destroy?.();
            backlogDiagramInstances.delete(id);
        }
    };
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
