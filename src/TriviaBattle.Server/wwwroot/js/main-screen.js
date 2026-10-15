// Main room screen: the match clock and a live leaderboard while everyone races, then results.
// Questions aren't shown here: each player has their own on their station screen.
// Like every screen, it only draws the state the server sends. There are no game rules here.
// All wording comes from the theme's copy.json via Theme.text() (see js/theme.js).

let stationsById = {};
let lastState = null;
let placements = [];   // leaderboard placements announced for the current match
let answerCounts = {}; // id -> { correct, wrong } as last drawn, to flash a row when it changes
let roomAudioPlaysIntro = false; // true when the server plays the room audio, intro sound included

const connection = new ScreenConnection({ role: 'main', onState: render, onEvent: handleEvent });
const introVideo = new SyncedVideo(document.getElementById('intro-video'), () => connection.serverNow());
fetch('/api/audio')
    .then(response => response.ok ? response.json() : null)
    .then(audio => { roomAudioPlaysIntro = !!audio?.enabled; })
    .catch(() => {})
    .finally(() => connection.start());
UI.startCountdowns(() => connection.serverNow());
Theme.load().then(() => LeaderboardView.startRotation(document.getElementById('lobby-leaderboard')));
if (devKeysRequested()) enableDevKeys(connection);

function render(state) {
    if (state.match?.matchId !== lastState?.match?.matchId) {
        placements = [];
        answerCounts = {};
    }
    lastState = state;
    stationsById = Object.fromEntries(state.room.stations.map(s => [s.id, s]));
    // All room audio comes from the server's sound card; the main screen only plays the intro's sound when that's off.
    introVideo.follow(state.cue, { muted: roomAudioPlaysIntro });
    const match = state.match;

    if (!match) {
        document.getElementById('lobby-title').innerHTML = Theme.text('brand.title');
        document.getElementById('lobby-subtitle').innerHTML = Theme.text('lobby.subtitle', {}, new Date().toDateString());
        UI.showView('view-lobby');
        return;
    }

    switch (match.phase) {
        case 'Intro':    renderIntro(match); break;
        case 'Playing':  renderPlaying(match); break;
        case 'Results':
        case 'Finished': renderResults(match); break;
    }
}

/** One-off moments. Leaderboard placements arrive shortly after Results appears. */
function handleEvent(event) {
    if (event.type === 'LeaderboardPlacement') {
        placements.push(event.placement);
        if (lastState) render(lastState);
    }
}

// ---------------------------------------------------------------- Intro

function renderIntro(match) {
    const lineup = match.usesTeams ? teamsVersus(match) : `<div class="chip-row">${match.players.map(playerChip).join('')}</div>`;

    document.getElementById('view-intro').innerHTML = `
        <div class="center-stack">
            <h1 class="big-title">${Theme.text('intro.title', {}, momentSeed(match))}</h1>
            <p class="subtitle">${modeName(match)} &middot; ${UI.escape(match.category)} &middot; ${UI.escape(match.difficulty)}</p>
            ${lineup}
            <p class="subtitle">${Theme.text('intro.rules', { seconds: match.matchSeconds }, momentSeed(match))}</p>
            <p class="subtitle">${Theme.text('intro.startingIn', { seconds: Theme.raw(`<span data-ends-at="${match.phaseEndsAtMs}"></span>`) })}</p>
        </div>`;
    showFitted('view-intro');
}

function teamsVersus(match) {
    const card = team => `
        <div class="team-card" style="border-top: 1vmin solid ${teamColor(match, team.id)}">
            <h2>${UI.escape(team.name)}</h2>
            <div class="members">${membersOf(match, team).map(p => UI.escape(p.name)).join(' &amp; ')}</div>
        </div>`;

    // Two teams side by side with "VS" between; more teams (future) just line up.
    return match.teams.length === 2
        ? `<div class="vs-layout">${card(match.teams[0])}<div class="vs-word">${Theme.text('intro.versus')}</div>${card(match.teams[1])}</div>`
        : `<div class="chip-row">${match.teams.map(card).join('')}</div>`;
}

// ---------------------------------------------------------------- Playing (the clock is running)

function renderPlaying(match) {
    document.getElementById('view-playing').innerHTML = `
        <div class="main-playing">
            <div class="playing-header">
                <span>${modeName(match)} &middot; ${UI.escape(match.category)}</span>
                <span>${Theme.text('playing.title', {}, momentSeed(match))}</span>
            </div>
            <div class="match-clock" data-ends-at="${match.phaseEndsAtMs}" data-hurry-at="10"></div>
            <div class="timer-bar" data-starts-at="${match.phaseStartedAtMs}" data-ends-at="${match.phaseEndsAtMs}"><div class="timer-bar-fill"></div></div>
            <div class="race-board">${match.standings.map(s => raceRow(match, s)).join('')}</div>
        </div>`;
    showFitted('view-playing');
}

/** One leaderboard line. Flashes green/red when this player/team has just answered. */
function raceRow(match, standing) {
    const team = standing.isTeam ? match.teams.find(t => t.id === standing.id) : null;
    const members = team ? membersOf(match, team) : [match.players.find(p => p.stationId === standing.id)];
    const counts = team ?? members[0];
    const color = team ? teamColor(match, team.id) : colorOf(standing.id);

    const flash = flashFor(standing.id, counts);
    const now = connection.serverNow();
    const lockedOut = members.some(p => p.lockedOutUntilMs && p.lockedOutUntilMs > now);
    const memberLine = team
        ? `<div class="members">${members.map(p => `${UI.escape(p.name)} ${p.score.toLocaleString()}`).join(' &middot; ')}</div>`
        : '';

    return `
        <div class="score-row race-row ${flash}" style="--row-color: ${color}">
            <span class="rank">${UI.ordinal(standing.rank)}</span>
            <span>${UI.escape(standing.name)}${lockedOut ? ` <span class="locked-badge">${Theme.text('playing.lockedOut')}</span>` : ''}${memberLine}</span>
            <span class="right-count">${Theme.text('playing.right', { n: standing.correctAnswers })}</span>
            <span class="score">${standing.score.toLocaleString()}</span>
        </div>`;
}

/** 'flash-correct' / 'flash-wrong' the first time we draw a row after it answered, else ''. */
function flashFor(id, { correctAnswers, wrongAnswers }) {
    const before = answerCounts[id] ?? { correct: correctAnswers, wrong: wrongAnswers };
    answerCounts[id] = { correct: correctAnswers, wrong: wrongAnswers };
    if (correctAnswers > before.correct) return 'flash-correct';
    if (wrongAnswers > before.wrong) return 'flash-wrong';
    return '';
}

// ---------------------------------------------------------------- Results

function renderResults(match) {
    const winners = match.standings.filter(s => s.rank === 1);
    const headline = winners.length > 1
        ? Theme.text('results.tie', {}, match.matchId)
        : Theme.text('results.winner', { name: winners[0].name }, match.matchId);

    const highScores = placements.map(p => `<div class="high-score">${Theme.text('results.highScore', {
        names: p.names,
        rank: p.rank,
        when: Theme.raw(Theme.text(p.board === 'today' ? 'results.today' : 'results.allTime')),
    }, match.matchId + p.ownerId)}</div>`).join('');

    document.getElementById('view-results').innerHTML = `
        <div class="center-stack">
            <h1 class="big-title">${headline}</h1>
            <p class="subtitle">${modeName(match)} &middot; ${UI.escape(match.category)}</p>
            ${highScores}
            ${scoreboard(match)}
        </div>`;
    showFitted('view-results');
}

/** Ranked rows. Team rows also list each teammate's points (their contribution). */
function scoreboard(match) {
    const rows = match.standings.map(standing => {
        let color, members = '';

        if (standing.isTeam) {
            const team = match.teams.find(t => t.id === standing.id);
            color = teamColor(match, team.id);
            members = membersOf(match, team)
                .map(p => `${UI.escape(p.name)} ${p.score.toLocaleString()}`)
                .join(' &middot; ');
        } else {
            color = colorOf(standing.id);
        }

        return `
            <div class="score-row race-row" style="--row-color: ${color}">
                <span class="rank">${UI.ordinal(standing.rank)}</span>
                <span>${UI.escape(standing.name)}<div class="members">${members}</div></span>
                <span class="right-count">${Theme.text('playing.right', { n: standing.correctAnswers })}</span>
                <span class="score">${standing.score.toLocaleString()}</span>
            </div>`;
    }).join('');

    return `<div class="scoreboard">${rows}</div>`;
}

// ---------------------------------------------------------------- Helpers

/** Shows a phase's view and shrinks its text if it would run off the bottom of the screen. */
function showFitted(viewId) {
    UI.showView(viewId);
    UI.fitToScreen(document.getElementById(viewId).firstElementChild);
}

function playerChip(player) {
    return `
        <div class="chip" style="--chip-color: ${colorOf(player.stationId)}">
            <div class="chip-name">${UI.escape(player.name)}</div>
            <div class="chip-detail">${UI.escape(stationsById[player.stationId]?.label || '')}</div>
        </div>`;
}

/** The mode's name as the theme words it. */
function modeName(match) {
    return Theme.text(`modes.${match.mode}.name`);
}

/** Keeps a randomly-picked line the same for one moment (this match and phase). */
function momentSeed(match) {
    return `${match.matchId}:${match.phase}`;
}

function membersOf(match, team) {
    return match.players.filter(p => p.teamId === team.id);
}

function colorOf(stationId) {
    return stationsById[stationId]?.color || 'var(--accent)';
}
