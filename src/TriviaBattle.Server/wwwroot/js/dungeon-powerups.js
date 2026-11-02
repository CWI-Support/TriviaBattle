// Power-ups on the station screen (Dungeon format). The rules are on the server (DungeonRun); this only
// draws what the snapshot's delve says:
//   - the slot: a row across the bottom of the player plate with the power-up you hold, lit like the
//     action button that uses it (nothing at all with empty pockets),
//     and what's armed (a Fireball ready, a Shield up). A Freeze shows on the hourglass instead
//     (DungeonView.enemy), which thaws by itself: a mark here would need a redraw to go away.
//   - a pop over the monster strip when one is used ("POTION! Ann +50 HP"), once per use
// Finding one is part of the battle beat (DungeonBattle.stamps: LOOT!). The 50/50's hidden answers are
// drawn by DungeonView.station (is-eliminated); the Time Crystal's seconds show as the clock's "+20s".
// Needs ui.js, theme.js, pixel-sprites.js and dungeon-view.js on the page. Styles: css/dungeon-powerups.css.

const DungeonPowerUps = {
    /** The power-up's name, in the theme's words. Literal keys, so ThemeFilesTests can check them. */
    name(kind) {
        switch (kind) {
            case 'Potion': return Theme.text('dungeon.powerUpPotion');
            case 'Fireball': return Theme.text('dungeon.powerUpFireball');
            case 'Freeze': return Theme.text('dungeon.powerUpFreeze');
            case 'FiftyFifty': return Theme.text('dungeon.powerUpFiftyFifty');
            case 'Shield': return Theme.text('dungeon.powerUpShield');
            case 'TimeCrystal': return Theme.text('dungeon.powerUpTimeCrystal');
            default: return UI.escape(kind);
        }
    },

    icon(kind, cssClass = 'dg-icon') {
        return PixelSprites.html(`icon-pu-${kind}`, cssClass);
    },

    /** The slot on the player plate: what you hold and what's armed. Nothing when there's neither. */
    slot(delve) {
        const held = delve.powerUp;
        const armed = [
            delve.fireballReady ? `<span class="dg-armed is-fireball">${DungeonPowerUps.icon('Fireball')}${Theme.text('dungeon.armedFireball')}</span>` : '',
            delve.shielded ? `<span class="dg-armed is-shield">${DungeonPowerUps.icon('Shield')}${Theme.text('dungeon.armedShield')}</span>` : '',
        ].join('');
        if (!held && !armed) return '';
        const inHand = held
            ? `<div class="dg-slot-held">
                   ${DungeonPowerUps.icon(held, 'dg-slot-icon')}
                   <b class="dg-slot-name">${DungeonPowerUps.name(held)}</b>
                   <small class="dg-slot-press">${Theme.text('dungeon.powerUpPress')}</small>
               </div>`
            : '';
        return `
            <div class="dg-slot ${held ? 'is-full' : ''}">
                ${inHand}
                ${armed ? `<div class="dg-slot-armed">${armed}</div>` : ''}
            </div>`;
    },

    /**
     * If a power-up was used since this key was last drawn, starts its pop (an effect under the key, so
     * a redraw carries it on). Teammates' uses show too when they're aimed at you (a Potion).
     */
    noticeUse(key, delve, nameOf) {
        const use = delve.lastPowerUp;
        const before = DungeonView.seen[key];
        DungeonView.seen[key] = use?.number || 0;
        if (before === undefined || !use || use.number === before) return;
        DungeonView.effect(key, DungeonPowerUps.pop(use, nameOf), 1800);
    },

    /** "POTION!" with what it did underneath. */
    pop(use, nameOf) {
        const values = { name: nameOf(use.targetOwnerId), amount: use.amount };
        let detail;
        switch (use.powerUp) {
            case 'Potion': detail = use.revived ? Theme.text('dungeon.usedRevive', values) : Theme.text('dungeon.usedPotion', values); break;
            case 'Fireball': detail = Theme.text('dungeon.usedFireball', values); break;
            case 'Freeze': detail = Theme.text('dungeon.usedFreeze', values); break;
            case 'FiftyFifty': detail = Theme.text('dungeon.usedFiftyFifty', values); break;
            case 'Shield': detail = Theme.text('dungeon.usedShield', values); break;
            case 'TimeCrystal': detail = Theme.text('dungeon.usedTimeCrystal', values); break;
            default: detail = '';
        }
        return `
            <div class="dg-pu-pop is-${use.powerUp}">
                ${DungeonPowerUps.icon(use.powerUp, 'dg-pu-pop-icon')}
                <div class="dg-pu-pop-name">${DungeonPowerUps.name(use.powerUp)}!</div>
                <div class="dg-pu-pop-detail">${detail}</div>
            </div>`;
    },
};

// The power-ups' pixel icons (placeholders, like every sprite in pixel-sprites.js).
Object.assign(PixelSprites.SPRITES, {
    'icon-pu-Potion': {
        palette: { k: '#000000', g: '#c9d6e3', r: '#ff3b6b', w: '#ffd0dc', c: '#8a5a2b' },
        rows: ['...ccc...', '...kgk...', '...kgk...', '..kgggk..', '.krrrrrk.', 'krwrrrrrk', 'krrrrrrrk', 'krrrrrrrk', '.krrrrrk.', '..kkkkk..'],
    },
    'icon-pu-Fireball': {
        palette: { k: '#000000', r: '#ff3b1f', o: '#ff9a1f', y: '#ffe04a' },
        rows: ['....r....', '...ror.r.', '..roor.r.', '.rooyor..', '.royyyor.', 'royyyyyor', 'royyyyyor', '.royyyor.', '..rooor..', '...rrr...'],
    },
    'icon-pu-Freeze': {
        palette: { b: '#7fd4ff', w: '#ffffff' },
        rows: ['....b....', '.b..b..b.', '..b.w.b..', '...bwb...', 'bbwwwwwbb', '...bwb...', '..b.w.b..', '.b..b..b.', '....b....'],
    },
    'icon-pu-FiftyFifty': {
        palette: { k: '#000000', y: '#ffd23f', w: '#ffffff' },
        rows: ['kkkkkkkkk', 'kyyyykwwk', 'kykkykwkk', 'kyyyykwwk', 'kkkyykkwk', 'kyyyykwwk', 'kkkkkkkkk'],
    },
    'icon-pu-Shield': {
        palette: { k: '#000000', s: '#9aa7b8', S: '#5f6b7c', y: '#ffd23f' },
        rows: ['kkkkkkkkk', 'ksssysssk', 'ksssysssk', 'kyyyyyyyk', 'ksssysssk', '.kSSySSk.', '..kSySk..', '...kyk...', '....k....'],
    },
    'icon-pu-TimeCrystal': {
        palette: { k: '#000000', c: '#b56bff', C: '#7a3fd0', w: '#f3e3ff' },
        rows: ['...kkk...', '..kwcck..', '.kwccCCk.', 'kwcccCCCk', 'kcccCCCCk', '.kccCCCk.', '..kcCCk..', '...kCk...', '....k....'],
    },
});
