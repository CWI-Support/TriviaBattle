// The station screen while a question is up (Dungeon format): the encounter, laid out like the design
// lead's example (docs/design/station-example.png). Top to bottom:
//   - the monster's nameplate: its name and red HP bar (green and a skull while poisoned), with the floor and
//     the time left at its corners; in a pack, the monsters waiting their turn in a strip underneath; against
//     a boss, its charge meter (a pip per step of its charge cycle). At full charge the plate and the room
//     pulse red and a warning takes the Host's place: get this one right (it staggers) or take its big blow
//   - the question on a parchment scroll hanging under it
//   - the monster, big, in the middle of its room (in 3D when the station draws it), a pack's others small
//     behind it (.dg-pack, which the 3D hides and draws itself), with the Host's line,
//     being stunned or downed, and power-up pops over it; across its top, the other players' moments that
//     matter to you, with their faces (js/dungeon-feed.js)
//   - the hourglass down the left of the answers; each answer a rune box (the button's symbol, in its
//     colour) and a framed answer. A Warlock's patron showing the answer makes it glow purple, and the
//     Host's line becomes the patron's whisper
//   - your party, then you
// The room fills the whole screen above your plate (.dg-stage, data-stage="portrait": where the 3D draws), and
// everything else floats over it (.dg-hud). The gap left between the scroll and the answers (.dg-window, data-focus)
// is where the monster stands: the 2D one is drawn in it, and the 3D camera frames the monster there.
// Between questions the battle beat takes the screen instead (js/dungeon-battle.js).
// Needs ui.js, theme.js, pixel-sprites.js, dungeon-view.js, dungeon-battle.js, dungeon-powerups.js and dungeon-feed.js.
// Styles: css/dungeon-encounter.css.

const DungeonEncounter = {
    /**
     * The whole question view. parts: what DungeonView.station already worked out for both views
     * (news, plate, clockKey, powerUpKey).
     */
    view(match, you, serverNow, { news, plate, clockKey, powerUpKey, healed }) {
        const delve = you.delve;
        // Out of lives counts as downed for good (in a party they still see a teammate's question, to shout the answer).
        const downed = delve.out || (delve.downedUntilMs && delve.downedUntilMs > serverNow);
        const stunned = !downed && you.lockedOutUntilMs && you.lockedOutUntilMs > serverNow;
        const seed = `${match.matchId}:${you.stationId}`;

        // Downed in a party: your buttons are dead, so you get a teammate's question to shout them the answer.
        const helping = downed ? you.helping : null;
        const shown = helping ? helping.question : you.question;
        const dimmed = (downed && !helping) || stunned;

        const overlaySeed = `${seed}:${delve.lastTaken?.number}`;
        const overlay = !downed
            ? stunned ? `<div class="dg-overlay is-stunned">${Theme.text('dungeon.stunned', {}, overlaySeed)}</div>` : ''
            : delve.out
                ? `<div class="dg-overlay is-downed is-out">${delve.party.length > 1 ? Theme.text('dungeon.outParty', {}, overlaySeed) : Theme.text('dungeon.outSolo', {}, overlaySeed)}</div>`
            : delve.party.length > 1
                ? `<div class="dg-overlay is-downed is-party">${Theme.text('dungeon.downedParty', { left: delve.reviveNeeded - delve.reviveProgress }, overlaySeed)}</div>`
                : `<div class="dg-overlay is-downed">${Theme.text('dungeon.downed', { seconds: Theme.raw(`<span data-ends-at="${delve.downedUntilMs}"></span>`) }, overlaySeed)}</div>`;

        // A Warlock's patron showing the answer (their own question only): that answer glows, and the
        // Host's line gives way to the patron's whisper, which slides in once (since).
        const revealed = helping ? null : shown.revealed ?? null;
        const feedback = revealed !== null
            ? { html: Theme.text('dungeon.patronWhispers', {}, `${seed}:${shown.number}`), tone: 'is-patron',
                style: `--t0: ${DungeonView.since(`patron:${match.matchId}:${you.stationId}:${shown.number}`)}ms` }
            : DungeonView.feedback(delve, you.lastAnswer, seed);
        const tag = helping
            ? `<div class="dg-scroll-tag is-help">${Theme.text('dungeon.helpTeammate', { name: helping.name }, overlaySeed)}</div>`
            : match.category.includes(',') ? `<div class="dg-scroll-tag">${UI.escape(shown.category)}</div>` : '';

        // Just after a battle beat the question comes in (and after a kill the next monster drops in, unless the
        // walk to the next room already brought it), timed from the beat's end so a redraw doesn't play it again.
        // No beat: it's simply there.
        const beat = you.beat;
        const newFoe = beat && ['kill', 'floor', 'boss'].includes(beat.kind) && !DungeonBattle.walks(you);
        const arrival = !beat ? '' : `is-after-beat ${newFoe ? 'is-new-foe' : ''}`;
        // A boss at full charge: the whole screen warns (the plate and the room pulse red, on the shared idle
        // beat so redraws don't jolt it). Downed, you can't answer, so the warning isn't yours to heed.
        const charge = DungeonEncounter.chargeOf(delve);
        const fullCharge = charge?.full && !downed;
        const styles = [beat ? `--q0: ${Math.round(beat.endsAtMs - serverNow)}ms` : '', fullCharge ? `--charge0: ${DungeonView.idlePhase(900)}ms` : '']
            .filter(Boolean).join('; ');

        return `
            <div class="dg-station is-question is-encounter ${arrival} ${news.taken ? 'is-hurt' : ''} ${fullCharge ? 'is-full-charge' : ''}" ${styles ? `style="${styles}"` : ''}>
                <div class="dg-world">
                    <div class="dg-stage" data-stage="portrait">${DungeonBattle.room()}</div>
                    <div class="dg-hud">
                        ${DungeonEncounter.foePlate(match, delve, clockKey)}
                        <div class="dg-scroll ${helping ? 'is-helping' : ''}">${tag}${UI.questionMedia(shown.mediaUrl)}<div>${UI.escape(shown.text)}</div></div>
                        <div class="dg-window" data-focus>
                            ${DungeonView.packSprites(delve.foes?.slice(1) || [])}
                            ${DungeonView.monster(delve, news, `fx:station:${you.stationId}`)}
                            <div class="dg-feedback ${feedback.tone}" ${feedback.style ? `style="${feedback.style}"` : ''}>${feedback.html}</div>
                            ${fullCharge ? `<div class="dg-charge-warning">${Theme.text('dungeon.chargeWarning', {}, `${seed}:${shown.number}`)}</div>` : ''}
                            ${DungeonFeed.caption()}
                            ${overlay}
                            ${DungeonView.activeEffects(powerUpKey)}
                        </div>
                        <div class="dg-answer-area">
                            ${DungeonEncounter.hourglass(delve, serverNow)}
                            <div class="dg-answers ${helping ? 'is-helping' : ''}">
                                ${shown.answers.map((text, i) => DungeonEncounter.answer(text, i, dimmed, shown.hidden?.includes(i), revealed === i)).join('')}
                            </div>
                        </div>
                    </div>
                </div>
                ${DungeonView.teammates(delve, you.stationId, match, healed)}
                ${plate}
            </div>`;
    },

    /**
     * The monster's nameplate: floor and time at the corners, its name (and a Healer's poison on it), its HP
     * bar between two gems (green while poisoned), and in a pack the ones waiting their turn underneath.
     */
    foePlate(match, delve, clockKey) {
        const m = delve.monster;
        const percent = m.maxHp > 0 ? Math.max(0, Math.min(100, (m.hp / m.maxHp) * 100)) : 0;
        const charge = DungeonEncounter.chargeOf(delve);
        return `
            <div class="dg-foe-plate ${m.isBoss ? 'is-boss' : ''} ${m.poisonStacks ? 'is-poisoned' : ''}">
                <div class="dg-foe-corners">
                    <span class="dg-floor-badge">${Theme.text('dungeon.floorShort', { floor: delve.floor })}</span>
                    <span class="dg-clock"><small>${Theme.text('dungeon.time')}</small><span ${UI.matchClock(match, 10)}></span>${DungeonView.activeEffects(clockKey)}</span>
                </div>
                <div class="dg-foe-name-line">
                    <div class="dg-foe-name">${UI.escape(m.name)}</div>
                    ${DungeonView.poisonBadge(m.poisonStacks)}
                </div>
                <div class="dg-foe-hp">
                    <i class="dg-gem is-red"></i>
                    <div class="dg-bar"><div class="dg-bar-fill" style="width: ${percent}%"></div><span class="dg-bar-text">${m.hp} / ${m.maxHp}</span></div>
                    <i class="dg-gem is-violet"></i>
                </div>
                ${charge ? DungeonEncounter.chargeMeter(charge) : ''}
                ${DungeonView.packStrip(delve.foes?.slice(1) || [])}
            </div>`;
    },

    /**
     * A boss's charge for your question now, from the snapshot: { step (0-based), steps, full }, or null when
     * you aren't fighting a boss. Full charge is the cycle's last step: the server decides what it does (a miss
     * takes the big blow, a right answer staggers it); the screen only has to say so.
     */
    chargeOf(delve) {
        const steps = delve.bossChargeSteps || 0;
        if (steps < 2) return null;  // the server sends 0 when there's no cycle
        const step = Math.max(0, Math.min(steps - 1, delve.bossCharge || 0));
        return { step, steps, full: step === steps - 1 };
    },

    /**
     * The charge meter under the boss's HP bar: a pip per step, filled up to this one (the last pip is red),
     * and a word for how charged it is. Calm while it builds; at full charge it pulses red (css/dungeon-encounter.css).
     */
    chargeMeter({ step, steps, full }) {
        const pips = Array.from({ length: steps }, (_, i) =>
            `<i class="dg-charge-pip ${i <= step ? 'is-lit' : ''} ${i === steps - 1 ? 'is-last' : ''}"></i>`).join('');
        const word = full ? Theme.text('dungeon.chargeFull')
            : step === 0 ? Theme.text('dungeon.chargeWeak')
            : Theme.text('dungeon.charging');
        return `
            <div class="dg-charge ${full ? 'is-full' : ''}" style="--step: ${step}">
                <span class="dg-charge-word">${word}</span>
                <span class="dg-charge-pips">${pips}</span>
            </div>`;
    },

    /**
     * The monster's hourglass, standing down the left of the answers: it drains from the top, and the
     * monster attacks when it's empty. Stopped while your buttons are dead; iced over by a Freeze, which
     * thaws by itself when the freeze ends (--thaw).
     */
    hourglass(delve, serverNow) {
        const running = !!delve.attackAtMs;
        const frozenFor = delve.frozenUntilMs > serverNow ? Math.round(delve.frozenUntilMs - serverNow) : 0;
        const bar = running
            ? `<div class="timer-bar is-vertical" data-starts-at="${delve.attackStartedAtMs}" data-ends-at="${delve.attackAtMs}"><div class="timer-bar-fill"></div></div>`
            : `<div class="timer-bar is-vertical is-stopped"><div class="timer-bar-fill"></div></div>`;
        return `
            <div class="dg-glass ${running ? '' : 'is-stopped'} ${frozenFor ? 'is-frozen' : ''}" style="--thaw: ${frozenFor}ms">
                ${running ? `<span class="dg-glass-seconds" data-ends-at="${delve.attackAtMs}" data-hurry-at="3"></span>` : ''}
                ${bar}
                ${PixelSprites.html('icon-hourglass', 'dg-glass-icon')}
            </div>`;
    },

    /**
     * One answer: the rune box (the symbol on that button, in its colour) and the framed answer. A 50/50
     * leaves the answers it took away in place, crossed out, so the buttons don't seem to move. revealed: a
     * Warlock's patron is pointing at this one (a purple glow, on the shared idle beat so redraws don't jolt it).
     */
    answer(text, i, dimmed, eliminated, revealed = false) {
        const letter = UI.ANSWER_LETTERS[i];
        const glow = revealed ? `style="--idle0: ${DungeonView.idlePhase(1200)}ms"` : '';
        return `
            <div class="dg-answer ${dimmed ? 'is-dimmed' : ''} ${eliminated ? 'is-eliminated' : ''} ${revealed ? 'is-revealed' : ''}" data-letter="${letter}" ${glow}>
                <span class="dg-rune"><i class="dg-rune-glyph" style="--glyph: url('${PixelSprites.url(`rune-${letter}`)}')"></i></span>
                <span class="dg-answer-text"><b>${letter})</b> ${UI.escape(text)}</span>
            </div>`;
    },
};

// The runes on the four answer buttons (drawn in white; CSS fills them with the answer's colour, so a
// theme's answer colours carry through) and the hourglass. Placeholders, like pixel-sprites.js.
Object.assign(PixelSprites.SPRITES, {
    'rune-A': { // a sigil in a circle
        palette: { w: '#ffffff' },
        rows: ['...wwwww...', '..w.....w..', '.w...w...w.', 'w...www...w', 'w..w.w.w..w', 'w.wwwwwww.w', 'w..w...w..w', 'w.w.....w.w', '.ww.....ww.', '..w.....w..', '...wwwww...'],
    },
    'rune-B': { // an anchor
        palette: { w: '#ffffff' },
        rows: ['....www....', '....w.w....', '....www....', '.wwwwwwwww.', '.....w.....', '.....w.....', 'w....w....w', 'ww...w...ww', '.ww..w..ww.', '..wwwwwww..', '....www....'],
    },
    'rune-C': { // a leaf
        palette: { w: '#ffffff' },
        rows: ['.......wwww', '.....ww..ww', '....w...w.w', '...w...w..w', '..w...w..w.', '.w...w...w.', '.w..w...w..', 'w..w...w...', 'w.w..ww....', 'ww.ww......', 'w..........'],
    },
    'rune-D': { // a lightning bolt
        palette: { w: '#ffffff' },
        rows: ['.....wwwww.', '....wwwww..', '...wwwww...', '..wwwww....', '.wwwwwwwww.', '....wwwww..', '...wwwww...', '..wwwww....', '.wwww......', 'www........', 'w..........'],
    },
    'icon-hourglass': {
        palette: { k: '#000000', w: '#c9a26b', s: '#ffd23f', g: '#a7d8ff' },
        rows: ['wwwwwwwww', '.kgggggk.', '.kssssgk.', '..kssgk..', '...ksk...', '..kgsgk..', '.kgssssk.', '.ksssssk.', 'wwwwwwwww'],
    },
});
