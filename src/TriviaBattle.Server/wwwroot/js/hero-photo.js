// Hero photos (Dungeon format): a player's face on their hero. The player says yes in the intro (the
// action button), their station takes it (js/hero-camera.js), and the server keeps it in memory until the
// match ends. Each photo is up to three pictures: "face" (just the face, which the station's 3D hero wears),
// "portrait" (the hero's head wearing it) and "sheet" (the same, animated: frames of it idling, blinking,
// cheering and wincing, from js/battle3d/portrait.js's heroSheet; a station that can't draw in 3D sends none).
// Everything drawn in 2D shows the sheet, playing (css/hero-photo.css), or the still portrait until it's in. The
// snapshot only gives each player's photo number (player.photo, 0 = none); this fetches a picture once per number
// and keeps it in memory as a blob URL (the server sends it no-store, so a face never lands in a screen's disk
// cache). Without a photo, everything shows the hero's own animated sheet (media/heroes, made by /dev/heroes).
// Needs ui.js and pixel-sprites.js on the page. Styles: css/hero-photo.css.

const HeroPhoto = {
    cache: new Map(),   // `${matchId}/${stationId}/${kind}` -> { matchId, photo, url, missing } (url is null while it loads)

    // The animated sheet's layout: SHEET in js/battle3d/portrait.js, played by css/hero-photo.css (keep all three in
    // step). Each row's animation length is in the CSS; IDLE_MS is the idle's, so a redraw carries it on where it was.
    SHEET: { frames: 16, rows: ['idle', 'cheer', 'hurt'] },
    IDLE_MS: 3200,
    REACT_MS: { cheer: 1300, hurt: 900 },

    /** The page's redraw, called when a picture has arrived. */
    onLoad: null,

    /**
     * One of this player's pictures ("portrait", "face" or "sheet") as a blob URL, or null: no photo, still loading
     * (onLoad says when it's in), or (a sheet) none was sent with it.
     */
    url(match, stationId, kind = 'portrait') {
        const player = match?.players?.find(p => p.stationId === stationId);
        if (!player?.photo) return null;

        const key = `${match.matchId}/${stationId}/${kind}`;
        const known = HeroPhoto.cache.get(key);
        if (known?.photo === player.photo) return known.url; // (null while it loads, or for good when it's missing)
        if (known?.url) URL.revokeObjectURL(known.url);

        const entry = { matchId: match.matchId, photo: player.photo, url: null };
        HeroPhoto.cache.set(key, entry);
        HeroPhoto.fetchInto(key, entry, `/api/photos/${encodeURIComponent(match.matchId)}/${encodeURIComponent(stationId)}/${kind}`);
        return null;
    },

    /** A hero photo taken in the lobby (a station's seat or a party member: photo is its number), as a blob URL, or null while it loads. */
    lobby(stationId, photo, kind = 'portrait') {
        if (!photo) return null;
        const key = `lobby/${stationId}/${kind}`;
        const known = HeroPhoto.cache.get(key);
        if (known?.photo === photo) return known.url;
        if (known?.url) URL.revokeObjectURL(known.url);
        const entry = { matchId: 'lobby', photo, url: null };
        HeroPhoto.cache.set(key, entry);
        HeroPhoto.fetchInto(key, entry, `/api/photos/lobby/${encodeURIComponent(stationId)}/${kind}`);
        return null;
    },

    /**
     * Fetches a picture into its cache entry, then redraws. Not there (the server restarted, or a network
     * blip): asks again a little later, on the next redraw after that. A sheet the photo came without (404) is
     * missing for good: the still portrait stands in, with no asking again.
     */
    fetchInto(key, entry, url) {
        const optional = key.endsWith('/sheet');
        fetch(url)
            .then(response => {
                if (optional && response.status === 404) entry.missing = true;
                return response.ok ? response.blob() : null;
            })
            .then(blob => {
                if (HeroPhoto.cache.get(key) !== entry) return; // gone, or a newer one came
                if (entry.missing) return;
                if (!blob) return setTimeout(() => HeroPhoto.cache.get(key) === entry && HeroPhoto.cache.delete(key), 1500);
                entry.url = URL.createObjectURL(blob);
                HeroPhoto.onLoad?.();
            })
            .catch(() => {}); // the hero's own picture stays; a photo isn't worth an error on a game screen
    },

    /**
     * This player's portrait, animated: their hero wearing their face (its sheet; the still portrait until that's
     * in, or if there's none), or their hero's own sheet without a photo (media/heroes). A team's lane (no single
     * player) shows the knight's pixel portrait. react: a moment it plays once before idling again, { kind: 'cheer'
     * or 'hurt', t0: when it started, as minus its age in ms or a CSS value (a caption's var(--t0)) }, or null.
     */
    portrait(match, stationId, cssClass, color, react = null) {
        const player = match?.players?.find(p => p.stationId === stationId);
        const sheet = HeroPhoto.url(match, stationId, 'sheet');
        if (sheet) return HeroPhoto.sheet(sheet, cssClass, react);
        const still = HeroPhoto.url(match, stationId);
        if (still) return `<img class="hero-photo ${cssClass}" src="${still}" alt="">`;
        if (player?.photo) {
            // Their photo is still on its way: their hero's own still meanwhile (not its animation, which would jump).
            return `<img class="hero-photo ${cssClass}" src="/media/heroes/${encodeURIComponent(player.hero)}-head.png" alt="">`;
        }
        if (player?.hero) return HeroPhoto.sheet(`/media/heroes/${encodeURIComponent(player.hero)}-sheet.webp`, cssClass, react);
        return PixelSprites.html('knight', cssClass, { r: color });
    },

    /**
     * An animated sheet, playing (css/hero-photo.css): its idle, timed from the clock so every redraw carries on where
     * it was, or a reaction (react, see portrait) and then the idle again.
     */
    sheet(url, cssClass, react = null) {
        const idle0 = -(Date.now() % HeroPhoto.IDLE_MS);
        const reacting = react && HeroPhoto.REACT_MS[react.kind] && (typeof react.t0 !== 'number' || -react.t0 < HeroPhoto.REACT_MS[react.kind]);
        const t0 = !reacting ? '' : typeof react.t0 === 'number' ? `${Math.round(react.t0)}ms` : react.t0;
        return `<span class="hero-photo hero-sheet ${cssClass} ${reacting ? `is-${react.kind}` : ''}" role="img"
                      style="background-image: url('${url}'); --idle0: ${idle0}ms${reacting ? `; --react0: ${t0}` : ''}"></span>`;
    },

    /** Just the small portrait (beside a party member's name, in the line-up). Nothing without a photo. */
    face(match, stationId, cssClass) {
        const url = HeroPhoto.url(match, stationId);
        return url ? `<img class="hero-photo ${cssClass}" src="${url}" alt="">` : '';
    },

    /** The dev previews: pictures made on the page, used as this player's photo instead of fetching them. */
    use(matchId, stationId, photo, urls) {
        for (const [kind, url] of Object.entries(urls))
            HeroPhoto.cache.set(`${matchId}/${stationId}/${kind}`, { matchId, photo, url });
    },

    /** Forgets every picture except those of these matches (the others have ended). */
    keepOnly(matchIds) {
        for (const [key, entry] of HeroPhoto.cache) {
            if (matchIds.includes(entry.matchId) || entry.matchId === 'lobby') continue;
            if (entry.url?.startsWith('blob:')) URL.revokeObjectURL(entry.url);
            HeroPhoto.cache.delete(key);
        }
    },
};
