// Hero photos (Dungeon format): a player's face on their hero. The player says yes in the intro (the
// action button), their station takes it (js/hero-camera.js), and the server keeps it in memory until
// the match ends. The snapshot only gives each player's photo number (player.photo, 0 = none); this
// fetches the picture once per number and keeps it in memory as a blob URL (the server sends it
// no-store, so a face never lands in a screen's disk cache), then draws it wherever the knight's
// portrait would be. Without a photo, everything shows the knight as before.
// Needs ui.js and pixel-sprites.js on the page. Styles: css/hero-photo.css.

const HeroPhoto = {
    cache: new Map(),   // `${matchId}/${stationId}` -> { matchId, photo, url } (url is null while it loads)

    /** The page's redraw, called when a picture has arrived. */
    onLoad: null,

    /** This player's face as a blob URL, or null: no photo, or still loading (onLoad says when it's in). */
    url(match, stationId) {
        const player = match?.players?.find(p => p.stationId === stationId);
        if (!player?.photo) return null;

        const key = `${match.matchId}/${stationId}`;
        const known = HeroPhoto.cache.get(key);
        if (known?.photo === player.photo) return known.url;
        if (known?.url) URL.revokeObjectURL(known.url);

        const entry = { matchId: match.matchId, photo: player.photo, url: null };
        HeroPhoto.cache.set(key, entry);
        fetch(`/api/photos/${encodeURIComponent(match.matchId)}/${encodeURIComponent(stationId)}`)
            .then(response => (response.ok ? response.blob() : null))
            .then(blob => {
                if (!blob || HeroPhoto.cache.get(key) !== entry) return; // gone, or a newer one came
                entry.url = URL.createObjectURL(blob);
                HeroPhoto.onLoad?.();
            })
            .catch(() => {}); // the knight stays; the photo isn't worth an error on a game screen
        return null;
    },

    /** The face in a ring of the player's colour, or the knight's pixel portrait without one. */
    portrait(match, stationId, cssClass, color) {
        const url = HeroPhoto.url(match, stationId);
        return url
            ? `<img class="hero-photo ${cssClass}" src="${url}" alt="" style="--photo-ring: ${color}">`
            : PixelSprites.html('knight', cssClass, { r: color });
    },

    /** Just the face (a party member's, or on the 2D hero's head). Nothing without one. */
    face(match, stationId, cssClass) {
        const url = HeroPhoto.url(match, stationId);
        return url ? `<img class="hero-photo ${cssClass}" src="${url}" alt="">` : '';
    },

    /** The dev previews: a picture made on the page, used as this player's photo instead of fetching one. */
    use(matchId, stationId, photo, url) {
        HeroPhoto.cache.set(`${matchId}/${stationId}`, { matchId, photo, url });
    },

    /** Forgets every picture except those of these matches (the others have ended). */
    keepOnly(matchIds) {
        for (const [key, entry] of HeroPhoto.cache) {
            if (matchIds.includes(entry.matchId)) continue;
            if (entry.url) URL.revokeObjectURL(entry.url);
            HeroPhoto.cache.delete(key);
        }
    },
};
