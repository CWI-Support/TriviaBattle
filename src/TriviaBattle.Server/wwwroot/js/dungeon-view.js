// Dungeon Quiz Quest views, shared by the player station screens and the main screen.
// The server sends each adventurer's (or party's) "delve": floor, monster, HP, hourglass, the path
// of monsters down to the Depth's boss, and the last hit dealt and taken (each numbered). This file
// turns that into HTML, and works out which hits are new since the last draw so each one animates
// once (screens redraw on every change).
//
// Both screens group things the same way so they read at a glance:
//   - the trail: where you are on the way to the boss (beaten monsters greyed, the current one framed)
//   - the monster: its name, its HP (red) and how long until it attacks, right under it
//   - you: your name, your HP (green), score and kills
// Layout is in css/dungeon.css; looks in the theme. Wording via Theme.text('dungeon.*').
// Needs ui.js, theme.js and pixel-sprites.js on the page.

const DungeonView = {
    seen: {}, // owner key -> { dealt, taken, monster } as last drawn

    /** What happened to this owner since they were last drawn: a new hit dealt, a hit taken, a new monster. */
    news(key, delve) {
        const before = DungeonView.seen[key];
        const now = { dealt: delve.lastDealt?.number || 0, taken: delve.lastTaken?.number || 0, monster: `${delve.floor}:${delve.room}` };
        DungeonView.seen[key] = now;
        if (!before) return { dealt: null, taken: null, newMonster: false };
        return {
            dealt: now.dealt > before.dealt ? delve.lastDealt : null,
            taken: now.taken > before.taken ? delve.lastTaken : null,
            newMonster: now.monster !== before.monster,
        };
    },

    /** Forget what was drawn (a new match). */
    reset() {
        DungeonView.seen = {};
    },

    icon(name) {
        return PixelSprites.html(`icon-${name}`, 'dg-icon');
    },

    /** A hit-point bar with a heart in front: red for the monster, green for the adventurer. */
    hp(value, max, cssClass) {
        const percent = max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0;
        return `
            <div class="dg-hp ${cssClass}">
                ${DungeonView.icon('heart')}
                <div class="dg-bar">
                    <div class="dg-bar-fill" style="width: ${percent}%"></div>
                    <span class="dg-bar-text">${value} / ${max}</span>
                </div>
            </div>`;
    },

    /**
     * The way down to the Depth's boss: a little icon per monster, grouped by floor with stairs between.
     * Beaten ones are greyed out, the one being fought is framed in gold, the boss is framed in red.
     */
    trail(delve) {
        const floors = [];
        for (const step of delve.path) {
            if (floors.at(-1)?.[0].floor !== step.floor) floors.push([]);
            floors.at(-1).push(step);
        }
        const state = step =>
            step.floor === delve.floor && step.room === delve.room ? 'is-current'
            : step.floor < delve.floor || (step.floor === delve.floor && step.room < delve.room) ? 'is-beaten'
            : 'is-ahead';
        return `
            <div class="dg-trail" style="--steps: ${delve.path.length + (floors.length - 1) * 0.5}">
                ${floors.map(floor => `
                    <div class="dg-trail-floor ${floor[0].floor === delve.floor ? 'is-here' : ''}">
                        ${floor.map(step => `
                            <span class="dg-step ${state(step)} ${step.isBoss ? 'is-boss' : ''}">${PixelSprites.html(step.sprite)}</span>`).join('')}
                    </div>`).join(`<span class="dg-trail-stairs">${DungeonView.icon('stairs')}</span>`)}
            </div>`;
    },

    /** The monster, animated by what just happened: shaken by a hit, lunging when it attacks, dropping in when new. */
    monster(delve, news) {
        const m = delve.monster;
        const states = [
            m.isBoss ? 'is-boss' : '',
            news.newMonster ? 'is-new' : news.dealt ? 'is-hit' : '',
            news.taken ? 'is-attacking' : '',
        ].join(' ');
        const floater = news.dealt
            ? `<div class="dg-float ${news.dealt.crit ? 'is-crit' : ''}">-${news.dealt.damage}${news.dealt.crit ? '!' : ''}</div>`
            : news.taken ? `<div class="dg-float is-taken">-${news.taken.damage}</div>` : '';
        const slain = news.dealt?.killed
            ? `<div class="dg-slain">${Theme.text(news.dealt.floorCleared ? 'dungeon.floorCleared' : 'dungeon.slain', { monster: news.dealt.monsterName })}</div>`
            : '';
        return `
            <div class="dg-monster-wrap">
                ${PixelSprites.html(m.sprite, `dg-monster ${states}`)}
                ${floater}${slain}
            </div>`;
    },

    /** The monster's nameplate: its name, its HP, and "Attacks in 7" with the hourglass draining beside it. */
    enemy(delve) {
        const m = delve.monster;
        const running = !!delve.attackAtMs;
        const label = running
            ? Theme.text('dungeon.attackIn', { seconds: Theme.raw(`<span data-ends-at="${delve.attackAtMs}" data-hurry-at="3"></span>`) })
            : Theme.text('dungeon.attackPaused');
        const hourglass = running
            ? `<div class="timer-bar dg-hourglass" data-starts-at="${delve.attackStartedAtMs}" data-ends-at="${delve.attackAtMs}"><div class="timer-bar-fill"></div></div>`
            : `<div class="dg-hourglass is-stopped"></div>`;
        return `
            <div class="dg-enemy ${m.isBoss ? 'is-boss' : ''}">
                <div class="dg-enemy-name">${UI.escape(m.name)}</div>
                ${DungeonView.hp(m.hp, m.maxHp, 'is-enemy')}
                <div class="dg-attack ${running ? '' : 'is-stopped'}">
                    ${DungeonView.icon('sword')}
                    <span class="dg-attack-label">${label}</span>
                    ${hourglass}
                </div>
            </div>`;
    },

    /**
     * The banner between the monster and the answers: how the last answer went, in the Host's words.
     * lastAnswer (the player's YourAnswerView) says whether it was a hit, a miss or too slow.
     * Returns { html, tone }: tone colours the banner (is-go, is-good, is-great, is-bad).
     */
    feedback(delve, lastAnswer, seed) {
        if (!lastAnswer) return { html: Theme.text('dungeon.go', {}, seed), tone: 'is-go' };
        const s = `${seed}:${lastAnswer.number}`;

        if (lastAnswer.isCorrect && delve.lastDealt) {
            const hit = delve.lastDealt;
            const values = { monster: hit.monsterName, damage: hit.damage, streak: delve.streak };
            if (hit.floorCleared) return { html: Theme.text('dungeon.floorClearedLine', values, s), tone: 'is-great' };
            if (hit.killed) return { html: Theme.text('dungeon.kill', values, s), tone: 'is-great' };
            if (hit.combo) return { html: Theme.text('dungeon.combo', values, s), tone: 'is-great' };
            if (hit.crit) return { html: Theme.text('dungeon.crit', values, s), tone: 'is-great' };
            return { html: Theme.text('dungeon.hit', values, s), tone: 'is-good' };
        }

        const taken = delve.lastTaken;
        const values = { monster: taken?.monsterName || delve.monster.name, damage: taken?.damage || 0 };
        const headline = lastAnswer.timedOut ? Theme.text('dungeon.tooSlow', values, s) : Theme.text('dungeon.wrong', values, s);
        const itWas = Theme.text('player.itWas', { letter: UI.ANSWER_LETTERS[lastAnswer.correctIndex], answer: lastAnswer.correctAnswer });
        return { html: `${headline}<div class="dg-feedback-detail">${itWas}</div>`, tone: 'is-bad' };
    },

    /** Score and kills, each with its label/icon so neither is just a bare number. */
    stats(score, kills) {
        return `
            <div class="dg-stats">
                <div class="dg-stat"><small>${Theme.text('dungeon.score')}</small><b>${score.toLocaleString()}</b></div>
                <div class="dg-stat is-kills">${DungeonView.icon('skull')}<b>${kills}</b></div>
            </div>`;
    },

    /**
     * The whole station screen while the clock runs, top to bottom: where you are (floor, trail, time
     * left), the question, the monster with its nameplate, the Host's banner, the answers, and you.
     */
    station(match, you, serverNow, stationColor) {
        const delve = you.delve;
        const q = you.question;
        const news = DungeonView.news(`station:${you.stationId}`, delve);
        const downed = delve.downedUntilMs && delve.downedUntilMs > serverNow;
        const stunned = !downed && you.lockedOutUntilMs && you.lockedOutUntilMs > serverNow;
        const seed = `${match.matchId}:${you.stationId}`;

        const answers = q.answers.map((text, i) => `
            <div class="dg-answer ${downed || stunned ? 'is-dimmed' : ''}" data-letter="${UI.ANSWER_LETTERS[i]}">
                <span class="dg-rune">${UI.ANSWER_LETTERS[i]}</span>
                <span class="dg-answer-text">${UI.escape(text)}</span>
            </div>`).join('');

        const overlay = downed
            ? `<div class="dg-overlay is-downed">${Theme.text('dungeon.downed', { seconds: Theme.raw(`<span data-ends-at="${delve.downedUntilMs}"></span>`) }, `${seed}:${delve.lastTaken?.number}`)}</div>`
            : stunned ? `<div class="dg-overlay is-stunned">${Theme.text('dungeon.stunned', {}, `${seed}:${delve.lastTaken?.number}`)}</div>` : '';

        const feedback = DungeonView.feedback(delve, you.lastAnswer, seed);
        const category = match.category.includes(',') ? `<div class="dg-scroll-tag">${UI.escape(q.category)}</div>` : '';

        return `
            <div class="dg-station ${news.taken ? 'is-hurt' : ''}">
                <div class="dg-top">
                    <div class="dg-top-line">
                        <span class="dg-floor-badge">${Theme.text('dungeon.floorShort', { floor: delve.floor })}</span>
                        <span class="dg-depth-name">${UI.escape(delve.depthName)}</span>
                        <span class="dg-clock"><small>${Theme.text('dungeon.time')}</small><span data-ends-at="${match.phaseEndsAtMs}" data-hurry-at="10"></span></span>
                    </div>
                    ${DungeonView.trail(delve)}
                </div>
                <div class="dg-scroll">${category}${UI.questionMedia(q.mediaUrl)}<div>${UI.escape(q.text)}</div></div>
                <div class="dg-stage">
                    ${DungeonView.monster(delve, news)}
                    ${overlay}
                </div>
                ${DungeonView.enemy(delve)}
                <div class="dg-feedback ${feedback.tone} ${news.dealt || news.taken ? 'pop' : ''}">${feedback.html}</div>
                <div class="dg-answers">${answers}</div>
                <div class="dg-plate">
                    ${PixelSprites.html('knight', 'dg-portrait', { r: stationColor })}
                    <div class="dg-plate-info">
                        <div class="dg-plate-name">${UI.escape(you.name)}</div>
                        ${DungeonView.hp(delve.hp, delve.maxHp, 'is-hero')}
                    </div>
                    ${DungeonView.stats(you.score, delve.kills)}
                </div>
            </div>`;
    },

    /** One adventurer's (or party's) column on the main screen. rank is 1-based, or null with one lane. */
    lane({ key, name, color, delve, score, rank, isLeader, serverNow }) {
        const news = DungeonView.news(`lane:${key}`, delve);
        const downed = delve.downedUntilMs && delve.downedUntilMs > serverNow;
        return `
            <div class="dg-lane ${isLeader ? 'is-leader' : ''} ${news.taken ? 'is-hurt' : ''}" style="--lane-color: ${color}">
                <div class="dg-lane-head">
                    ${rank ? `<span class="dg-rank">${UI.ordinal(rank)}</span>` : ''}
                    <span class="dg-lane-name">${name}</span>
                    <span class="dg-floor-badge">${Theme.text('dungeon.floorShort', { floor: delve.floor })}</span>
                </div>
                ${DungeonView.trail(delve)}
                <div class="dg-lane-stage">
                    ${DungeonView.monster(delve, news)}
                    ${downed ? `<div class="dg-overlay is-downed small">${Theme.text('dungeon.downedShort')}</div>` : ''}
                </div>
                ${DungeonView.enemy(delve)}
                <div class="dg-lane-hero">
                    ${PixelSprites.html('knight', 'dg-portrait small', { r: color })}
                    ${DungeonView.hp(delve.hp, delve.maxHp, 'is-hero')}
                </div>
                ${DungeonView.stats(score, delve.kills)}
            </div>`;
    },
};
