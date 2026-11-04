// The other players on your station (Dungeon format): the moments of theirs that matter to you, with
// their face (their hero photo, or their hero), so a group game feels like one game. Only a few kinds, so
// they mean something: a teammate revived or healed you, a teammate is down (answer right to revive them), a
// party combo, a boss slain by anyone, a rival overtaking you.
//
// They have their own place: the caption bar across the top of the monster's room, under the question,
// one at a time (a short queue), and only while a question is up, never over your own fight. So nothing
// covers the question, the answers or your plate. It all comes from the snapshot every screen gets (each
// player's delve and the standings); nothing here is a rule.
// Needs ui.js, theme.js and hero-photo.js. Styles: css/dungeon-encounter.css (.dg-feed-caption).

const DungeonFeed = {
    seen: {},          // `${matchId}:${stationId}` -> what we last saw of that player (hits, place)
    queue: [],         // moments waiting for the caption: { html, at, stationId, rival }
    showing: null,     // the moment in the caption: { html, from, stationId, rival } (js/battle3d/others.js drops a rival in)
    SHOW_MS: 2600,     // how long each one stays
    STALE_MS: 10000,   // one still waiting after this long is dropped (it's old news)

    /** Call on each redraw of a station in a game: queues each new moment of another player that matters to you. */
    note(match, you) {
        const meStanding = DungeonFeed.standingOf(match, you);
        for (const p of match.players) {
            if (p.stationId === you.stationId || !p.delve) continue;
            const key = `${match.matchId}:${p.stationId}`;
            const now = {
                dealt: p.delve.lastDealt?.number || 0,
                taken: p.delve.lastTaken?.number || 0,
                ahead: DungeonFeed.isAhead(match, p, meStanding),
            };
            const before = DungeonFeed.seen[key];
            DungeonFeed.seen[key] = now;
            if (!before) continue;

            const teammate = !!you.teamId && p.teamId === you.teamId;
            const hit = now.dealt > before.dealt ? p.delve.lastDealt : null;
            const taken = now.taken > before.taken ? p.delve.lastTaken : null;
            const say = (lineKey, values = {}) => DungeonFeed.queue.push({
                at: Date.now(),
                stationId: p.stationId,
                rival: !teammate,
                html: `
                    ${HeroPhoto.portrait(match, p.stationId, 'dg-feed-face', '')}
                    <span class="${teammate ? 'is-teammate' : 'is-rival'}">${Theme.text(lineKey, { name: p.name, ...values }, `${key}:${now.dealt}:${now.taken}`)}</span>`,
            });

            if (hit?.revivedOwnerId === you.stationId) say('feed.revivedYou');
            if (hit?.healedOwnerId === you.stationId) say('feed.healedYou', { amount: hit.healed }); // a teammate's Healer
            if (teammate && taken?.downed) say('feed.teammateDown');
            if (teammate && hit?.partyCombo) say('feed.partyCombo');
            if (hit?.killed && hit.isBoss) say('feed.bossSlain', { monster: hit.monsterName });
            if (!teammate && now.ahead && !before.ahead) say('feed.overtook');
        }
    },

    /** The caption for the monster's room: the moment showing now (the next one starts when it ends), or nothing. */
    caption() {
        const now = Date.now();
        if (DungeonFeed.showing && now - DungeonFeed.showing.from >= DungeonFeed.SHOW_MS)
            DungeonFeed.showing = null;
        DungeonFeed.queue = DungeonFeed.queue.filter(m => now - m.at < DungeonFeed.STALE_MS);
        if (!DungeonFeed.showing && DungeonFeed.queue.length)
            DungeonFeed.showing = { ...DungeonFeed.queue.shift(), from: now };
        const m = DungeonFeed.showing;
        return m ? `<div class="dg-feed-caption" style="--t0: ${m.from - now}ms">${m.html}</div>` : '';
    },

    /** Your row in the standings: your own, or your team's. */
    standingOf(match, player) {
        return match.standings.find(s => s.id === (match.usesTeams ? player.teamId : player.stationId));
    },

    /** A rival is ahead of you when their row (their team's, in a team game) ranks above yours. */
    isAhead(match, player, meStanding) {
        const theirs = DungeonFeed.standingOf(match, player);
        return !!theirs && !!meStanding && theirs.id !== meStanding.id && theirs.rank < meStanding.rank;
    },

    /** Forget everything (a new match). */
    reset() {
        DungeonFeed.seen = {};
        DungeonFeed.queue = [];
        DungeonFeed.showing = null;
    },
};
