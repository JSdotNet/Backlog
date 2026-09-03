/*
    Storybook chrome only. This never touches a component: it reads the tokens
    the library declares and reports what the browser made of them, so the
    Foundations page can show measurements instead of a second, hand-copied
    transcript of components.css that would quietly drift from it.
*/
(() => {
    'use strict';

    // The surface each token is used against and the bar it has to clear there,
    // taken from .design/color-scheme.md#contrast-rules rather than from plain
    // WCAG: that file sets AAA (7:1) for primary body text, 4.5:1 for supporting
    // text, 3:1 for focus rings and control boundaries, and explicitly exempts
    // --color-text-disabled, which signals unavailability on purpose.
    //
    // These are the twenty-two colour tokens that file defines, which is also
    // exactly what components.css declares — the code theme in
    // .design/color-scheme.md#syntax-highlighting-tokens is the only other colour
    // in the stylesheet, and it is scored where it is used, on a code block.
    // "Exactly" is now a test rather than a claim: StorybookCoverageTests reads
    // this array back and fails if it and components.css disagree either way,
    // because a token missing from here is a token the page never measures.
    // Grouped by role, in the order a reader needs them: what the brand is, what
    // a state means, what text does, what it sits on, and where the edges are.
    // Each semantic is an ink and the surface it sits on, kept adjacent so the
    // pair is obvious and a stray third green would have nowhere to hide.
    const COLOR_GROUPS = [
        {
            title: 'Brand',
            tokens: [
                { name: '--color-primary', against: '--color-background', threshold: 4.5 },
                { name: '--color-primary-light', against: '--color-background', threshold: 4.5 },
                { name: '--color-primary-dark', against: '--color-background', threshold: 3 },
                { name: '--color-secondary', against: '--color-background', threshold: 4.5 }
            ]
        },
        {
            // Surfaces, so each is scored by what body text does on top of it.
            title: 'Semantic',
            tokens: [
                { name: '--color-success', against: '--color-text-primary', threshold: 4.5 },
                // The two foregrounds in this group, so each is scored the other
                // way round: against the surface it has to stay legible on rather
                // than against the text that sits on it. The raised surface is the
                // binding pair for both — it is the lightest thing either ink sits
                // on, and it is what fixed both values.
                { name: '--color-success-text', against: '--color-background-raised', threshold: 4.5 },
                { name: '--color-warning', against: '--color-text-primary', threshold: 4.5 },
                { name: '--color-error', against: '--color-text-primary', threshold: 4.5 },
                { name: '--color-error-text', against: '--color-background-raised', threshold: 4.5 },
                { name: '--color-info', against: '--color-text-primary', threshold: 4.5 }
            ]
        },
        {
            title: 'Text',
            tokens: [
                { name: '--color-text-primary', against: '--color-background', threshold: 7 },
                { name: '--color-text-secondary', against: '--color-background-alt', threshold: 4.5 },
                { name: '--color-text-disabled', against: '--color-background-alt', threshold: null },
                { name: '--color-text-inverse', against: '--color-primary', threshold: 4.5 },
                { name: '--color-text-link', against: '--color-background', threshold: 4.5 }
            ]
        },
        {
            title: 'Background',
            tokens: [
                { name: '--color-background', against: '--color-text-primary', threshold: 7 },
                { name: '--color-background-alt', against: '--color-text-primary', threshold: 4.5 },
                { name: '--color-background-raised', against: '--color-text-primary', threshold: 4.5 },
                { name: '--color-background-overlay', against: '--color-text-primary', threshold: null }
            ]
        },
        {
            title: 'Border',
            tokens: [
                { name: '--color-border', against: '--color-background', threshold: 3 },
                { name: '--color-border-strong', against: '--color-background', threshold: 3 },
                { name: '--color-border-focus', against: '--color-background', threshold: 3 }
            ]
        }
    ];

    const FONT_TOKENS = ['--font-family-base', '--font-family-heading', '--font-family-mono'];

    const prefixed = prefix => name => name.startsWith(prefix);

    function declaredCustomProperties() {
        // Only same-origin sheets can be walked; a cross-origin one throws on
        // .cssRules. Everything this page cares about is served from here.
        const names = new Set();

        for (const sheet of document.styleSheets) {
            let rules;
            try {
                rules = sheet.cssRules;
            } catch {
                continue;
            }

            for (const rule of rules ?? []) {
                if (!(rule.style && rule.selectorText === ':root')) continue;

                for (const property of rule.style) {
                    if (property.startsWith('--')) names.add(property);
                }
            }
        }

        return [...names];
    }

    const readToken = name => getComputedStyle(document.documentElement).getPropertyValue(name).trim();

    /**
     * The computed length a token resolves to, in pixels. Read off a probe
     * element rather than parsed out of the string, so rem, px, em and calc()
     * all sort against each other correctly.
     */
    function measuredPixels(token) {
        const probe = document.createElement('div');
        probe.style.cssText = `position:absolute;visibility:hidden;width:var(${token})`;
        document.body.appendChild(probe);
        const width = probe.getBoundingClientRect().width;
        probe.remove();

        return width;
    }

    const measuredMilliseconds = value => {
        const match = value.match(/([\d.]+)\s*(m?s)/);
        if (!match) return Number.MAX_SAFE_INTEGER;

        return parseFloat(match[1]) * (match[2] === 's' ? 1000 : 1);
    };

    /**
     * Tokens sharing a prefix, smallest first. A scale read in alphabetical order
     * ("2xl, 4xl, base, lg, sm, xl, xs") is not a scale, so `by` sorts on what the
     * value actually means; without one, declaration order stands.
     */
    function valueTokens(prefix, by) {
        const tokens = declaredCustomProperties()
            .filter(prefixed(prefix))
            .map(name => ({ name, value: readToken(name) }));

        return by ? tokens.sort((a, b) => by(a) - by(b)) : tokens;
    }

    // --- Contrast -----------------------------------------------------------

    function channels(color) {
        // Everything in the palette is either #rrggbb or an rgb()/rgba() the
        // browser normalised for us. Anything else is reported as unmeasurable
        // rather than guessed at.
        const hex = color.match(/^#([0-9a-f]{6})$/i);
        if (hex) {
            return [0, 2, 4].map(i => parseInt(hex[1].substr(i, 2), 16) / 255);
        }

        const rgb = color.match(/rgba?\(([^)]+)\)/i);
        if (rgb) {
            const parts = rgb[1].split(/[ ,/]+/).filter(Boolean).slice(0, 3);
            if (parts.length === 3) return parts.map(part => parseFloat(part) / 255);
        }

        return null;
    }

    function relativeLuminance(color) {
        const rgb = channels(color);
        if (!rgb) return null;

        const [r, g, b] = rgb.map(c => (c <= 0.03928 ? c / 12.92 : Math.pow((c + 0.055) / 1.055, 2.4)));
        return (0.2126 * r) + (0.7152 * g) + (0.0722 * b);
    }

    function contrastRatio(foreground, background) {
        const a = relativeLuminance(foreground);
        const b = relativeLuminance(background);
        if (a === null || b === null) return null;

        return (Math.max(a, b) + 0.05) / (Math.min(a, b) + 0.05);
    }

    // --- Fonts --------------------------------------------------------------

    function firstFamily(stack) {
        return (stack.split(',')[0] ?? '').trim().replace(/^['"]|['"]$/g, '');
    }

    const GENERIC_FAMILIES = ['sans-serif', 'serif', 'monospace', 'system-ui', 'cursive', 'fantasy'];

    // Deliberately wide and irregular, so two different typefaces are very
    // unlikely to set it to the same width by coincidence.
    const PROBE_TEXT = 'MMMWWWiiilll1234567890@#%&';

    function textWidth(fontFamily) {
        const probe = document.createElement('span');
        probe.textContent = PROBE_TEXT;
        probe.style.cssText =
            `position:absolute;visibility:hidden;white-space:nowrap;font-size:72px;font-family:${fontFamily}`;
        document.body.appendChild(probe);
        const width = probe.getBoundingClientRect().width;
        probe.remove();

        return width;
    }

    /**
     * Whether the browser can actually render this family.
     *
     * Not document.fonts.check(): that only knows about faces in the FontFaceSet,
     * so with no @font-face rule anywhere it answers true for every name it is
     * given — including one no machine has installed. It reported Inter, Poppins
     * and Fira Code as loaded on a box that has none of them.
     *
     * Instead, set the same string in the candidate and in a generic, and compare
     * widths. A family the browser does not have falls back to the generic and
     * measures identically; one it does have almost certainly does not.
     */
    function isLoaded(family) {
        if (!family || GENERIC_FAMILIES.includes(family)) return false;

        const quoted = `"${CSS.escape ? family.replace(/"/g, '') : family}"`;

        return GENERIC_FAMILIES.slice(0, 3).some(
            generic => Math.abs(textWidth(`${quoted}, ${generic}`) - textWidth(generic)) > 0.5);
    }

    window.backlogStorybookFoundations = {
        read() {
            const colorGroups = COLOR_GROUPS.map(group => ({
                title: group.title,
                tokens: group.tokens.map(({ name, against, threshold }) => {
                    const value = readToken(name);
                    const ratio = contrastRatio(value, readToken(against));

                    return {
                        name,
                        value,
                        against,
                        threshold,
                        contrast: ratio === null || threshold === null ? null : Math.round(ratio * 100) / 100
                    };
                }).filter(token => token.value.length > 0)
            })).filter(group => group.tokens.length > 0);

            const fonts = FONT_TOKENS.map(token => {
                const declared = firstFamily(readToken(token));
                return { token, declared, loaded: isLoaded(declared) };
            });

            return {
                colorGroups,
                fonts,
                missingFonts: fonts.filter(font => !font.loaded).map(font => font.declared),
                fontSizes: valueTokens('--font-size-', token => measuredPixels(token.name)),
                spacing: valueTokens('--spacing-', token => measuredPixels(token.name)),
                radii: valueTokens('--border-radius-', token => measuredPixels(token.name)),
                shadows: valueTokens('--shadow-'),
                motion: valueTokens('--transition-', token => measuredMilliseconds(token.value))
            };
        }
    };
})();
