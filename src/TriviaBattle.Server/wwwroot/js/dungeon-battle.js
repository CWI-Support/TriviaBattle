// The battle beat: for a moment after every answer the station screen becomes the fight, full size,
// before the next question appears (the server holds the question back: see the beat settings in
// DungeonSettings). It plays what just happened, from the snapshot's you.beat:
//
//   hit     your knight charges in and strikes (bigger for a crit, a combo or a party combo)
//   kill    the strike, then the monster flashes and bursts apart: SLAIN!, then what's next (upNext): NEXT UP and
//           its bar, or a new room's ROOM 3 OF 4. A teammate's kill (beat.by) plays on your screen too, at the same
//           moment as theirs: your knight stays put while theirs strikes.
//   walk    a kill that emptied the room (walks(); the room beat, 3 s): the kill, then your knight runs on and the
//           dungeon scrolls past until the next room's monster is in front of you
//   floor   the kill, then the screen goes dark: down the stairs to the next floor
//   boss    the kill emptied the room before the boss's: WARNING!, the boss slams down with its name card
//   hurt    the monster lunges at you and claws you (a wrong answer, or too slow), then the right
//           answer flips over like a card. A Shield blocks it: BLOCKED!, no damage. A boss at full
//           charge (taken.charged) lands its big blow: CRUSHING BLOW!, a heavier slam, shake and flash
// A right answer at a boss's full charge (hit.staggered) gets STAGGERED!, and the boss reels.
// A Fireball hit gets its own stamp, and a power-up found with the hit (dropped, or earned by the
// streak) is held up as LOOT!.
//   downed  the same, and your knight falls
//
// In a pack the monsters waiting their turn stand small behind the one fighting (.dg-pack; the 3D draws
// its own), and their strip of mini HP bars sits under the big bar. The hero classes add notes in a fixed
// spot under the damage number, so they read the same over the 3D scene: a Wizard's SPLASH (the pack
// flinches and its bars drain), a Healer's heal or POISONED x3, a Warlock's patron's price, a Tank's
// ARMOUR. A Ranger sees the next question slide in over the end of the fight (peek), greyed until it's up.
//
// Every animation is timed from the beat's start on the server clock: the scene carries
// --t0 (minus how long ago it started) and each animation's delay is calc(var(--t0) + its offset).
// So a redraw halfway through (a teammate answered, say) carries on where it was instead of starting
// over. The offsets in css/dungeon-battle.css fit the default beat lengths (hit 1.5 s, hurt 1.7 s,
// kill 2 s, floor and boss 3 s). The scene also carries --t1 (how long until the beat ends): the fight
// fades out just before then, so the next question comes in over a calm screen instead of cutting in.
// Needs ui.js, theme.js, pixel-sprites.js and dungeon-view.js on the page.

const DungeonBattle = {
    /** True while the beat after the player's last answer is still playing. */
    isPlaying(you, serverNow) {
        return !!you?.beat && serverNow < you.beat.endsAtMs;
    },

    /**
     * The hit on the monster this beat shows: the player's own last hit, or on a kill they watched (a teammate's,
     * beat.by, or the party's poison, beat.byPoison) the killing hit, which the beat carries. Null on a beat where
     * the monster hit you.
     */
    beatHit(you) {
        const beat = you.beat;
        if (!beat || beat.kind === 'hurt' || beat.kind === 'downed') return null;
        return beat.hit || (beat.byPoison ? you.delve.lastHit : you.delve.lastDealt);
    },

    /**
     * After a kill (not a floor cleared or the boss's entrance: they have their curtains), the monster that's up
     * next, for the end of the beat: { monster (delve.monster, already the next one), newRoom (the kill emptied its
     * room), room, rooms }. Null on other beats, and when a mini game comes next instead.
     */
    upNext(you) {
        const hit = DungeonBattle.beatHit(you);
        const delve = you.delve;
        if (you.beat?.kind !== 'kill' || !hit?.killed || you.minigame) return null;
        return { monster: delve.monster, newRoom: !!hit.roomCleared, room: delve.room, rooms: delve.roomsOnFloor };
    },

    /**
     * The walk: a kill beat whose kill emptied the room, with the floor's next room ahead (not the boss's, which has
     * its entrance, nor a mini game). The hero runs on and the dungeon scrolls past until the next room's monsters
     * are in front (the server makes it the longer room beat, DungeonSettings.RoomBeatSeconds).
     */
    walks(you) {
        const hit = DungeonBattle.beatHit(you);
        return you.beat?.kind === 'kill' && !!hit?.roomCleared && !hit.minigameAhead && !you.minigame;
    },

    /** The whole fight for the beat. It fills the space between the top line and the player plate. */
    scene(match, you, stationColor, serverNow) {
        const beat = you.beat;
        const delve = you.delve;
        const hurt = beat.kind === 'hurt' || beat.kind === 'downed';
        const hit = DungeonBattle.beatHit(you);
        const byPoison = !!beat.byPoison; // the Healer's poison killed it: no blow, it chokes and bursts
        const byTeammate = !!beat.by;     // a teammate's blow killed it: your knight stays put and cheers
        const next = DungeonBattle.upNext(you);
        const taken = hurt ? delve.lastTaken : null;
        const seed = `${match.matchId}:${you.stationId}:${beat.startedAtMs}`;

        const classes = [
            'dg-battle',
            `is-${beat.kind}`,
            hurt ? 'is-hurting' : byPoison ? 'is-poison-kill' : byTeammate ? 'is-watching' : 'is-striking', // watching: the knight stays put
            next ? 'has-next' : '',
            DungeonBattle.walks(you) ? 'is-walking' : '',          // on to the next room: the dungeon scrolls past
            hit?.crit ? 'is-crit' : '',
            hit?.combo ? 'is-combo' : '',
            hit?.partyCombo ? 'is-party-combo' : '',
            hit?.isBoss ? 'is-boss-foe' : '',
            hit?.fireball ? 'is-fireball' : '',
            hit?.splashes?.length ? 'is-splash' : '',
            taken?.blocked ? 'is-blocked' : '',
            taken?.charged && !taken.blocked ? 'is-charged' : '',  // a boss's full-charge blow
            hit?.staggered ? 'is-staggered' : '',                  // a right answer at a boss's full charge
        ].join(' ');

        // The monster in the fight: the one you hit (it may already be dead), or the one hitting you.
        const foe = hit
            ? { sprite: hit.monsterSprite, name: hit.monsterName, isBoss: hit.isBoss, max: hit.monsterMaxHp, after: hit.monsterHpLeft,
                // The poison's last bite isn't on the hit (it did no damage of its own): the bar drains its last tenth.
                before: byPoison ? Math.ceil(hit.monsterMaxHp * 0.1) : Math.min(hit.monsterMaxHp, hit.monsterHpLeft + hit.damage) }
            : { sprite: taken?.monsterSprite || delve.monster.sprite, name: taken?.monsterName || delve.monster.name, isBoss: delve.monster.isBoss };
        // Still alive, it's the one in front: its poison (this hit's, if a Healer just poisoned it) shows on its bar.
        const foePoison = hit && !hit.killed ? delve.monster.poisonStacks || hit.poisonStacks || 0 : 0;
        const pack = DungeonBattle.pack(delve, hit);

        const feedback = byPoison
            ? { html: Theme.text('dungeon.slainByPoison', { monster: hit?.monsterName || '' }, seed), tone: 'is-great' }
            : byTeammate
            ? { html: Theme.text('dungeon.slainByTeammate', { name: beat.by, monster: hit?.monsterName || '' }, seed), tone: 'is-great' }
            : DungeonView.feedback(delve, you.lastAnswer, `${match.matchId}:${you.stationId}`);

        return `
            <div class="${classes}" data-stage="fight" style="--t0: ${Math.round(beat.startedAtMs - serverNow)}ms; --t1: ${Math.round(beat.endsAtMs - serverNow)}ms">
                ${DungeonBattle.room()}
                <div class="dg-arena" data-focus>
                    ${DungeonView.packSprites(pack)}
                    <div class="dg-actor is-hero">${PixelSprites.html('hero', 'dg-actor-sprite', { r: stationColor })}<i class="dg-shadow"></i></div>
                    <div class="dg-actor is-foe ${foe.isBoss ? 'is-boss' : ''}">${PixelSprites.html(foe.sprite, 'dg-actor-sprite')}<i class="dg-shadow"></i></div>
                    ${next?.newRoom && DungeonBattle.walks(you) ? `<div class="dg-actor is-foe is-arriving ${next.monster.isBoss ? 'is-boss' : ''}">${PixelSprites.html(next.monster.sprite, 'dg-actor-sprite')}<i class="dg-shadow"></i></div>` : ''}
                    ${hurt ? DungeonBattle.claws() : byPoison ? '' : '<div class="dg-fx-slash"></div>'}
                    ${hit?.staggered && !hit.killed ? '<div class="dg-fx-daze"><i></i><i></i><i></i></div>' : ''}
                    ${DungeonBattle.sparks(hurt ? 10 : 14, 'is-impact')}
                    ${beat.kind === 'kill' || beat.kind === 'floor' || beat.kind === 'boss' ? DungeonBattle.sparks(22, 'is-death') : ''}
                    ${taken?.blocked || byPoison ? '' : `<div class="dg-bignum">-${hit ? hit.damage : taken?.damage || 0}${hit?.crit || hit?.partyCombo || hit?.fireball || hit?.staggered || taken?.charged ? '!' : ''}</div>`}
                    ${DungeonBattle.stamps(beat, hit, taken, delve, seed)}
                    ${DungeonBattle.notes(hit, taken, delve, you, seed)}
                    ${hurt ? DungeonBattle.reveal(you.lastAnswer, seed) : ''}
                    ${hit?.found ? DungeonBattle.loot(hit.found, beat.kind, seed) : ''}
                </div>
                <div class="dg-battle-bars">
                    ${hit
                        ? DungeonBattle.bar(foe.name, foe.before, foe.after, foe.max, `for-foe ${foePoison ? 'is-poisoned' : ''}`, DungeonView.poisonBadge(foePoison))
                        : DungeonBattle.bar(you.name, Math.min(delve.maxHp, (taken?.hpLeft ?? delve.hp) + (taken?.damage || 0)), taken?.hpLeft ?? delve.hp, delve.maxHp, 'for-hero')}
                    ${DungeonView.packStrip(pack, 'is-battle')}
                    ${next ? DungeonBattle.nextBar(next, seed) : ''}
                </div>
                <div class="dg-battle-line dg-feedback ${feedback.tone}">${feedback.html}</div>
                <div class="dg-battle-flash"></div>
                ${beat.kind === 'floor' || beat.kind === 'boss' ? DungeonBattle.curtain(beat.kind, delve, seed) : ''}
                ${DungeonBattle.peek(you.question, serverNow, seed)}
            </div>`;
    },

    /**
     * The monsters waiting in the room while the beat plays, for the small sprites and the strip. After a
     * Wizard's splash: the ones it hit, as they were (before) and are now (some may have died). Otherwise
     * the room's others: behind the one in front, or after a kill, the ones still standing in that room
     * (the next is already in front in the snapshot; a new room's monsters don't belong in this fight).
     */
    pack(delve, hit) {
        const foes = delve.foes || [];
        if (hit?.splashes?.length) {
            // The survivors are in the snapshot in the same order as the splashes, so their max HP and poison come from there.
            const alive = hit.killed ? foes.slice() : foes.slice(1);
            return hit.splashes.map(s => {
                const foe = s.killed ? null : alive.shift();
                return {
                    name: s.monsterName, sprite: s.monsterSprite, isBoss: !!foe?.isBoss, killed: s.killed,
                    hp: s.hpLeft, before: s.hpLeft + s.damage, maxHp: foe?.maxHp ?? s.hpLeft + s.damage, poisonStacks: foe?.poisonStacks || 0,
                    cssClass: s.killed ? 'is-splash-killed' : 'is-splashed',
                };
            });
        }
        if (!hit?.killed) return foes.slice(1);
        return DungeonView.beatenInRoom(delve) > 0 ? foes : [];
    },

    /**
     * What the hero's class added to this blow, in a fixed spot under the damage number (so it reads over
     * the 3D scene as well), each timed after the blow (--at): a Wizard's splash on the pack, a Healer's heal
     * or poison, the price a Warlock's patron took, a Tank's armour softening the hit.
     */
    notes(hit, taken, delve, you, seed) {
        const notes = [];
        const note = (cssClass, at, html) => notes.push(`<div class="dg-note ${cssClass}" style="--at: ${at}ms">${html}</div>`);

        if (hit?.splashes?.length) {
            const numbers = hit.splashes.map(s => `<b>${s.damage}</b>${s.killed ? DungeonView.icon('skull') : ''}`).join(' &middot; ');
            note('is-splash', 260, `${Theme.text('dungeon.splash')} ${numbers}`);
        }
        if (hit?.healedOwnerId) {
            const self = hit.healedOwnerId === you.stationId || delve.party.length < 2;
            const name = delve.party.find(m => m.ownerId === hit.healedOwnerId)?.name || '';
            note('is-heal', 420, self
                ? Theme.text('dungeon.healedSelf', { amount: hit.healed })
                : Theme.text('dungeon.healedTeammate', { name, amount: hit.healed }));
        }
        if (hit?.poisonStacks)
            note('is-poison', 420, Theme.text('dungeon.poisoned', { stacks: hit.poisonStacks }, seed));
        const price = hit?.patronPrice || taken?.patronPrice;
        if (price)
            note('is-patron', 560, Theme.text('dungeon.patronPrice', { amount: price }, seed));
        if (taken && !taken.blocked && delve.heroClass === 'tank')
            note('is-armour', 300, `${DungeonPowerUps.icon('Shield')}${Theme.text('dungeon.armour')}`);

        return notes.length ? `<div class="dg-beat-notes">${notes.join('')}</div>` : '';
    },

    /**
     * A Ranger sees the next question early: from q.peekFromMs it slides in over the bottom of the fight
     * (timed by --p0, so no redraw is needed for it to appear), its answers greyed with "Press in 1" until
     * the question is up (q.shownAtMs), when the beat ends and the question view takes over.
     */
    peek(q, serverNow, seed) {
        if (!q?.peekFromMs || !q.shownAtMs) return '';
        const answers = q.answers.map((text, i) => {
            const letter = UI.ANSWER_LETTERS[i];
            return `
                <div class="dg-answer is-locked ${q.hidden?.includes(i) ? 'is-eliminated' : ''}" data-letter="${letter}">
                    <span class="dg-rune">${letter}</span>
                    <span class="dg-answer-text">${UI.escape(text)}</span>
                </div>`;
        }).join('');
        return `
            <div class="dg-peek" style="--p0: ${Math.round(q.peekFromMs - serverNow)}ms">
                <div class="dg-peek-label">
                    <span>${Theme.text('dungeon.peekLabel', {}, seed)}</span>
                    <span class="dg-peek-wait">${Theme.text('dungeon.peekWait', { seconds: Theme.raw(`<span data-ends-at="${q.shownAtMs}"></span>`) })}</span>
                </div>
                <div class="dg-scroll dg-peek-question">${UI.escape(q.text)}</div>
                <div class="dg-peek-answers">${answers}</div>
            </div>`;
    },

    /** The room the fight is in: a stone wall, two flickering torches, the floor, a dark vignette. */
    room() {
        return `
            <div class="dg-room">
                <div class="dg-torch is-left"><i></i></div>
                <div class="dg-torch is-right"><i></i></div>
                <div class="dg-room-floor"></div>
            </div>`;
    },

    /** Three claw marks raked across your knight. */
    claws() {
        return '<div class="dg-fx-claws"><i></i><i></i><i></i></div>';
    },

    /**
     * A burst of pixel sparks flying out from the monster. Each gets its own direction and distance
     * (--dx, --dy), spread evenly round the circle and varied a little so it doesn't look like a clock face.
     */
    sparks(count, cssClass) {
        let html = '';
        for (let i = 0; i < count; i++) {
            const angle = (i / count) * Math.PI * 2 + (i % 3) * 0.21;
            const distance = 14 + ((i * 37) % 19);
            html += `<i style="--dx: ${(Math.cos(angle) * distance).toFixed(1)}vmin; --dy: ${(Math.sin(angle) * distance).toFixed(1)}vmin; --n: ${i}"></i>`;
        }
        return `<div class="dg-sparks ${cssClass}">${html}</div>`;
    },

    /**
     * The words slammed onto the fight. Early: what kind of hit (CRITICAL!, COMBO x4, PARTY COMBO!,
     * someone revived, a boss STAGGERED!) or what went wrong (WRONG!, TOO SLOW!, a boss's CRUSHING BLOW!).
     * Late: SLAIN! or DOWN!.
     */
    stamps(beat, hit, taken, delve, seed) {
        let early = beat.byPoison ? Theme.text('dungeon.stampPoison', {}, seed) : ''; // the Healer's poison finished it
        if (hit?.revivedOwnerId) {
            const name = delve.party.find(m => m.ownerId === hit.revivedOwnerId)?.name || '';
            early = Theme.text('dungeon.stampRevived', { name }, seed);
        }
        else if (hit?.staggered) early = Theme.text('dungeon.stampStaggered', {}, seed);
        else if (hit?.fireball) early = Theme.text('dungeon.stampFireball', {}, seed);
        else if (taken?.blocked) early = Theme.text('dungeon.stampBlocked', {}, seed);
        else if (taken?.charged) early = Theme.text('dungeon.stampCharged', {}, seed);
        else if (hit?.partyCombo) early = Theme.text('dungeon.partyComboShort');
        else if (hit?.combo) early = Theme.text('dungeon.stampCombo', { streak: hit.comboCount || delve.streak }); // a Pugilist's
        else if (hit?.crit) early = Theme.text('dungeon.stampCrit', {}, seed);
        else if (taken) early = Theme.text(taken.reason === 'TooSlow' ? 'dungeon.stampSlow' : 'dungeon.stampWrong', {}, seed);

        const late = taken?.out ? Theme.text('dungeon.stampOut', {}, seed) // downed on their last life
            : beat.kind === 'downed' ? Theme.text('dungeon.stampDowned', {}, seed)
            : beat.kind === 'kill' || beat.kind === 'floor' || beat.kind === 'boss' ? Theme.text('dungeon.stampSlain', {}, seed)
            : '';

        const stamp = (cssClass, html) =>
            `<div class="dg-stamp ${cssClass}" style="--chars: ${html.replace(/<[^>]*>|&[a-z#0-9]+;/gi, 'x').length}">${html}</div>`;
        return (early ? stamp('is-early', early) : '') + (late ? stamp('is-late', late) : '');
    },

    /**
     * The right answer, turned over like a card once the blow has landed. Finding out is a reward too
     * (the "a-ha" moment the design is built on), so it gets the middle of the stage, not small print.
     */
    reveal(lastAnswer, seed) {
        if (!lastAnswer || lastAnswer.isCorrect) return '';
        return `
            <div class="dg-reveal">
                <div class="dg-reveal-label">${Theme.text('dungeon.revealLabel', {}, seed)}</div>
                <div class="dg-reveal-answer">
                    <span class="dg-reveal-letter">${UI.ANSWER_LETTERS[lastAnswer.correctIndex]}</span>
                    <span>${UI.escape(lastAnswer.correctAnswer)}</span>
                </div>
            </div>`;
    },

    /**
     * A power-up found with this hit, held up for the player: after SLAIN! on a kill, over the curtain on
     * a floor cleared (it's drawn above it), soon after the blow on a plain hit (the streak earned it).
     */
    loot(kind, beatKind, seed) {
        const at = beatKind === 'kill' ? 1250 : beatKind === 'floor' || beatKind === 'boss' ? 1450 : 600;
        return `
            <div class="dg-loot" style="--loot-at: ${at}ms">
                <div class="dg-loot-label">${Theme.text('dungeon.stampLoot', {}, seed)}</div>
                ${DungeonPowerUps.icon(kind, 'dg-loot-icon')}
                <div class="dg-loot-name">${DungeonPowerUps.name(kind)}</div>
            </div>`;
    },

    /** A big HP bar under the fight that drains from before to after as the hit lands. badge: HTML after the name (poison). */
    bar(name, before, after, max, cssClass, badge = '') {
        const percent = value => (max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0).toFixed(1);
        return `
            <div class="dg-battle-bar ${cssClass}">
                <div class="dg-battle-bar-name">${UI.escape(name)}${badge}</div>
                <div class="dg-bar">
                    <div class="dg-bar-ghost" style="width: ${percent(before)}%"></div>
                    <div class="dg-bar-fill dg-drain" style="--from: ${percent(before)}%; --to: ${percent(after)}%"></div>
                    <span class="dg-bar-text">${after} / ${max}</span>
                </div>
            </div>`;
    },

    /**
     * The monster that's up next, announced at the end of a kill beat so the question doesn't just come back with a
     * different monster in it: its bar, full, takes the slain one's place under NEXT UP (ROOM 3 OF 4 when the kill
     * emptied the room; then it also drops into the fight where the dead one stood).
     */
    nextBar(next, seed) {
        const m = next.monster;
        const tag = next.newRoom
            ? Theme.text('dungeon.nextRoom', { room: next.room, rooms: next.rooms }, seed)
            : Theme.text('dungeon.nextUp', {}, seed);
        return `
            <div class="dg-next">
                <div class="dg-next-tag">${tag}</div>
                ${DungeonBattle.bar(m.name, m.hp, m.hp, m.maxHp, 'for-foe is-next')}
            </div>`;
    },

    /**
     * Floor cleared: the screen goes dark while you head down the stairs ("FLOOR 3", the Depth's name).
     * The boss's room next (kind 'boss': the boss waits in the last room of its floor, so no new floor is
     * claimed): it turns red, WARNING! and the boss's name card. delve.monster is already the boss. It stays up until the beat ends; the next
     * monster then drops into the question screen's strip (see is-new-foe in css/dungeon.css).
     */
    curtain(kind, delve, seed) {
        const body = kind === 'boss'
            ? `<div class="dg-curtain-warning">${Theme.text('dungeon.stampWarning', {}, seed)}</div>
               ${PixelSprites.html(delve.monster.sprite, 'dg-curtain-boss')}
               <div class="dg-curtain-title">${UI.escape(delve.monster.name)}</div>
               <div class="dg-curtain-sub">${Theme.text('dungeon.bossApproaches', {}, seed)}</div>`
            : `<div class="dg-curtain-stairs">${DungeonView.icon('stairs')}${DungeonView.icon('stairs')}${DungeonView.icon('stairs')}</div>
               <div class="dg-curtain-title">${Theme.text('dungeon.floorShort', { floor: delve.floor })}</div>
               <div class="dg-curtain-sub">${UI.escape(delve.depthName)}</div>
               ${DungeonRewards.curtainLine(delve)}`;
        return `<div class="dg-curtain">${body}</div>`;
    },
};
