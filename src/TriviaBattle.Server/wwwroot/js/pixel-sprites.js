// Placeholder pixel-art sprites for Dungeon Quiz Quest, drawn from text maps so the game is
// playable before the real art exists. Each sprite is a grid of characters: '.' is see-through,
// every other character is a colour from the sprite's palette. It's drawn once at 1 pixel per
// character and scaled up by CSS (image-rendering: pixelated), so it stays crisp at any size.
//
// Real art replaces these without code changes: in config/dungeon.json, set a monster's Sprite to
// a picture path (e.g. /media/dungeon/goblin.png) instead of one of the keys below.
//
// Usage:
//   PixelSprites.html('goblin', 'dg-monster')                       -> an <img> tag
//   PixelSprites.html('knight', 'dg-portrait', { r: '#2a8cff' })    -> with palette overrides

const PixelSprites = {
    cache: {},

    /** An <img> for a sprite key, or for a picture path (anything starting with '/' or 'http'). */
    html(sprite, cssClass = '', palette = null) {
        const isPicture = /^(\/|https?:)/.test(sprite || '');
        const src = isPicture ? sprite : PixelSprites.url(sprite, palette);
        return `<img class="pixel-sprite ${cssClass}" src="${UI.escape(src)}" alt="">`;
    },

    /** A data: URL of the sprite drawn at 1 pixel per character (cached). Unknown keys get a "?" box. */
    url(key, palette = null) {
        const cacheKey = key + JSON.stringify(palette || {});
        if (PixelSprites.cache[cacheKey]) return PixelSprites.cache[cacheKey];

        const def = PixelSprites.SPRITES[key] || PixelSprites.SPRITES.unknown;
        const colours = { ...def.palette, ...(palette || {}) };
        const width = Math.max(...def.rows.map(r => r.length));
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = def.rows.length;
        const ctx = canvas.getContext('2d');
        def.rows.forEach((row, y) => [...row].forEach((ch, x) => {
            if (ch === '.' || !colours[ch]) return;
            ctx.fillStyle = colours[ch];
            ctx.fillRect(x, y, 1, 1);
        }));
        return (PixelSprites.cache[cacheKey] = canvas.toDataURL());
    },

    SPRITES: {
        rat: {
            palette: { k: '#1b1b22', g: '#9a9aa8', G: '#6c6c7a', p: '#ff9fb5', r: '#ff3030', w: '#ffffff' },
            rows: [
                '................',
                '................',
                '................',
                '................',
                '................',
                '...kk...........',
                '..kppk..........',
                '.kkgggkkkkkkk...',
                'kgrggggggggggk..',
                'kggggggggggggGk.',
                '.kwgggggggggGGk.',
                '..kkgggggggGGk.k',
                '...kGkkkkkGkkpk.',
                '...kk.....kk.pk.',
                '..............p.',
                '................',
            ],
        },
        slime: {
            palette: { k: '#0d3b1e', g: '#4cd964', G: '#2fa84f', w: '#e9ffe9', e: '#101010' },
            rows: [
                '................',
                '................',
                '................',
                '................',
                '......kkkk......',
                '....kkggggkk....',
                '...kggwwgggGk...',
                '..kggwwgggggGk..',
                '..kgggeggeggGk..',
                '.kggggeggegggGk.',
                '.kgggggggggggGk.',
                '.kggggkkkkgggGk.',
                'kgggggggggggggGk',
                'kGgggggggggggGGk',
                '.kkGGGGGGGGGGkk.',
                '...kkkkkkkkkk...',
            ],
        },
        bat: {
            palette: { k: '#14091f', p: '#6b3fa0', r: '#ff3b3b', w: '#ffffff' },
            rows: [
                '................',
                '................',
                '................',
                '................',
                'k.....k..k.....k',
                'kk...kpkkpk...kk',
                'kpk..kpppppk.kpk',
                'kppkkprpprpkkppk',
                'kppppppppppppppk',
                '.kppppkwwkppppk.',
                '..kpppkkkkpppk..',
                '...kpk....kpk...',
                '....k......k....',
                '................',
                '................',
                '................',
            ],
        },
        goblin: {
            palette: { k: '#14200c', g: '#5a9e3a', y: '#ffd23f', w: '#ffffff', b: '#7a4a2a', B: '#4a2c18', s: '#c8d0d8' },
            rows: [
                '................',
                '................',
                'k..............k',
                'kgk..kkkkkk..kgk',
                '.kgkkggggggkkgk.',
                '.kggggggggggggk.',
                '..kgyykggkyygk..',
                '..kggggggggggk..',
                '...kgwkwwkwgk...',
                '....kggggggk....',
                '...kbbbbbbbbk.s.',
                '..kgbbBBBBbbgks.',
                '..kgkbbbbbbkgkk.',
                '....kbbkkbbk....',
                '....kgk..kgk....',
                '...kkkk..kkkk...',
            ],
        },
        'goblin-boss': {
            palette: { k: '#14200c', g: '#4f8f30', y: '#ff4d4d', w: '#ffffff', b: '#6b2fa0', B: '#ffd23f', s: '#c8d0d8', c: '#ffd23f' },
            rows: [
                '....c..c..c.....',
                '....cccccccc....',
                'k...kkkkkkkk...k',
                'kgk.kggggggk.kgk',
                '.kgkkggggggkkgk.',
                '.kggggggggggggk.',
                '..kgyykggkyygk..',
                '..kggggggggggk..',
                '...kgwkwwkwgk...',
                '....kggggggk....',
                '..kbbbbbbbbbbk.s',
                '.kgbbbBBBBbbbgks',
                '.kgkbbbbbbbbkgkk',
                '...kbbbbbbbbk...',
                '...kbbbkkbbbk...',
                '..kkkkk..kkkkk..',
            ],
        },
        skeleton: {
            palette: { k: '#1a1a1a', w: '#e8e4d0', r: '#ff3030', t: '#d62828' },
            rows: [
                '................',
                '.....kkkkkk.....',
                '....kwwwwwwk....',
                '...kwwwwwwwwk...',
                '...kwkkwwkkwk...',
                '...kwkrwwkrwk...',
                '...kwwwkkwwwk...',
                '....kwkwkwkk....',
                '.....kwwwwk.....',
                '...kkkkttkkkk...',
                '..kw.kwttwk.wk..',
                '..kw.kwttwk.wk..',
                '..k..kwkkwk..k..',
                '.....kwwwwk.....',
                '.....kw..wk.....',
                '....kkk..kkk....',
            ],
        },
        // A hero portrait (the player plate). r = the plume, recoloured per station.
        knight: {
            palette: { k: '#111111', s: '#b8c0c8', S: '#7d858d', r: '#d62828', v: '#1a1a1a', e: '#ffd23f' },
            rows: [
                '.......rr.......',
                '......rrrr......',
                '.......rr.......',
                '....kkkkkkkk....',
                '...kssssssssk...',
                '..kssSSSSSSssk..',
                '..ksSkkkkkkSsk..',
                '..ksSkveevkSsk..',
                '..ksSkkkkkkSsk..',
                '..kssSSkkSSssk..',
                '..kssssSSssssk..',
                '...kssskksssk...',
                '..kkSssssssSkk..',
                '.kSSSkkkkkkSSSk.',
                'kSSSSSSSSSSSSSSk',
                'kkkkkkkkkkkkkkkk',
            ],
        },
        // The adventurer in the battle scene: a knight side-on, sword up, facing right (towards the
        // monster). 'r' (plume and cape) is the station's colour.
        hero: {
            palette: { k: '#111111', s: '#c9d1d9', S: '#7d858d', v: '#1a1a1a', e: '#ffd23f', r: '#d62828',
                       w: '#eef3f8', W: '#9aa6b2', h: '#8a5a2b', g: '#f0b429', b: '#4a3424' },
            rows: [
                '..........rr............',
                '.........rrrr...........',
                '..........rrr...........',
                '........kkkkkk..........',
                '.......kssssssk.........',
                '......ksssSSsssk......kw',
                '......kssvvvesk......kwk',
                '......ksSSSSSSsk....kwWk',
                '.......kssssssk....kwWk.',
                '....rr..kkSSkk....kwWk..',
                '...rrrkkssssssk..kwWk...',
                '..rrrksssSSssssk.kwk....',
                '..rrrkssSkkSsssgkgk.....',
                '..rrrksSk..kSssshhk.....',
                '...rrksSk..ksssgkgk.....',
                '...rrkssSkkSssk.k.......',
                '....rkssssssssk.........',
                '.....kSSSSSSSSk.........',
                '.....kssk..kssk.........',
                '.....kssk..kssk.........',
                '.....kSSk..kSSk.........',
                '....kbbbk..kbbbk........',
                '....kkkkk..kkkkk........',
            ],
        },
        // Small icons for labels: hit points, kills, the monster's attack, the stairs between floors.
        'icon-heart': {
            palette: { k: '#000000', r: '#e8323c', w: '#ffc0c4' },
            rows: [
                '.kk...kk.',
                'krrk.krrk',
                'krwrkrrrk',
                'krrrrrrrk',
                '.krrrrrk.',
                '..krrrk..',
                '...krk...',
                '....k....',
            ],
        },
        'icon-skull': {
            palette: { k: '#000000', w: '#eee6d0' },
            rows: [
                '..kkkkk..',
                '.kwwwwwk.',
                'kwwwwwwwk',
                'kwkkwkkwk',
                'kwkkwkkwk',
                'kwwwkwwwk',
                '.kwwwwwk.',
                '..kwkwk..',
                '..kkkkk..',
            ],
        },
        'icon-sword': {
            palette: { k: '#000000', w: '#dfe6ee', b: '#a0672e' },
            rows: [
                '......kkk',
                '.....kwwk',
                '....kwwk.',
                '.k.kwwk..',
                '.kkwwk...',
                '..kkk....',
                '.kbkkk...',
                'kbk..k...',
                'kk.......',
            ],
        },
        'icon-stairs': {
            palette: { k: '#000000', g: '#8a7f78' },
            rows: [
                'kkkk.....',
                'kggk.....',
                'kggkkk...',
                'kggggk...',
                'kggggkkk.',
                'kggggggk.',
                'kggggggkk',
                'kgggggggk',
                'kkkkkkkkk',
            ],
        },
        unknown: {
            palette: { k: '#000000', y: '#ffd23f' },
            rows: ['kkkkkk', 'kyyyyk', 'kkkkyk', 'kkyykk', 'kkkkkk', 'kkykkk', 'kkkkkk'],
        },
    },
};
