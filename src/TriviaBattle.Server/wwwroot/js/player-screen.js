// Player station screen (portrait, next to the 4 answer buttons and the action button).
// Between games it's the arcade lobby (js/arcade-lobby.js): press any button, choose a hero, choose a game.
// While the clock runs, this screen shows the player's own question (their team's in 2v2 Group).
// Answering moves straight on to the next one, with a right/wrong banner for the last answer
// (in Dungeon Quiz Quest, after a battle beat: the fight plays full screen, then the next question).
// The server sends a "you" section already worked out for the current mode, so this file
// never needs to know mode rules. In a Dungeon game's intro the player can put their face on their
// hero (js/hero-camera.js takes the photo, js/hero-photo.js draws it).
// All wording comes from the theme's copy.json via Theme.text() (see js/theme.js).

const stationNumber = Number(location.pathname.match(/\/display\/player\/(\d+)/)?.[1]);

let lastState = null;
let myPlacement = null;     // set if this player/team made a leaderboard this match
let lastAnswerShown = null; // number of the last answer we drew feedback for, so new feedback can pop
let beatTimer = null;       // redraws when a battle beat ends (no update comes from the server then)
let peekTimer = null;       // a Ranger: redraws when the next question starts to show during the beat (nor then)
let sharedChecked = false;  // asked once whether another screen is open as this station
let cameraToldAt = 0;       // the join the server last heard about this screen's camera on

const connection = new ScreenConnection({ role: 'player', stationNumber, onState: render, onEvent: handleEvent });
const introVideo = new SyncedVideo(document.getElementById('intro-video'), () => connection.serverNow());
connection.start();
UI.startCountdowns(() => connection.serverNow());
enableStationKeypad(connection, () => lastState?.station.id); // demo: keys 1-4 / A-D or taps answer, Space swipes a card
const redraw = () => lastState && render(lastState);
HeroCamera.check(redraw, text => connection.reportError(text)); // photos only happen on a station with a camera
Theme.load().then(() => HeroCamera.goSecure()); // opened over http by network address: switch to the https address, or offer to
HeroPhoto.onLoad = redraw;  // a face arrived

// The Dungeon fight is drawn in 3D (js/battle3d), unless the URL says ?view=2d or this PC can't do
// WebGL 2: then it's the CSS version, without faces on the heroes or teammates in the room, and the log
// says why once the screen is connected (render). The CSS version also shows until three.js has loaded.
const asked2d = new URLSearchParams(location.search).get('view') === '2d';
let why2d = !asked2d && !canDraw3d() ? 'The fight is drawn in 2D here: this browser has no WebGL 2 (is hardware acceleration off?)' : null;
if (!asked2d && !why2d) {
    import('/js/battle3d/battle3d.js').then(() => {
        Battle3D.start(document.getElementById('stage3d'), () => connection.serverNow());
        document.body.classList.add('is-3d');
        if (lastState) render(lastState);
    });
}

/**
 * Another screen is open as this station too (often a second PC that picked the same player number): a
 * bar says so, since each one with a camera would take the hero photo. It doesn't stop anything, and
 * goes once this screen is the only one.
 */
async function warnIfShared() {
    const others = await connection.hub.invoke('OtherScreensHere').catch(() => []);
    let bar = document.querySelector('.station-shared');
    if (!others.length) return bar?.remove();
    if (!bar) {
        bar = document.createElement('a');
        bar.className = 'station-shared';
        bar.href = '/';
        bar.target = '_top'; // out of the 9:16 frame (js/portrait-frame.js)
        document.body.appendChild(bar);
    }
    bar.innerHTML = `<b>${Theme.text('station.alsoOpen', { name: lastState?.station.label || '', where: others.map(a => a || '?').join(', ') })}</b>
        <small>${Theme.text('station.alsoOpenSteps')}</small>`;
}

/** Each station's colour, by station id (the 3D fight dresses teammates in theirs). */
function stationColors(state) {
    return Object.fromEntries(state.room.stations.map(s => [s.id, s.color]));
}

function canDraw3d() {
    try {
        return !!document.createElement('canvas').getContext('webgl2');
    } catch {
        return false;
    }
}

function render(state) {
    if (HeroCamera.checked && cameraToldAt !== connection.joins) {
        // After each (re)join: a screen without a camera mustn't skip the lobby's photo for one that has one.
        cameraToldAt = connection.joins;
        connection.hub.invoke('ReportCamera', HeroCamera.hasCamera).catch(() => {});
    }
    if (!sharedChecked) {
        sharedChecked = true;
        warnIfShared();
        setInterval(warnIfShared, 15000); // and again now and then: one may open or close later
    }
    if (why2d) {
        connection.reportError(why2d);
        why2d = null;
    }
    if (state.match?.matchId !== lastState?.match?.matchId) {
        myPlacement = null;
        lastAnswerShown = null;
        DungeonView.reset();
        DungeonFeed.reset();
    }
    lastState = state;
    introVideo.follow(state.cue, { muted: true }); // sound comes from the main screen only

    const match = state.match;
    const you = match?.you;
    HeroPhoto.keepOnly(match ? [match.matchId] : []);

    document.documentElement.style.setProperty('--station-color', state.station.color);
    document.getElementById('station-label').textContent = state.station.label;
    document.getElementById('player-name').textContent = you?.name || '';
    document.getElementById('player-score').textContent = you ? you.score.toLocaleString() : '';

    const body = document.getElementById('player-body');
    // Dungeon Quiz Quest draws its own enemy bar and player plate, so the header steps aside while fighting.
    const fighting = match?.format === 'Dungeon' && match.phase === 'Playing' && !!you?.delve;
    document.querySelector('.player-screen').classList.toggle('is-dungeon', fighting);

    if (!match || !you) {
        body.innerHTML = idle(state, match);
    } else {
        switch (match.phase) {
            case 'Intro':    body.innerHTML = intro(match, you); break;
            case 'Playing':  body.innerHTML = fighting ? dungeonStation(match, you, state.station.color) : playing(match, you); break;
            case 'Continue': body.innerHTML = continueOffer(match, you); break;
            case 'Results':
            case 'Finished': body.innerHTML = results(match, you); break;
        }
    }
    UI.fitToScreen(body.querySelector('.intro-stage') || body); // long questions shrink rather than run off the bottom
    // The dungeon's room clips what overflows it, so the screen as a whole always "fits": fit the question,
    // the room and the answers inside it too (a long question or answer shrinks them, never pushes them off).
    UI.fitToScreen(body.querySelector('.dg-station.is-encounter .dg-hud'));
    // The lobby's photo step first: a game taking over mid-photo closes its window before the intro's opens.
    const seat = match ? null : state.lobby.seats.find(s => s.stationId === state.station.id);
    HeroCamera.followLobby(seat, () => connection.serverNow(), state.station.color);
    if (match && you) HeroCamera.follow(match, you, () => connection.serverNow(), state.station.color); // a kiosk game's intro photo
    if (fighting) window.Battle3D?.show({ match, you, color: state.station.color, colors: stationColors(state) }); // once the 3D has loaded

    // A battle beat is playing: the next question appears when it ends, so redraw then.
    clearTimeout(beatTimer);
    if (fighting && DungeonBattle.isPlaying(you, connection.serverNow()))
        beatTimer = setTimeout(() => render(lastState), you.beat.endsAtMs - connection.serverNow() + 20);
    // A Ranger's peek at the next question: its slide-in is timed in CSS (--p0), but redraw as it starts
    // too, so everything drawn from the snapshot (the 3D scene included) knows it's showing.
    clearTimeout(peekTimer);
    const peekIn = fighting && you.question?.peekFromMs ? you.question.peekFromMs - connection.serverNow() : 0;
    if (peekIn > 0)
        peekTimer = setTimeout(() => render(lastState), peekIn + 20);
}

/** Leaderboard placements arrive shortly after Results appears; show ours if we got one. */
function handleEvent(event) {
    const you = lastState?.match?.you;
    if (event.type !== 'LeaderboardPlacement' || !you) return;

    const mine = event.placement.ownerId === you.stationId || event.placement.ownerId === you.teamId;
    if (mine) {
        myPlacement = event.placement;
        render(lastState);
    }
}

/** Not in a game: the arcade lobby (press any button, choose a hero, choose a game). */
function idle(state, match) {
    if (!match) return ArcadeLobby.station(state);
    return `
        <div class="center-stack">
            <h1 class="big-title">${UI.escape(state.station.label)}</h1>
            <p class="subtitle">${Theme.text('player.busy', {}, new Date().toDateString())}</p>
        </div>`;
}

function intro(match, you) {
    const teammates = match.players.filter(p => p.teamId && p.teamId === you.teamId && p.stationId !== you.stationId);
    const teamLine = !you.teamName ? ''
        : teammates.length
            ? Theme.text('intro.playerTeamWith', { team: you.teamName, teammates: teammates.map(p => p.name).join(' & ') }, seedFor(match))
            : Theme.text('intro.playerTeam', { team: you.teamName }, seedFor(match));

    // The same themed show as the main screen, in step with it (see renderIntro in main-screen.js).
    const at = ms => `style="${UI.animateAt(match.phaseStartedAtMs + ms)}"`;
    return `
        <div class="intro-show">
            <div class="center-stack intro-stage" style="${UI.animateAt(match.phaseEndsAtMs - 3000)}">
                <div class="intro-slam" ${at(0)}><h1 class="big-title">${Theme.text('intro.playerTitle', { name: you.name }, seedFor(match, you))}</h1></div>
                <p class="subtitle intro-rise" ${at(600)}>${teamLine}</p>
                <p class="subtitle intro-rise" ${at(1100)}>${Theme.text('intro.rules', { seconds: match.matchSeconds }, seedFor(match))}</p>
                ${match.earnsTime ? `<p class="subtitle intro-rise" ${at(1500)}>${Theme.text('intro.earnTime', {}, seedFor(match))}</p>` : ''}
                ${HeroCamera.offer(you, connection.serverNow())}
                ${HeroCamera.hint(you)}
            </div>
            ${UI.introCountdown(match.phaseEndsAtMs)}
        </div>`;
}

// ---------------------------------------------------------------- Playing

/** The Dungeon fight. The other players' moments that matter to you are noted here, and shown in the monster's room (DungeonFeed). */
function dungeonStation(match, you, color) {
    DungeonFeed.note(match, you);
    return DungeonView.station(match, you, connection.serverNow(), color);
}

function playing(match, you) {
    const q = you.question;
    const lockedOut = you.lockedOutUntilMs && you.lockedOutUntilMs > connection.serverNow();

    const answers = q.answers.map((text, i) => `
        <div class="answer ${lockedOut ? 'is-dimmed' : ''}" data-letter="${UI.ANSWER_LETTERS[i]}">
            <span class="answer-letter">${UI.ANSWER_LETTERS[i]}</span>
            <span>${UI.escape(text)}</span>
        </div>`).join('');

    // With several categories mixed, say which one this question is from.
    const category = match.category.includes(',') ? ` <span class="question-category">&middot; ${UI.escape(q.category)}</span>` : '';

    return `
        <div class="player-clock">
            <span>${Theme.text('player.questionNumber', { n: q.number })}${category}</span>
            <span class="player-clock-seconds" data-ends-at="${match.phaseEndsAtMs}" data-hurry-at="10"></span>
        </div>
        <div class="timer-bar" data-starts-at="${match.phaseStartedAtMs}" data-ends-at="${match.phaseEndsAtMs}"><div class="timer-bar-fill"></div></div>
        ${lockedOut ? lockoutBanner(match, you) : feedbackBanner(match, you)}
        <div class="question-text">${UI.questionMedia(q.mediaUrl)}<div>${UI.escape(q.text)}</div></div>
        <div class="answer-grid">${answers}</div>`;
}

/** How the last answer went ("Correct! +100" / "Wrong! It was B: ..."), or "Go!" before the first one. */
function feedbackBanner(match, you) {
    const last = you.lastAnswer;
    if (!last) return `<div class="banner">${Theme.text('player.go', {}, seedFor(match, you))}</div>`;

    // Pop the banner only when it's new, not on every redraw (other players' answers redraw us too).
    const pop = last.number !== lastAnswerShown ? 'pop' : '';
    lastAnswerShown = last.number;

    const seed = `${seedFor(match, you)}:${last.number}`;
    const byTeammate = last.pressedByStationId !== you.stationId;

    if (last.isCorrect) {
        const text = byTeammate
            ? Theme.text('player.teammateCorrect', { name: last.pressedByName, points: last.points }, seed)
            : Theme.text('player.correct', { points: last.points }, seed);
        return `<div class="banner correct ${pop}">${text}</div>`;
    }

    const headline = byTeammate
        ? Theme.text('player.teammateWrong', { name: last.pressedByName }, seed)
        : Theme.text('player.wrong', {}, seed);
    const lost = last.points < 0 ? ` ${Theme.text('player.lostPoints', { points: last.points })}` : '';
    const itWas = Theme.text('player.itWas', { letter: UI.ANSWER_LETTERS[last.correctIndex], answer: last.correctAnswer });

    return `<div class="banner wrong ${pop}">${headline}${lost}<div class="banner-detail">${itWas}</div></div>`;
}

/** Wrong answer with the lockout penalty: buttons are dead for a moment. */
function lockoutBanner(match, you) {
    const last = you.lastAnswer;
    const itWas = last
        ? `<div class="banner-detail">${Theme.text('player.itWas', { letter: UI.ANSWER_LETTERS[last.correctIndex], answer: last.correctAnswer })}</div>`
        : '';
    const pop = last && last.number !== lastAnswerShown ? 'pop' : '';
    lastAnswerShown = last?.number ?? null;

    return `
        <div class="banner wrong ${pop}">
            ${Theme.text('player.lockedOut', {}, `${seedFor(match, you)}:${last?.number}`)} <span data-ends-at="${you.lockedOutUntilMs}"></span>
            ${itWas}
        </div>`;
}

// ---------------------------------------------------------------- Results

/** Dungeon: the clock ran out. "Continue?" with a big countdown: swipe your card (a second session; in a group, anyone's will do). */
function continueOffer(match, you) {
    const seed = `${seedFor(match, you)}:${match.continuesUsed}`;
    const progress = you.delve
        ? `<p class="subtitle">${Theme.text('continue.progress', { floor: you.delve.floor, kills: you.delve.kills, score: you.score.toLocaleString() })}</p>`
        : '';
    return `
        <div class="center-stack">
            <h1 class="big-title">${Theme.text('continue.title', {}, seed)}</h1>
            <div class="match-clock" data-ends-at="${match.phaseEndsAtMs}" data-hurry-at="4"></div>
            ${you.delve?.out ? `<p class="subtitle continue-out">${Theme.text('continue.outOfLives', { lives: you.delve.maxLives })}</p>` : ''}
            <p class="subtitle">${Theme.text('continue.swipe', { seconds: match.continueSeconds }, seed)}</p>
            ${progress}
        </div>`;
}

function results(match, you) {
    const mine = myStanding(match, you);
    const won = mine.rank === 1;
    const seed = seedFor(match, you);

    const headline = won
        ? Theme.text('results.playerWon', { who: you.teamName || Theme.raw(Theme.text('results.you')) }, seed)
        : Theme.text('results.place', { place: UI.ordinal(mine.rank) }, seed);

    // In team modes, also show this player's own contribution to the team total.
    const detail = you.teamName
        ? Theme.text('results.contribution', { score: you.score.toLocaleString(), team: you.teamName, total: mine.score.toLocaleString() })
        : Theme.text('results.points', { points: mine.score.toLocaleString() });

    const highScore = myPlacement
        ? `<div class="high-score">${Theme.text('results.playerHighScore', {
              rank: myPlacement.rank,
              when: Theme.raw(Theme.text(myPlacement.board === 'today' ? 'results.today' : 'results.allTime')),
          }, seed)}</div>`
        : '';

    return `
        <div class="center-stack">
            <h1 class="big-title">${headline}</h1>
            <p class="subtitle">${detail}</p>
            <p class="subtitle">${Theme.text('playing.right', { n: mine.correctAnswers })}</p>
            ${highScore}
            ${podium(match)}
        </div>`;
}

/** A group game's final places, with everyone's portrait (their face, or their hero). Nothing when playing alone. */
function podium(match) {
    if (match.players.length < 2) return '';
    const rows = match.standings.map(standing => {
        const players = standing.isTeam ? match.players.filter(p => p.teamId === standing.id) : match.players.filter(p => p.stationId === standing.id);
        const faces = players.map(p => HeroPhoto.portrait(match, p.stationId, 'podium-face', '')).join('');
        return `
            <div class="podium-row">
                <span class="podium-rank">${UI.ordinal(standing.rank)}</span>
                <span class="podium-faces">${faces}</span>
                <span class="podium-name">${UI.escape(standing.name)}</span>
                <b>${standing.score.toLocaleString()}</b>
            </div>`;
    }).join('');
    return `<div class="podium">${rows}</div>`;
}

/** This player's row in the standings: their own (free-for-all) or their team's. */
function myStanding(match, you) {
    const id = match.usesTeams ? you.teamId : you.stationId;
    return match.standings.find(s => s.id === id);
}

/** Keeps a randomly-picked line the same for one moment, but different per station so screens vary. */
function seedFor(match, you = null) {
    return `${match.matchId}:${match.phase}:${you?.stationId ?? ''}`;
}
