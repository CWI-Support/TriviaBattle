// Dungeon Quiz Quest views, shared by the player station screens and the main screen.
// The server sends each adventurer's "delve": their party's floor, monster, path of monsters down to
// the Depth's boss and last hit on the monster (teammates share all of these), plus their own HP,
// hourglass, last hit dealt and last hit taken. Hits are numbered. This file turns that into HTML,
// and works out which hits are new since the last draw so each one animates once (screens redraw on
// every change).
//
// The station screen has two modes. While a question is up, the encounter (js/dungeon-encounter.js):
// the monster's nameplate, the question, the monster big in its room, the hourglass and the answers.
// Between questions, the battle beat takes the whole screen (js/dungeon-battle.js). The main screen adds big banners across
// the room for the moments worth shouting about (DungeonView.noteMoments).
//
// Short effects (damage numbers, "+10s", banners) are kept in a little store with the time they
// started, so a redraw in the middle (someone else answered) carries them on instead of cutting them off.
// The same goes for short animation states (a monster hit, lunging or dropping in; a lane shaking): see
// lasting(). Idle loops (the monster's bob) run on one shared beat (--idle0), so a redraw doesn't restart them.
//
// Both screens group things the same way so they read at a glance:
//   - the trail: where you are on the way to the boss (beaten monsters greyed, the current one framed,
//     a pack's monsters grouped as one room)
//   - the monster: its name, its HP (red, green while poisoned) and how long until it attacks, right under
//     it; in a pack, the ones waiting their turn in a strip with their own little HP bars (packStrip)
//   - you: your name, your HP (green), score and kills
//   - your party (2v2 Combined): each teammate's HP, and "DOWN 1/3" while they wait for a revive.
//     Downed in a party, you get a teammate's question instead of your own, to shout them the answer.
// Layout is in css/dungeon.css; looks in the theme. Wording via Theme.text('dungeon.*').
// Needs ui.js, theme.js and pixel-sprites.js on the page.

const DungeonView = {
    seen: {},    // owner key -> { dealt, taken, monster } as last drawn
    effects: {}, // key -> [{ html, at, ms }]: short effects still playing (see effect / activeEffects)
    states: {},  // key -> { cls, at }: short animation states still playing (see lasting)
    firstSeen: {}, // key -> when it was first drawn (see since)

    /** Starts a short effect under a key: it's drawn by activeEffects until it has played for ms. */
    effect(key, html, ms) {
        (DungeonView.effects[key] ??= []).push({ html, at: Date.now(), ms });
    },

    /**
     * The effects still playing under a key, each carrying on where it was: the wrapper sets --t0 to
     * minus its age, and the effects' CSS animations use it as their delay.
     */
    activeEffects(key) {
        const now = Date.now();
        const playing = (DungeonView.effects[key] || []).filter(e => now - e.at < e.ms);
        DungeonView.effects[key] = playing;
        return playing.map(e => `<div class="dg-effect" style="--t0: ${e.at - now}ms">${e.html}</div>`).join('');
    },

    /**
     * A short animation state under a key (a CSS class such as 'is-hit'): it starts when start is given and
     * is kept, with its start time, for ms, so a redraw in the middle carries it on instead of cutting it
     * off. Returns the class to add (or '') and t0, minus its age in ms, for the animation's delay (--t0).
     */
    lasting(key, start, ms) {
        const now = Date.now();
        if (start) DungeonView.states[key] = { cls: start, at: now };
        const state = DungeonView.states[key];
        if (!state || now - state.at >= ms) return { cls: '', t0: 0 };
        return { cls: state.cls, t0: state.at - now };
    },

    /**
     * Minus how long ago something was first drawn under this key (for --t0): for a state the server turns
     * on mid-question (a Warlock's patron showing the answer), so it animates in once, not on every redraw.
     */
    since(key) {
        const now = Date.now();
        DungeonView.firstSeen[key] ??= now;
        return DungeonView.firstSeen[key] - now;
    },

    /** The shared beat for idle loops: as a negative delay, every redraw joins the loop where it is. */
    idlePhase(loopMs) {
        return -(Date.now() % loopMs);
    },

    /**
     * What happened to this owner since they were last drawn: a new hit on the monster (by anyone in
     * the party, so teammates see each other's hits land), a hit taken, a new monster.
     */
    news(key, delve) {
        const before = DungeonView.seen[key];
        const now = { dealt: delve.lastHit?.number || 0, taken: delve.lastTaken?.number || 0, monster: `${delve.floor}:${delve.room}` };
        DungeonView.seen[key] = now;
        if (!before) return { dealt: null, taken: null, newMonster: false };
        return {
            dealt: now.dealt > before.dealt ? delve.lastHit : null,
            taken: now.taken > before.taken ? delve.lastTaken : null,
            // A kill in a pack keeps the room number, but the next monster still steps up.
            newMonster: now.monster !== before.monster || (now.dealt > before.dealt && !!delve.lastHit?.killed),
        };
    },

    /**
     * Seconds the match clock gained since it was last drawn under this key (a solo kill earns time),
     * or 0. Only call it while Playing, so the jump from the intro's end to the clock's isn't counted.
     */
    timeAdded(key, match) {
        // The clock being held still (a mini game, a reward pick) pushes the end back too: that isn't time earned.
        const earnedEnd = match.phaseEndsAtMs - (match.clockHeldMs || 0);
        const before = DungeonView.seen[key];
        DungeonView.seen[key] = earnedEnd;
        return before && earnedEnd > before + 500 ? Math.round((earnedEnd - before) / 1000) : 0;
    },

    /** "+10s" popping up beside a clock, or nothing. */
    timeAddedPop(seconds) {
        return seconds ? `<span class="dg-time-added">${Theme.text('dungeon.timeAdded', { seconds })}</span>` : '';
    },

    /** Forget what was drawn (a new match). */
    reset() {
        DungeonView.seen = {};
        DungeonView.effects = {};
        DungeonView.states = {};
        DungeonView.firstSeen = {};
    },

    icon(name) {
        return PixelSprites.html(`icon-${name}`, 'dg-icon');
    },

    /**
     * A hit-point bar with a heart in front: red for the monster, green for the adventurer. cssClass can
     * add is-poisoned (a monster's bar turns poison green) or is-healed (a heal flashes it, from --t0).
     */
    hp(value, max, cssClass, style = '') {
        const percent = max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0;
        return `
            <div class="dg-hp ${cssClass}" ${style ? `style="${style}"` : ''}>
                ${DungeonView.icon('heart')}
                <div class="dg-bar">
                    <div class="dg-bar-fill" style="width: ${percent}%"></div>
                    <span class="dg-bar-text">${value} / ${max}</span>
                </div>
            </div>`;
    },

    /** A Healer's poison on a monster: a green skull and its stacks ("x3"). Nothing when it isn't poisoned. */
    poisonBadge(stacks) {
        if (!stacks) return '';
        return `<span class="dg-poison">${PixelSprites.html('icon-skull', 'dg-poison-icon', { w: '#9be35a' })}${Theme.text('dungeon.poisonBadge', { stacks })}</span>`;
    },

    /**
     * A hero class's name in the theme's words ("tank" -> "Tank"). Literal keys, so ThemeFilesTests can check
     * them. (ArcadeLobby.heroName says the same, but not every page that draws the fight loads the lobby.)
     */
    className(heroClass) {
        switch (heroClass) {
            case 'tank': return Theme.text('heroes.tank');
            case 'healer': return Theme.text('heroes.healer');
            case 'wizard': return Theme.text('heroes.wizard');
            case 'rogue': return Theme.text('heroes.rogue');
            case 'ranger': return Theme.text('heroes.ranger');
            case 'pugilist': return Theme.text('heroes.pugilist');
            case 'warlock': return Theme.text('heroes.warlock');
            default: return UI.escape(heroClass || '');
        }
    },

    /** The trail's monsters in the room the delve is in (one, or a pack). */
    roomSteps(delve) {
        return delve.path.filter(step => step.floor === delve.floor && step.room === delve.room);
    },

    /**
     * How many of the current room's monsters are already beaten: the room's monsters less the ones still
     * alive (delve.foes). The trail greys out that many from the front.
     */
    beatenInRoom(delve) {
        return Math.max(0, DungeonView.roomSteps(delve).length - (delve.foes?.length || 1));
    },

    /**
     * The way down to the Depth's boss: a little icon per monster, grouped by floor with stairs between,
     * and a pack's monsters grouped as one room. Beaten ones are greyed out, the one being fought is framed
     * in gold, the ones waiting behind it in its room are dimmed, the boss is framed in red.
     */
    trail(delve) {
        const floors = []; // floors -> rooms -> steps
        for (const step of delve.path) {
            if (floors.at(-1)?.[0][0].floor !== step.floor) floors.push([]);
            const rooms = floors.at(-1);
            if (rooms.at(-1)?.[0].room !== step.room) rooms.push([]);
            rooms.at(-1).push(step);
        }
        const beatenHere = DungeonView.beatenInRoom(delve);
        const state = step => {
            if (step.floor < delve.floor || (step.floor === delve.floor && step.room < delve.room)) return 'is-beaten';
            if (step.floor > delve.floor || step.room > delve.room) return 'is-ahead';
            const slot = step.slot || 1; // in the current room: the first ones are dead, then the one in front
            return slot <= beatenHere ? 'is-beaten' : slot === beatenHere + 1 ? 'is-current' : 'is-waiting';
        };
        const stepHtml = step => `<span class="dg-step ${state(step)} ${step.isBoss ? 'is-boss' : ''}">${PixelSprites.html(step.sprite)}</span>`;
        return `
            <div class="dg-trail" style="--steps: ${delve.path.length + (floors.length - 1) * 0.5}">
                ${floors.map(rooms => `
                    <div class="dg-trail-floor ${rooms[0][0].floor === delve.floor ? 'is-here' : ''}">
                        ${rooms.map(room => room.length > 1
                            ? `<span class="dg-trail-room is-pack">${room.map(stepHtml).join('')}</span>`
                            : stepHtml(room[0])).join('')}
                    </div>`).join(`<span class="dg-trail-stairs">${DungeonView.icon('stairs')}</span>`)}
            </div>`;
    },

    /**
     * The monsters waiting their turn in the room (a pack), as small sprites behind the one in front, in a
     * .dg-pack (the 3D scene hides it and draws the pack itself). foes: [{ sprite, isBoss, cssClass }].
     */
    packSprites(foes) {
        if (!foes.length) return '';
        return `<div class="dg-pack" style="--idle0: ${DungeonView.idlePhase(1200)}ms">${foes.map((f, i) =>
            `<div class="dg-pack-member ${f.cssClass || ''}" style="--n: ${i}">${PixelSprites.html(f.sprite, `dg-pack-sprite ${f.isBoss ? 'is-boss' : ''}`)}</div>`).join('')}</div>`;
    },

    /**
     * The strip of monsters waiting their turn: each one's picture, a mini HP bar and its poison. Shown in
     * both the 2D and the 3D fight (it isn't in .dg-pack). foes: [{ sprite, hp, maxHp, poisonStacks }], plus
     * in the battle beat before (its HP before a Wizard's splash: the bar drains, from --t0) and killed.
     */
    packStrip(foes, cssClass = '') {
        if (!foes.length) return '';
        const percent = (value, max) => (max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0).toFixed(1);
        return `
            <div class="dg-pack-strip ${cssClass}">
                <span class="dg-pack-label">${Theme.text('dungeon.packNext')}</span>
                ${foes.map(f => {
                    const drains = f.before !== undefined && f.before !== f.hp;
                    const fill = drains
                        ? `<div class="dg-bar-fill dg-drain" style="--from: ${percent(f.before, f.maxHp)}%; --to: ${percent(f.hp, f.maxHp)}%"></div>`
                        : `<div class="dg-bar-fill" style="width: ${percent(f.hp, f.maxHp)}%"></div>`;
                    return `
                        <div class="dg-pack-foe ${f.poisonStacks ? 'is-poisoned' : ''} ${f.killed ? 'is-dead' : ''} ${drains ? 'is-splashed' : ''}">
                            ${PixelSprites.html(f.sprite, 'dg-pack-foe-sprite')}
                            <div class="dg-pack-foe-info">
                                <div class="dg-bar">${fill}<span class="dg-bar-text">${f.hp}</span></div>
                                ${DungeonView.poisonBadge(f.poisonStacks)}
                            </div>
                        </div>`;
                }).join('')}
            </div>`;
    },

    /**
     * The monster, animated by what just happened: shaken by a hit, lunging when it attacks, dropping in
     * when new. The damage numbers and "slain" are effects under key, so a redraw doesn't cut them off.
     */
    monster(delve, news, key) {
        const m = delve.monster;
        const moment = DungeonView.lasting(`${key}:monster`, news.newMonster ? 'is-new' : news.dealt ? 'is-hit' : news.taken ? 'is-attacking' : null, 650);
        const big = news.dealt && (news.dealt.crit || news.dealt.partyCombo);
        // A Healer's poison finishing a monster off is a "hit" of its own with no damage number to show.
        const byPoison = !!news.dealt?.poison;
        const floater = news.dealt && !byPoison
            ? `<div class="dg-float ${big ? 'is-crit' : ''}">-${news.dealt.damage}${big ? '!' : ''}</div>`
            : news.taken ? `<div class="dg-float is-taken">-${news.taken.damage}</div>` : '';
        const partyCombo = news.dealt?.partyCombo ? `<div class="dg-party-combo">${Theme.text('dungeon.partyComboShort')}</div>` : '';
        const slainText = !news.dealt?.killed ? ''
            : news.dealt.floorCleared ? Theme.text('dungeon.floorCleared')
            : byPoison ? Theme.text('dungeon.slainByPoison', { monster: news.dealt.monsterName })
            : Theme.text('dungeon.slain', { monster: news.dealt.monsterName });
        const slain = slainText ? `<div class="dg-slain ${byPoison ? 'is-poison' : ''}">${slainText}</div>` : '';
        if (floater || slain || partyCombo)
            DungeonView.effect(key, floater + slain + partyCombo, 1800);
        return `
            <div class="dg-monster-wrap" style="--t0: ${moment.t0}ms; --idle0: ${DungeonView.idlePhase(1200)}ms">
                ${PixelSprites.html(m.sprite, `dg-monster ${m.isBoss ? 'is-boss' : ''} ${moment.cls}`)}
                ${DungeonView.activeEffects(key)}
            </div>`;
    },

    /**
     * The monster's nameplate: its name, its HP, and "Attacks in 7" with the hourglass draining beside it.
     * serverNow (the station passes it): while a Freeze holds the hourglass it's iced over, and thaws by
     * itself when the freeze ends (a CSS animation timed by --thaw, so no redraw is needed).
     */
    enemy(delve, serverNow = null) {
        const m = delve.monster;
        const running = !!delve.attackAtMs;
        const frozenFor = serverNow && delve.frozenUntilMs > serverNow ? Math.round(delve.frozenUntilMs - serverNow) : 0;
        const label = running
            ? Theme.text('dungeon.attackIn', { seconds: Theme.raw(`<span data-ends-at="${delve.attackAtMs}" data-hurry-at="3"></span>`) })
            : Theme.text('dungeon.attackPaused');
        const hourglass = running
            ? `<div class="timer-bar dg-hourglass" data-starts-at="${delve.attackStartedAtMs}" data-ends-at="${delve.attackAtMs}"><div class="timer-bar-fill"></div></div>`
            : `<div class="dg-hourglass is-stopped"></div>`;
        return `
            <div class="dg-enemy ${m.isBoss ? 'is-boss' : ''}">
                <div class="dg-enemy-name">${UI.escape(m.name)}${DungeonView.poisonBadge(m.poisonStacks)}</div>
                ${DungeonView.hp(m.hp, m.maxHp, `is-enemy ${m.poisonStacks ? 'is-poisoned' : ''}`)}
                <div class="dg-attack ${running ? '' : 'is-stopped'} ${frozenFor ? 'is-frozen' : ''}" style="--thaw: ${frozenFor}ms">
                    ${DungeonView.icon('sword')}
                    <span class="dg-attack-label">${label}</span>
                    ${frozenFor ? `<span class="dg-frozen-label">${Theme.text('dungeon.attackFrozen')}</span>` : ''}
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
            // {streak} in a combo line is the Pugilist's combo count (the only class with one).
            const values = { monster: hit.monsterName, damage: hit.damage, streak: hit.comboCount || delve.streak };
            if (hit.revivedOwnerId) {
                const name = delve.party.find(m => m.ownerId === hit.revivedOwnerId)?.name || '';
                return { html: Theme.text('dungeon.revived', { ...values, name }, s), tone: 'is-great' };
            }
            if (hit.floorCleared) return { html: Theme.text('dungeon.floorClearedLine', values, s), tone: 'is-great' };
            if (hit.bossAhead) return { html: Theme.text('dungeon.bossAheadLine', values, s), tone: 'is-great' }; // the room before the boss's
            if (hit.partyCombo) return { html: Theme.text('dungeon.partyCombo', values, s), tone: 'is-great' };
            if (hit.killed) return { html: Theme.text('dungeon.kill', values, s), tone: 'is-great' };
            if (hit.staggered) return { html: Theme.text('dungeon.staggered', values, s), tone: 'is-great' }; // right at a boss's full charge
            if (hit.combo) return { html: Theme.text('dungeon.combo', values, s), tone: 'is-great' };
            if (hit.crit) return { html: Theme.text('dungeon.crit', values, s), tone: 'is-great' };
            return { html: Theme.text('dungeon.hit', values, s), tone: 'is-good' };
        }

        const taken = delve.lastTaken;
        const values = { monster: taken?.monsterName || delve.monster.name, damage: taken?.damage || 0 };
        const headline = taken?.blocked ? Theme.text('dungeon.blocked', values, s)
            : taken?.lastStand ? Theme.text('dungeon.lastStand', values, s)   // a Last Stand reward kept them up
            : taken?.charged ? Theme.text('dungeon.chargedHit', values, s)   // a boss's full-charge blow
            : lastAnswer.timedOut ? Theme.text('dungeon.tooSlow', values, s)
            : Theme.text('dungeon.wrong', values, s);
        const itWas = Theme.text('player.itWas', { letter: UI.ANSWER_LETTERS[lastAnswer.correctIndex], answer: lastAnswer.correctAnswer });
        return { html: `${headline}<div class="dg-feedback-detail">${itWas}</div>`, tone: 'is-bad' };
    },

    /**
     * One party member: their portrait (their face, or their hero), name, HP, and "DOWN 1/3" while they wait
     * for their teammates to revive them. healed (see healFlash): their HP bar flashes for a Healer's heal.
     */
    member(m, reviveNeeded, match, healed = null) {
        const down = !!m.downedUntilMs || m.out;
        const flash = healed?.ownerId === m.ownerId;
        return `
            <div class="dg-member ${down ? 'is-down' : ''}">
                <div class="dg-member-line">
                    ${HeroPhoto.portrait(match, m.ownerId, 'dg-member-face', '')}
                    <span class="dg-member-name">${UI.escape(m.name)}</span>
                    ${m.out ? `<span class="dg-member-down is-out">${Theme.text('dungeon.memberOut')}</span>`
                        : down ? `<span class="dg-member-down">${Theme.text('dungeon.memberDown', { progress: m.reviveProgress, needed: reviveNeeded })}</span>` : ''}
                    ${DungeonView.lives(m.lives)}
                </div>
                ${DungeonView.hp(m.hp, m.maxHp, `is-hero ${flash ? 'is-healed' : ''}`, flash ? `--t0: ${healed.t0}ms` : '')}
            </div>`;
    },

    /** Your teammates on your station screen (not you: your HP is on your plate). Nothing in a party of one. */
    teammates(delve, stationId, match, healed = null) {
        if (delve.party.length < 2) return '';
        const others = delve.party.filter(m => m.ownerId !== stationId);
        return `<div class="dg-party">${others.map(m => DungeonView.member(m, delve.reviveNeeded, match, healed)).join('')}</div>`;
    },

    /**
     * A Healer in the party just healed someone (you, or anyone, when you're the Healer): { ownerId, t0 }
     * while that member's HP bar flashes, else null. Kept with its start time, so a redraw carries it on.
     */
    healFlash(key, news) {
        const flash = DungeonView.lasting(key, news.dealt?.healedOwnerId || null, 1600);
        return flash.cls ? { ownerId: flash.cls, t0: flash.t0 } : null;
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
     * The whole station screen while the clock runs. A question is up: the encounter (js/dungeon-encounter.js,
     * laid out like the design lead's example). Between questions: the battle beat, under where you are
     * (floor, trail, time left), with you at the bottom.
     */
    station(match, you, serverNow, stationColor) {
        const delve = you.delve;
        const q = you.question;
        const news = DungeonView.news(`station:${you.stationId}`, delve);
        const clockKey = `clock:${match.matchId}`;
        const timeAdded = DungeonView.timeAdded(clockKey, match);
        if (timeAdded)
            DungeonView.effect(clockKey, DungeonView.timeAddedPop(timeAdded), 1600);

        const top = `
            <div class="dg-top">
                <div class="dg-top-line">
                    <span class="dg-floor-badge">${Theme.text('dungeon.floorShort', { floor: delve.floor })}</span>
                    <span class="dg-depth-name">${UI.escape(delve.depthName)}</span>
                    ${DungeonRewards.meter(delve)}
                    <span class="dg-clock"><small>${Theme.text('dungeon.time')}</small><span ${UI.matchClock(match, 10)}></span>${DungeonView.activeEffects(clockKey)}</span>
                </div>
                ${DungeonView.trail(delve)}
            </div>`;

        // A Healer's heal flashes the healed one's HP bar: with the blow in your own beat, or when a
        // teammate's heal reaches you (no beat of yours plays then).
        const beatHit = DungeonBattle.isPlaying(you, serverNow) ? DungeonBattle.beatHit(you) : null;
        const healed = beatHit?.healedOwnerId
            ? { ownerId: beatHit.healedOwnerId, t0: Math.round(you.beat.startedAtMs - serverNow) }
            : DungeonView.healFlash(`heal:${you.stationId}`, news);
        const selfHealed = healed && (healed.ownerId === you.stationId || delve.party.length < 2);

        const plate = `
            <div class="dg-plate">
                ${HeroPhoto.portrait(match, you.stationId, 'dg-portrait', stationColor)}
                <div class="dg-plate-info">
                    <div class="dg-plate-name"><span class="dg-plate-name-text">${UI.escape(you.name)}</span>${DungeonView.classBadge(delve.heroClass)}${DungeonView.lives(delve.lives, delve.maxLives)}</div>
                    ${DungeonView.hp(delve.hp, delve.maxHp, `is-hero ${selfHealed ? 'is-healed' : ''}`, selfHealed ? `--t0: ${healed.t0}ms` : '')}
                    ${DungeonRewards.badges(delve)}
                </div>
                ${DungeonView.stats(you.score, delve.kills)}
                ${DungeonPowerUps.slot(delve)}
            </div>`;

        // A power-up used (by you, or a teammate's Potion aimed at you): a pop over the monster, once.
        const powerUpKey = `pu:${you.stationId}`;
        DungeonPowerUps.noticeUse(powerUpKey, delve, ownerId => delve.party.find(p => p.ownerId === ownerId)?.name || you.name);

        // Between two rooms: the floor's mini game (js/dungeon-minigames.js).
        if (DungeonMinigames.showing(you, serverNow))
            return DungeonMinigames.station(match, you, serverNow, plate);

        // After a floor's boss: the rewards to pick from, or the one just picked (js/dungeon-rewards.js).
        if (DungeonRewards.showing(you, serverNow))
            return DungeonRewards.station(match, you, serverNow, top, plate);

        // Between questions: the fight takes the whole screen above your plate (what just happened plays out),
        // with where you are (floor, trail, time) floating across its top.
        if (DungeonBattle.isPlaying(you, serverNow)) {
            return `
                <div class="dg-station is-battle">
                    <div class="dg-world">
                        ${DungeonBattle.scene(match, you, stationColor, serverNow)}
                        ${top}
                    </div>
                    ${DungeonView.teammates(delve, you.stationId, match, healed)}
                    ${plate}
                </div>`;
        }

        return DungeonEncounter.view(match, you, serverNow, { news, plate, clockKey, powerUpKey, healed });
    },

    /**
     * Lives as hearts: one lit for each left, and (given max) a dark one for each lost. Nothing with no limit on lives
     * (the server sends 0 then).
     */
    lives(left, max = left) {
        if (!max) return '';
        const hearts = Array.from({ length: max }, (_, i) =>
            PixelSprites.html('icon-heart', `dg-life ${i < left ? '' : 'is-lost'}`)).join('');
        return `<span class="dg-lives">${hearts}</span>`;
    },

    /** The player's hero class on their plate ("WIZARD"), in that class's colour. */
    classBadge(heroClass) {
        if (!heroClass) return '';
        return `<span class="dg-class-badge is-${UI.escape(heroClass)}">${DungeonView.className(heroClass)}</span>`;
    },

    /**
     * One adventurer's (or party's) column on the main screen. rank is 1-based, or null with one lane.
     * A pack shows its waiting monsters small beside the one being fought, and in a strip under its nameplate.
     * A party of separate adventurers (2v2 Combined) shows each member's HP instead of one hero bar.
     * A player with a hero photo has their face where the knight's portrait would be.
     */
    lane({ key, name, color, delve, score, rank, isLeader, serverNow, match }) {
        const news = DungeonView.news(`lane:${key}`, delve);
        const downed = delve.downedUntilMs && delve.downedUntilMs > serverNow;
        const shake = DungeonView.lasting(`lane:${key}:hurt`, news.taken ? 'is-hurt' : null, 450);
        const waiting = delve.foes?.slice(1) || []; // a pack: the monsters behind the one being fought
        return `
            <div class="dg-lane ${isLeader ? 'is-leader' : ''} ${shake.cls}" style="--lane-color: ${color}; --t0: ${shake.t0}ms">
                <div class="dg-lane-head">
                    ${rank ? `<span class="dg-rank">${UI.ordinal(rank)}</span>` : ''}
                    <span class="dg-lane-name ${name.replace(/&[^;]+;/g, 'x').length > 16 ? 'is-long' : ''}">${name}</span>
                    <span class="dg-floor-badge">${Theme.text('dungeon.floorShort', { floor: delve.floor })}</span>
                </div>
                ${DungeonView.trail(delve)}
                <div class="dg-lane-stage">
                    ${DungeonView.packSprites(waiting)}
                    ${DungeonView.monster(delve, news, `fx:lane:${key}`)}
                    ${downed ? `<div class="dg-overlay is-downed small">${Theme.text('dungeon.downedShort')}</div>` : ''}
                    ${delve.minigame ? `<div class="dg-overlay is-minigame small">${Theme.text('dungeon.laneMinigame', { game: Theme.raw(DungeonMinigames.name(delve.minigame)) })}</div>` : ''}
                    ${DungeonView.packStrip(waiting, 'is-lane')}
                </div>
                ${DungeonView.enemy(delve)}
                <div class="dg-lane-hero">
                    ${delve.party.length > 1
                        ? `<div class="dg-party is-lane">${delve.party.map(m => DungeonView.member(m, delve.reviveNeeded, match)).join('')}</div>`
                        : `${HeroPhoto.portrait(match, delve.party[0]?.ownerId ?? key, 'dg-portrait small', color)}${DungeonView.hp(delve.hp, delve.maxHp, 'is-hero')}`}
                </div>
                ${DungeonView.stats(score, delve.kills)}
                ${DungeonRewards.badges(delve, 'is-lane')}
            </div>`;
    },

    /**
     * Main screen: the big moments of one lane since it was last drawn become banners sweeping across
     * the room screen (drawn by activeEffects('moments')): a boss slain, a floor cleared or a boss
     * stepping up, someone revived, a party combo. The Host has something to say about each
     * (js/dungeon-host.js, on the main screen). Each banner shows the faces of the lane's players.
     * name is already HTML-escaped.
     */
    noteMoments({ key, name, color, delve, match }) {
        const hit = DungeonView.news(`moment:${key}`, delve).dealt;
        const picked = DungeonRewards.newPick(key, delve); // a floor reward picked
        const lockWon = DungeonMinigames.newWin(key, delve); // a mini game won
        if (!hit && !picked && !lockWon) return;

        const lanePlayers = match.players.filter(p => p.stationId === key || p.teamId === key);
        const faces = `<div class="dg-moment-faces">${lanePlayers.map(p => HeroPhoto.portrait(match, p.stationId, 'dg-moment-face', color)).join('')}</div>`;
        const banner = (kind, text, sprite = null) => DungeonView.effect('moments', `
            <div class="dg-moment is-${kind}" style="--lane-color: ${color}">
                ${faces}
                ${sprite ? PixelSprites.html(sprite, 'dg-moment-sprite') : ''}
                <div class="dg-moment-text">${text}</div>
            </div>`, 2600);
        const who = Theme.raw(name);

        if (lockWon)
            banner('reward', Theme.text('dungeon.momentMinigameWon', { name: who, game: Theme.raw(DungeonMinigames.name(lockWon)) }), 'icon-chest');
        if (picked) {
            banner('reward', Theme.text('dungeon.momentReward', { name: who, reward: Theme.raw(DungeonRewards.name(picked.reward)) }), `icon-rw-${picked.reward}`);
            DungeonHost.say(Theme.text('dungeon.hostReward', { name: who, reward: Theme.raw(DungeonRewards.name(picked.reward)) }, `${key}:reward:${picked.number}`));
        }
        if (!hit) return;

        const seed = `${key}:${hit.number}`;
        const host = (lineKey, values) => DungeonHost.say(Theme.text(lineKey, values, seed));

        if (hit.killed && hit.isBoss) {
            banner('slain', Theme.text('dungeon.momentBossSlain', { name: who, monster: hit.monsterName }), hit.monsterSprite);
            host('dungeon.hostBossSlain', { name: who, monster: hit.monsterName });
        }
        else if (hit.bossAhead || (hit.floorCleared && delve.monster.isBoss)) { // a boss steps up (bossAhead: its room is next)
            banner('boss', Theme.text('dungeon.momentBoss', { name: who, monster: delve.monster.name }), delve.monster.sprite);
            host('dungeon.hostBoss', { name: who, monster: delve.monster.name });
        }
        else if (hit.floorCleared) {
            banner('floor', Theme.text('dungeon.momentFloor', { name: who, floor: delve.floor }));
            host('dungeon.hostFloor', { name: who, floor: delve.floor });
        }

        if (hit.revivedOwnerId) {
            const revived = delve.party.find(m => m.ownerId === hit.revivedOwnerId)?.name || '';
            banner('revive', Theme.text('dungeon.momentRevived', { name: revived }));
            host('dungeon.hostRevived', { name: revived });
        }
        if (hit.partyCombo) {
            banner('combo', Theme.text('dungeon.momentPartyCombo', { name: who }));
            host('dungeon.hostPartyCombo', { name: who });
        }
    },
};
