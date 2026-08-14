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
    window.backlogFocus = (id) => {
        const element = document.getElementById(id);
        if (element) element.focus();
    };

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
        const workspaceMinRem = parseFloat(styles.getPropertyValue('--workspace-min-width')) || 22;
        const gapRem = ((parseFloat(styles.columnGap) || 0) * 2) / rem;
        const available = (layout.clientWidth / rem) - workspaceMinRem - gapRem - 1;

        return Math.min(BACKLOG_PANE_ABSOLUTE_MAX_REM, Math.max(BACKLOG_PANE_MIN_REM, Math.round(available * 2) / 2));
    }

    function backlogPaneWidthAt(layout, clientX) {
        const rem = (layout.getBoundingClientRect().right - clientX) / backlogRootFontSize();
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

    function backlogReportPaneBounds() {
        const layout = backlogPaneLayout();
        if (!layout || !backlogPaneOwner) return;

        backlogPaneOwner.invokeMethodAsync('SetSidePaneMaxWidthAsync', backlogPaneMaxRem(layout));
        backlogPaneOwner.invokeMethodAsync('SetGlobalPaneCapacityAsync', backlogPaneCapacity());
    }

    window.backlogPaneResizer = {
        initialize(owner) {
            backlogPaneOwner = owner;
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

        event.preventDefault();
        handle.focus();
        document.body.classList.add('is-resizing-pane');

        let width = backlogPaneWidthAt(layout, event.clientX);

        const onMove = (move) => {
            width = backlogPaneWidthAt(layout, move.clientX);
            // Both names are set so the app's knowledge layout and the library's
            // SplitPane each read the one their stylesheet knows.
            layout.style.setProperty('--knowledge-panel-width', `${width}rem`);
            layout.style.setProperty('--split-pane-start', `${width}rem`);
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
    const backlogDiagramLibrarySources = {
        mermaid: [
            '/vendor/mermaid/mermaid.esm.min.mjs',
            'https://cdn.jsdelivr.net/npm/mermaid@11/dist/mermaid.esm.min.mjs'
        ],
        g6: [
            '/vendor/g6/g6.min.js',
            'https://unpkg.com/@antv/g6@5/dist/g6.min.js'
        ]
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
                                theme: 'dark',
                                securityLevel: 'strict',
                                deterministicIds: true
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

    function backlogRenderDiagramError(element, message) {
        element.innerHTML = `<div class="diagram-view__fallback" role="note">${backlogEscapeHtml(message)} Source is available below.</div>`;
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

    window.backlogDiagrams = {
        // Exposed so a host's own renderer can register a teardown against the
        // same id `dispose` is called with, and reuse the loaders and escaping.
        instances: backlogDiagramInstances,
        loadG6: backlogLoadG6,
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
})();
