// Themes: how the screens look and what they say.
//
// A theme is a folder in /themes/<name>/ with:
//   theme.css  - colour/font/shape tokens and decorations (see css/screens.css for the token list)
//   copy.json  - every line of text the screens show, by key (see themes/neon/copy.json for all keys)
// The "neon" copy is the base: a theme's copy.json only needs the lines it wants to change.
// docs/THEMES.md explains how to make a new theme.
//
// Which theme: ?theme=<name> in the URL (for previewing), otherwise the one chosen on the
// admin page. ScreenConnection keeps it up to date when the admin switches themes.

const Theme = {
    BASE: 'neon',
    name: null,
    copy: {},
    urlOverride: new URLSearchParams(location.search).get('theme'),

    /** Loads a theme (or, with no name, the URL override / the server's current theme). Does nothing if already loaded. */
    async load(name) {
        name = Theme.urlOverride || name || (await (await fetch('/api/display')).json()).theme;
        if (name === Theme.name) return false;

        const [base, own] = await Promise.all([
            Theme.fetchCopy(Theme.BASE),
            name === Theme.BASE ? {} : Theme.fetchCopy(name),
        ]);
        Theme.copy = Theme.merge(base, own);

        let link = document.getElementById('theme-css');
        if (!link) {
            link = Object.assign(document.createElement('link'), { id: 'theme-css', rel: 'stylesheet' });
            document.head.appendChild(link);
        }
        link.href = `/themes/${encodeURIComponent(name)}/theme.css`;
        document.documentElement.dataset.theme = name;
        Theme.name = name;
        return true;
    },

    /**
     * A line of on-screen text, ready to put in innerHTML.
     *   Theme.text('player.correct', { points: 850 })   ->  "Correct! +850"
     * {placeholders} are filled from vars. Everything is HTML-escaped (player names are typed by the public).
     * If copy.json has a list for this key, one is picked at random, but always the same one for the
     * same seed (e.g. the match + question), so the text doesn't flicker every time the screen redraws.
     */
    text(key, vars = {}, seed = '') {
        return UI.escape(Theme.pick(key, seed)).replace(/\{(\w+)\}/g, (match, name) => {
            if (!(name in vars)) return match;
            const v = vars[name];
            return v?.trustedHtml !== undefined ? v.trustedHtml : UI.escape(v);
        });
    },

    /** Same as text() but plain, unescaped text: for textContent, placeholders and data sent to the server. */
    plain(key, vars = {}, seed = '') {
        return Theme.pick(key, seed).replace(/\{(\w+)\}/g, (match, name) => name in vars ? String(vars[name]) : match);
    },

    /** The raw copy line for a key (one variant, chosen by seed). */
    pick(key, seed) {
        let value = key.split('.').reduce((node, part) => node?.[part], Theme.copy);
        if (Array.isArray(value)) value = value[Theme.hash(seed + key) % value.length];
        if (typeof value !== 'string') {
            console.warn('No copy for', key);
            return key;
        }
        return value;
    },

    /**
     * Marks a placeholder value as HTML we built ourselves, so it isn't escaped a second time:
     * a live countdown <span>, or another Theme.text() result. Never use with typed-in text.
     */
    raw(html) {
        return { trustedHtml: html };
    },

    async fetchCopy(name) {
        try {
            const response = await fetch(`/themes/${encodeURIComponent(name)}/copy.json`);
            return response.ok ? await response.json() : {};
        } catch {
            return {};
        }
    },

    /** Deep-merges theme copy over the base copy. */
    merge(base, own) {
        const result = { ...base };
        for (const [key, value] of Object.entries(own)) {
            const bothObjects = value && typeof value === 'object' && !Array.isArray(value) && typeof base[key] === 'object' && !Array.isArray(base[key]);
            result[key] = bothObjects ? Theme.merge(base[key], value) : value;
        }
        return result;
    },

    hash(text) {
        let h = 0;
        for (let i = 0; i < text.length; i++) h = (h * 31 + text.charCodeAt(i)) >>> 0;
        return h;
    },
};
