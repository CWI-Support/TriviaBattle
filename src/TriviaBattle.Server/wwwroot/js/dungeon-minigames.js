// Mini games on the station screen (Dungeon format). The rules are on the server (Core/Dungeon/Minigames): this only
// draws what the snapshot's you.minigame says. Every mini game has the same three parts, all timed from the server's
// clock so a redraw carries on where it was:
//   - the intro (from introAtMs): "MINI GAME!", the game's name and how to play, and a 3, 2, 1 to playsFromMs. It's a
//     veil over the game that lifts by itself when play starts (a CSS animation), so no redraw is needed then.
//   - play: the game itself, with its time limit running down (endsAtMs)
//   - the result (once finishedAtMs is set): won or out of time, until the fight (or a reward pick) comes back
// The arcade clock is stopped meanwhile, and says so.
// Games so far: the rune lock (runeLock): a needle spins round a dial; stop it on the glowing mark, 6 times in a row.
// The needle is a CSS animation from the snapshot's turn (its angle at turnStartedAtMs and its speed), the same numbers
// the server times presses by. On the main screen a lane shows when its player is in one, and a win gets a banner.
// Needs ui.js, theme.js, pixel-sprites.js, dungeon-view.js and dungeon-battle.js. Styles: css/dungeon-minigames.css.

const DungeonMinigames = {
    /** The game's name, in the theme's words. Literal keys, so ThemeFilesTests can check them. */
    name(kind) {
        switch (kind) {
            case 'RuneLock': return Theme.text('dungeon.minigameRuneLock');
            default: return UI.escape(kind);
        }
    },

    /** How to play it, for the intro. */
    howTo(minigame) {
        switch (minigame.kind) {
            case 'RuneLock': return Theme.text('dungeon.minigameRuneLockHow', { hits: minigame.runeLock.hitsNeeded });
            default: return '';
        }
    },

    /** True while the station should show the mini game: from the end of the kill beat that led to it. */
    showing(you, serverNow) {
        return !!you.minigame && !DungeonBattle.isPlaying(you, serverNow);
    },

    /** The station screen during a mini game: the room behind it, the game, the stopped clock, and you. */
    station(match, you, serverNow, plate) {
        const game = you.minigame;
        const seed = `${match.matchId}:${you.stationId}:minigame:${game.introAtMs}`;
        const toPlay = Math.round(game.playsFromMs - serverNow);
        const body = game.kind === 'RuneLock' ? DungeonMinigames.runeLock(game, you, serverNow, seed) : '';
        const result = game.finishedAtMs ? DungeonMinigames.result(game, seed, Math.round(game.finishedAtMs - serverNow)) : '';
        return `
            <div class="dg-station is-minigame">
                <div class="dg-world">
                    <div class="dg-minigame-room">${DungeonBattle.room()}</div>
                    <div class="dg-minigame" style="--t0: ${Math.round(game.introAtMs - serverNow)}ms; --play: ${toPlay}ms">
                        <div class="dg-minigame-top">
                            <span class="dg-minigame-name">${DungeonMinigames.name(game.kind)}</span>
                            <span class="dg-minigame-clock">${Theme.text('dungeon.minigameClockStopped')}
                                <b>${Theme.text('dungeon.time')} <span ${UI.matchClock(match)}></span></b></span>
                        </div>
                        ${body}
                        <div class="dg-minigame-limit">
                            <span>${Theme.text('dungeon.minigameTimeLeft')} <b data-ends-at="${game.endsAtMs}" data-hurry-at="5"></b></span>
                            <div class="timer-bar" data-starts-at="${game.playsFromMs}" data-ends-at="${game.endsAtMs}"><div class="timer-bar-fill"></div></div>
                        </div>
                        ${game.finishedAtMs ? '' : DungeonMinigames.intro(game, seed)}
                        ${result}
                    </div>
                </div>
                ${plate}
            </div>`;
    },

    /** The intro veil: what the game is and how to play, and the 3, 2, 1. It lifts by itself when play starts. */
    intro(game, seed) {
        return `
            <div class="dg-minigame-intro">
                <div class="dg-minigame-intro-stamp">${Theme.text('dungeon.minigameStamp', {}, seed)}</div>
                <div class="dg-minigame-intro-name">${DungeonMinigames.name(game.kind)}</div>
                <div class="dg-minigame-intro-how">${DungeonMinigames.howTo(game)}</div>
                <div class="dg-minigame-intro-prize">${Theme.text('dungeon.minigamePrize')}</div>
                ${UI.introCountdown(game.playsFromMs)}
            </div>`;
    },

    /** How it went: won (a reward to pick next) or out of time. Timed from when it finished (r0: minus how long ago, in ms). */
    result(game, seed, r0) {
        return game.won
            ? `<div class="dg-minigame-result is-won" style="--r0: ${r0}ms">
                   <div class="dg-minigame-result-title">${Theme.text('dungeon.minigameWon', {}, seed)}</div>
                   <div class="dg-minigame-result-sub">${Theme.text('dungeon.minigameWonSub')}</div>
               </div>`
            : `<div class="dg-minigame-result is-lost" style="--r0: ${r0}ms">
                   <div class="dg-minigame-result-title">${Theme.text('dungeon.minigameLost', {}, seed)}</div>
                   <div class="dg-minigame-result-sub">${Theme.text('dungeon.minigameLostSub')}</div>
               </div>`;
    },

    /**
     * The rune lock: the dial with its runes, the glowing mark, the needle spinning (a CSS animation timed from the
     * turn's start: --a0 its angle then, a full turn every --period, backwards when it turns the other way), the hits
     * so far as pips, and LOCKED over it after a miss until the buttons work again. A new press (its number went up)
     * flashes the dial gold for a hit or red for a miss.
     */
    runeLock(game, you, serverNow, seed) {
        const lock = game.runeLock;
        const speed = lock.degreesPerSecond;
        const period = speed ? 360 / Math.abs(speed) : 9999;
        const key = `rl:${you.stationId}:${game.introAtMs}`;
        const before = DungeonView.seen[key];
        DungeonView.seen[key] = lock.presses;
        const fresh = before !== undefined && lock.presses > before;
        if (fresh)
            DungeonView.effect(key, `<div class="dg-rl-pop ${lock.lastWasHit ? 'is-hit' : 'is-miss'}">${
                Theme.text(lock.lastWasHit ? 'dungeon.minigameHit' : 'dungeon.minigameMiss', {}, `${seed}:${lock.presses}`)}</div>`, 700);
        const flash = DungeonView.lasting(`${key}:flash`, fresh ? (lock.lastWasHit ? 'is-hit' : 'is-miss') : null, 450);
        const locked = lock.lockedUntilMs && lock.lockedUntilMs > serverNow ? Math.round(lock.lockedUntilMs - serverNow) : 0;
        const runes = ['A', 'B', 'C', 'D'].flatMap(l => [l, l]).map((l, i) =>
            `<i class="dg-rl-rune" style="--at: ${i * 45 + 22.5}deg; --glyph: url('${PixelSprites.url(`rune-${l}`)}')"></i>`).join('');
        const pips = Array.from({ length: lock.hitsNeeded }, (_, i) => `<i class="dg-rl-pip ${i < lock.hits ? 'is-lit' : ''}"></i>`).join('');
        return `
            <div class="dg-rl ${flash.cls} ${game.finishedAtMs ? (game.won ? 'is-open' : 'is-stuck') : ''}" style="--flash0: ${flash.t0}ms">
                <div class="dg-rl-dial" style="--mark: ${lock.markAngle}deg; --width: ${lock.markDegrees}deg">
                    <div class="dg-rl-mark"></div>
                    ${runes}
                    <div class="dg-rl-needle ${speed < 0 ? 'is-back' : ''} ${game.finishedAtMs ? 'is-still' : ''}"
                         style="--a0: ${lock.turnAngle}deg; --period: ${period}s; --turn0: ${Math.round(lock.turnStartedAtMs - serverNow)}ms"><i></i></div>
                    <div class="dg-rl-hub">${PixelSprites.html('icon-rw-Mastery', 'dg-rl-hub-icon')}</div>
                    ${locked ? `<div class="dg-rl-locked" style="--unlock: ${locked}ms">${Theme.text('dungeon.minigameLocked')}</div>` : ''}
                    ${DungeonView.activeEffects(key)}
                </div>
                <div class="dg-rl-pips">${pips}</div>
                <div class="dg-rl-press">${Theme.text('dungeon.minigameRuneLockPress')}</div>
            </div>`;
    },

    /**
     * Main screen: a mini game won since this lane was last drawn, once (DungeonView.noteMoments shows its banner),
     * or null.
     */
    newWin(key, delve) {
        const won = delve.minigamesWon || 0;
        const before = DungeonView.seen[`minigame:${key}`];
        DungeonView.seen[`minigame:${key}`] = won;
        return before !== undefined && won > before;
    },
};
