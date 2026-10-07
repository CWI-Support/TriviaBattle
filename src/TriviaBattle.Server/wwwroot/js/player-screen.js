// Player station screen (portrait, next to the 4 answer buttons).
// The server sends a "you" section already worked out for the current mode, so this file
// never needs to know mode rules: you.answer is "the answer that counts for me".
// All wording comes from the theme's copy.json via Theme.text() (see js/theme.js).

const stationNumber = Number(location.pathname.match(/\/display\/player\/(\d+)/)?.[1]);

let lastState = null;
let myPlacement = null; // set if this player/team made a leaderboard this match

const connection = new ScreenConnection({ role: 'player', stationNumber, onState: render, onEvent: handleEvent });
const introVideo = new SyncedVideo(document.getElementById('intro-video'), () => connection.serverNow());
connection.start();
UI.startCountdowns(() => connection.serverNow());
enableStationKeypad(connection); // demo: keys 1-4 / A-D or tapping answers press this station's buttons

function render(state) {
    if (state.match?.matchId !== lastState?.match?.matchId) myPlacement = null;
    lastState = state;
    introVideo.follow(state.cue, { muted: true }); // sound comes from the main screen only

    const match = state.match;
    const you = match?.you;

    document.documentElement.style.setProperty('--station-color', state.station.color);
    document.getElementById('station-label').textContent = state.station.label;
    document.getElementById('player-name').textContent = you?.name || '';
    document.getElementById('player-score').textContent = you ? you.score.toLocaleString() : '';

    const body = document.getElementById('player-body');

    if (!match || !you) {
        body.innerHTML = idle(state, match);
        return;
    }

    switch (match.phase) {
        case 'Intro':          body.innerHTML = intro(match, you); break;
        case 'QuestionLeadIn': body.innerHTML = leadIn(match); break;
        case 'QuestionOpen':
        case 'Reveal':         body.innerHTML = question(match, you); break;
        case 'Standings':      body.innerHTML = standings(match, you); break;
        case 'Results':
        case 'Finished':       body.innerHTML = results(match, you); break;
    }
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

function idle(state, match) {
    return `
        <div class="center-stack">
            <h1 class="big-title">${UI.escape(state.station.label)}</h1>
            <p class="subtitle">${Theme.text(match ? 'player.busy' : 'player.idle', {}, new Date().toDateString())}</p>
        </div>`;
}

function intro(match, you) {
    const teammates = match.players.filter(p => p.teamId && p.teamId === you.teamId && p.stationId !== you.stationId);
    const teamLine = !you.teamName ? ''
        : teammates.length
            ? Theme.text('intro.playerTeamWith', { team: you.teamName, teammates: teammates.map(p => p.name).join(' & ') }, seedFor(match))
            : Theme.text('intro.playerTeam', { team: you.teamName }, seedFor(match));

    return `
        <div class="center-stack">
            <h1 class="big-title">${Theme.text('intro.playerTitle', { name: you.name }, seedFor(match, you))}</h1>
            <p class="subtitle">${teamLine}</p>
            <p class="subtitle">${Theme.text(`modes.${match.mode}.name`)} &middot; ${UI.escape(match.category)}</p>
            <p class="big-title" data-ends-at="${match.phaseEndsAtMs}"></p>
        </div>`;
}

function leadIn(match) {
    return `
        <div class="center-stack">
            <h1 class="big-title">${Theme.text('leadIn.playerTitle', { n: match.roundNumber, total: match.totalRounds })}</h1>
            <p class="subtitle">${Theme.text('leadIn.playerHint', {}, seedFor(match))}</p>
        </div>`;
}

function question(match, you) {
    const q = match.question;
    const isRevealed = q.correctIndex !== null;
    const chosen = you.answer ? you.answer.index : null;

    const answers = q.answers.map((text, i) => {
        const classes = ['answer'];
        if (isRevealed && i === q.correctIndex) classes.push('is-correct');
        else if (chosen === i) classes.push('is-chosen');
        else if (chosen !== null || isRevealed) classes.push('is-dimmed');

        return `
            <div class="${classes.join(' ')}" data-letter="${UI.ANSWER_LETTERS[i]}">
                <span class="answer-letter">${UI.ANSWER_LETTERS[i]}</span>
                <span>${UI.escape(text)}</span>
            </div>`;
    }).join('');

    return `
        ${banner(match, you)}
        <div class="question-text">${UI.questionMedia(q.mediaUrl)}<div>${UI.escape(q.text)}</div></div>
        <div class="answer-grid">${answers}</div>
        ${match.phase === 'QuestionOpen'
            ? `<div class="timer-bar" data-starts-at="${q.openedAtMs}" data-ends-at="${q.closesAtMs}"><div class="timer-bar-fill"></div></div>`
            : ''}`;
}

/** The big message above the question: press now / locked in / correct / wrong. */
function banner(match, you) {
    const answer = you.answer;
    const letter = answer ? UI.ANSWER_LETTERS[answer.index] : '';
    const seed = seedFor(match, you);

    if (you.result) {
        if (you.result.isCorrect) return `<div class="banner correct">${Theme.text('player.correct', { points: you.result.points }, seed)}</div>`;
        return `<div class="banner wrong">${Theme.text(answer ? 'player.wrong' : 'player.timeUp', {}, seed)}</div>`;
    }

    if (!answer) {
        return `<div class="banner">${Theme.text('player.pressNow', {}, seed)} <span data-ends-at="${match.question.closesAtMs}"></span></div>`;
    }

    // In 2v2 Group a teammate may have locked in for both of you.
    const pressedByTeammate = answer.pressedByStationId !== you.stationId;
    return pressedByTeammate
        ? `<div class="banner">${Theme.text('player.teammateLockedIn', { name: answer.pressedByName, letter }, seed)}</div>`
        : `<div class="banner">${Theme.text('player.lockedIn', { letter }, seed)}</div>`;
}

function standings(match, you) {
    const mine = myStanding(match, you);
    return `
        <div class="center-stack">
            <p class="subtitle">${you.teamName ? Theme.text('standings.playerTeam', { team: you.teamName }) : Theme.text('standings.playerYou')}</p>
            <h1 class="big-title">${Theme.text('standings.place', { place: UI.ordinal(mine.rank) }, seedFor(match, you))}</h1>
            <p class="subtitle">${Theme.text('standings.points', { points: mine.score.toLocaleString() })}</p>
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
            ${highScore}
        </div>`;
}

/** This player's row in the standings: their own (free-for-all) or their team's. */
function myStanding(match, you) {
    const id = match.usesTeams ? you.teamId : you.stationId;
    return match.standings.find(s => s.id === id);
}

/** Keeps a randomly-picked line the same for one moment, but different per station so screens vary. */
function seedFor(match, you = null) {
    return `${match.matchId}:${match.roundNumber}:${match.phase}:${you?.stationId ?? ''}`;
}
