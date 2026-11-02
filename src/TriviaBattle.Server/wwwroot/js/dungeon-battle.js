// The battle beat: for a moment after every answer the station screen becomes the fight, full size,
// before the next question appears (the server holds the question back: see the beat settings in
// DungeonSettings). It plays what just happened, from the snapshot's you.beat:
//
//   hit     your knight charges in and strikes (bigger for a crit, a combo or a party combo)
//   kill    the strike, then the monster flashes and bursts apart: SLAIN!
//   floor   the kill, then the screen goes dark: down the stairs to the next floor
//   boss    the kill, then WARNING!: the floor's boss slams down with its name card
//   hurt    the monster lunges at you and claws you (a wrong answer, or too slow), then the right
//           answer flips over like a card
//   downed  the same, and your knight falls
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

    /** The whole fight for the beat. It fills the space between the top line and the player plate. */
    scene(match, you, stationColor, serverNow) {
        const beat = you.beat;
        const delve = you.delve;
        const hurt = beat.kind === 'hurt' || beat.kind === 'downed';
        const hit = hurt ? null : delve.lastDealt;
        const taken = hurt ? delve.lastTaken : null;
        const seed = `${match.matchId}:${you.stationId}:${beat.startedAtMs}`;

        const classes = [
            'dg-battle',
            `is-${beat.kind}`,
            hurt ? 'is-hurting' : 'is-striking',
            hit?.crit ? 'is-crit' : '',
            hit?.combo ? 'is-combo' : '',
            hit?.partyCombo ? 'is-party-combo' : '',
            hit?.isBoss ? 'is-boss-foe' : '',
        ].join(' ');

        // The monster in the fight: the one you hit (it may already be dead), or the one hitting you.
        const foe = hit
            ? { sprite: hit.monsterSprite, name: hit.monsterName, isBoss: hit.isBoss, max: hit.monsterMaxHp, after: hit.monsterHpLeft,
                before: Math.min(hit.monsterMaxHp, hit.monsterHpLeft + hit.damage) }
            : { sprite: taken?.monsterSprite || delve.monster.sprite, name: taken?.monsterName || delve.monster.name, isBoss: delve.monster.isBoss };

        const feedback = DungeonView.feedback(delve, you.lastAnswer, `${match.matchId}:${you.stationId}`);

        return `
            <div class="${classes}" data-stage="fight" style="--t0: ${Math.round(beat.startedAtMs - serverNow)}ms; --t1: ${Math.round(beat.endsAtMs - serverNow)}ms">
                ${DungeonBattle.room()}
                <div class="dg-arena">
                    <div class="dg-actor is-hero">${PixelSprites.html('hero', 'dg-actor-sprite', { r: stationColor })}<i class="dg-shadow"></i></div>
                    <div class="dg-actor is-foe ${foe.isBoss ? 'is-boss' : ''}">${PixelSprites.html(foe.sprite, 'dg-actor-sprite')}<i class="dg-shadow"></i></div>
                    ${hurt ? DungeonBattle.claws() : '<div class="dg-fx-slash"></div>'}
                    ${DungeonBattle.sparks(hurt ? 10 : 14, 'is-impact')}
                    ${beat.kind === 'kill' || beat.kind === 'floor' || beat.kind === 'boss' ? DungeonBattle.sparks(22, 'is-death') : ''}
                    <div class="dg-bignum">-${hit ? hit.damage : taken?.damage || 0}${hit?.crit || hit?.partyCombo ? '!' : ''}</div>
                    ${DungeonBattle.stamps(beat, hit, taken, delve, seed)}
                    ${hurt ? DungeonBattle.reveal(you.lastAnswer, seed) : ''}
                </div>
                <div class="dg-battle-bars">
                    ${hit
                        ? DungeonBattle.bar(foe.name, foe.before, foe.after, foe.max, 'for-foe')
                        : DungeonBattle.bar(you.name, Math.min(delve.maxHp, (taken?.hpLeft ?? delve.hp) + (taken?.damage || 0)), taken?.hpLeft ?? delve.hp, delve.maxHp, 'for-hero')}
                </div>
                <div class="dg-battle-line dg-feedback ${feedback.tone}">${feedback.html}</div>
                <div class="dg-battle-flash"></div>
                ${beat.kind === 'floor' || beat.kind === 'boss' ? DungeonBattle.curtain(beat.kind, delve, seed) : ''}
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
     * someone revived) or what went wrong (WRONG!, TOO SLOW!). Late: SLAIN! or DOWN!.
     */
    stamps(beat, hit, taken, delve, seed) {
        let early = '';
        if (hit?.revivedOwnerId) {
            const name = delve.party.find(m => m.ownerId === hit.revivedOwnerId)?.name || '';
            early = Theme.text('dungeon.stampRevived', { name }, seed);
        }
        else if (hit?.partyCombo) early = Theme.text('dungeon.partyComboShort');
        else if (hit?.combo) early = Theme.text('dungeon.stampCombo', { streak: delve.streak });
        else if (hit?.crit) early = Theme.text('dungeon.stampCrit', {}, seed);
        else if (taken) early = Theme.text(taken.reason === 'TooSlow' ? 'dungeon.stampSlow' : 'dungeon.stampWrong', {}, seed);

        const late = beat.kind === 'downed' ? Theme.text('dungeon.stampDowned', {}, seed)
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

    /** A big HP bar under the fight that drains from before to after as the hit lands. */
    bar(name, before, after, max, cssClass) {
        const percent = value => (max > 0 ? Math.max(0, Math.min(100, (value / max) * 100)) : 0).toFixed(1);
        return `
            <div class="dg-battle-bar ${cssClass}">
                <div class="dg-battle-bar-name">${UI.escape(name)}</div>
                <div class="dg-bar">
                    <div class="dg-bar-ghost" style="width: ${percent(before)}%"></div>
                    <div class="dg-bar-fill dg-drain" style="--from: ${percent(before)}%; --to: ${percent(after)}%"></div>
                    <span class="dg-bar-text">${after} / ${max}</span>
                </div>
            </div>`;
    },

    /**
     * Floor cleared: the screen goes dark while you head down the stairs ("FLOOR 3", the Depth's name),
     * or turns red for a boss (WARNING! and its name card). It stays up until the beat ends; the next
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
               <div class="dg-curtain-sub">${UI.escape(delve.depthName)}</div>`;
        return `<div class="dg-curtain">${body}</div>`;
    },
};
