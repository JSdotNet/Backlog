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

    // Copying is the browser's job, and the browser is allowed to refuse: the
    // async clipboard needs a secure context and a permission the host WebView
    // may not have granted. The execCommand path is the fallback for exactly
    // that case — deprecated, but it is what still works in a WebView2 without
    // clipboard-write. Either way the caller is told whether it worked, so the
    // UI can say so instead of claiming a copy that never happened.
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
     * from the pointer to the layout's right edge. SplitPane's bound value is the
     * *start* pane, on the left, whose width is the distance from the left edge.
     * Measuring both the same way made the library's separator run backwards:
     * dragging right narrowed the pane it was supposed to widen.
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

    function backlogRenderDiagramError(element, message) {
        element.innerHTML = `<div class="diagram-view__fallback" role="note">${backlogEscapeHtml(message)} Source is available below.</div>`;
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
