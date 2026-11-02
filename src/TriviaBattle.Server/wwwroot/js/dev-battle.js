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
// Kinds: hit, crit, combo, party, revive, kill, floor, boss, hurt, slow, downed.

const KINDS = ['hit', 'crit', 'combo', 'party', 'revive', 'kill', 'floor', 'boss', 'hurt', 'slow', 'downed'];
const BEAT_MS = { kill: 2000, floor: 3000, boss: 3000, hurt: 1700, slow: 1700, downed: 1700 }; // others (hits) 1500
const QUESTION_MS = 1500; // the question shown between two beats in the loop

const params = new URLSearchParams(location.search);
const only = params.get('kind');
const frozenAt = params.has('at') ? Number(params.get('at')) : null;
const COLOR = '#2a8cff';

// The clock everything is drawn by. Frozen (?at=), it stands still, so the 3D scene holds its moment too.
const pageOpened = Date.now();
const devNow = () => (frozenAt !== null ? pageOpened : Date.now());

UI.getServerNow = devNow;
UI.startCountdowns(devNow);
document.documentElement.style.setProperty('--station-color', COLOR);
if (frozenAt !== null)
    document.body.classList.add('is-frozen');

Theme.load().then(start);

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
    const { match, you } = fakeState(kind, startedAt, question);
    document.getElementById('dev-label').textContent = question ? `${kind}: next question` : kind;
    document.getElementById('player-body').innerHTML = DungeonView.station(match, you, devNow(), COLOR);
    window.Battle3D?.show({ match, you, color: COLOR });
}

function showMoment(kind) {
    const texts = {
        floor: ['floor', Theme.text('dungeon.momentFloor', { name: 'Red', floor: 3 }), null],
        boss: ['boss', Theme.text('dungeon.momentBoss', { name: 'Red', monster: 'The Goblin Quizmaster' }), 'goblin-boss'],
        slain: ['slain', Theme.text('dungeon.momentBossSlain', { name: 'Blue', monster: 'The Goblin Quizmaster' }), 'goblin-boss'],
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
    const striking = !['hurt', 'slow', 'downed'].includes(kind);
    const killed = ['kill', 'floor', 'boss'].includes(kind);
    const length = BEAT_MS[kind] || 1500;
    const party = kind === 'party' || kind === 'revive';

    const struck = kind === 'boss' || kind === 'floor'
        ? { name: 'Skeleton Intern', sprite: 'skeleton', maxHp: 120 }
        : { name: 'Goblin Bandit', sprite: 'goblin', maxHp: 100 };
    const damage = kind === 'crit' ? 30 : kind === 'combo' ? 45 : kind === 'party' ? 60 : 20;
    const hpLeft = killed ? 0 : 55;
    const current = kind === 'boss' ? { name: 'The Goblin Quizmaster', sprite: 'goblin-boss', hp: 300, maxHp: 300, isBoss: true }
        : kind === 'floor' ? { name: 'Bat Swarm', sprite: 'bat', hp: 70, maxHp: 70, isBoss: false }
        : killed ? { name: 'Skeleton Intern', sprite: 'skeleton', hp: 120, maxHp: 120, isBoss: false }
        : { name: struck.name, sprite: struck.sprite, hp: striking ? hpLeft : 55, maxHp: 100, isBoss: false };

    const hit = striking ? {
        number: 7, damage, crit: kind === 'crit', combo: kind === 'combo', killed, floorCleared: kind === 'floor' || kind === 'boss',
        monsterName: struck.name, monsterSprite: struck.sprite, isBoss: false, monsterHpLeft: hpLeft, monsterMaxHp: struck.maxHp,
        partyCombo: kind === 'party', revivedOwnerId: kind === 'revive' ? 'A3' : null,
    } : null;
    const taken = striking ? null : {
        number: 3, damage: 15, reason: kind === 'slow' ? 'TooSlow' : 'WrongAnswer', downed: kind === 'downed',
        monsterName: struck.name, monsterSprite: struck.sprite, hpLeft: kind === 'downed' ? 0 : 45,
    };
    const hp = taken ? taken.hpLeft : 85;
    const floor = kind === 'floor' ? 2 : kind === 'boss' ? 5 : 1;

    const delve = {
        floor, room: 1, roomsOnFloor: 2, depthId: 'cellar', depthName: 'The Cellar of Common Knowledge',
        monster: current, hp, maxHp: 100, streak: kind === 'combo' ? 4 : 1, kills: killed ? 3 : 2,
        attackStartedAtMs: question ? Date.now() : null, attackAtMs: question ? Date.now() + 10000 : null,
        downedUntilMs: kind === 'downed' && question ? Date.now() + 5000 : null,
        lastHit: hit, lastDealt: hit, lastTaken: taken,
        path: [['rat', 1, 1], ['slime', 1, 2], ['bat', 2, 1], ['goblin', 2, 2], ['slime', 3, 1], ['skeleton', 3, 2],
               ['goblin', 4, 1], ['skeleton', 4, 2], ['goblin-boss', 5, 1]]
            .map(([sprite, f, room]) => ({ floor: f, room, name: sprite, sprite, isBoss: sprite === 'goblin-boss' })),
        reviveProgress: 0, reviveNeeded: 3,
        party: party
            ? [{ ownerId: 'A1', name: 'Ann', hp, maxHp: 100, downedUntilMs: null, reviveProgress: 0 },
               { ownerId: 'A3', name: 'Cat', hp: kind === 'revive' ? 100 : 70, maxHp: 100, downedUntilMs: null, reviveProgress: 0 }]
            : [{ ownerId: 'A1', name: 'Ann', hp, maxHp: 100, downedUntilMs: null, reviveProgress: 0 }],
    };

    const you = {
        stationId: 'A1', name: 'Ann', score: 1240,
        question: { number: 8, id: 1, category: 'Movies', text: 'Which wizard school does Harry Potter attend?',
                    answers: ['Hogwarts', 'Durmstrang', 'Beauxbatons', 'Ilvermorny'], mediaUrl: null, shownAtMs: startedAt + length },
        lockedOutUntilMs: null,
        lastAnswer: { number: 7, chosenIndex: striking ? 0 : 1, correctIndex: 0, correctAnswer: 'Austin', isCorrect: striking,
                      points: striking ? damage : 0, pressedByStationId: 'A1', pressedByName: 'Ann', timedOut: kind === 'slow' },
        delve,
        helping: null,
        beat: { kind: { crit: 'hit', combo: 'hit', party: 'hit', revive: 'hit', slow: 'hurt' }[kind] || kind,
                startedAtMs: startedAt, endsAtMs: startedAt + length },
    };
    const match = { matchId: 'dev', category: 'Movies', soloClock: true, phaseEndsAtMs: Date.now() + 75000 };
    return { match, you };
}
