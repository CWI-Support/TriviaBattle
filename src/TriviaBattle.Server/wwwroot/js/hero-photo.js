// Hero photos (Dungeon format): a player's face on their hero. The player says yes in the intro (the
// action button), their station takes it (js/hero-camera.js), and the server keeps it in memory until the
// match ends. Each photo is two pictures: "face" (just the face, which the station's 3D knight wears) and
// "portrait" (the knight's head wearing it, for everything drawn in 2D). The snapshot only gives each
// player's photo number (player.photo, 0 = none); this fetches a picture once per number and keeps it in
// memory as a blob URL (the server sends it no-store, so a face never lands in a screen's disk cache).
// Without a photo, everything shows the knight as before.
// Needs ui.js and pixel-sprites.js on the page. Styles: css/hero-photo.css.

const HeroPhoto = {
    cache: new Map(),   // `${matchId}/${stationId}/${kind}` -> { matchId, photo, url } (url is null while it loads)

    /** The page's redraw, called when a picture has arrived. */
    onLoad: null,

    /** One of this player's pictures ("portrait" or "face") as a blob URL, or null: no photo, or still loading (onLoad says when it's in). */
    url(match, stationId, kind = 'portrait') {
        const player = match?.players?.find(p => p.stationId === stationId);
        if (!player?.photo) return null;

        const key = `${match.matchId}/${stationId}/${kind}`;
        const known = HeroPhoto.cache.get(key);
        if (known?.photo === player.photo) return known.url;
        if (known?.url) URL.revokeObjectURL(known.url);

        const entry = { matchId: match.matchId, photo: player.photo, url: null };
        HeroPhoto.cache.set(key, entry);
        fetch(`/api/photos/${encodeURIComponent(match.matchId)}/${encodeURIComponent(stationId)}/${kind}`)
            .then(response => (response.ok ? response.blob() : null))
            .then(blob => {
                if (!blob || HeroPhoto.cache.get(key) !== entry) return; // gone, or a newer one came
                entry.url = URL.createObjectURL(blob);
                HeroPhoto.onLoad?.();
            })
            .catch(() => {}); // the knight stays; the photo isn't worth an error on a game screen
        return null;
    },

    /**
     * This player's portrait: their hero wearing their face, or their hero's own picture without a photo
     * (media/heroes). A team's lane (no single player) shows the knight's pixel portrait.
     */
    portrait(match, stationId, cssClass, color) {
        const url = HeroPhoto.url(match, stationId);
        const hero = match?.players?.find(p => p.stationId === stationId)?.hero;
        if (url) return `<img class="hero-photo ${cssClass}" src="${url}" alt="">`;
        if (hero) return `<img class="hero-photo ${cssClass}" src="/media/heroes/${encodeURIComponent(hero)}-head.png" alt="">`;
        return PixelSprites.html('knight', cssClass, { r: color });
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
            if (matchIds.includes(entry.matchId)) continue;
            if (entry.url?.startsWith('blob:')) URL.revokeObjectURL(entry.url);
            HeroPhoto.cache.delete(key);
        }
    },
};
