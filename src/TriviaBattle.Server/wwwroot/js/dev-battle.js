// Dev page /dev/battle: plays the station screen's battle beats on a loop with made-up data, so the
// animations can be watched and tuned without playing a game. Nothing here talks to the server
// except to load the theme. Best viewed portrait (1080 x 1920, or a phone size in the browser's
// device mode), like a station screen.
//
//   /dev/battle                      every kind of beat in turn, on a loop
//   /dev/battle?kind=boss            just that one, on a loop
//   /dev/battle?kind=boss&at=2100    frozen 2.1 s into it (for screenshots)
//   /dev/battle?kind=kill&at=2100    past the beat's end: the next question coming in, frozen
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
//   /dev/battle?kind=hit&charge=2&at=1600   any kind against the boss, and the question after it with the
//                                    boss's charge at that step (0-based, of 3; 2 = full charge)

const KINDS = ['hit', 'crit', 'combo', 'party', 'revive', 'kill', 'floor', 'boss', 'hurt', 'slow', 'downed', 'fireball', 'loot', 'blocked',
    'splash', 'heal', 'poison', 'patron', 'reveal', 'peek', 'armour', 'charged', 'stagger'];
const CLASS_KINDS = { splash: 'wizard', heal: 'healer', poison: 'healer', patron: 'warlock', reveal: 'warlock', peek: 'ranger', armour: 'tank', combo: 'pugilist' };
const RANGER_PEEK_MS = 1500;
const BEAT_MS = { kill: 2000, loot: 2000, floor: 3000, boss: 3000, hurt: 1700, slow: 1700, downed: 1700, blocked: 1700, charged: 1700 }; // others (hits) 1500
const BOSS_KINDS = ['charged', 'stagger'];  // fought against the boss (so is any kind with ?charge=)
const BOSS_CHARGE_STEPS = 3;                // the server's default (Game:Dungeon:BossChargeSteps)
const QUESTION_MS = 1500; // the question shown between two beats in the loop

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
        setTimeout(next, length + QUESTION_MS);
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
    const striking = !['hurt', 'slow', 'downed', 'blocked', 'armour', 'charged'].includes(kind);
    // Against the boss (a boss kind, or any kind with ?charge=): it's the one fought, and its charge shows.
    const vsBoss = BOSS_KINDS.includes(kind) || params.has('charge');
    const boss = { name: 'Goblin Quizmaster', sprite: 'goblin-boss', maxHp: 300, isBoss: true };
    const killed = ['kill', 'loot', 'floor', 'boss'].includes(kind);
    const length = BEAT_MS[kind] || 1500;
    const party = kind === 'party' || kind === 'revive' || kind === 'heal';
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
        : kind === 'floor' ? { name: 'Bat Swarm', sprite: 'bat', hp: 70, maxHp: 70, isBoss: false }
        : killed ? { name: 'Skeleton Intern', sprite: 'skeleton', hp: 120, maxHp: 120, isBoss: false }
        : { name: struck.name, sprite: struck.sprite, hp: striking ? hpLeft : 55, maxHp: 100, isBoss: false };
    current.poisonStacks = poisonStacks;

    const hit = striking ? {
        number: 7, damage, crit: fast, combo: kind === 'combo', killed, floorCleared: kind === 'floor', bossAhead: kind === 'boss',
        staggered: kind === 'stagger',
        monsterName: struck.name, monsterSprite: struck.sprite, isBoss: !!struck.isBoss, monsterHpLeft: hpLeft, monsterMaxHp: struck.maxHp,
        partyCombo: kind === 'party', revivedOwnerId: kind === 'revive' ? 'A3' : null,
        fireball: kind === 'fireball', found: kind === 'loot' ? 'Potion' : null,
        splashes: kind === 'splash' ? waiting.map(f => ({ monsterName: f.name, monsterSprite: f.sprite, damage: 15, hpLeft: f.hp, killed: false })) : null,
        comboCount: kind === 'combo' ? 4 : 0,
        healedOwnerId: kind === 'heal' ? 'A3' : null, healed: kind === 'heal' ? 15 : 0,
        poisonStacks, poison: false,
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
    };

    const you = {
        stationId: 'A1', name: 'Ann', score: 1240, teamId: TEAM.length ? 'side1' : null,
        question: { number: 8, id: 1, category: 'Movies', text: 'Which wizard school does Harry Potter attend?',
                    answers: ['Hogwarts', 'Durmstrang', 'Beauxbatons', 'Ilvermorny'], mediaUrl: null, shownAtMs: startedAt + length,
                    hidden: kind === 'blocked' ? [1, 3] : null,
                    peekFromMs: kind === 'peek' ? startedAt + length - RANGER_PEEK_MS : null,
                    revealed: kind === 'reveal' && question ? 0 : null },
        lockedOutUntilMs: null,
        lastAnswer: { number: 7, chosenIndex: striking ? 0 : 1, correctIndex: 0, correctAnswer: 'Austin', isCorrect: striking,
                      points: striking ? damage : 0, pressedByStationId: 'A1', pressedByName: 'Ann', timedOut: kind === 'slow' },
        delve,
        helping: null,
        beat: { kind: { crit: 'hit', combo: 'hit', party: 'hit', revive: 'hit', fireball: 'hit', slow: 'hurt', blocked: 'hurt', loot: 'kill',
                        splash: 'hit', heal: 'hit', poison: 'hit', patron: 'hit', reveal: 'hit', peek: 'hit', armour: 'hurt',
                        charged: 'hurt', stagger: 'hit' }[kind] || kind,
                startedAtMs: startedAt, endsAtMs: startedAt + length },
    };
    const match = { matchId: 'dev', category: 'Movies', earnsTime: true, phaseEndsAtMs: Date.now() + 75000,
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
