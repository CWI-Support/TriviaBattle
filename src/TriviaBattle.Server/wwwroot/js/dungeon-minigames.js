// Mini games on the station screen (Dungeon format). The rules are on the server (Core/Dungeon/Minigames): this only
// draws what the snapshot's you.minigame says. Every mini game has the same three parts, all timed from the server's
// clock so a redraw carries on where it was:
//   - the intro (from introAtMs): "MINI GAME!", the game's name and how to play, and a 3, 2, 1 to playsFromMs. It's a
//     veil over the game that lifts by itself when play starts (a CSS animation), so no redraw is needed then.
//   - play: the game itself, with its time limit running down (endsAtMs)
//   - the result (once finishedAtMs is set): won or out of time, until the fight (or a reward pick) comes back
// The arcade clock is stopped meanwhile, and says so.
// Games so far, each drawn from the numbers the server times presses by:
//   - the rune lock (runeLock): a needle spins round a dial; stop it on the glowing mark, 6 times in a row. The needle
//     is a CSS animation from its turn (its angle at turnStartedAtMs and its speed).
//   - the rune echo (runeEcho, a "Simon says"): the runes light up in a sequence (each step timed from showFromMs); press
//     them back in order, one longer each round.
//   - the chest shuffle (chestShuffle, a shell game): a coin goes in one of five chests, they swap places (followChests
//     moves them each frame from the round's list of swaps); pick it with the button under it, the action button the fifth.
//   - the ring puzzle (ringPuzzle): a hero's picture cut into rings, each turned out of line; A and D turn the ring
//     you're on, the action button moves out a ring. Each round has a ring more (and another hero).
// On the main screen a lane shows when its player is in one, and a win gets a banner.
// Needs ui.js, theme.js, pixel-sprites.js, dungeon-view.js and dungeon-battle.js. Styles: css/dungeon-minigames.css.

const DungeonMinigames = {
    /** The game's name, in the theme's words. Literal keys, so ThemeFilesTests can check them. */
    name(kind) {
        switch (kind) {
            case 'RuneLock': return Theme.text('dungeon.minigameRuneLock');
            case 'RuneEcho': return Theme.text('dungeon.minigameRuneEcho');
            case 'ChestShuffle': return Theme.text('dungeon.minigameChestShuffle');
            case 'RingPuzzle': return Theme.text('dungeon.minigameRingPuzzle');
            case 'Minigame': return Theme.text('dungeon.minigameGeneric'); // which one isn't known
            default: return UI.escape(kind);
        }
    },

    /** How to play it, for the intro. */
    howTo(minigame) {
        switch (minigame.kind) {
            case 'RuneLock': return Theme.text('dungeon.minigameRuneLockHow', { hits: minigame.runeLock.hitsNeeded });
            case 'RuneEcho': return Theme.text('dungeon.minigameRuneEchoHow', { rounds: minigame.runeEcho.rounds });
            case 'ChestShuffle': return Theme.text('dungeon.minigameChestShuffleHow', { rounds: minigame.chestShuffle.rounds });
            case 'RingPuzzle': return Theme.text('dungeon.minigameRingPuzzleHow', { rounds: minigame.ringPuzzle.rounds });
            default: return '';
        }
    },

    /** The game itself, by kind. */
    body(game, you, serverNow, seed) {
        switch (game.kind) {
            case 'RuneLock': return DungeonMinigames.runeLock(game, you, serverNow, seed);
            case 'RuneEcho': return DungeonMinigames.runeEcho(game, you, serverNow, seed);
            case 'ChestShuffle': return DungeonMinigames.chestShuffle(game, you, serverNow, seed);
            case 'RingPuzzle': return DungeonMinigames.ringPuzzle(game, you, serverNow, seed);
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
        const body = DungeonMinigames.body(game, you, serverNow, seed);
        // The result from resultFromMs: when it finished, or a moment later for a win that holds its picture (the ring puzzle).
        const result = game.finishedAtMs ? DungeonMinigames.result(game, seed, Math.round((game.resultFromMs ?? game.finishedAtMs) - serverNow)) : '';
        return `
            <div class="dg-station is-minigame">
                <div class="dg-world">
                    <div class="dg-minigame-room">${DungeonBattle.room()}</div>
                    <div class="dg-minigame is-${game.kind.toLowerCase()} ${game.resultEndsAtMs ? 'is-leaving' : ''}"
                         style="--t0: ${Math.round(game.introAtMs - serverNow)}ms; --play: ${toPlay}ms${game.resultEndsAtMs ? `; --out: ${Math.round(game.resultEndsAtMs - serverNow) - 450}ms` : ''}">
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
     * The rune echo (a "Simon says"): the four runes, one per answer button, in a square. While a round is shown each
     * rune in its sequence lights up in turn (a flash per step, timed from showFromMs: --at), "Watch..." gives way to
     * "Your turn!" at inputFromMs, and a pip per rune fills as they're pressed back. A press flashes its rune: gold
     * right, red wrong (then the round is shown again).
     */
    runeEcho(game, you, serverNow, seed) {
        const echo = game.runeEcho;
        const key = `re:${you.stationId}:${game.introAtMs}`;
        const before = DungeonView.seen[key];
        DungeonView.seen[key] = echo.presses;
        const fresh = before !== undefined && echo.presses > before;
        const flash = DungeonView.lasting(`${key}:flash`, fresh ? `${echo.lastWasRight ? 'is-right' : 'is-wrong'} rune-${echo.lastRune}` : null, 450);
        const at = ms => Math.round(ms - serverNow);
        const pads = [0, 1, 2, 3].map(rune => {
            const letter = UI.ANSWER_LETTERS[rune];
            const lights = echo.sequence.map((r, step) => r !== rune ? ''
                : `<i class="dg-re-lit" style="--at: ${at(echo.showFromMs + step * echo.stepMs)}ms; --lit: ${echo.litMs}ms"></i>`).join('');
            const pressed = flash.cls.endsWith(`rune-${rune}`) ? flash.cls.split(' ')[0] : '';
            return `
                <div class="dg-re-pad ${pressed}" data-letter="${letter}" style="--flash0: ${flash.t0}ms">
                    ${lights}
                    <i class="dg-re-glyph" style="--glyph: url('${PixelSprites.url(`rune-${letter}`)}')"></i>
                    <b class="dg-re-letter">${letter}</b>
                </div>`;
        }).join('');
        const pips = echo.sequence.map((_, i) => `<i class="dg-rl-pip ${i < echo.entered ? 'is-lit' : ''}"></i>`).join('');
        return `
            <div class="dg-re" style="--input: ${at(echo.inputFromMs)}ms">
                <div class="dg-re-round">${Theme.text('dungeon.minigameRound', { round: echo.round, rounds: echo.rounds })}</div>
                <div class="dg-re-status">
                    <span class="dg-re-watch">${Theme.text('dungeon.minigameRuneEchoWatch', {}, seed)}</span>
                    <span class="dg-re-turn">${Theme.text('dungeon.minigameRuneEchoTurn', {}, seed)}</span>
                </div>
                <div class="dg-re-pads">${pads}</div>
                <div class="dg-rl-pips">${pips}</div>
            </div>`;
    },

    /**
     * The chest shuffle: five chests in a row with a button under each (A-D, then the action button). The coin shows over
     * its chest for the reveal, then the chests move through the round's swaps (followChests: a little animation loop from
     * the server's list, so a redraw mid-shuffle carries on where it was). Once it's over, the coin's chest lights up where
     * it ended (and the one picked, if that was another).
     * After a right pick the snapshot is already the next round, starting a moment later (roundStartsAtMs): "Found it!"
     * and the coin popping out of the chest they picked (foundAt) show until then, and everything else (the coin dropping in, the words, the chests moving) is timed from that start,
     * because nothing comes from the server when it does.
     */
    chestShuffle(game, you, serverNow, seed) {
        const shuffle = game.chestShuffle;
        const id = `cs-${you.stationId}-${game.introAtMs}-${shuffle.round}-${shuffle.picks}`;
        const at = ms => Math.round(ms - serverNow);
        const over = !!game.finishedAtMs;
        const between = !over && shuffle.foundAt !== null && serverNow < shuffle.roundStartsAtMs;
        const coinPlace = shuffle.finalPlaces.indexOf(shuffle.coinChest);
        const chests = [0, 1, 2, 3, 4].map(chest => {
            const isCoin = chest === shuffle.coinChest;
            const lit = over && isCoin ? 'is-coin' : '';
            const wrong = over && !game.won && shuffle.lastPick !== null && shuffle.finalPlaces[shuffle.lastPick] === chest && !isCoin ? 'is-picked' : '';
            // Between rounds the chests already stand in order for the next one (chest n at place n; they all look
            // alike), so the chest at the place they picked is the one the coin pops out of.
            const found = between && chest === shuffle.foundAt ? 'is-found' : '';
            return `
                <div class="dg-cs-chest ${lit} ${wrong} ${found}" data-chest="${chest}" style="--x: ${over ? shuffle.finalPlaces.indexOf(chest) : chest}">
                    ${found ? PixelSprites.html('icon-coin', 'dg-cs-coin dg-cs-popped') : ''}
                    ${isCoin ? PixelSprites.html('icon-coin', 'dg-cs-coin') : ''}
                    ${PixelSprites.html('icon-chest', 'dg-cs-sprite')}
                </div>`;
        }).join('');
        const labels = [0, 1, 2, 3, 4].map(place =>
            `<span class="dg-cs-label ${place === 4 ? 'is-action' : ''}" data-letter="${UI.ANSWER_LETTERS[place] || ''}">${place === 4 ? Theme.text('dungeon.minigameActionButton') : UI.ANSWER_LETTERS[place]}</span>`).join('');
        // The chests follow the round's swaps from its start (they stand still until then), whenever it starts.
        if (!over)
            requestAnimationFrame(() => DungeonMinigames.followChests(id, shuffle));
        const found = between ? `<span class="dg-cs-found">${Theme.text('dungeon.minigameChestFound', {}, `${seed}:${shuffle.round}`)}</span>` : '';
        const status = over ? ''
            : `${found}
               <span class="dg-cs-watch">${Theme.text('dungeon.minigameChestWatch', {}, seed)}</span>
               <span class="dg-cs-shuffle">${Theme.text('dungeon.minigameChestShuffling', {}, seed)}</span>
               <span class="dg-cs-pick">${Theme.text('dungeon.minigameChestPick', {}, seed)}</span>`;
        return `
            <div class="dg-cs ${over ? 'is-over' : ''}" id="${id}"
                 style="--reveal-end: ${at(shuffle.roundStartsAtMs + shuffle.revealMs)}ms; --pick: ${at(shuffle.pickFromMs)}ms; --start: ${at(over ? game.finishedAtMs : shuffle.roundStartsAtMs)}ms; --picked: ${at(shuffle.lastPickAtMs ?? serverNow)}ms"
                 data-coin-place="${coinPlace}">
                <div class="dg-re-round">${Theme.text('dungeon.minigameRound', { round: shuffle.round, rounds: shuffle.rounds })}</div>
                <div class="dg-cs-status">${status}</div>
                <div class="dg-cs-row">${chests}</div>
                <div class="dg-cs-labels">${labels}</div>
            </div>`;
    },

    /**
     * Moves the chests of one round of the shuffle, every frame, from the server's numbers: before the swaps they stand in
     * order; swap i runs from reveal + i x swap, its two chests trading places along arcs (one over, one under). It stops
     * by itself once that round's view is gone from the page (a redraw starts a fresh one).
     */
    followChests(id, shuffle) {
        const root = document.getElementById(id);
        if (!root) return;
        const chests = [...root.querySelectorAll('.dg-cs-chest')];
        const step = () => {
            if (!root.isConnected) return;
            const t = UI.getServerNow() - shuffle.roundStartsAtMs - shuffle.revealMs;
            const places = [0, 1, 2, 3, 4]; // place -> chest
            const lift = {};                 // chest -> how far up (+) or down (-) it is, as a share of a chest
            const x = {};                    // chest -> place, in between while it moves
            const done = Math.max(0, Math.min(shuffle.swaps.length, Math.floor(t / shuffle.swapMs)));
            for (let i = 0; i < done; i++) {
                const [a, b] = shuffle.swaps[i];
                [places[a], places[b]] = [places[b], places[a]];
            }
            places.forEach((chest, place) => { x[chest] = place; lift[chest] = 0; });
            if (t >= 0 && done < shuffle.swaps.length) {
                const p = (t - done * shuffle.swapMs) / shuffle.swapMs;
                const ease = p < 0.5 ? 2 * p * p : 1 - Math.pow(-2 * p + 2, 2) / 2;
                const [a, b] = shuffle.swaps[done];
                const [ca, cb] = [places[a], places[b]];
                x[ca] = a + (b - a) * ease;
                x[cb] = b + (a - b) * ease;
                lift[ca] = Math.sin(Math.PI * p) * 0.55;
                lift[cb] = -Math.sin(Math.PI * p) * 0.35;
            }
            for (const el of chests) {
                const chest = Number(el.dataset.chest);
                el.style.setProperty('--x', x[chest]);
                el.style.setProperty('--lift', lift[chest]);
            }
            if (t < shuffle.swaps.length * shuffle.swapMs) requestAnimationFrame(step);
        };
        step();
    },

    /** The heroes whose pictures (media/heroes/{key}-head.png) the ring puzzle cuts up. */
    RING_PICTURES: ['tank', 'pugilist', 'wizard', 'healer', 'rogue', 'ranger', 'warlock'],

    /**
     * The ring puzzle: a hero's picture cut into rings round a middle disc, each turned out of line (offsets, in steps of
     * 360 / steps degrees clockwise). The ring A and D turn is lit gold. Each ring is a disc of the whole picture, the
     * bigger ones under the smaller, so only its band shows; a ring turns smoothly from where it was last drawn (the
     * short way round). Which hero is cosmetic: a different one each round, from the seed.
     * A picture put back together holds a moment, its last ring turning into place, then shows whole from wholeAtMs
     * (--whole: the cuts fade, it glows, "Solved!"; the last round's is the win, its result from resultFromMs). After a
     * solved round the snapshot is already the next one, starting at roundStartsAtMs: until then the solved picture lies
     * over it, and lifts by itself when the next starts (no redraw comes then).
     */
    ringPuzzle(game, you, serverNow, seed) {
        const puzzle = game.ringPuzzle;
        const over = !!game.finishedAtMs;
        const picture = round => {
            const all = DungeonMinigames.RING_PICTURES;
            return `/media/heroes/${all[(Theme.hash(seed) + (round - 1) * 3) % all.length]}-head.png`;
        };
        // One round's picture: each ring where it was last drawn (an angle that keeps counting past 360), turning on to
        // its offset the short way.
        const discs = (round, rings, offsets) => Array.from({ length: rings }, (_, i) => rings - 1 - i).map(ring => {
            const key = `rp:${you.stationId}:${game.introAtMs}:${round}:${ring}`;
            const deg = (offsets[ring] * 360) / puzzle.steps;
            const from = DungeonView.seen[key] ?? deg;
            const to = from + ((((deg - from) % 360) + 540) % 360) - 180;
            DungeonView.seen[key] = to;
            return `<i class="dg-rp-disc" style="--share: ${(ring + 1) / rings}; --from: ${from}deg; --deg: ${to}deg; background-image: url('${picture(round)}')"></i>`;
        }).join('');
        // The ring A and D turn: a gold band between its two edges (radii as shares of the whole picture's).
        const inner = (puzzle.selected / puzzle.rings) * 100, outer = ((puzzle.selected + 1) / puzzle.rings) * 100;
        const band = 'rgba(255, 210, 63, 0.16)', edge = 'var(--dg-mg-gold)';
        const lit = over ? '' : `<i class="dg-rp-lit" style="background: radial-gradient(circle closest-side,
                ${inner ? `transparent ${inner - 1.6}%, ${edge} ${inner - 1.6}%, ${edge} ${inner}%,` : ''}
                ${band} ${inner}%, ${band} ${outer - 1.6}%, ${edge} ${outer - 1.6}%, ${edge} ${outer}%, transparent ${outer}%)"></i>`;
        const between = !over && puzzle.solvedRings && serverNow < puzzle.roundStartsAtMs;
        const solved = between
            ? `<div class="dg-rp-board is-solving is-between">${discs(puzzle.round - 1, puzzle.solvedRings, Array(puzzle.solvedRings).fill(0))}</div>`
            : '';
        const status = over ? ''
            : `${between ? `<span class="dg-rp-found">${Theme.text('dungeon.minigameRingSolved', {}, `${seed}:${puzzle.round}`)}</span>` : ''}
               <span class="dg-rp-goal">${Theme.text('dungeon.minigameRingGoal', {}, seed)}</span>`;
        const at = ms => Math.round((ms ?? serverNow) - serverNow);
        return `
            <div class="dg-rp ${over ? 'is-over' : ''}" style="--start: ${at(puzzle.roundStartsAtMs)}ms; --whole: ${at(puzzle.wholeAtMs)}ms">
                <div class="dg-re-round">${Theme.text('dungeon.minigameRound', { round: puzzle.round, rounds: puzzle.rounds })}</div>
                <div class="dg-rp-status">${status}</div>
                <div class="dg-rp-frame">
                    <div class="dg-rp-board ${over && game.won ? 'is-solving' : ''}">${discs(puzzle.round, puzzle.rings, puzzle.offsets)}${lit}</div>
                    ${solved}
                </div>
                <div class="dg-rp-keys">
                    <span class="dg-rp-key" data-letter="A"><b>A</b>${Theme.text('dungeon.minigameRingLeft')}</span>
                    <span class="dg-rp-key is-action"><b>${Theme.text('dungeon.minigameActionButton')}</b>${Theme.text('dungeon.minigameRingNext')}</span>
                    <span class="dg-rp-key" data-letter="D"><b>D</b>${Theme.text('dungeon.minigameRingRight')}</span>
                </div>
            </div>`;
    },

    /**
     * Main screen: a mini game won since this lane was last drawn: its kind, once (DungeonView.noteMoments shows its
     * banner), or null.
     */
    newWin(key, delve) {
        // The game they were in (it's gone from the snapshot by the time the win is counted).
        if (delve.minigame) DungeonView.seen[`minigame-kind:${key}`] = delve.minigame;
        const won = delve.minigamesWon || 0;
        const before = DungeonView.seen[`minigame:${key}`];
        DungeonView.seen[`minigame:${key}`] = won;
        return before !== undefined && won > before ? DungeonView.seen[`minigame-kind:${key}`] || 'Minigame' : null;
    },
};

// The chest shuffle's coin (a placeholder, like every sprite in pixel-sprites.js). The chests are the rewards' icon-chest.
Object.assign(PixelSprites.SPRITES, {
    'icon-coin': {
        palette: { k: '#000000', y: '#ffd23f', o: '#c98a12', w: '#fff6c8' },
        rows: ['..kkkkk..', '.kyyyyyk.', 'kywwyyyok', 'kywyyyyok', 'kyyyyyyok', 'kyyyyyook', '.kyyyook.', '..kkkkk..'],
    },
});
