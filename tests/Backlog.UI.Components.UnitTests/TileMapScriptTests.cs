using System.Diagnostics;

namespace Backlog.UI.Components.UnitTests;

/// <summary>
/// <c>window.backlogTileMap</c>, run rather than read.
/// <para>
/// bUnit has no JS engine, so what <c>TileMap</c> leaves to the script runs here in
/// Node, cut out of <c>components.js</c> as it ships, against a stand-in Leaflet:
/// what Leaflet is asked to draw, when the tiles count as unreachable, and how a map
/// with no size yet is framed.
/// </para>
/// </summary>
public sealed class TileMapScriptTests
{
    /// <summary>Leaflet draws tiles and pans; its own controls, attribution and
    /// keyboard handler are off, because the library draws those itself.</summary>
    [Fact]
    public void Leaflet_draws_the_tiles_and_none_of_its_own_controls()
    {
        var result = Run("""
            await start();
            const o = maps[0].options;
            console.log(`zoomControl=${o.zoomControl} attribution=${o.attributionControl} keyboard=${o.keyboard} zoomAnimation=${o.zoomAnimation} url=${layers[0].url}`);
            """);

        Assert.Equal("zoomControl=false attribution=false keyboard=false zoomAnimation=false url=https://tiles.test/{z}/{x}/{y}.png", result);
    }

    [Fact]
    public void A_view_where_every_tile_failed_is_unreachable_until_a_tile_loads()
    {
        var result = Run("""
            await start();
            const layer = layers[0];
            layer.fire('loading'); layer.fire('tileerror'); layer.fire('tileerror'); layer.fire('load');
            layer.fire('loading'); layer.fire('tileerror'); layer.fire('load');
            layer.fire('tileload');
            console.log(calls.filter(c => c[0] === 'TilesReachable').map(c => c[1]).join(','));
            """);

        Assert.Equal("false,true", result);
    }

    /// <summary>A 429 on one tile leaves that tile blank (ADR 0061 §6); it does not
    /// take the whole map away.</summary>
    [Fact]
    public void One_failed_tile_among_loaded_ones_says_nothing()
    {
        var result = Run("""
            await start();
            const layer = layers[0];
            layer.fire('loading'); layer.fire('tileload'); layer.fire('tileerror'); layer.fire('tileload'); layer.fire('load');
            console.log(`reports=${calls.filter(c => c[0] === 'TilesReachable').length}`);
            """);

        Assert.Equal("reports=0", result);
    }

    [Fact]
    public void The_browser_going_offline_is_unreachable_and_coming_back_reloads_the_tiles()
    {
        var result = Run("""
            await start();
            windowListeners.offline();
            windowListeners.online();
            console.log(`${calls.filter(c => c[0] === 'TilesReachable').map(c => c[1]).join(',')} redraws=${layers[0].redraws}`);
            """);

        Assert.Equal("false redraws=1", result);
    }

    [Fact]
    public void A_map_with_a_size_frames_the_bounds_it_was_given()
    {
        var result = Run("""
            await start({ x: 600, y: 400 });
            const fit = maps[0].fits[0];
            console.log(`${JSON.stringify(fit.bounds)} topLeft=${fit.options.paddingTopLeft} bottomRight=${fit.options.paddingBottomRight} maxZoom=${fit.options.maxZoom} views=${maps[0].views.length}`);
            """);

        Assert.Equal("[[51,3],[60,11]] topLeft=48,48 bottomRight=48,48 maxZoom=13 views=0", result);
    }

    /// <summary>A map laid out after it started — a pane still opening — cannot
    /// work out a zoom for its bounds, so it opens on the default view and frames
    /// the bounds once it has a size.</summary>
    [Fact]
    public void A_map_with_no_size_yet_frames_its_bounds_once_it_has_one()
    {
        var result = Run("""
            await start({ x: 0, y: 0 });
            const map = maps[0];
            const before = `views=${JSON.stringify(map.views)} fits=${map.fits.length}`;
            map.size = { x: 600, y: 400 };
            observers[0].fire();
            observers[0].fire();
            console.log(`${before} after=${map.fits.length}`);
            """);

        Assert.Equal("views=[[[50,6],4]] fits=0 after=1", result);
    }

    [Fact]
    public void The_view_is_reported_settled_when_a_move_ends()
    {
        var result = Run("""
            await start({ x: 600, y: 400 });
            calls.length = 0;
            maps[0].fire('move');
            maps[0].fire('moveend');
            console.log(calls.map(c => `${c[0]}:${c.slice(1).join('/')}`).join(' '));
            """);

        Assert.Equal("ViewMoved:50/6/7/600/400/true/0/0", result);
    }

    /// <summary>What the map's chrome covers — the controls' column on the right, the foot
    /// along the bottom — is measured from the page and reported with every view, so the
    /// pins and pills keep out from under it.</summary>
    [Fact]
    public void The_view_reports_what_the_chrome_covers()
    {
        var result = Run("""
            chromed(element);
            await start({ x: 600, y: 400 });
            const last = calls.filter(c => c[0] === 'ViewMoved').at(-1);
            console.log(last.slice(1).join('/'));
            """);

        // The map is 0..600 by 0..400; the controls start at x=548, the foot at y=330.
        Assert.Equal("50/6/7/600/400/true/52/70", result);
    }

    /// <summary>Fit all frames the marks inside the part of the map the chrome leaves free:
    /// no framed pin lands under the legend or the controls.</summary>
    [Fact]
    public void A_fit_pads_for_the_chrome()
    {
        var result = Run("""
            chromed(element);
            await start({ x: 600, y: 400 });
            const fit = maps[0].fits[0];
            console.log(`topLeft=${fit.options.paddingTopLeft} bottomRight=${fit.options.paddingBottomRight}`);
            """);

        Assert.Equal("topLeft=48,48 bottomRight=100,118", result);
    }

    /// <summary>A short map — 390px wide, 144 tall, the narrow layout — has less height
    /// than the padding and the foot together. Fitting into a negative area sent Leaflet to
    /// the closest zoom it was allowed, framing no pin. The chrome's reserve is dropped
    /// first, then the padding shrinks, so the fit always has room to frame the marks.</summary>
    [Fact]
    public void A_map_too_short_for_its_chrome_fits_into_the_room_it_has()
    {
        var result = Run("""
            chromed(element, { box: rect(0, 0, 390, 144), controls: rect(346, 8, 382, 110), foot: rect(8, 74, 338, 136) });
            await start({ x: 390, y: 144 });
            const o = maps[0].fits[0].options;
            const free = [390 - o.paddingTopLeft[0] - o.paddingBottomRight[0], 144 - o.paddingTopLeft[1] - o.paddingBottomRight[1]];
            console.log(`topLeft=${o.paddingTopLeft} bottomRight=${o.paddingBottomRight} free=${free}`);
            """);

        Assert.Equal("topLeft=48,48 bottomRight=48,48 free=294,48", result);
    }

    [Fact]
    public void A_map_too_small_even_for_its_padding_shrinks_the_padding()
    {
        var result = Run("""
            await start({ x: 120, y: 80 });
            const o = maps[0].fits[0].options;
            console.log(`topLeft=${o.paddingTopLeft} bottomRight=${o.paddingBottomRight}`);
            """);

        // 80px tall leaves 48 of room with 16 on either side.
        Assert.Equal("topLeft=16,16 bottomRight=16,16", result);
    }

    /// <summary>
    /// The view handed to pin placement always carries what the chrome covers, measured on
    /// the page: a pin under a drawn overlay is never counted in view (it becomes a pill). A
    /// fit keeps the same reserve and gives up its own padding first, down to 16px, so the
    /// marks it frames land clear of the chrome. Only a map too small even for that drops
    /// the reserve from the fit.
    /// <para>
    /// Collapsed is the short map's layout: the legend folded into a button among the
    /// controls (a 2×2 block, 86px in from the right) and only the attribution in the foot
    /// (30px). Expanded is the tall map's: the controls' column (44px) and a two-line legend
    /// in the foot (70px).
    /// </para>
    /// </summary>
    [Theory]
    [InlineData(128, "collapsed", "reported=86/30 topLeft=25,25 bottomRight=111,55")]
    [InlineData(144, "collapsed", "reported=86/30 topLeft=33,33 bottomRight=119,63")]
    [InlineData(160, "collapsed", "reported=86/30 topLeft=41,41 bottomRight=127,71")]
    [InlineData(160, "expanded", "reported=44/70 topLeft=21,21 bottomRight=65,91")]
    [InlineData(256, "expanded", "reported=44/70 topLeft=48,48 bottomRight=92,118")]
    [InlineData(128, "expanded", "reported=44/70 topLeft=40,40 bottomRight=40,40")]
    public void Placement_always_reserves_the_chrome_and_a_fit_gives_up_padding_first(int height, string layout, string expected)
    {
        var (right, bottom) = layout == "collapsed" ? (86, 30) : (44, 70);
        var result = Run($$"""
            chromed(element, { box: rect(0, 0, 390, {{height}}), controls: rect({{390 - right}}, 8, 390, 80), foot: rect(8, {{height - bottom}}, 296, {{height}}) });
            await start({ x: 390, y: {{height}} });
            const view = calls.filter(c => c[0] === 'ViewMoved').at(-1);
            const o = maps[0].fits[0].options;
            console.log(`reported=${view[7]}/${view[8]} topLeft=${o.paddingTopLeft} bottomRight=${o.paddingBottomRight}`);
            """);

        Assert.Equal(expected, result);
    }

    /// <summary>On the wide desktop map the reserve and the full padding both fit.</summary>
    [Fact]
    public void A_wide_map_keeps_its_full_padding_and_reserve()
    {
        var result = Run("""
            chromed(element, { box: rect(0, 0, 1440, 400), controls: rect(1396, 8, 1440, 120), foot: rect(8, 360, 1380, 400) });
            await start({ x: 1440, y: 400 });
            const o = maps[0].fits[0].options;
            console.log(`topLeft=${o.paddingTopLeft} bottomRight=${o.paddingBottomRight}`);
            """);

        Assert.Equal("topLeft=48,48 bottomRight=92,88", result);
    }

    /// <summary>The component asks for a measure when its chrome changed after the start; the
    /// map answers with a settled view carrying the new frame.</summary>
    [Fact]
    public void Measure_reports_the_view_again_with_the_chrome_as_it_is_now()
    {
        var result = Run("""
            await start({ x: 600, y: 400 });
            chromed(element);
            calls.length = 0;
            window.backlogTileMap.measure('m');
            console.log(calls.map(c => c.join('/')).join(' '));
            """);

        Assert.Equal("ViewMoved/50/6/7/600/400/true/52/70", result);
    }

    [Fact]
    public void Dispose_removes_the_map_and_its_listeners()
    {
        var result = Run("""
            await start();
            window.backlogTileMap.dispose('m');
            console.log(`removed=${maps[0].removed} disconnected=${observers[0].disconnected} offline=${'offline' in windowListeners} online=${'online' in windowListeners}`);
            """);

        Assert.Equal("removed=true disconnected=true offline=false online=false", result);
    }

    [Fact]
    public void A_map_disposed_while_the_library_loads_is_never_started()
    {
        var result = Run("""
            const starting = window.backlogTileMap.initialize(element, 'm', options(), dotnet);
            window.backlogTileMap.dispose('m');
            await starting;
            console.log(`maps=${maps.length}`);
            """);

        Assert.Equal("maps=0", result);
    }

    [Fact]
    public void A_library_that_will_not_load_says_so()
    {
        var result = Run("""
            delete window.L;
            loadFails = true;
            await window.backlogTileMap.initialize(element, 'm', options(), dotnet);
            console.log(calls.map(c => c[0]).join(','));
            """);

        Assert.Equal("MapUnavailable", result);
    }

    /// <summary>A pan that uncovers a strip of tiles which all answer 429, while the
    /// rest of the view stays drawn, leaves that strip blank (ADR 0061 §6). It is
    /// not the offline plate: only "no tile in view loads" is.</summary>
    [Fact]
    public void Tiles_failing_beside_drawn_ones_are_not_offline()
    {
        var result = Run("""
            await start();
            const layer = layers[0];
            const a = {}, b = {};
            layer.fire('loading'); layer.fire('tileload', { tile: a }); layer.fire('tileload', { tile: b }); layer.fire('load');
            layer.fire('loading'); layer.fire('tileerror', { tile: {} }); layer.fire('tileerror', { tile: {} }); layer.fire('tileerror', { tile: {} }); layer.fire('load');
            console.log(`reports=${calls.filter(c => c[0] === 'TilesReachable').length}`);
            """);

        Assert.Equal("reports=0", result);
    }

    /// <summary>Once every drawn tile has gone — a zoom, a new source — a view where
    /// all the new tiles fail is unreachable after all.</summary>
    [Fact]
    public void A_view_whose_drawn_tiles_were_all_unloaded_and_whose_new_ones_fail_is_unreachable()
    {
        var result = Run("""
            await start();
            const layer = layers[0];
            const a = {};
            layer.fire('loading'); layer.fire('tileload', { tile: a }); layer.fire('load');
            layer.fire('tileunload', { tile: a });
            layer.fire('loading'); layer.fire('tileerror', { tile: {} }); layer.fire('load');
            console.log(calls.filter(c => c[0] === 'TilesReachable').map(c => c[1]).join(','));
            """);

        Assert.Equal("false", result);
    }

    /// <summary>A new source while the map is offline: the first new tile that
    /// loads says so, so the note comes down. setTiles used to mark the map
    /// reachable silently, which made that report a no-op.</summary>
    [Fact]
    public void A_new_source_reports_reachable_when_its_first_tile_loads()
    {
        var result = Run("""
            await start();
            const layer = layers[0];
            layer.fire('loading'); layer.fire('tileerror', { tile: {} }); layer.fire('load');
            window.backlogTileMap.setTiles('m', 'https://tiles.test/new/{z}/{x}/{y}.png');
            layer.fire('loading'); layer.fire('tileload', { tile: {} }); layer.fire('load');
            console.log(`${calls.filter(c => c[0] === 'TilesReachable').map(c => c[1]).join(',')} url=${layer.url}`);
            """);

        Assert.Equal("false,true url=https://tiles.test/new/{z}/{x}/{y}.png", result);
    }

    /// <summary>The first map of a session waits for leaflet.js. A fit and a new
    /// source sent meanwhile are held and applied once the map exists — the map
    /// opens on the new source, framed on the late marks.</summary>
    [Fact]
    public void A_fit_and_a_source_sent_while_the_library_loads_are_applied_once_it_has()
    {
        var result = Run("""
            delete window.L;
            let release;
            loadGate = new Promise(resolve => release = resolve);
            const starting = window.backlogTileMap.initialize(element, 'm', { ...options(), fit: { bounds: null, center: [50, 6], zoom: 4 } }, dotnet);
            window.backlogTileMap.fit('m', { bounds: [40, -10, 60, 11], padding: 48, maxZoom: 13, center: [50, 6], zoom: 4 });
            window.backlogTileMap.setTiles('m', 'https://tiles.test/late/{z}/{x}/{y}.png');
            release();
            await starting;
            console.log(`url=${layers[0].url} fits=${JSON.stringify(maps[0].fits.map(f => f.bounds))}`);
            """);

        Assert.Equal("url=https://tiles.test/late/{z}/{x}/{y}.png fits=[[[40,-10],[60,11]]]", result);
    }

    private static string Run(string body)
    {
        var harness = $$"""
            const calls = [];
            const maps = [];
            const layers = [];
            const observers = [];
            const windowListeners = {};
            let loadFails = false;
            let loadGate = null;

            const dotnet = { invokeMethodAsync(method, ...args) { calls.push([method, ...args]); return Promise.resolve(); } };
            const element = { isConnected: true };

            const rect = (left, top, right, bottom) => ({ left, top, right, bottom, width: right - left, height: bottom - top });
            function chromed(target, layout) {
                const parts = {
                    '.tile-map__controls': { getBoundingClientRect: () => layout?.controls ?? rect(548, 8, 592, 120) },
                    '.tile-map__foot': { getBoundingClientRect: () => layout?.foot ?? rect(8, 330, 540, 392) }
                };
                const section = { querySelector: selector => parts[selector] ?? null };
                target.getBoundingClientRect = () => layout?.box ?? rect(0, 0, 600, 400);
                target.closest = selector => selector === '.tile-map' ? section : null;
            }

            class Events {
                constructor() { this.handlers = {}; }
                on(names, fn) { for (const name of names.split(' ')) (this.handlers[name] ??= []).push(fn); return this; }
                fire(name, event) { for (const fn of this.handlers[name] ?? []) fn(event ?? {}); }
            }

            class FakeMap extends Events {
                constructor(el, options) { super(); this.options = options; this.size = { x: 600, y: 400 }; this.center = { lat: 50, lng: 6 }; this.zoom = 7; this.views = []; this.fits = []; this.removed = false; maps.push(this); }
                getSize() { return this.size; }
                getCenter() { return this.center; }
                getZoom() { return this.zoom; }
                setView(center, zoom) { this.views.push([center, zoom]); }
                fitBounds(bounds, options) { this.fits.push({ bounds, options }); }
                setZoom(zoom) { this.zoom = zoom; }
                panTo() { }
                invalidateSize() { }
                remove() { this.removed = true; }
            }

            class FakeLayer extends Events {
                constructor(url) { super(); this.url = url; this.redraws = 0; layers.push(this); }
                addTo() { return this; }
                setUrl(url) { this.url = url; }
                redraw() { this.redraws++; }
            }

            class ResizeObserver {
                constructor(callback) { this.callback = callback; this.disconnected = false; observers.push(this); }
                observe() { }
                disconnect() { this.disconnected = true; }
                fire() { if (!this.disconnected) this.callback([]); }
            }

            const leaflet = { map: (el, o) => new FakeMap(el, o), tileLayer: (url) => new FakeLayer(url) };
            const window = {
                L: leaflet,
                addEventListener(type, fn) { windowListeners[type] = fn; },
                removeEventListener(type, fn) { if (windowListeners[type] === fn) delete windowListeners[type]; }
            };
            const navigator = { onLine: true };
            const document = { querySelector: () => ({ before() { } }), createElement: () => ({ dataset: {} }), head: { prepend() { } } };
            const requestAnimationFrame = () => 1;
            const cancelAnimationFrame = () => { };
            const backlogComponentsScriptUrl = 'https://app.test/_content/Backlog.UI.Components/components.js';
            async function backlogLoadScript() {
                if (loadGate) await loadGate;
                if (loadFails) throw new Error('404');
                window.L = leaflet;
            }

            function options() {
                return {
                    tileUrl: 'https://tiles.test/{z}/{x}/{y}.png',
                    minZoom: 2,
                    maxZoom: 18,
                    fit: { bounds: [51, 3, 60, 11], padding: 48, maxZoom: 13, center: [50, 6], zoom: 4 }
                };
            }

            async function start(size) {
                const original = leaflet.map;
                if (size) leaflet.map = (el, o) => { const map = original(el, o); map.size = size; return map; };
                await window.backlogTileMap.initialize(element, 'm', options(), dotnet);
                leaflet.map = original;
            }

            {{TileMapRegion()}}

            (async () => {
            {{body}}
            })().catch(error => { console.error(error); process.exit(1); });
            """;

        var start = new ProcessStartInfo("node")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false
        };

        Process process;
        try
        {
            process = Process.Start(start) ?? throw new InvalidOperationException("node did not start");
        }
        catch (System.ComponentModel.Win32Exception exception)
        {
            Assert.Fail($"This test runs components.js and needs Node on the PATH: {exception.Message}");
            return string.Empty;
        }

        using (process)
        {
            process.StandardInput.Write(harness);
            process.StandardInput.Close();
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();

            Assert.True(process.WaitForExit(TimeSpan.FromSeconds(30)), "node did not finish in thirty seconds.");
            Assert.True(process.ExitCode == 0, $"node failed ({process.ExitCode}):\n{error.Result}");

            return output.Result.Trim();
        }
    }

    /// <summary>The tile map from its registry to the end of its object, cut out of
    /// the script as it ships, so the code under test is the code the app loads.</summary>
    private static string TileMapRegion() =>
        ComponentsScript.Region("    const backlogTileMaps = new Map();", "})();\n\n(() => {\n    /*\n        The C4 zoom navigation");
}
