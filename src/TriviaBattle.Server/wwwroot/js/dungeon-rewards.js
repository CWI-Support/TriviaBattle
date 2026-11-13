// Floor rewards on the screens (Dungeon format). The rules are on the server (DungeonRun: answer enough of a
// floor right and, once its boss dies, pick a reward); this only draws what the snapshot says:
//   - the meter in the station's top line: this floor's right answers so far, against the share needed
//     (delve.trial, delve.rewardAccuracy)
//   - the pick: after the boss's death beat, the station shows the cards (you.reward), one per answer button,
//     with the time left to choose. The question waits (you.question is null) until one is picked.
//   - the moment after a pick (beat kind "reward"): what it gives, big, before the question comes back
//   - the rewards held, as little badges on the player plate and the main screen's lanes (delve.rewards)
//   - a line on the floor curtain: reward earned, or how close they came (delve.lastTrial)
// Needs ui.js, theme.js, pixel-sprites.js and dungeon-view.js on the page. Styles: css/dungeon-rewards.css.

const DungeonRewards = {
    /** The reward's name, in the theme's words. Literal keys, so ThemeFilesTests can check them. */
    name(kind) {
        switch (kind) {
            case 'Vitality': return Theme.text('dungeon.rewardVitality');
            case 'Might': return Theme.text('dungeon.rewardMight');
            case 'Armor': return Theme.text('dungeon.rewardArmor');
            case 'Time': return Theme.text('dungeon.rewardTime');
            case 'Focus': return Theme.text('dungeon.rewardFocus');
            case 'Mastery': return Theme.text('dungeon.rewardMastery');
            case 'LastStand': return Theme.text('dungeon.rewardLastStand');
            default: return UI.escape(kind);
        }
    },

    /**
     * What a reward's card says it gives, with its number (amount: HP, percent or seconds, see
     * DungeonRun.RewardAmount). Mastery says what the class's own power becomes.
     */
    describe(kind, amount, heroClass) {
        const values = { amount: DungeonRewards.number(amount) };
        switch (kind) {
            case 'Vitality': return Theme.text('dungeon.rewardVitalityText', values);
            case 'Might': return Theme.text('dungeon.rewardMightText', values);
            case 'Armor': return Theme.text('dungeon.rewardArmorText', values);
            case 'Time': return Theme.text('dungeon.rewardTimeText', values);
            case 'Focus': return Theme.text('dungeon.rewardFocusText', values);
            case 'LastStand': return Theme.text('dungeon.rewardLastStandText', values);
            case 'Mastery':
                switch (heroClass) {
                    case 'rogue': return Theme.text('dungeon.rewardMasteryRogue', values);
                    case 'tank': return Theme.text('dungeon.rewardMasteryTank', values);
                    case 'healer': return Theme.text('dungeon.rewardMasteryHealer', values);
                    case 'wizard': return Theme.text('dungeon.rewardMasteryWizard', values);
                    case 'pugilist': return Theme.text('dungeon.rewardMasteryPugilist', values);
                    case 'warlock': return Theme.text('dungeon.rewardMasteryWarlock', values);
                    case 'ranger': return Theme.text('dungeon.rewardMasteryRanger', values);
                    default: return '';
                }
            default: return '';
        }
    },

    /** 3 -> "3", 2.5 -> "2.5" (amounts are whole numbers or halves). */
    number(amount) {
        return Number.isInteger(amount) ? String(amount) : amount.toFixed(1);
    },

    icon(kind, cssClass = 'dg-icon') {
        return PixelSprites.html(`icon-rw-${kind}`, cssClass);
    },

    /** The share needed as a whole percent (0.8 -> 80). */
    neededPercent(delve) {
        return Math.round((delve.rewardAccuracy || 0) * 100);
    },

    /**
     * The meter in the station's top line: a chest, this floor's right answers of those asked so far, and a bar
     * with a mark where the reward starts. Gold while they're on course for it, red when they're under.
     * Nothing with rewards off.
     */
    meter(delve) {
        if (!delve.rewardAccuracy || !delve.trial) return '';
        const { right, answered } = delve.trial;
        const share = answered ? right / answered : 1;
        const onCourse = share >= delve.rewardAccuracy - 1e-9;
        return `
            <span class="dg-trial ${onCourse ? 'is-on-course' : 'is-under'}" style="--share: ${(share * 100).toFixed(1)}%; --needed: ${DungeonRewards.neededPercent(delve)}%">
                ${PixelSprites.html('icon-chest', 'dg-trial-icon')}
                <span class="dg-trial-count">${right}/${answered}</span>
                <span class="dg-trial-bar"><i></i></span>
            </span>`;
    },

    /** The rewards they hold, one badge each (a repeat shows "x2"). Nothing before the first. */
    badges(delve, cssClass = '') {
        const counts = new Map();
        for (const taken of delve.rewards || []) counts.set(taken.reward, (counts.get(taken.reward) || 0) + 1);
        if (!counts.size) return '';
        return `<div class="dg-boons ${cssClass}">${[...counts].map(([kind, n]) =>
            `<span class="dg-boon is-${kind.toLowerCase()}">${DungeonRewards.icon(kind, 'dg-boon-icon')}${n > 1 ? `<b>x${n}</b>` : ''}</span>`).join('')}</div>`;
    },

    /** True while this screen should show the pick or the moment after it, instead of the fight. */
    showing(you, serverNow) {
        if (you.beat?.kind === 'reward') return DungeonBattle.isPlaying(you, serverNow);
        return !!you.reward && !DungeonBattle.isPlaying(you, serverNow);
    },

    /** The station screen while choosing, or just after: the top line and the player plate stay. */
    station(match, you, serverNow, top, plate) {
        const body = you.beat?.kind === 'reward' && DungeonBattle.isPlaying(you, serverNow)
            ? DungeonRewards.chosen(match, you, serverNow)
            : DungeonRewards.pick(match, you, serverNow);
        return `<div class="dg-station is-reward">${top}${body}${plate}</div>`;
    },

    /**
     * The cards, one per answer button (A, B, C...), dealt in when they come up (after the boss's death beat).
     * Presses only count from pickFromMs: until then each card has a lock that fades. The bar under them runs
     * down to when one is picked for them.
     */
    pick(match, you, serverNow) {
        return DungeonRewards.board(match, you, serverNow, you.reward, null);
    },

    /**
     * The cards screen for an offer. Before a pick (chosen null) it's dealt in, with the time to choose. Just after
     * one (chosen: the card picked) it's the same screen already in place, its cards marked is-chosen and is-passed for
     * the moment after the pick to play out (css/dungeon-rewards.css, timed from the pick: --t0).
     */
    board(match, you, serverNow, offer, chosen) {
        const picking = chosen === null;
        const seed = `${match.matchId}:${you.stationId}:reward:${offer.floor}`;
        const toPick = picking ? Math.round(offer.pickFromMs - serverNow) : -99999; // picked: the veils are long gone
        const cards = offer.choices.map((choice, i) => DungeonRewards.card(choice, i, you.delve.heroClass,
            picking ? '' : i === chosen ? 'is-chosen' : 'is-passed', picking ? UI.animateAt(offer.opensAtMs + i * 140) : '')).join('');
        return `
            <div class="dg-reward ${picking ? '' : 'is-picked'}" style="--pick: ${toPick}ms">
                <div class="dg-reward-head" style="${picking ? UI.animateAt(offer.opensAtMs) : ''}">
                    ${PixelSprites.html('icon-chest', 'dg-reward-chest')}
                    <div class="dg-reward-title">${offer.forMinigame ? Theme.text('dungeon.rewardTitleMinigame', {}, seed) : Theme.text('dungeon.rewardTitle', {}, seed)}</div>
                    <div class="dg-reward-sub">${offer.forMinigame ? Theme.text('dungeon.rewardSubMinigame')
                        : Theme.text('dungeon.rewardSub', { right: offer.right, answered: offer.answered, floor: offer.floor })}</div>
                </div>
                <div class="dg-reward-cards">${cards}</div>
                <div class="dg-reward-foot">
                    <span>${Theme.text('dungeon.rewardPress')}</span>
                    <span class="dg-reward-seconds" ${picking ? `data-ends-at="${offer.endsAtMs}" data-hurry-at="4"` : ''}></span>
                </div>
                ${picking ? `<div class="timer-bar dg-reward-timer" data-starts-at="${offer.opensAtMs}" data-ends-at="${offer.endsAtMs}"><div class="timer-bar-fill"></div></div>`
                    : '<div class="timer-bar dg-reward-timer"></div>'}
            </div>`;
    },

    /** One card: its answer button's letter, the reward's icon, name and what it gives. */
    card(choice, i, heroClass, cssClass = '', style = '') {
        const letter = UI.ANSWER_LETTERS[i];
        return `
            <div class="dg-reward-card is-${choice.reward.toLowerCase()} ${cssClass}" data-letter="${letter}" style="${style}">
                <span class="dg-reward-letter">${letter}</span>
                ${DungeonRewards.icon(choice.reward, 'dg-reward-icon')}
                <span class="dg-reward-text">
                    <b class="dg-reward-name">${DungeonRewards.name(choice.reward)}</b>
                    <span class="dg-reward-detail">${DungeonRewards.describe(choice.reward, choice.amount, heroClass)}</span>
                </span>
            </div>`;
    },

    /**
     * The moment after a pick (the "reward" beat), timed from the pick (--t0) so a redraw carries on where it was:
     * the cards screen stays as it was (taken.offer: the newest pick's), the card picked lights up and the others fall
     * away; then the reward, big, with what it gives, and "picked for you" if time ran out; then it all fades (--out)
     * just before the question rises back in.
     */
    chosen(match, you, serverNow) {
        const taken = you.delve.rewards?.at(-1);
        if (!taken) return '';
        const t0 = Math.round(taken.atMs - serverNow);
        const out = Math.round(you.beat.endsAtMs - serverNow) - 350;
        const cards = taken.offer ? DungeonRewards.board(match, you, serverNow, taken.offer, taken.choice) : '';
        return `
            <div class="dg-reward-chosen ${cards ? 'has-cards' : ''}" style="--t0: ${t0}ms; --out: ${out}ms">
                ${cards}
                <div class="dg-reward-got is-${taken.reward.toLowerCase()}">
                    ${DungeonRewards.icon(taken.reward, 'dg-reward-got-icon')}
                    <div class="dg-reward-got-name">${DungeonRewards.name(taken.reward)}!</div>
                    <div class="dg-reward-got-detail">${DungeonRewards.describe(taken.reward, taken.amount, you.delve.heroClass)}</div>
                    ${taken.picked ? '' : `<div class="dg-reward-got-auto">${Theme.text('dungeon.rewardAuto')}</div>`}
                </div>
            </div>`;
    },

    /** The floor curtain's line: the floor just cleared earned a reward, or how close it came. */
    curtainLine(delve) {
        const trial = delve.lastTrial;
        if (!delve.rewardAccuracy || !trial || trial.floor !== delve.floor - 1) return '';
        const values = { right: trial.right, answered: trial.answered, needed: DungeonRewards.neededPercent(delve) };
        return trial.earned
            ? `<div class="dg-curtain-reward is-earned">${PixelSprites.html('icon-chest', 'dg-curtain-chest')}${Theme.text('dungeon.rewardEarned', values)}</div>`
            : `<div class="dg-curtain-reward is-missed">${Theme.text('dungeon.rewardMissed', values)}</div>`;
    },

    /**
     * Main screen: a reward picked since this lane was last drawn becomes one of the banners across the room
     * (DungeonView.noteMoments draws them). Returns the newest pick, once, or null.
     */
    newPick(key, delve) {
        const number = delve.rewards?.at(-1)?.number || 0;
        const before = DungeonView.seen[`reward:${key}`];
        DungeonView.seen[`reward:${key}`] = number;
        return before !== undefined && number > before ? delve.rewards.at(-1) : null;
    },
};

// The rewards' pixel icons, and the chest (placeholders, like every sprite in pixel-sprites.js).
Object.assign(PixelSprites.SPRITES, {
    'icon-chest': {
        palette: { k: '#000000', b: '#b0743a', B: '#6b4220', y: '#ffd23f' },
        rows: ['.kkkkkkk.', 'kbbbbbbbk', 'kBBBBBBBk', 'kkkkykkkk', 'kbbbybbbk', 'kbbbbbbbk', 'kBBBBBBBk', 'kkkkkkkkk'],
    },
    'icon-rw-Vitality': {
        palette: { k: '#000000', r: '#e8323c', w: '#ffc0c4', g: '#7dff8a' },
        rows: ['.kk...kk.', 'krrk.krrk', 'krwrkrrrk', 'krrrgrrrk', '.krgggrk.', '..krgrk..', '...krk...', '....k....'],
    },
    'icon-rw-Might': {
        palette: { k: '#000000', w: '#dfe6ee', b: '#a0672e', y: '#ffd23f' },
        rows: ['.......kk', '......kwk', '.....kwk.', '....kwk..', 'k..kwk...', 'kykwk....', '.kyk.....', 'kbkyk....', 'kk..k....'],
    },
    'icon-rw-Armor': {
        palette: { k: '#000000', s: '#bcc8d8', S: '#6f7c8e', w: '#ffffff' },
        rows: ['kkkkkkkkk', 'kswsssssk', 'kwsssssSk', 'ksssssSSk', 'ksssssSSk', '.ksssSSk.', '..kssSk..', '...kSk...', '....k....'],
    },
    'icon-rw-Time': {
        palette: { k: '#000000', y: '#f0b429', w: '#fff2c4', c: '#9a6a1a' },
        rows: ['kkkkkkkkk', '.kwwwwwk.', '..kyyyk..', '...kyk...', '...kwk...', '..kwywk..', '.kwyyywk.', 'kkkkkkkkk'],
    },
    'icon-rw-Focus': {
        palette: { k: '#000000', y: '#ffe04a', o: '#ff9a1f' },
        rows: ['.....kkk.', '....kyyk.', '...kyyk..', '..kyyykkk', '.kyyyyyyk', 'kkkkyyyk.', '...kyyk..', '..kyok...', '..kok....', '..kk.....'],
    },
    'icon-rw-Mastery': {
        palette: { k: '#000000', y: '#ffd23f', w: '#fff6c8' },
        rows: ['....k....', '...kyk...', '...kyk...', 'kkkkwkkkk', 'kyyywyyyk', '.kyyyyyk.', '..kyyyk..', '.kyykyyk.', 'kyyk.kyyk', 'kkk...kkk'],
    },
    'icon-rw-LastStand': {
        palette: { k: '#000000', w: '#eee6d0', r: '#e8323c' },
        rows: ['..kkkkk..', '.kwwwwwk.', 'kwwwwwwwk', 'kwkkwkkwk', 'kwkrwkrwk', 'kwwwkwwwk', '.kwwwwwk.', '..kwkwk..', '..kkkkk..'],
    },
});
