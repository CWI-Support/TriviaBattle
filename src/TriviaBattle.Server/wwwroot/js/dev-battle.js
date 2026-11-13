// Dev page /dev/battle: plays the station screen's battle beats on a loop with made-up data, so the
// animations can be watched and tuned without playing a game. Nothing here talks to the server
// except to load the theme. Best viewed portrait (1080 x 1920, or a phone size in the browser's
// device mode), like a station screen.
//
//   /dev/battle                      every kind of beat in turn, on a loop
//   /dev/battle?kind=boss            just that one, on a loop
//   /dev/battle?kind=boss&at=2100    frozen 2.1 s into it (for screenshots)
//   /dev/battle?kind=kill&at=2100    past the beat's end: the next question coming in, frozen
//   /dev/battle3d?kind=floor&next=goblin   after the beat, that monster's entrance as the question comes in (goblin, rat,
//                                    slime, bat, skeleton); kind=boss: the boss leaping in after its curtain
//   /dev/battle?kind=moment-floor    a main-screen banner (moment-floor, -boss, -slain, -revive, -combo)
//   /dev/battle3d                    the same, drawn in 3D (js/battle3d); takes the same options
//
//   /dev/battle3d?hero=wizard        another hero fights (tank, healer, wizard, rogue, ranger, pugilist, warlock)
//   /dev/battle3d?rival=/path/picture.jpg   a rival (as in a race) with that face: the monsters wear it
//   /dev/battle3d?photo=/path/picture.jpg   the hero wears a hero photo made from that picture (any picture
//                                    with a face in it, on the server), as js/hero-camera.js makes them
//   /dev/battle3d?team=pugilist,wizard  teammates in your room (js/battle3d/others.js): they strike while
//                                    each question is up
//   /dev/battle3d?cameo              a rival's moment in the caption with each question: their hero drops in
//
//   /dev/battle3d?foes=3             a pack: that many monsters in the room (the one in front, and the rest waiting)
//   /dev/battle?kind=kill&at=2100&long   a long question and long answers (they shrink to fit)
//
// Kinds: hit, crit, combo, party, revive, kill, floor, boss, hurt, slow, downed, and with power-ups:
// fireball (a Fireball hit), loot (a kill that drops a Potion), blocked (a Shield takes the hit; the
// question after it has a 50/50's answers crossed out). The plate always holds a Shield.
// Hero classes (each sets ?hero= unless given): splash (a Wizard's fast answer hits the whole pack),
// heal (a Healer's fast answer heals a teammate), poison (a Healer's with nobody hurt: the monster is
// poisoned, 3 stacks), patron (a Warlock's slow right answer costs HP), reveal (the question after it has
// the Warlock's patron showing the answer), peek (a Ranger: the next question shows before the beat ends),
// armour (a Tank takes a softened hit).
// Bosses: boss (a kill that empties the room before the boss's: its entrance), charged (a boss at full
// charge lands its big blow on a miss), stagger (a right answer at full charge staggers it).
// Floor rewards: reward (the cards to pick from after a boss), rewarded (the moment after picking one: card C, Mastery,
// lights up as the others fall away, then the reward big, then it fades for the question).
// poisonkill: the Healer's poison kills the monster between answers (its own kill beat, no blow).
// kill ends on NEXT UP (the next monster's bar); roomclear: a kill that emptied the room (ROOM 2 OF 2: the walk,
// the dungeon scrolling past to the next room's monster); teamkill: a teammate (Cat) lands the kill and your knight watches.
// Mini games: runelock (the intro, then play: the needle spinning, 2 hits so far), runelock-won and runelock-lost (the result).
// runeecho (the intro, the runes lighting up, then your turn), chestshuffle (the intro, the coin, the shuffle, the pick),
// chestshuffle-found (a right pick: the coin pops out of chest D, then round 2 starts), chestshuffle-lost (the wrong
// chest: the coin shown where it was). ringpuzzle (the intro, then round 2 of 3: four rings, the second lit),
// ringpuzzle-solved (round 1's picture whole, then round 2 starts), ringpuzzle-won (the last one whole).
// out: downed on the last life (OUT!; the plate's hearts all dark).
//   /dev/battle?kind=hit&charge=2&at=1600   any kind against the boss, and the question after it with the
//                                    boss's charge at that step (0-based, of 3; 2 = full charge)

const KINDS = ['hit', 'crit', 'combo', 'party', 'revive', 'kill', 'roomclear', 'teamkill', 'floor', 'boss', 'hurt', 'slow', 'downed', 'fireball', 'loot', 'blocked',
    'splash', 'heal', 'poison', 'poisonkill', 'patron', 'reveal', 'peek', 'armour', 'charged', 'stagger', 'reward', 'rewarded', 'runelock', 'runelock-won', 'runelock-lost', 'runeecho', 'chestshuffle', 'chestshuffle-found', 'chestshuffle-lost', 'ringpuzzle', 'ringpuzzle-solved', 'ringpuzzle-won', 'out'];
const CLASS_KINDS = { splash: 'wizard', heal: 'healer', poison: 'healer', poisonkill: 'healer', patron: 'warlock', reveal: 'warlock', peek: 'ranger', armour: 'tank', combo: 'pugilist' };
const RANGER_PEEK_MS = 1500;
// ?long: a long question and long answers (the admin editor allows 250 and 80 letters), to check they shrink to fit.
const LONG_QUESTION = {
    text: 'In the 1985 film Back to the Future, what speed in miles per hour does the DeLorean have to reach before it travels through time?',
    answers: ['Eighty-eight miles per hour', 'One hundred and twenty-one', 'Sixty-six, like the highway', 'It depends entirely on how much plutonium the flux capacitor gets'],
};
const BEAT_MS = { out: 1700, kill: 2000, roomclear: 3000, teamkill: 2000, loot: 2000, poisonkill: 2000, floor: 3000, boss: 5000, hurt: 1700, slow: 1700, downed: 1700, blocked: 1700, charged: 1700, reward: 4000, rewarded: 2800, runelock: 9000, runeecho: 9000, chestshuffle: 10000, 'chestshuffle-found': 8000, 'chestshuffle-lost': 3600, ringpuzzle: 9000, 'ringpuzzle-solved': 5000, 'ringpuzzle-won': 4600, 'runelock-won': 3600, 'runelock-lost': 3600 }; // others (hits) 1500
const BOSS_KINDS = ['charged', 'stagger'];  // fought against the boss (so is any kind with ?charge=)
const BOSS_CHARGE_STEPS = 3;                // the server's default (Game:Dungeon:BossChargeSteps)
const QUESTION_MS = 1500; // the question shown between two beats in the loop
// What a Mastery card says each class's power becomes, at the server's defaults (DungeonRun.RewardAmount).
const MASTERY = { rogue: 3, tank: 15, healer: 25, wizard: 75, pugilist: 20, warlock: 70, ranger: 2.5 };

const params = new URLSearchParams(location.search);
const only = params.get('kind');
const frozenAt = params.has('at') ? Number(params.get('at')) : null;
const COLOR = '#2a8cff';
const HERO_PARAM = params.get('hero'); // ?hero=wizard: who fights (Core's Heroes.All); otherwise the kind's class, or the tank
const heroFor = kind => HERO_PARAM || CLASS_KINDS[kind] || 'tank';
const HERO = heroFor(only);
const TEAM = params.get('team')?.split(',').filter(Boolean) || []; // ?team=pugilist,wizard: teammates' heroes
const TEAM_COLORS = ['#ff3355', '#ffcc00', '#22dd66'];
let teamHits = 0; // goes up with each question, so the teammates strike

// The clock everything is drawn by. Frozen (?at=), it stands still, so the 3D scene holds its moment too.
const pageOpened = Date.now();
const devNow = () => (frozenAt !== null ? pageOpened : Date.now());

UI.getServerNow = devNow;
UI.startCountdowns(devNow);
document.documentElement.style.setProperty('--station-color', COLOR);
if (frozenAt !== null)
    document.body.classList.add('is-frozen');

Theme.load()
    .then(() => params.has('photo') && usePhoto('A1', params.get('photo'), HERO))
    .then(() => params.has('rival') && usePhoto('A2', params.get('rival'), 'rogue'))
    .then(start);

/** ?photo= and ?rival=: a player's face, made from a picture the way a station makes one from its camera. */
async function usePhoto(stationId, source, hero) {
    const picture = new Image();
    picture.src = source;
    await picture.decode();
    const face = await FaceCutout.cut(picture);
    if (!face) return console.warn('No face found in', source);
    const faceUrl = face.toDataURL('image/png');
    const portrait = window.HeroPortrait ? await HeroPortrait.render(faceUrl, COLOR, { hero }) : face;
    HeroPhoto.use('dev', stationId, 1, { face: faceUrl, portrait: portrait.toDataURL('image/png') });
}

function start() {
    window.Battle3D?.start(document.getElementById('stage3d'), devNow);  // only on /dev/battle3d
    if (only?.startsWith('moment-')) {
        showMoment(only.slice('moment-'.length));
        return;
    }
    const kinds = only ? [only] : KINDS;
    if (frozenAt !== null) {
        show(kinds[0], devNow() - frozenAt, frozenAt >= (BEAT_MS[kinds[0]] || 1500));
        return;
    }

    let i = 0;
    const next = () => {
        const kind = kinds[i++ % kinds.length];
        const length = BEAT_MS[kind] || 1500;
        const startedAt = Date.now();
        show(kind, startedAt);
        setTimeout(() => show(kind, startedAt, true), length);    // the beat ends: the next question
        setTimeout(next, length + (Number(params.get('hold')) || QUESTION_MS)); // ?hold=4000: the question stays up 4 s (a monster's entrance)
    };
    next();
}

/** Draws the station screen for one beat (or, with question set, the question that follows it). */
function show(kind, startedAt, question = false) {
    if (question) teamHits++;
    if (question && params.has('cameo'))
        DungeonFeed.queue.push({ at: Date.now(), stationId: 'A2', rival: true, html: `<span class="is-rival">${Theme.text('feed.overtook', { name: 'Bob' })}</span>` });
    const { match, you } = fakeState(kind, startedAt, question);
    document.getElementById('dev-label').textContent = question ? `${kind}: next question` : kind;
    document.getElementById('player-body').innerHTML = DungeonView.station(match, you, devNow(), COLOR);
    const colors = Object.fromEntries([['A1', COLOR], ...TEAM.map((_, i) => [`A${i + 3}`, TEAM_COLORS[i]])]);
    UI.fitToScreen(document.querySelector('#player-body .dg-station.is-encounter .dg-hud')); // as player-screen.js does
    window.Battle3D?.show({ match, you, color: COLOR, colors });
}

function showMoment(kind) {
    const texts = {
        floor: ['floor', Theme.text('dungeon.momentFloor', { name: 'Red', floor: 3 }), null],
        boss: ['boss', Theme.text('dungeon.momentBoss', { name: 'Red', monster: 'Goblin Quizmaster' }), 'goblin-boss'],
        slain: ['slain', Theme.text('dungeon.momentBossSlain', { name: 'Blue', monster: 'Goblin Quizmaster' }), 'goblin-boss'],
        revive: ['revive', Theme.text('dungeon.momentRevived', { name: 'Ann' }), null],
        combo: ['combo', Theme.text('dungeon.momentPartyCombo', { name: 'Red' }), null],
    };
    const [cls, text, sprite] = texts[kind] || texts.floor;
    document.querySelector('.player-screen').hidden = true;
    document.getElementById('dev-label').textContent = `moment: ${kind}`;
    document.getElementById('dev-moment').innerHTML = `
        <div class="dg-main"><div class="dg-moments"><div class="dg-effect" style="--t0: ${-(frozenAt ?? 0)}ms">
            <div class="dg-moment is-${cls}" style="--lane-color: ${kind === 'slain' ? '#2a8cff' : '#ff2a3d'}">
                ${sprite ? PixelSprites.html(sprite, 'dg-moment-sprite') : ''}
                <div class="dg-moment-text">${text}</div>
            </div>
        </div></div></div>`;
}

/** A match and "you" for one kind of beat, shaped like the server's snapshot. */
function fakeState(kind, startedAt, question) {
    const striking = !['hurt', 'slow', 'downed', 'out', 'blocked', 'armour', 'charged'].includes(kind);
    // Against the boss (a boss kind, or any kind with ?charge=): it's the one fought, and its charge shows.
    const vsBoss = BOSS_KINDS.includes(kind) || params.has('charge');
    const boss = { name: 'Goblin Quizmaster', sprite: 'goblin-boss', maxHp: 300, isBoss: true };
    const killed = ['kill', 'roomclear', 'teamkill', 'loot', 'floor', 'boss', 'poisonkill'].includes(kind);
    const length = BEAT_MS[kind] || 1500;
    const party = kind === 'party' || kind === 'revive' || kind === 'heal' || kind === 'teamkill';
    const hero = heroFor(kind);
    const poisonStacks = kind === 'poison' ? 3 : 0;

    // The room's pack (?foes=N; a splash always shows three): the one in front, then the ones waiting. A
    // splash has hit the waiting ones for 15 each.
    const foeCount = Math.max(1, Number(params.get('foes')) || (kind === 'splash' ? 3 : 1));
    const waiting = [{ name: 'Cellar Rat', sprite: 'rat', hp: 60, maxHp: 60 }, { name: 'Damp Slime', sprite: 'slime', hp: 80, maxHp: 80 },
                     { name: 'Bat Swarm', sprite: 'bat', hp: 70, maxHp: 70 }]
        .slice(0, foeCount - 1)
        .map((f, i) => ({ ...f, hp: kind === 'splash' ? f.hp - 15 : f.hp, isBoss: false,
                          poisonStacks: kind === 'poison' && i === 0 ? 2 : 0 })); // a poisoned one waiting too, for the pack strip's badge

    const struck = vsBoss ? boss
        : kind === 'boss' || kind === 'floor' ? { name: 'Skeleton Intern', sprite: 'skeleton', maxHp: 120 }
        : { name: 'Goblin Bandit', sprite: 'goblin', maxHp: 100 };
    const fast = ['crit', 'splash', 'heal', 'poison'].includes(kind);
    // A stagger does half again (Game:Dungeon:StaggerBonus).
    const damage = fast ? 30 : kind === 'combo' ? 45 : kind === 'party' || kind === 'fireball' || kind === 'stagger' ? 60 : 20;
    const hpLeft = killed ? 0 : vsBoss ? 180 : 55;
    // boss: the kill emptied the room before the boss's, so the boss is in front now (at full HP).
    const current = vsBoss && !killed ? { ...boss, hp: striking ? hpLeft : 240 }
        : kind === 'boss' ? { ...boss, hp: 300 }
        // ?next=goblin (rat, slime, bat, skeleton): the monster that comes in after the kill or the floor, for its entrance.
        : params.has('next') && (kind === 'floor' || killed) ? { name: params.get('next'), sprite: params.get('next'), hp: 100, maxHp: 100, isBoss: false }
        : kind === 'floor' ? { name: 'Bat Swarm', sprite: 'bat', hp: 70, maxHp: 70, isBoss: false }
        : killed ? { name: 'Skeleton Intern', sprite: 'skeleton', hp: 120, maxHp: 120, isBoss: false }
        : { name: struck.name, sprite: struck.sprite, hp: striking ? hpLeft : 55, maxHp: 100, isBoss: false };
    current.poisonStacks = poisonStacks;

    const hit = striking ? {
        number: 7, damage, crit: fast, combo: kind === 'combo', killed, floorCleared: kind === 'floor', bossAhead: kind === 'boss',
        staggered: kind === 'stagger', roomCleared: kind === 'roomclear',
        monsterName: struck.name, monsterSprite: struck.sprite, isBoss: !!struck.isBoss, monsterHpLeft: hpLeft, monsterMaxHp: struck.maxHp,
        partyCombo: kind === 'party', revivedOwnerId: kind === 'revive' ? 'A3' : null,
        fireball: kind === 'fireball', found: kind === 'loot' ? 'Potion' : null,
        splashes: kind === 'splash' ? waiting.map(f => ({ monsterName: f.name, monsterSprite: f.sprite, damage: 15, hpLeft: f.hp, killed: false })) : null,
        comboCount: kind === 'combo' ? 4 : 0,
        healedOwnerId: kind === 'heal' ? 'A3' : null, healed: kind === 'heal' ? 15 : 0,
        poisonStacks, poison: kind === 'poisonkill',
        patronPrice: kind === 'patron' ? 8 : 0,
    } : null;
    if (hit) hit.totalDamage = damage + (hit.splashes?.reduce((sum, s) => sum + s.damage, 0) ?? 0);
    const taken = striking ? null : {
        // charged: the boss's big blow (250% of its 15)
        number: 3, damage: kind === 'blocked' ? 0 : kind === 'armour' ? 9 : kind === 'charged' ? 38 : 15, reason: kind === 'slow' ? 'TooSlow' : 'WrongAnswer',
        downed: kind === 'downed', monsterName: struck.name, monsterSprite: struck.sprite,
        hpLeft: kind === 'downed' ? 0 : kind === 'blocked' ? 85 : kind === 'armour' ? 91 : kind === 'charged' ? 47 : 45,
        blocked: kind === 'blocked', patronPrice: 0, charged: kind === 'charged',
    };
    const hp = taken ? taken.hpLeft : kind === 'patron' ? 77 : 85;
    const floor = kind === 'floor' ? 2 : kind === 'boss' || vsBoss ? 5 : 1;
    // The boss's charge for the question now: ?charge=N, or 0 (a stagger or the big blow starts it over; a
    // new boss starts weak).
    const bossCharge = current.isBoss ? Math.max(0, Math.min(BOSS_CHARGE_STEPS - 1, Number(params.get('charge')) || 0)) : 0;

    const delve = {
        floor, room: current.isBoss ? 2 : 1, roomsOnFloor: 2,
        bossCharge, bossChargeSteps: current.isBoss ? BOSS_CHARGE_STEPS : 0, depthId: 'cellar', depthName: 'The Cellar of Common Knowledge',
        monster: current, hp, maxHp: 100, streak: kind === 'combo' ? 4 : 1, kills: killed ? 3 : 2,
        attackStartedAtMs: question ? Date.now() : null, attackAtMs: question ? Date.now() + 10000 : null,
        downedUntilMs: kind === 'downed' && question ? Date.now() + 5000 : null,
        lastHit: hit, lastDealt: hit, lastTaken: taken,
        // Floor 1 room 1 is the room you're in: a rat already beaten (slot 1), the one you're fighting, then the pack
        // waiting, so the trail shows a pack's grouping and what's been beaten in it.
        path: [['rat', 1, 1, 1], [current.sprite, 1, 1, 2], ...waiting.map((f, i) => [f.sprite, 1, 1, i + 3]),
               ['slime', 1, 2, 1], ['bat', 2, 1, 1], ['goblin', 2, 2, 1], ['rat', 2, 2, 2], ['slime', 3, 1, 1], ['skeleton', 3, 2, 1],
               ['goblin', 4, 1, 1], ['skeleton', 4, 1, 2], ['skeleton', 5, 1, 1], ['goblin-boss', 5, 2, 1]]
            .map(([sprite, f, room, slot]) => ({ floor: f, room, slot, name: sprite, sprite, isBoss: sprite === 'goblin-boss' })),
        reviveProgress: 0, reviveNeeded: 3,
        powerUp: 'Shield', fireballReady: false, shielded: false, frozenUntilMs: null, lastPowerUp: null,
        party: party
            ? [{ ownerId: 'A1', name: 'Ann', hp, maxHp: 100, downedUntilMs: null, reviveProgress: 0 },
               { ownerId: 'A3', name: 'Cat', hp: kind === 'revive' ? 100 : kind === 'heal' ? 85 : 70, maxHp: 100, downedUntilMs: null, reviveProgress: 0 }]
            : [{ ownerId: 'A1', name: 'Ann', hp, maxHp: 100, downedUntilMs: null, reviveProgress: 0 }],
        foes: [current, ...waiting],
        heroClass: hero,
        // Floor rewards: the top line's meter (7 of 8 right this floor, 80% needed), two held, and the floor curtain's line.
        trial: { floor, right: 7, answered: 8 }, rewardAccuracy: 0.8, lastTrial: { floor: floor - 1, right: 9, answered: 10, earned: true },
        rewards: [{ number: 1, reward: 'Might', amount: 20, floor: 1, atMs: 0, picked: true }, { number: 2, reward: 'Armor', amount: 15, floor: 2, atMs: 0, picked: true },
                  ...(kind === 'rewarded' ? [{ number: 3, reward: 'Mastery', amount: 3, floor: 3, atMs: startedAt, picked: true }] : [])],
        lastStands: 0,
        lives: kind === 'out' ? 0 : 2, maxLives: 3, out: false, // two hearts left of three (the out kind: none)
    };

    const you = {
        stationId: 'A1', name: 'Ann', score: 1240, teamId: TEAM.length ? 'side1' : null,
        question: { number: 8, id: 1, category: 'Movies', text: params.has('long') ? LONG_QUESTION.text : 'Which wizard school does Harry Potter attend?',
                    answers: params.has('long') ? LONG_QUESTION.answers : ['Hogwarts', 'Durmstrang', 'Beauxbatons', 'Ilvermorny'], mediaUrl: null, shownAtMs: startedAt + length,
                    hidden: kind === 'blocked' ? [1, 3] : null,
                    peekFromMs: kind === 'peek' ? startedAt + length - RANGER_PEEK_MS : null,
                    revealed: kind === 'reveal' && question ? 0 : null },
        lockedOutUntilMs: null,
        lastAnswer: { number: 7, chosenIndex: striking ? 0 : 1, correctIndex: 0, correctAnswer: 'Austin', isCorrect: striking,
                      points: striking ? damage : 0, pressedByStationId: 'A1', pressedByName: 'Ann', timedOut: kind === 'slow' },
        delve,
        helping: null,
        beat: { kind: { crit: 'hit', combo: 'hit', party: 'hit', revive: 'hit', fireball: 'hit', slow: 'hurt', blocked: 'hurt', loot: 'kill', poisonkill: 'kill',
                        roomclear: 'kill', teamkill: 'kill',
                        splash: 'hit', heal: 'hit', poison: 'hit', patron: 'hit', reveal: 'hit', peek: 'hit', armour: 'hurt',
                        charged: 'hurt', stagger: 'hit', rewarded: 'reward' }[kind] || kind,
                startedAtMs: startedAt, endsAtMs: startedAt + length },
    };
    // Floor rewards: the cards up after the boss's death beat (it has ended), and no question meanwhile.
    if (kind === 'reward') {
        you.beat = { kind: 'floor', startedAtMs: startedAt - 3000, endsAtMs: startedAt };
        you.question = null;
        you.reward = { floor: 2, opensAtMs: startedAt, pickFromMs: startedAt + 1000, endsAtMs: startedAt + 12000, right: 9, answered: 10,
                       choices: [{ reward: 'Vitality', amount: 25 }, { reward: 'Focus', amount: 1 }, { reward: 'Mastery', amount: MASTERY[hero] }] };
    }
    if (kind === 'rewarded') { // picked C (Mastery) from the cards the reward preview shows
        const offer = { floor: 3, opensAtMs: startedAt - 4000, pickFromMs: startedAt - 3000, endsAtMs: startedAt + 8000, right: 9, answered: 10,
                        choices: [{ reward: 'Vitality', amount: 25 }, { reward: 'Focus', amount: 1 }, { reward: 'Mastery', amount: MASTERY[hero] }] };
        Object.assign(delve.rewards.at(-1), { amount: MASTERY[hero], offer, choice: 2 });
    }
    if (kind === 'poisonkill') you.beat.byPoison = true; // nobody answered: the party's poison did it (delve.lastHit)
    if (kind === 'teamkill') Object.assign(you.beat, { by: 'Cat', hit }); // Cat's blow, carried on the beat
    if (kind === 'roomclear') { // the kill emptied room 1: the one in front is room 2's first, nothing beaten there yet
        delve.room = 2;
        delve.path = delve.path.filter(s => !(s.floor === 1 && s.room === 2))
            .concat({ floor: 1, room: 2, slot: 1, name: current.sprite, sprite: current.sprite, isBoss: false });
    }
    if (kind === 'out') { // downed on the last life
        you.beat.kind = 'downed';
        taken.downed = true; taken.out = true; taken.hpLeft = 0;
    }
    if (kind === 'runeecho' || kind.startsWith('chestshuffle')) {
        you.beat = { kind: 'kill', startedAtMs: startedAt - 2000, endsAtMs: startedAt };
        you.question = null;
        const playsFromMs = startedAt + 3500;
        const lost = kind === 'chestshuffle-lost';
        // found: they've just picked chest D (right): the coin pops out, and round 2 starts 1.4 s later.
        const found = kind === 'chestshuffle-found';
        const swaps = found ? [[1, 2], [0, 2], [2, 4], [3, 4], [0, 1], [0, 3], [1, 3]] : [[0, 1], [1, 3], [2, 4], [0, 3], [3, 4]];
        const swapMs = found ? 320 : 400;
        const roundStartsAtMs = found ? startedAt + 1400 : playsFromMs;
        const finalPlaces = [0, 1, 2, 3, 4];
        for (const [a, b] of swaps) [finalPlaces[a], finalPlaces[b]] = [finalPlaces[b], finalPlaces[a]];
        const shuffle = { round: found ? 2 : 1, rounds: 3, coinChest: found ? 2 : 1, roundStartsAtMs, revealMs: 1600, swapMs, swaps,
                          pickFromMs: roundStartsAtMs + 1600 + swaps.length * swapMs, finalPlaces, picks: lost || found ? 1 : 0,
                          lastPick: lost ? 2 : found ? 3 : null, lastWasRight: found, foundAt: found ? 3 : null,
                          lastPickAtMs: lost || found ? startedAt : null };
        const playing = lost || found; // past the intro
        you.minigame = { kind: kind === 'runeecho' ? 'RuneEcho' : 'ChestShuffle', introAtMs: playing ? startedAt - 20000 : startedAt,
                         playsFromMs: playing ? startedAt - 16500 : playsFromMs, endsAtMs: playing ? startedAt + 13500 : playsFromMs + 30000,
                         finishedAtMs: lost ? startedAt : null, won: false, resultEndsAtMs: lost ? startedAt + 3200 : null,
                         runeLock: null,
                         runeEcho: { round: 2, rounds: 5, sequence: [0, 2, 1, 3], showFromMs: playsFromMs + 800, stepMs: 650, litMs: 450,
                                     inputFromMs: playsFromMs + 800 + 4 * 650, entered: 0, presses: 0, lastRune: -1, lastWasRight: false },
                         chestShuffle: shuffle };
    }
    if (kind.startsWith('runelock')) {
        // The kill beat that emptied the room has played; the intro starts now, play 3.5 s later.
        you.beat = { kind: 'kill', startedAtMs: startedAt - 2000, endsAtMs: startedAt };
        you.question = null;
        const playsFromMs = startedAt + 3500;
        const over = kind !== 'runelock';
        you.minigame = { kind: 'RuneLock', introAtMs: over ? startedAt - 20000 : startedAt, playsFromMs: over ? startedAt - 16500 : playsFromMs,
                         endsAtMs: over ? startedAt + 13500 : playsFromMs + 30000, finishedAtMs: over ? startedAt : null, won: kind === 'runelock-won',
                         resultEndsAtMs: over ? startedAt + 3200 : null,
                         runeLock: { hits: kind === 'runelock-won' ? 6 : 2, hitsNeeded: 6, lockedUntilMs: null, turnStartedAtMs: playsFromMs, turnAngle: 0,
                                     degreesPerSecond: 200, markAngle: 150, markDegrees: 40, presses: 2, lastWasHit: true } };
    }
    if (kind.startsWith('ringpuzzle')) {
        // ringpuzzle: the intro now, then round 2 (four rings, the second lit). solved: round 1's last ring was just
        // turned into place (its outside ring turns in), it shows whole 1 s later, and round 2 starts 1.5 s after that.
        // won: the last round (five rings): its outside ring turns in, then whole and the result 1 s later.
        you.beat = { kind: 'kill', startedAtMs: startedAt - 2000, endsAtMs: startedAt };
        you.question = null;
        const intro = kind === 'ringpuzzle';
        const won = kind === 'ringpuzzle-won';
        const playsFromMs = intro ? startedAt + 3500 : startedAt - 16500;
        const wholeAtMs = intro ? null : startedAt + 1000;
        // Where the screen last drew the ring that's just been turned in (one step short of in line), so it turns in.
        DungeonView.seen[`rp:A1:${playsFromMs - 3500}:${won ? 3 : 1}:${won ? 4 : 2}`] = 330;
        you.minigame = { kind: 'RingPuzzle', introAtMs: playsFromMs - 3500, playsFromMs, endsAtMs: playsFromMs + 60000,
                         finishedAtMs: won ? startedAt : null, won, resultFromMs: won ? wholeAtMs : null,
                         resultEndsAtMs: won ? wholeAtMs + 3200 : null, runeLock: null,
                         ringPuzzle: won
                             ? { round: 3, rounds: 3, rings: 5, steps: 12, offsets: [0, 0, 0, 0, 0], selected: 4, roundStartsAtMs: startedAt - 9000, solvedRings: 4,
                                 presses: 40, wholeAtMs }
                             : { round: 2, rounds: 3, rings: 4, steps: 12, offsets: [0, 7, 3, 10], selected: 1,
                                 roundStartsAtMs: intro ? playsFromMs : wholeAtMs + 1500, solvedRings: intro ? null : 3, presses: intro ? 0 : 14, wholeAtMs } };
    }

    const match = { matchId: 'dev', category: 'Movies', earnsTime: true, phaseEndsAtMs: Date.now() + 75000,
                    clockStoppedLeftMs: kind.startsWith('runelock') || kind === 'runeecho' || kind.startsWith('chestshuffle') || kind.startsWith('ringpuzzle') ? 74000 : null,
                    players: [
                        { stationId: 'A1', name: 'Ann', photo: params.has('photo') ? 1 : 0, hero },
                        ...(party ? [{ stationId: 'A3', name: 'Cat', teamId: 'side1', photo: 0, hero: 'rogue' }] : []),
                        ...(params.has('rival') || params.has('cameo') ? [{ stationId: 'A2', name: 'Bob', photo: params.has('rival') ? 1 : 0, hero: 'rogue' }] : []),
                        ...TEAM.map((hero, i) => ({
                            stationId: `A${i + 3}`, name: ['Cat', 'Dan', 'Eve'][i], teamId: 'side1', photo: 0, hero,
                            delve: { lastDealt: { number: teamHits }, lastTaken: null, downedUntilMs: kind === 'downed' && i === 1 ? Date.now() + 5000 : null },
                        })),
                    ] };
    return { match, you };
}
