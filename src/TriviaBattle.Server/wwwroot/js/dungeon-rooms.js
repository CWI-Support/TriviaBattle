// The fork before a boss, and the special rooms behind its doors, on the station screen (Dungeon format). The rules are
// on the server (Core/Dungeon/Fork.cs, Core/Dungeon/Minigames/TreasureRoom.cs, TrapRoom.cs, HighStakesRoom.cs): this only
// draws what the snapshot says, timed from the server's clock so a redraw carries on where it was.
//   - the fork (you.fork): once the boss's entrance has played, two doors, A and B, in their buttons' colours, that look the
//     same (what's behind them is a secret: the snapshot doesn't know until one's opened), the faces of who's voted for
//     each, and the time left to vote. Once it's decided the door taken swings open on its room, and the room's intro
//     takes over.
//   - the rooms are mini games, so DungeonMinigames draws their frame (the intro, the time limit, the result); their
//     bodies are here: the treasure room's chest and lightning round, the trap's true/false statements (each one's truth
//     shows a moment after it's answered, then the next comes in by itself), and High Stakes' bet and brutal question.
// Needs ui.js, theme.js, pixel-sprites.js, hero-photo.js, dungeon-view.js, dungeon-battle.js, dungeon-minigames.js and
// dungeon-powerups.js. Styles: css/dungeon-rooms.css.

const DungeonRooms = {
    /** The mini games that are special rooms (behind a fork's doors). */
    KINDS: ['Treasure', 'Trap', 'HighStakes'],

    isRoom(kind) {
        return DungeonRooms.KINDS.includes(kind);
    },

    /** What's in a room, in a few words, once its door is open. Literal keys, so ThemeFilesTests can check them. */
    roomHint(kind) {
        switch (kind) {
            case 'Treasure': return Theme.text('dungeon.doorTreasureHint');
            case 'Trap': return Theme.text('dungeon.doorTrapHint');
            case 'HighStakes': return Theme.text('dungeon.doorHighStakesHint');
            default: return '';
        }
    },

    /** A room's picture (the chest, the spikes, the dice), or a closed door's "?" (kind null). */
    icon(kind, cssClass) {
        return PixelSprites.html(kind === 'Treasure' ? 'icon-chest' : `icon-room-${kind || 'Mystery'}`, cssClass);
    },

    /** The stamp as a room opens: on the fork as its door swings open, and on the room's intro. */
    stamp(kind, seed) {
        switch (kind) {
            case 'Treasure': return Theme.text('dungeon.roomTreasureStamp', {}, seed);
            case 'Trap': return Theme.text('dungeon.roomTrapStamp', {}, seed);
            case 'HighStakes': return Theme.text('dungeon.roomHighStakesStamp', {}, seed);
            default: return '';
        }
    },

    /** The stamp's length, so a long one shrinks to fit (--chars). */
    stampChars(kind, seed) {
        const key = { Treasure: 'dungeon.roomTreasureStamp', Trap: 'dungeon.roomTrapStamp', HighStakes: 'dungeon.roomHighStakesStamp' }[kind];
        return key ? Theme.plain(key, {}, seed).length : 10;
    },

    /** How to play a room, for its intro. */
    howTo(minigame) {
        switch (minigame.kind) {
            case 'Treasure': return Theme.text('dungeon.minigameTreasureHow', { right: minigame.treasure.rightNeeded });
            case 'Trap': return Theme.text('dungeon.minigameTrapHow', { statements: minigame.trap.statements });
            case 'HighStakes': return Theme.text('dungeon.minigameHighStakesHow', { hp: minigame.highStakes.penaltyHp });
            default: return '';
        }
    },

    /** What's to be won, for the intro (in place of the mini games' "win it for a reward"). */
    prize(minigame) {
        switch (minigame.kind) {
            case 'Treasure': return Theme.text('dungeon.roomTreasurePrize');
            case 'Trap': return Theme.text('dungeon.roomTrapPrize');
            case 'HighStakes': return Theme.text('dungeon.roomHighStakesPrize');
            default: return '';
        }
    },

    /** A room's body, by kind. */
    body(game, you, serverNow, seed) {
        switch (game.kind) {
            case 'Treasure': return DungeonRooms.treasure(game, you, serverNow, seed);
            case 'Trap': return DungeonRooms.trap(game, serverNow, seed);
            case 'HighStakes': return DungeonRooms.highStakes(game, serverNow);
            default: return '';
        }
    },

    // ---------- The fork ----------

    /** True while the station should show the fork: from the end of the boss's entrance. */
    forkShowing(you, serverNow) {
        return !!you.fork && !DungeonBattle.isPlaying(you, serverNow);
    },

    /**
     * The station at a fork: two doors that look the same (A and B, a "?" on each), who's voted for which, and the time
     * left to vote; once it's decided, the door taken swinging open (--taken: when) on what's behind it and the other one
     * dimming, with the room's stamp over them.
     */
    fork(match, you, serverNow, plate) {
        const fork = you.fork;
        const seed = `${match.matchId}:${you.stationId}:fork:${fork.opensAtMs}`;
        const at = ms => Math.round(ms - serverNow);
        const decided = fork.taken !== null && fork.taken !== undefined;
        const party = fork.voters.length > 1;
        const doors = Array.from({ length: fork.doors }, (_, i) => {
            const letter = UI.ANSWER_LETTERS[i];
            const taken = decided && fork.taken === i;
            const room = taken ? fork.room : null; // known only for the door taken, once it's opened
            const faces = fork.voters.filter(v => v.door === i)
                .map(v => HeroPhoto.portrait(match, v.stationId, 'dg-fork-face', '')).join('');
            return `
                <div class="dg-fork-door ${room ? `is-${room.toLowerCase()}` : 'is-mystery'} ${taken ? 'is-taken' : decided ? 'is-passed' : ''} ${fork.yourVote === i ? 'is-yours' : ''}" data-letter="${letter}">
                    <div class="dg-fork-arch">
                        <div class="dg-fork-light ${room ? `is-${room.toLowerCase()}` : 'is-mystery'}">${room ? DungeonRooms.icon(room, 'dg-fork-behind') : ''}</div>
                        <div class="dg-fork-leaf">${DungeonRooms.icon(null, 'dg-fork-icon')}<i class="dg-fork-handle"></i></div>
                    </div>
                    <b class="dg-fork-key">${letter}</b>
                    <div class="dg-fork-name">${room ? DungeonMinigames.name(room) : Theme.text('dungeon.forkDoor', { letter })}</div>
                    <div class="dg-fork-hint">${room ? DungeonRooms.roomHint(room) : Theme.text('dungeon.forkMystery')}</div>
                    <div class="dg-fork-votes">${faces}</div>
                </div>`;
        }).join('');
        const voted = fork.yourVote !== null && fork.yourVote !== undefined;
        const status = decided
            ? `<div class="dg-fork-reveal" style="--chars: ${DungeonRooms.stampChars(fork.room, seed)}">${DungeonRooms.stamp(fork.room, seed)}</div>`
            : `<div class="dg-fork-status">${voted
                    ? (party ? Theme.text('dungeon.forkVoted', { door: Theme.raw(Theme.text('dungeon.forkDoor', { letter: UI.ANSWER_LETTERS[fork.yourVote] })) }) : '')
                    : Theme.text('dungeon.forkPress')}</div>
               <div class="dg-minigame-limit">
                   <span>${Theme.text('dungeon.forkTimeLeft')} <b data-ends-at="${fork.endsAtMs}" data-hurry-at="3"></b></span>
                   <div class="timer-bar" data-starts-at="${fork.votesFromMs}" data-ends-at="${fork.endsAtMs}"><div class="timer-bar-fill"></div></div>
               </div>`;
        const querie = DungeonView.querie(you, serverNow, '', true);
        const said0 = querie ? Math.round(you.quip.atMs - serverNow) : 0;
        return `
            <div class="dg-station is-minigame is-fork">
                <div class="dg-world">
                    <div class="dg-minigame-room">${DungeonBattle.room()}</div>
                    <div class="dg-minigame dg-fork ${decided ? 'is-decided' : ''}" style="--t0: ${at(fork.opensAtMs)}ms; --taken: ${decided ? at(fork.takenAtMs) : 0}ms">
                        <div class="dg-minigame-top">
                            <span class="dg-minigame-name">${Theme.text('dungeon.forkTitle', {}, seed)}</span>
                            <span class="dg-minigame-clock">${Theme.text('dungeon.minigameClockStopped')}
                                <b>${Theme.text('dungeon.time')} <span ${UI.matchClock(match)}></span></b></span>
                        </div>
                        <div class="dg-fork-sub">${party ? Theme.text('dungeon.forkSubParty') : Theme.text('dungeon.forkSub')}</div>
                        <div class="dg-fork-doors">${doors}</div>
                        ${status}
                        <div class="dg-mg-querie">${querie ? `<div class="dg-feedback is-querie" style="--said0: ${said0}ms">${querie.html}</div>` : ''}</div>
                    </div>
                </div>
                ${plate}
            </div>`;
    },

    // ---------- The rooms ----------

    /** A question's four answers as rows in their buttons' colours (the pop quiz's look); mark(i) adds a class to one. */
    answers(answers, mark) {
        return `
            <div class="dg-pq-answers">${answers.map((text, i) => `
                <div class="dg-pq-answer ${mark ? mark(i) : ''}" data-letter="${UI.ANSWER_LETTERS[i]}">
                    <b>${UI.ANSWER_LETTERS[i]}</b><span>${UI.escape(text)}</span>
                </div>`).join('')}
            </div>`;
    },

    /**
     * The treasure room: the chest with a pip for each right answer it needs, and the question up (they come one after
     * another, no pause). Each new answer flashes the chest, gold for right and red for wrong; a wrong one jams the lock
     * until the buttons work again (--unlock). Opened: the chest's open, and what was in it pops out.
     */
    treasure(game, you, serverNow, seed) {
        const t = game.treasure;
        const over = !!game.finishedAtMs;
        const opened = over && game.won;
        const key = `tr:${you.stationId}:${game.introAtMs}`;
        const answeredBefore = DungeonView.seen[`${key}:answered`];
        DungeonView.seen[`${key}:answered`] = t.right + t.wrong;
        const isNew = answeredBefore !== undefined && t.right + t.wrong > answeredBefore;
        const flash = DungeonView.lasting(`${key}:flash`, isNew ? (t.lastWasRight ? 'is-right' : 'is-wrong') : null, 500);
        const jammedFor = t.lockedUntilMs && t.lockedUntilMs > serverNow ? Math.round(t.lockedUntilMs - serverNow) : 0;
        const pips = Array.from({ length: t.rightNeeded }, (_, i) => `<i class="dg-rl-pip ${i < t.right ? 'is-lit' : ''}"></i>`).join('');
        const prize = opened && t.prize ? DungeonPowerUps.icon(t.prize, 'dg-treasure-prize') : '';
        return `
            <div class="dg-treasure ${flash.cls} ${opened ? 'is-open' : ''}" style="--flash0: ${flash.t0}ms; --r0: ${over ? Math.round(game.finishedAtMs - serverNow) : 0}ms">
                <div class="dg-treasure-chest">
                    ${PixelSprites.html(opened ? 'icon-chest-open' : 'icon-chest', 'dg-treasure-sprite')}
                    ${prize}
                </div>
                <div class="dg-rl-pips">${pips}</div>
                ${over ? '' : `
                    <div class="dg-scroll dg-pq-card"><div class="dg-scroll-tag">${UI.escape(t.category)}</div><div>${UI.escape(t.text)}</div></div>
                    <div class="dg-treasure-answers ${jammedFor ? 'is-jammed' : ''}" style="--unlock: ${jammedFor}ms">
                        ${DungeonRooms.answers(t.answers)}
                        ${jammedFor ? `<div class="dg-treasure-jammed">${Theme.text('dungeon.roomTreasureJammed', {}, `${seed}:${t.wrong}`)}</div>` : ''}
                    </div>`}
            </div>`;
    },

    /**
     * The trap room: a pip per statement (gold for each got right, red for each that sprang it), the statement up (a
     * question with one answer shown: true or false?), the two buttons that answer it, and its time running out. After
     * each, the last one shows a moment with its truth (and what a miss cost), then gives way to the next by itself (CSS
     * from --ask), as nothing comes from the server when it does. Over: the last one stays up.
     */
    trap(game, serverNow, seed) {
        const t = game.trap;
        const over = !!game.finishedAtMs;
        const last = t.last;
        const endedOnAnswer = over && !!last;
        const revealing = !!last && (endedOnAnswer || t.askedAtMs > serverNow);
        const pips = Array.from({ length: t.statements }, (_, i) =>
            `<i class="dg-trap-pip ${i < t.right ? 'is-right' : i < t.right + t.misses ? 'is-miss' : ''}"></i>`).join('');
        const keys = (said, truth) => `
            <div class="dg-trap-keys">
                <div class="dg-trap-key ${said === true ? 'is-said' : ''} ${truth === true ? 'is-truth' : ''}" data-letter="A"><b>A</b>${Theme.text('dungeon.roomTrapTrue')}</div>
                <div class="dg-trap-key ${said === false ? 'is-said' : ''} ${truth === false ? 'is-truth' : ''}" data-letter="B"><b>B</b>${Theme.text('dungeon.roomTrapFalse')}</div>
            </div>`;
        const card = (number, category, text, shown) => `
            <div class="dg-scroll dg-trap-card">
                <div class="dg-scroll-tag">${Theme.text('dungeon.roomTrapStatement', { number, statements: t.statements })}${category ? ` · ${UI.escape(category)}` : ''}</div>
                <div class="dg-trap-question">${UI.escape(text)}</div>
                <div class="dg-trap-claim"><small>${Theme.text('dungeon.roomTrapClaim')}</small><b>${UI.escape(shown)}</b></div>
            </div>`;
        const reveal = revealing ? `
            <div class="dg-trap-page is-reveal ${endedOnAnswer ? 'is-final' : ''} ${last.wasRight ? 'is-safe' : 'is-sprung'}" style="--said: ${Math.round(last.atMs - serverNow)}ms">
                ${card(last.number, '', last.text, last.shown)}
                ${keys(last.said, last.wasTrue)}
                <div class="dg-trap-verdict">
                    <b>${last.wasRight ? Theme.text('dungeon.roomTrapSafe', {}, `${seed}:${last.number}`)
                        : last.said === null ? Theme.text('dungeon.roomTrapSlow') : Theme.text('dungeon.roomTrapOuch', {}, `${seed}:${last.number}`)}</b>
                    <span>${last.wasTrue ? Theme.text('dungeon.roomTrapWasTrue') : Theme.text('dungeon.roomTrapWasFalse', { answer: last.correct })}</span>
                    ${last.wasRight ? '' : `<span class="dg-trap-hurt">${Theme.text('dungeon.roomTrapHurt', { amount: last.damage })}</span>`}
                </div>
            </div>` : '';
        const next = endedOnAnswer ? '' : `
            <div class="dg-trap-page ${revealing ? 'is-next' : ''}">
                ${card(t.number, t.category, t.text, t.shown)}
                ${keys(null, null)}
                <div class="timer-bar dg-trap-timer" data-starts-at="${t.askedAtMs}" data-ends-at="${t.answerByMs}"><div class="timer-bar-fill"></div></div>
            </div>`;
        return `
            <div class="dg-trap ${over ? (game.won ? 'is-won' : 'is-lost') : ''}" style="--ask: ${Math.round(t.askedAtMs - serverNow)}ms">
                <div class="dg-trap-spikes"></div>
                <div class="dg-trap-pips">${pips}</div>
                <div class="dg-trap-stage">${reveal}${next}</div>
            </div>`;
    },

    /**
     * High Stakes. First the bet: the dice, what's at stake, A to bet and D to pass, and the time to choose. Once they've
     * bet, its brutal question (the answers dim until they can be pressed: --ask) and the time to answer it; once it's
     * answered, the right answer lit and the one picked (if another) red.
     */
    highStakes(game, serverNow) {
        const h = game.highStakes;
        const at = ms => Math.round(ms - serverNow);
        if (!h.betAtMs) {
            return `
                <div class="dg-stakes ${h.passed ? 'is-passed' : ''}">
                    <div class="dg-stakes-dice">${PixelSprites.html('icon-room-HighStakes', 'dg-stakes-sprite')}</div>
                    <div class="dg-stakes-stake">${Theme.text('dungeon.roomHighStakesStake', { hp: h.penaltyHp })}</div>
                    <div class="dg-trap-keys dg-stakes-keys">
                        <div class="dg-trap-key" data-letter="A"><b>A</b>${Theme.text('dungeon.roomHighStakesBet')}</div>
                        <div class="dg-trap-key" data-letter="D"><b>D</b>${Theme.text('dungeon.roomHighStakesPass')}</div>
                    </div>
                    <div class="timer-bar dg-trap-timer" data-starts-at="${game.playsFromMs}" data-ends-at="${h.decideByMs}"><div class="timer-bar-fill"></div></div>
                </div>`;
        }
        const answered = h.answeredAtMs !== null && h.answeredAtMs !== undefined;
        const mark = answered ? i => i === h.correct ? 'is-right' : i === h.chosen ? 'is-picked' : 'is-out' : null;
        return `
            <div class="dg-stakes is-betting ${answered ? 'is-answered' : ''}" style="--ask: ${at(h.askedAtMs)}ms">
                <div class="dg-stakes-dice is-small">${PixelSprites.html('icon-room-HighStakes', 'dg-stakes-sprite')}</div>
                <div class="dg-scroll dg-pq-card"><div class="dg-scroll-tag">${Theme.text('dungeon.roomHighStakesAsks')} · ${UI.escape(h.category)}</div><div>${UI.escape(h.text)}</div></div>
                <div class="dg-stakes-answers">${DungeonRooms.answers(h.answers, mark)}</div>
                ${answered ? '' : `<div class="timer-bar dg-trap-timer" data-starts-at="${h.askedAtMs}" data-ends-at="${h.answerByMs}"><div class="timer-bar-fill"></div></div>`}
            </div>`;
    },

    /** How a room went, over the room (timed from r0, like every mini game's result). */
    result(game, seed, r0) {
        const box = (cls, title, sub) => `
            <div class="dg-minigame-result ${cls}" style="--r0: ${r0}ms">
                <div class="dg-minigame-result-title">${title}</div>
                <div class="dg-minigame-result-sub">${sub}</div>
            </div>`;
        switch (game.kind) {
            case 'Treasure': {
                const t = game.treasure;
                if (!game.won)
                    return box('is-lost', Theme.text('dungeon.roomTreasureLost', {}, seed), Theme.text('dungeon.roomTreasureLostSub'));
                const sub = t.prize ? Theme.text('dungeon.roomTreasurePrizeSub', { powerUp: Theme.raw(DungeonPowerUps.name(t.prize)) })
                    : t.healed ? Theme.text('dungeon.roomTreasurePotionSub', { amount: t.healed })
                    : Theme.text('dungeon.roomTreasureFullSub');
                return box('is-won', Theme.text('dungeon.roomTreasureWon', {}, seed), sub);
            }
            case 'Trap': {
                const t = game.trap;
                if (!game.won)
                    return box('is-lost', Theme.text('dungeon.roomTrapLost', {}, seed), Theme.text('dungeon.roomTrapLostSub', { amount: t.damage }));
                return box('is-won', Theme.text('dungeon.roomTrapWon', {}, seed),
                    t.healed ? Theme.text('dungeon.roomTrapWonSub', { amount: t.healed }) : Theme.text('dungeon.roomTrapWonFullSub'));
            }
            case 'HighStakes': {
                const h = game.highStakes;
                if (h.passed)
                    return box('is-left', Theme.text('dungeon.roomHighStakesPassed', {}, seed), Theme.text('dungeon.roomHighStakesPassedSub'));
                if (game.won)
                    return box('is-won', Theme.text('dungeon.roomHighStakesWon', {}, seed), Theme.text('dungeon.roomHighStakesWonSub'));
                const answer = h.answers?.[h.correct] ?? '';
                return box('is-lost', Theme.text('dungeon.roomHighStakesLost', {}, seed), h.chosen === null
                    ? Theme.text('dungeon.roomHighStakesSlowSub', { amount: h.penalty, answer })
                    : Theme.text('dungeon.roomHighStakesLostSub', { amount: h.penalty, answer }));
            }
            default:
                return '';
        }
    },
};

// The rooms' pixel pictures (placeholders, like every sprite in pixel-sprites.js): spikes, a pair of dice, a door's "?",
// and the chest opened (the closed one is the rewards' icon-chest).
Object.assign(PixelSprites.SPRITES, {
    'icon-room-Trap': {
        palette: { s: '#e2e8f0', S: '#7d8b9c', k: '#000000', r: '#9b1c2e' },
        rows: ['.s...s...s.', '.s...s...s.', 'sSs.sSs.sSs', 'sSs.sSs.sSs', 'SSSsSSSsSSS', 'kkkkkkkkkkk', 'krrrrrrrrrk', 'kkkkkkkkkkk'],
    },
    'icon-room-HighStakes': {
        palette: { k: '#000000', w: '#fff6e0', g: '#d8cbb0', r: '#e02a3a' },
        rows: ['kkkkkkk......', 'kwwwwgk......', 'kwrwwgk.kkkkk', 'kwwwwgk.kwwwk', 'kwwwrgk.kwrwk', 'kggggk..kwwwk',
               'kkkkkk..krwrk', '........kwwwk', '........kkkkk'],
    },
    'icon-room-Mystery': {
        palette: { c: '#6ff0de', C: '#2a9d8f' },
        rows: ['..ccccc..', '.cC...cC.', '.cC...cC.', '......cC.', '....ccC..', '....cC...', '.........', '....cC...', '....cC...'],
    },
    'icon-chest-open': {
        palette: { k: '#000000', b: '#b0743a', B: '#6b4220', y: '#ffd23f', w: '#fff6d8' },
        rows: ['kkkkkkkkk', 'kBBBBBBBk', 'kbbbbbbbk', 'kywyywyyk', 'kbbbybbbk', 'kbbbbbbbk', 'kBBBBBBBk', 'kkkkkkkkk'],
    },
});
