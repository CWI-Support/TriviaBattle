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
// height  how tall it stands in the room (the knight is about 2.3; a back wall is 4)
// turn    extra rotation (radians) if the model doesn't face +Z like glTF models should
// hover   how high it floats (the bat)
// mask    how big a rival's face is on it (a race or a battle), as a share of its height (0.5 if not given)
// face    heroes only: where a player's hero photo goes (js/battle3d/face.js). The head mesh, the height
//         of its eyes, mouth, chin and forehead and the distance between its pupils, all in the model's own
//         units (it faces +Z), and the square of the head front the face map covers.

// The heroes a player picks at their station (Core's Heroes.All): KayKit adventurers, all on one rig with
// the same clips and nearly the same head, so they share these numbers and only say what's theirs.
const ADVENTURER = { height: 2.3, idle: 'Idle', hurt: 'Hit_A', block: 'Block_Hit', death: 'Death_A', cheer: 'Cheer' };

/**
 * Where a hero photo goes on an adventurer's head: its own face, inside the helmet, hat or hood (about 1.40
 * to 1.70 high). The face's curve was measured from the knight's skin; the heights set by eye. lift moves it
 * all up (the mage's face sits higher, under his hat).
 */
const face = (mesh, lift = 0) => ({
    mesh,
    top: 2.0 + lift, eyes: 1.61 + lift, mouth: 1.445 + lift, chin: 1.345 + lift, eyeSpan: 0.34,
    skin: '#f6c29e', // the head's skin, round the edge of the face
    area: { left: -0.55, bottom: 1.21 + lift, side: 1.1 },
    surface: { front: 0.45, curve: 1.25, area: { from: 0.2, halfWidth: 0.42, bottom: 1.36 + lift, top: 1.8 + lift } },
});

const melee = (clip, contact) => ({ clip, contact });

export const HEROES = {
    knight: {
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
        face: face('Knight_Head'),
    },
    barbarian: {
        ...ADVENTURER,
        file: '/media/models/characters/barbarian.glb',
        attacks: [melee('1H_Melee_Attack_Chop', 0.42), melee('1H_Melee_Attack_Slice_Diagonal', 0.38), melee('1H_Melee_Attack_Slice_Horizontal', 0.42)],
        critAttack: melee('Dualwield_Melee_Attack_Chop', 0.5),
        comboAttack: melee('Dualwield_Melee_Attack_Slice', 0.45),
        gear: ['1H_Axe', 'Barbarian_Round_Shield', 'Barbarian_Hat', 'Barbarian_Cape'],
        allGear: ['1H_Axe', '1H_Axe_Offhand', '2H_Axe', 'Barbarian_Round_Shield', 'Mug', 'Barbarian_Hat', 'Barbarian_Cape'],
        tinted: 'Barbarian_Cape',
        face: face('Barbarian_Head'),
    },
    mage: {
        ...ADVENTURER,
        file: '/media/models/characters/mage.glb',
        attacks: [melee('Spellcast_Shoot', 0.45), melee('1H_Melee_Attack_Chop', 0.42)],
        critAttack: melee('Spellcast_Shoot', 0.45),
        comboAttack: melee('Spellcast_Shoot', 0.45),
        gear: ['2H_Staff', 'Mage_Hat', 'Mage_Cape'],
        allGear: ['Spellbook', 'Spellbook_open', '1H_Wand', '2H_Staff', 'Mage_Hat', 'Mage_Cape'],
        tinted: 'Mage_Cape',
        face: face('Mage_Head', 0.1),
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
        face: face('Rogue_Head'),
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
        face: face('Rogue_Head_Hooded'),
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
        height: 3.2,
        idle: 'CharacterArmature|Idle', hurt: 'CharacterArmature|HitReact', death: 'CharacterArmature|Death',
        attack: { clip: 'CharacterArmature|Weapon', contact: 0.4 },
    },
};

/** The room the fight is in: pieces of the dungeon kit, placed as [file, x, y, z, turn]. */
export const ROOM = (() => {
    const piece = name => `/media/models/room/${name}.glb`;
    const pieces = [];
    // The floor: 4 x 4 tiles, from well left of the hero to well right of the monster.
    for (const x of [-10, -6, -2, 2, 6, 10])
        for (const z of [-2, 2, 6])
            pieces.push([piece('floor_tile_large'), x, 0, z, 0]);
    // The back wall, two storeys high, with arches in the middle behind the fight.
    for (const x of [-10, -6, 6, 10]) pieces.push([piece('wall'), x, 0, -4.5, 0]);
    for (const x of [-2, 2]) pieces.push([piece('wall_arched'), x, 0, -4.5, 0]);
    for (const x of [-10, -6, -2, 2, 6, 10]) pieces.push([piece('wall'), x, 4, -4.5, 0]);
    pieces.push(
        [piece('banner_patternA_red'), 0, 0.6, -4.9, 0],
        [piece('torch_mounted'), -4, 2.6, -4, 0],
        [piece('torch_mounted'), 4, 2.6, -4, 0],
        [piece('pillar_decorated'), -6.5, 0, -2.8, 0],
        [piece('pillar_decorated'), 6.5, 0, -2.8, 0],
        [piece('barrel_large'), -5.2, 0, -2.4, 0.4],
        [piece('crates_stacked'), 5.3, 0, -2.6, -0.3],
        [piece('candle_triple'), -4.2, 0, -2.9, 0],
        [piece('candle_triple'), 4.4, 0, -1.5, 0.8],
    );
    return pieces;
})();

/** Where the torch flames are (for their lights): just in front of the two mounted torches. */
export const TORCH_LIGHTS = [[-4, 3.4, -3.4], [4, 3.4, -3.4]];
