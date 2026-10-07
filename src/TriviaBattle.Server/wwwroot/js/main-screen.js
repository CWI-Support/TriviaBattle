// Main room screen: question, timer, who has answered, scores and results.
// Like every screen, it only draws the state the server sends. There are no game rules here.
// All wording comes from the theme's copy.json via Theme.text() (see js/theme.js).

let stationsById = {};
let lastState = null;
let placements = []; // leaderboard placements announced for the current match

const connection = new ScreenConnection({ role: 'main', onState: render, onEvent: handleEvent });
const introVideo = new SyncedVideo(document.getElementById('intro-video'), () => connection.serverNow());
connection.start();
UI.startCountdowns(() => connection.serverNow());
Theme.load().then(() => LeaderboardView.startRotation(document.getElementById('lobby-leaderboard')));
if (devKeysRequested()) enableDevKeys(connection);

function render(state) {
    if (state.match?.matchId !== lastState?.match?.matchId) placements = [];
    lastState = state;
    stationsById = Object.fromEntries(state.room.stations.map(s => [s.id, s]));
    introVideo.follow(state.cue, { muted: false }); // the main screen carries the sound
    const match = state.match;

    if (!match) {
        document.getElementById('lobby-title').innerHTML = Theme.text('brand.title');
        document.getElementById('lobby-subtitle').innerHTML = Theme.text('lobby.subtitle', {}, new Date().toDateString());
        UI.showView('view-lobby');
        return;
    }

    switch (match.phase) {
        case 'Intro':          renderIntro(match); break;
        case 'QuestionLeadIn': renderLeadIn(match); break;
        case 'QuestionOpen':
        case 'Reveal':         renderQuestion(match); break;
        case 'Standings':      renderStandings(match); break;
        case 'Results':
        case 'Finished':       renderResults(match); break;
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
            <p class="subtitle">${Theme.text('intro.startingIn', { seconds: Theme.raw(`<span data-ends-at="${match.phaseEndsAtMs}"></span>`) })}</p>
        </div>`;
    UI.showView('view-intro');
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

// ---------------------------------------------------------------- Lead-in

function renderLeadIn(match) {
    document.getElementById('view-leadin').innerHTML = `
        <div class="center-stack">
            <p class="subtitle">${UI.escape(match.category)}</p>
            <h1 class="big-title">${Theme.text('leadIn.title', { n: match.roundNumber, total: match.totalRounds }, momentSeed(match))}</h1>
        </div>`;
    UI.showView('view-leadin');
}

// ---------------------------------------------------------------- Question + Reveal

function renderQuestion(match) {
    const q = match.question;
    const isOpen = match.phase === 'QuestionOpen';
    const isRevealed = q.correctIndex !== null;

    const answers = q.answers.map((text, i) => {
        const classes = ['answer'];
        if (isRevealed) classes.push(i === q.correctIndex ? 'is-correct' : 'is-dimmed');
        return `
            <div class="${classes.join(' ')}" data-letter="${UI.ANSWER_LETTERS[i]}">
                <span class="answer-letter">${UI.ANSWER_LETTERS[i]}</span>
                <span>${UI.escape(text)}</span>
            </div>`;
    }).join('');

    // In 2v2 Group the TEAM answers, so show team status; otherwise show each player.
    const statusChips = match.mode === 'TeamSharedAnswer'
        ? match.teams.map(team => answerChip(match, team.name, teamColor(match, team.id), team)).join('')
        : match.players.map(p => answerChip(match, p.name, colorOf(p.stationId), p)).join('');

    document.getElementById('view-question').innerHTML = `
        <div class="main-question">
            <div class="question-header">
                <span>${Theme.text('question.header', { n: match.roundNumber, total: match.totalRounds, category: match.category })}</span>
                ${isOpen ? `<span class="countdown" data-ends-at="${q.closesAtMs}"></span>` : `<span class="countdown">${Theme.text('question.timeUp', {}, momentSeed(match))}</span>`}
            </div>
            ${isOpen ? `<div class="timer-bar" data-starts-at="${q.openedAtMs}" data-ends-at="${q.closesAtMs}"><div class="timer-bar-fill"></div></div>` : '<div></div>'}
            <div class="question-text">${UI.questionMedia(q.mediaUrl)}<div>${UI.escape(q.text)}</div></div>
            <div class="answer-grid">${answers}</div>
            <div class="chip-row">${statusChips}</div>
        </div>`;
    UI.showView('view-question');
}

/** A chip showing whether a player/team has answered, and after reveal, how they did. */
function answerChip(match, name, color, owner) {
    const seed = momentSeed(match) + name;
    let detail = '';
    let state = '';

    if (owner.result) {
        state = owner.result.isCorrect ? 'was-correct' : 'was-wrong';
        detail = owner.result.isCorrect ? Theme.text('status.points', { points: owner.result.points })
            : Theme.text(owner.hasAnswered ? 'status.wrong' : 'status.noAnswer', {}, seed);
    } else if (owner.hasAnswered) {
        state = 'has-answered';
        detail = Theme.text('status.lockedIn', {}, seed);
    }

    return `
        <div class="chip ${state}" style="--chip-color: ${color}">
            <div class="chip-name">${UI.escape(name)}</div>
            <div class="chip-detail">${detail}</div>
        </div>`;
}

// ---------------------------------------------------------------- Standings + Results

function renderStandings(match) {
    document.getElementById('view-standings').innerHTML = `
        <div class="center-stack">
            <h1 class="big-title">${Theme.text('standings.title', {}, momentSeed(match))}</h1>
            <p class="subtitle">${Theme.text('standings.subtitle', { n: match.roundNumber, total: match.totalRounds })}</p>
            ${scoreboard(match)}
        </div>`;
    UI.showView('view-standings');
}

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
    UI.showView('view-results');
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
            <div class="score-row" style="--row-color: ${color}">
                <span class="rank">${UI.ordinal(standing.rank)}</span>
                <span>${UI.escape(standing.name)}<div class="members">${members}</div></span>
                <span class="score">${standing.score.toLocaleString()}</span>
            </div>`;
    }).join('');

    return `<div class="scoreboard">${rows}</div>`;
}

// ---------------------------------------------------------------- Helpers

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

/** Keeps a randomly-picked line the same for one moment (this match, question and phase). */
function momentSeed(match) {
    return `${match.matchId}:${match.roundNumber}:${match.phase}`;
}

function membersOf(match, team) {
    return match.players.filter(p => p.teamId === team.id);
}

function colorOf(stationId) {
    return stationsById[stationId]?.color || 'var(--accent)';
}
