// The 3D models for the battle scene, by the same keys config/dungeon.json uses for a monster's Sprite
// (plus the heroes, by Core's hero keys). Placeholders for now (CC0 packs, see media/models/CREDITS.md); the real art drops in
// here. A Sprite with no entry here (a new monster, or a picture path) is shown as its flat 2D picture
// standing in the room, so nothing is ever missing.
//
// Every model's animations have different names, so each entry says which clip plays which part:
//   idle    loops while nothing is happening
//   attack  the blow; contact = how far into the clip (seconds) the blow lands, so it can be lined
//           up with the beat's impact moment (and the server's sound)
//   hurt    being hit (optional: without it the model is just knocked back)
//   death   being killed (optional: without it the model just bursts)
//   run     heroes: running on to the next room (the walk; without it they just stand)
// height  how tall it stands in the room (the knight is about 2.3; a back wall is 4)
// scale   optional: a fixed scale instead (the heroes: one rig, one size, so a hat makes one taller)
// width   its body's width, in the room's units (optional: measured from the model otherwise). Give it when the idle
//         pose is much wider than the body (arms or a weapon held out): the width frames the camera on a question
//         and sets where fighters stop to strike, so a wide pose pulls the camera back and keeps the knight away.
// turn    extra rotation (radians) if the model doesn't face +Z like glTF models should
// hover   how high it floats (the bat)
// mask    how big a rival's face is on it (a race or a battle), as a share of its height (0.5 if not given)
// face    heroes only: where a player's hero photo goes (js/battle3d/face.js). The head mesh, the height
//         of its eyes, mouth, chin and forehead and the distance between its pupils, all in the model's own
//         units (it faces +Z), and the square of the head front the face map covers.

// The heroes a player picks at their station (Core's Heroes.All, one per hero class): KayKit adventurers, all on
// one rig with the same clips and nearly the same head, so they share these numbers and only say what's theirs.
// Two classes have no model of their own yet and borrow one with different gear: the Healer is the mage without
// his hat, with a wand and a book; the Warlock is the hooded ranger, casting spells.
//
// A new model: put its .glb in media/models/characters (credit it in CREDITS.md), give its key an entry here
// (file, which clips are its attacks, which gear shows, which part is dyed the station's colour, where a
// face goes), then save its pictures with /dev/heroes. A hero class needs its key in Core's Heroes.All too.
// The adventurers are all built on one rig, the same size bare-headed (about 2.2 to 2.3 high), so they keep the
// model's own scale (scale: 1) rather than each being squeezed to a height: the wizard's hat stands above the
// rest instead of shrinking him to fit it. height is still their nominal height, for the cameras and effects.
const ADVENTURER = { height: 2.3, scale: 1, idle: 'Idle', hurt: 'Hit_A', block: 'Block_Hit', death: 'Death_A', cheer: 'Cheer', run: 'Running_A' };

/**
 * Where a hero photo goes on an adventurer's head: its own face. The adventurers' heads are all one shape
 * (eyes at about 1.60, chin about 1.22, measured from each head's own picture and vertices), so two layouts fit
 * them all. Only the skin in the area becomes the photo (js/battle3d/face.js): hair, hoods and beards stay.
 *   helmeted: a helmet or a beard covers the lower face, so the photo is the middle of the face (about 1.30
 *             to 1.80 high), its chin squashed up under the mouth where the helmet hides it.
 *   open:     nothing covers it, so the photo is the whole face, from the hairline (1.92) to the bottom of the
 *             chin (1.23), in a face's own proportions; the surface curves back under the chin as the head's does.
 * hairline: how high the face reaches (1.8: higher, it would take bits of a fringe or a hood's rim). The
 * mage's hair parts over his forehead, showing skin up to about 1.9, so his reaches 1.95 to cover it.
 * brows: brown brows (not dark like the eyes, so not found that way): the band they sit in, above the eyes and
 * under the fringe; the photo's own brows take their place.
 * Of what lies between skin, a helmeted face takes anything (the barbarian's lip under his moustache); an open
 * face only what's dark (eyes, mouth), since a hood sits between skin there too.
 * The face's curve across was measured from the knight's skin.
 */
const face = (mesh, { layout = 'open', hairline = 1.8, brows = null } = {}) => ({
    mesh,
    brows,
    ...(layout === 'helmeted'
        ? { top: 2.0, eyes: 1.61, mouth: 1.445, chin: 1.345 }
        : { top: 1.92, eyes: 1.61, mouth: 1.39, chin: 1.23 }),
    eyeSpan: 0.34,
    skin: '#f6c29e', // the head's skin, round the edge of the face
    area: { left: -0.55, bottom: layout === 'helmeted' ? 1.21 : 1.1, side: 1.1 },
    surface: layout === 'helmeted'
        ? { front: 0.45, curve: 1.25, enclosed: 'any', area: { from: 0.2, halfWidth: 0.42, bottom: 1.3, top: hairline } }
        // chin: below from, the face falls back by curve * (from - y) squared (0.2 at the bottom of the chin)
        : { front: 0.45, curve: 1.25, enclosed: 'dark', chin: { from: 1.4, curve: 5 }, area: { from: 0.2, halfWidth: 0.42, bottom: 1.2, top: hairline } },
});

// The rogue's and the ranger's brows (measured: the ranger's from 1.63, the rogue's up to 1.77, out to 0.33; the
// rogue's fringe starts at 1.78). The eyes below are dark, not brown, so the band can reach down to them.
const BROWS = { bottom: 1.62, top: 1.77, halfWidth: 0.34 };

const melee = (clip, contact) => ({ clip, contact });

export const HEROES = {
    tank: {
        ...ADVENTURER,
        file: '/media/models/characters/knight.glb',
        // A few different swings, picked per beat so the 40th hit doesn't look like the first.
        attacks: [melee('1H_Melee_Attack_Chop', 0.42), melee('1H_Melee_Attack_Slice_Diagonal', 0.38), melee('1H_Melee_Attack_Stab', 0.55)],
        critAttack: melee('1H_Melee_Attack_Slice_Horizontal', 0.42),
        comboAttack: melee('Dualwield_Melee_Attack_Slice', 0.45),
        // Each comes with all its gear as separate parts. Only these show; the rest are for later
        // (power-ups and loot can swap in another shield or the big sword).
        gear: ['1H_Sword', 'Round_Shield', 'Knight_Helmet', 'Knight_Cape'],
        allGear: ['1H_Sword', '1H_Sword_Offhand', '2H_Sword', 'Badge_Shield', 'Rectangle_Shield', 'Round_Shield', 'Spike_Shield', 'Knight_Helmet', 'Knight_Cape'],
        tinted: 'Knight_Cape', // the part dyed the station's colour
        face: face('Knight_Head', { layout: 'helmeted' }),
    },
    pugilist: {
        ...ADVENTURER,
        file: '/media/models/characters/barbarian.glb',
        attacks: [melee('1H_Melee_Attack_Chop', 0.42), melee('1H_Melee_Attack_Slice_Diagonal', 0.38), melee('1H_Melee_Attack_Slice_Horizontal', 0.42)],
        critAttack: melee('Dualwield_Melee_Attack_Chop', 0.5),
        comboAttack: melee('Dualwield_Melee_Attack_Slice', 0.45),
        gear: ['1H_Axe', 'Barbarian_Round_Shield', 'Barbarian_Hat', 'Barbarian_Cape'],
        allGear: ['1H_Axe', '1H_Axe_Offhand', '2H_Axe', 'Barbarian_Round_Shield', 'Mug', 'Barbarian_Hat', 'Barbarian_Cape'],
        tinted: 'Barbarian_Cape',
        face: face('Barbarian_Head', { layout: 'helmeted' }), // the beard covers the lower face
    },
    wizard: {
        ...ADVENTURER,
        file: '/media/models/characters/mage.glb',
        attacks: [melee('Spellcast_Shoot', 0.45), melee('1H_Melee_Attack_Chop', 0.42)],
        critAttack: melee('Spellcast_Shoot', 0.45),
        comboAttack: melee('Spellcast_Shoot', 0.45),
        gear: ['2H_Staff', 'Mage_Hat', 'Mage_Cape'],
        allGear: ['Spellbook', 'Spellbook_open', '1H_Wand', '2H_Staff', 'Mage_Hat', 'Mage_Cape'],
        tinted: 'Mage_Cape',
        face: face('Mage_Head', { hairline: 1.95 }),
    },
    healer: { // the mage's model (no model of its own yet): no hat, a wand and a book
        ...ADVENTURER,
        file: '/media/models/characters/mage.glb',
        attacks: [melee('Spellcast_Shoot', 0.45), melee('1H_Melee_Attack_Chop', 0.42)],
        critAttack: melee('Spellcast_Shoot', 0.45),
        comboAttack: melee('Spellcast_Shoot', 0.45),
        gear: ['1H_Wand', 'Spellbook', 'Mage_Cape'],
        allGear: ['Spellbook', 'Spellbook_open', '1H_Wand', '2H_Staff', 'Mage_Hat', 'Mage_Cape'],
        tinted: 'Mage_Cape',
        face: face('Mage_Head', { hairline: 1.95 }),
    },
    rogue: {
        ...ADVENTURER,
        file: '/media/models/characters/rogue.glb',
        attacks: [melee('Dualwield_Melee_Attack_Chop', 0.5), melee('Dualwield_Melee_Attack_Slice', 0.45), melee('Dualwield_Melee_Attack_Stab', 0.6)],
        critAttack: melee('1H_Melee_Attack_Slice_Horizontal', 0.42),
        comboAttack: melee('Dualwield_Melee_Attack_Slice', 0.45),
        gear: ['Knife', 'Knife_Offhand', 'Rogue_Cape'],
        allGear: ['Knife_Offhand', '1H_Crossbow', '2H_Crossbow', 'Knife', 'Throwable', 'Rogue_Cape'],
        tinted: 'Rogue_Cape',
        face: face('Rogue_Head', { brows: BROWS }),
    },
    ranger: {
        ...ADVENTURER,
        file: '/media/models/characters/ranger.glb',
        attacks: [melee('1H_Ranged_Shoot', 0.35), melee('Throw', 0.5)],
        critAttack: melee('1H_Ranged_Shoot', 0.35),
        comboAttack: melee('Dualwield_Melee_Attack_Stab', 0.6),
        gear: ['1H_Crossbow', 'Knife_Offhand', 'Rogue_Cape'],
        allGear: ['Knife_Offhand', '1H_Crossbow', '2H_Crossbow', 'Knife', 'Throwable', 'Rogue_Cape'],
        tinted: 'Rogue_Cape',
        face: face('Rogue_Head_Hooded', { brows: BROWS }),
    },
    warlock: { // the ranger's model (no model of its own yet), hooded and casting
        ...ADVENTURER,
        file: '/media/models/characters/ranger.glb',
        attacks: [melee('Spellcast_Shoot', 0.45), melee('Throw', 0.5)],
        critAttack: melee('Spellcast_Shoot', 0.45),
        comboAttack: melee('Spellcast_Shoot', 0.45),
        gear: ['Knife_Offhand', 'Rogue_Cape'],
        allGear: ['Knife_Offhand', '1H_Crossbow', '2H_Crossbow', 'Knife', 'Throwable', 'Rogue_Cape'],
        tinted: 'Rogue_Cape',
        face: face('Rogue_Head_Hooded', { brows: BROWS }),
    },
};

export const MODELS = {
    ...HEROES,
    rat: {
        file: '/media/models/characters/rat.glb',
        height: 1.0, idle: 'RatArmature|Rat_Idle', death: 'RatArmature|Rat_Death',
        attack: { clip: 'RatArmature|Rat_Attack', contact: 0.3 },
    },
    slime: {
        file: '/media/models/characters/slime.glb',
        height: 1.2, idle: 'Armature|Slime_Idle', death: 'Armature|Slime_Death',
        attack: { clip: 'Armature|Slime_Attack', contact: 0.3 },
    },
    bat: {
        file: '/media/models/characters/bat.glb',
        height: 1.1, hover: 1.2, idle: 'BatArmature|Bat_Flying', hurt: 'BatArmature|Bat_Hit', death: 'BatArmature|Bat_Death',
        attack: { clip: 'BatArmature|Bat_Attack', contact: 0.4 },
    },
    goblin: {
        file: '/media/models/characters/goblin.glb',
        height: 1.7,
        idle: 'EnemyArmature|EnemyArmature|EnemyArmature|Idle',
        hurt: 'EnemyArmature|EnemyArmature|EnemyArmature|HitRecieve',
        death: 'EnemyArmature|EnemyArmature|EnemyArmature|Death',
        attack: { clip: 'EnemyArmature|EnemyArmature|EnemyArmature|Attack', contact: 0.25 },
    },
    skeleton: {
        file: '/media/models/characters/skeleton-minion.glb',
        height: 2.2, idle: 'Idle', hurt: 'Hit_A', death: 'Death_C_Skeletons',
        attack: { clip: 'Unarmed_Melee_Attack_Punch_A', contact: 0.55 },
    },
    'goblin-boss': {
        file: '/media/models/characters/orc.glb',
        // A head taller than the knight. Its idle pose holds its arms and club wide (it measures as wide as it is
        // tall), so its body width is given: measured, the camera backed off to fit the arms and the knight couldn't reach it.
        height: 2.8, width: 1.6,
        idle: 'CharacterArmature|Idle', hurt: 'CharacterArmature|HitReact', death: 'CharacterArmature|Death',
        attack: { clip: 'CharacterArmature|Weapon', contact: 0.4 },
    },
};

/**
 * The dungeon is a corridor of the same stretch over and over, ROOM_LENGTH long, so that when a kill empties a
 * room the hero can walk on to the next (beats.js, the walk): the corridor scrolls past by exactly one stretch, the
 * next room's monsters come in with it, and at the end it's put back where it started (the same picture, so it
 * never shows). Three stretches, one each side of the fight, so the scroll never runs out of corridor.
 */
export const ROOM_LENGTH = 12;

/** One stretch of corridor: pieces of the dungeon kit, placed as [file, x, y, z, turn] (x from about -4 to 8). */
const STRETCH = (() => {
    const piece = name => `/media/models/room/${name}.glb`;
    const pieces = [];
    // The floor: 4 x 4 tiles, from the hero's side to the monster's.
    for (const x of [-2, 2, 6])
        for (const z of [-2, 2, 6])
            pieces.push([piece('floor_tile_large'), x, 0, z, 0]);
    // The back wall, two storeys high: arches behind the fight, a plain stretch between one room and the next.
    for (const x of [-2, 2]) pieces.push([piece('wall_arched'), x, 0, -4.5, 0]);
    pieces.push([piece('wall'), 6, 0, -4.5, 0]);
    for (const x of [-2, 2, 6]) pieces.push([piece('wall'), x, 4, -4.5, 0]);
    pieces.push(
        [piece('banner_patternA_red'), 0, 0.6, -4.9, 0],
        [piece('torch_mounted'), -4, 2.6, -4, 0],
        [piece('torch_mounted'), 4, 2.6, -4, 0],
        // A pillar where one room meets the next (so the fight has one each side, at -6 and 6), crates against its
        // near side and a barrel against its far side (which the fight sees against the left pillar), as the room
        // always had them; a candle by each.
        [piece('pillar_decorated'), 6, 0, -2.8, 0],
        [piece('crates_stacked'), 4.8, 0, -2.6, -0.3],
        [piece('barrel_large'), 7.3, 0, -2.4, 0.4],
        [piece('candle_triple'), 8.3, 0, -2.9, 0],     // by the barrel (the fight's left: -3.7)
        [piece('candle_triple'), 3.9, 0, -1.5, 0.8],
    );
    return pieces;
})();

const STRETCHES = [-ROOM_LENGTH, 0, ROOM_LENGTH];

/** The corridor the fight is in: every stretch's pieces, as [file, x, y, z, turn]. */
export const ROOM = STRETCHES.flatMap(dx => STRETCH.map(([file, x, y, z, turn]) => [file, x + dx, y, z, turn]));

/** Where the torch flames are (for their lights): just in front of each stretch's two mounted torches. */
export const TORCH_LIGHTS = STRETCHES.flatMap(dx => [[-4 + dx, 3.4, -3.4], [4 + dx, 3.4, -3.4]]);
