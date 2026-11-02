// The other players on your station (Dungeon format): their big moments pop up over your fight with
// their face (their hero photo, or their hero), so a group game feels like one game. Teammates: slaying
// a monster, a party combo, a revive (yours or another's), going down. Rivals (a race, or the other team):
// a boss slain, a new floor, getting knocked out, overtaking you. It all comes from the snapshot every
// screen gets (each player's delve and the standings); nothing here is a rule.
// Needs ui.js, theme.js, hero-photo.js and dungeon-view.js (effects). Styles: css/dungeon.css (.dg-feed).

const DungeonFeed = {
    seen: {},       // `${matchId}:${stationId}` -> what we last saw of that player (hits, floor, place)
    SHOW_MS: 3200,  // how long a moment stays up

    /** Call on each redraw of a station in a game: starts a pop for each new moment of another player. */
    note(match, you) {
        const meStanding = DungeonFeed.standingOf(match, you);
        for (const p of match.players) {
            if (p.stationId === you.stationId || !p.delve) continue;
            const key = `${match.matchId}:${p.stationId}`;
            const now = {
                dealt: p.delve.lastDealt?.number || 0,
                taken: p.delve.lastTaken?.number || 0,
                floor: p.delve.floor,
                ahead: DungeonFeed.isAhead(match, p, meStanding),
            };
            const before = DungeonFeed.seen[key];
            DungeonFeed.seen[key] = now;
            if (!before) continue;

            const teammate = !!you.teamId && p.teamId === you.teamId;
            const say = (lineKey, values = {}) => DungeonFeed.pop(match, p, Theme.text(lineKey, { name: p.name, ...values }, `${key}:${now.dealt}:${now.taken}`), teammate);
            const hit = now.dealt > before.dealt ? p.delve.lastDealt : null;
            const taken = now.taken > before.taken ? p.delve.lastTaken : null;

            if (hit?.revivedOwnerId === you.stationId) say('feed.revivedYou');
            else if (teammate && hit?.revivedOwnerId) say('feed.revived', { other: DungeonFeed.nameOf(match, hit.revivedOwnerId) });
            if (hit?.killed && hit.isBoss) say('feed.bossSlain', { monster: hit.monsterName });
            else if (teammate && hit?.killed) say('feed.slain', { monster: hit.monsterName });
            if (teammate && hit?.partyCombo) say('feed.partyCombo');
            if (taken?.downed) say(teammate ? 'feed.teammateDown' : 'feed.rivalDown');
            if (!teammate && now.floor > before.floor && !hit?.isBoss) say('feed.rivalFloor', { floor: now.floor });
            if (!teammate && now.ahead && !before.ahead) say('feed.overtook');
        }
    },

    /** The pops still showing, newest last. */
    html() {
        return `<div class="dg-feed">${DungeonView.activeEffects('feed')}</div>`;
    },

    pop(match, player, text, teammate) {
        DungeonView.effect('feed', `
            <div class="dg-feed-item ${teammate ? 'is-teammate' : 'is-rival'}">
                ${HeroPhoto.portrait(match, player.stationId, 'dg-feed-face', '')}
                <span>${text}</span>
            </div>`, DungeonFeed.SHOW_MS);
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

    nameOf(match, stationId) {
        return match.players.find(p => p.stationId === stationId)?.name || '';
    },

    /** Forget everything (a new match). */
    reset() {
        DungeonFeed.seen = {};
    },
};
