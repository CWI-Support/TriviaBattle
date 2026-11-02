// The station screen while a question is up (Dungeon format): the encounter, laid out like the design
// lead's example (docs/design/station-example.png). Top to bottom:
//   - the monster's nameplate: its name and red HP bar, with the floor and the time left at its corners
//   - the question on a parchment scroll hanging under it
//   - the monster, big, in the middle of its room (in 3D when the station draws it: data-stage="portrait"),
//     with the Host's line, being stunned or downed, and power-up pops over it; across its top, the other
//     players' moments that matter to you, with their faces (js/dungeon-feed.js)
//   - the hourglass down the left of the answers; each answer a rune box (the button's symbol, in its
//     colour) and a framed answer
//   - your party, then you
// Between questions the battle beat takes the screen instead (js/dungeon-battle.js).
// Needs ui.js, theme.js, pixel-sprites.js, dungeon-view.js, dungeon-battle.js, dungeon-powerups.js and dungeon-feed.js.
// Styles: css/dungeon-encounter.css.

const DungeonEncounter = {
    /**
     * The whole question view. parts: what DungeonView.station already worked out for both views
     * (news, plate, clockKey, powerUpKey).
     */
    view(match, you, serverNow, { news, plate, clockKey, powerUpKey }) {
        const delve = you.delve;
        const downed = delve.downedUntilMs && delve.downedUntilMs > serverNow;
        const stunned = !downed && you.lockedOutUntilMs && you.lockedOutUntilMs > serverNow;
        const seed = `${match.matchId}:${you.stationId}`;

        // Downed in a party: your buttons are dead, so you get a teammate's question to shout them the answer.
        const helping = downed ? you.helping : null;
        const shown = helping ? helping.question : you.question;
        const dimmed = (downed && !helping) || stunned;

        const overlaySeed = `${seed}:${delve.lastTaken?.number}`;
        const overlay = !downed
            ? stunned ? `<div class="dg-overlay is-stunned">${Theme.text('dungeon.stunned', {}, overlaySeed)}</div>` : ''
            : delve.party.length > 1
                ? `<div class="dg-overlay is-downed is-party">${Theme.text('dungeon.downedParty', { left: delve.reviveNeeded - delve.reviveProgress }, overlaySeed)}</div>`
                : `<div class="dg-overlay is-downed">${Theme.text('dungeon.downed', { seconds: Theme.raw(`<span data-ends-at="${delve.downedUntilMs}"></span>`) }, overlaySeed)}</div>`;

        const feedback = DungeonView.feedback(delve, you.lastAnswer, seed);
        const tag = helping
            ? `<div class="dg-scroll-tag is-help">${Theme.text('dungeon.helpTeammate', { name: helping.name }, overlaySeed)}</div>`
            : match.category.includes(',') ? `<div class="dg-scroll-tag">${UI.escape(shown.category)}</div>` : '';

        // Just after a battle beat the question comes in (and after a kill the next monster drops in),
        // timed from the beat's end so a redraw doesn't play it again. No beat: it's simply there.
        const beat = you.beat;
        const arrival = !beat ? '' : `is-after-beat ${['kill', 'floor', 'boss'].includes(beat.kind) ? 'is-new-foe' : ''}`;
        const arrivalStyle = beat ? `style="--q0: ${Math.round(beat.endsAtMs - serverNow)}ms"` : '';

        return `
            <div class="dg-station is-question is-encounter ${arrival} ${news.taken ? 'is-hurt' : ''}" ${arrivalStyle}>
                ${DungeonEncounter.foePlate(match, delve, clockKey)}
                <div class="dg-scroll ${helping ? 'is-helping' : ''}">${tag}${UI.questionMedia(shown.mediaUrl)}<div>${UI.escape(shown.text)}</div></div>
                <div class="dg-stage" data-stage="portrait">
                    ${DungeonBattle.room()}
                    ${DungeonView.monster(delve, news, `fx:station:${you.stationId}`)}
                    <div class="dg-feedback ${feedback.tone}">${feedback.html}</div>
                    ${DungeonFeed.caption()}
                    ${overlay}
                    ${DungeonView.activeEffects(powerUpKey)}
                </div>
                <div class="dg-answer-area">
                    ${DungeonEncounter.hourglass(delve, serverNow)}
                    <div class="dg-answers ${helping ? 'is-helping' : ''}">
                        ${shown.answers.map((text, i) => DungeonEncounter.answer(text, i, dimmed, shown.hidden?.includes(i))).join('')}
                    </div>
                </div>
                ${DungeonView.teammates(delve, you.stationId, match)}
                ${plate}
            </div>`;
    },

    /** The monster's nameplate: floor and time at the corners, its name, its HP bar between two gems. */
    foePlate(match, delve, clockKey) {
        const m = delve.monster;
        const percent = m.maxHp > 0 ? Math.max(0, Math.min(100, (m.hp / m.maxHp) * 100)) : 0;
        return `
            <div class="dg-foe-plate ${m.isBoss ? 'is-boss' : ''}">
                <div class="dg-foe-corners">
                    <span class="dg-floor-badge">${Theme.text('dungeon.floorShort', { floor: delve.floor })}</span>
                    <span class="dg-clock"><small>${Theme.text('dungeon.time')}</small><span data-ends-at="${match.phaseEndsAtMs}" data-hurry-at="10"></span>${DungeonView.activeEffects(clockKey)}</span>
                </div>
                <div class="dg-foe-name">${UI.escape(m.name)}</div>
                <div class="dg-foe-hp">
                    <i class="dg-gem is-red"></i>
                    <div class="dg-bar"><div class="dg-bar-fill" style="width: ${percent}%"></div><span class="dg-bar-text">${m.hp} / ${m.maxHp}</span></div>
                    <i class="dg-gem is-violet"></i>
                </div>
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
     * leaves the answers it took away in place, crossed out, so the buttons don't seem to move.
     */
    answer(text, i, dimmed, eliminated) {
        const letter = UI.ANSWER_LETTERS[i];
        return `
            <div class="dg-answer ${dimmed ? 'is-dimmed' : ''} ${eliminated ? 'is-eliminated' : ''}" data-letter="${letter}">
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
