// The 3D models for the battle scene, by the same keys config/dungeon.json uses for a monster's Sprite
// (plus 'hero'). Placeholders for now (CC0 packs, see media/models/CREDITS.md); the real art drops in
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
// photo   the hero only: where a player's hero photo goes (the head bone), how big (times the head), and
//         the parts it stands in for (hidden while it's worn)

export const MODELS = {
    hero: {
        file: '/media/models/characters/knight.glb',
        height: 2.3,
        idle: 'Idle',
        // A few different swings, picked per beat so the 40th hit doesn't look like the first.
        attacks: [
            { clip: '1H_Melee_Attack_Chop', contact: 0.42 },
            { clip: '1H_Melee_Attack_Slice_Diagonal', contact: 0.38 },
            { clip: '1H_Melee_Attack_Stab', contact: 0.55 },
        ],
        critAttack: { clip: '1H_Melee_Attack_Slice_Horizontal', contact: 0.42 },
        comboAttack: { clip: 'Dualwield_Melee_Attack_Slice', contact: 0.45 },
        hurt: 'Hit_A',
        block: 'Block_Hit',   // a Shield takes the hit
        death: 'Death_A',
        cheer: 'Cheer',
        // The knight comes with all its gear as separate parts. Only these show; the rest are for
        // later (power-ups and loot can swap in another shield or the big sword).
        gear: ['1H_Sword', 'Round_Shield', 'Knight_Helmet', 'Knight_Cape'],
        allGear: ['1H_Sword', '1H_Sword_Offhand', '2H_Sword', 'Badge_Shield', 'Rectangle_Shield', 'Round_Shield', 'Spike_Shield', 'Knight_Helmet', 'Knight_Cape'],
        // The part dyed the station's colour.
        tinted: 'Knight_Cape',
        // A hero photo is worn as a bobblehead: the player's face, big and round, in place of the head and helmet.
        photo: { bone: 'head', size: 1.0, replaces: ['Knight_Head', 'Knight_Helmet'] },
    },
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
