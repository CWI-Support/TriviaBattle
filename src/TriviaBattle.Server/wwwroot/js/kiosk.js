// Kiosk outside the room: players swipe in, enter names, and pick mode, teams, category and difficulty.
// This page only collects choices. The server validates them and runs the match.
// All wording (mode names, team names, prompts) comes from the theme's copy.json via Theme.text().

const MODES = [
    { id: 'FreeForAll', usesTeams: false },
    { id: 'TeamSharedAnswer', usesTeams: true },
    { id: 'TeamCombinedScore', usesTeams: true },
];
const DIFFICULTIES = ['Easy', 'Medium', 'Hard'];
const TEAM_IDS = ['red', 'blue']; // names come from copy.json "teams"
const MAX_NAME_LENGTH = 16;               // same limit as the server (MatchSetupValidator)
const RESET_AFTER_IDLE_MS = 2 * 60 * 1000; // walk-away protection

let room = null;           // this room and its stations, from the server
let categories = [];
let matchRunning = false;
let step = 'mode';
let setup = emptySetup();
let listeningStationId = null; // the player slot waiting for a card swipe

function emptySetup() {
    return {
        mode: null,
        seats: {},     // stationId -> { name, rfidTag (null for guests) }
        teamOf: {},    // stationId -> teamId
        categoryId: null,
        difficulty: 'Medium',
    };
}

// ------------------------------------------------------------------ Start-up

const connection = new ScreenConnection({ role: 'kiosk', onState: receiveState });
connection.start();
loadCategories();
listenForCardSwipes(cardSwiped);
setUpIdleReset();

document.getElementById('back-button').addEventListener('click', goBack);
document.getElementById('next-button').addEventListener('click', goNext);

async function loadCategories() {
    const response = await fetch('/api/categories');
    categories = await response.json();
    if (step === 'category') renderStep();
}

function receiveState(state) {
    room = state.room;

    if (state.match) {
        matchRunning = true;
        renderBusy(state.match);
        UI.showView('view-busy');
    } else {
        if (matchRunning) resetKiosk(); // a match just ended: ready for the next group
        matchRunning = false;
        UI.showView('view-setup');
        renderStep();
    }
}

// ------------------------------------------------------------------ Step navigation

function steps() {
    const mode = MODES.find(m => m.id === setup.mode);
    return mode?.usesTeams
        ? ['mode', 'players', 'teams', 'category', 'confirm']
        : ['mode', 'players', 'category', 'confirm'];
}

function goNext() {
    if (step === 'confirm') {
        startMatch();
        return;
    }
    const list = steps();
    step = list[list.indexOf(step) + 1];
    if (step === 'teams') assignDefaultTeams();
    renderStep();
}

function goBack() {
    const list = steps();
    const index = list.indexOf(step);
    if (index > 0) step = list[index - 1];
    renderStep();
}

function resetKiosk() {
    setup = emptySetup();
    step = 'mode';
    listeningStationId = null;
    showProblems([]);
    renderStep();
}

function setUpIdleReset() {
    let timer;
    const restart = () => {
        clearTimeout(timer);
        timer = setTimeout(() => { if (!matchRunning && step !== 'mode') resetKiosk(); }, RESET_AFTER_IDLE_MS);
    };
    ['pointerdown', 'keydown'].forEach(type => document.addEventListener(type, restart));
    restart();
}

/** Whether the current step has what it needs to move on (the server re-checks everything). */
function canContinue() {
    const seated = seatedStationIds();
    switch (step) {
        case 'mode':
            return setup.mode !== null;
        case 'players': {
            const mode = MODES.find(m => m.id === setup.mode);
            const everyoneNamed = seated.every(id => setup.seats[id].name.trim().length > 0);
            return everyoneNamed && seated.length >= (mode.usesTeams ? 2 : 1);
        }
        case 'teams':
            return TEAM_IDS.every(teamId => seated.some(id => setup.teamOf[id] === teamId));
        case 'category':
            return setup.categoryId !== null;
        default:
            return true;
    }
}

function updateFooter() {
    const list = steps();
    document.getElementById('step-indicator').textContent = Theme.plain('kiosk.stepOf', { n: list.indexOf(step) + 1, total: list.length });
    const back = document.getElementById('back-button');
    back.hidden = step === 'mode';
    back.textContent = Theme.plain('kiosk.back');
    const next = document.getElementById('next-button');
    next.textContent = Theme.plain(step === 'confirm' ? 'kiosk.start' : 'kiosk.next');
    next.disabled = !canContinue();
}

// ------------------------------------------------------------------ Rendering each step

function renderStep() {
    if (!room) return;
    document.getElementById('kiosk-title').innerHTML = Theme.text('brand.title');
    const container = document.getElementById('kiosk-step');

    switch (step) {
        case 'mode':     container.innerHTML = modeStep(); break;
        case 'players':  container.innerHTML = playersStep(); break;
        case 'teams':    container.innerHTML = teamsStep(); break;
        case 'category': container.innerHTML = categoryStep(); break;
        case 'confirm':  container.innerHTML = confirmStep(); break;
    }
    updateFooter();
}

function modeStep() {
    const cards = MODES.map(mode => `
        <div class="choice ${setup.mode === mode.id ? 'is-selected' : ''}" onclick="chooseMode('${mode.id}')">
            <h3>${Theme.text(`modes.${mode.id}.name`)}</h3>
            <p>${Theme.text(`modes.${mode.id}.description`)}</p>
        </div>`).join('');
    return `<h2>${Theme.text('kiosk.modeTitle')}</h2><div class="choice-grid">${cards}</div>`;
}

function playersStep() {
    const cards = room.stations.map(station => {
        const seat = setup.seats[station.id];
        const listening = listeningStationId === station.id;

        const content = seat
            ? `<input class="keyboard-input" maxlength="${MAX_NAME_LENGTH}" placeholder="${Theme.text('kiosk.namePlaceholder')}"
                      value="${UI.escape(seat.name)}" oninput="nameTyped('${station.id}', this.value)">
               <div class="dim">${Theme.text(seat.rfidTag ? 'kiosk.seatCardRegistered' : 'kiosk.seatGuest')}</div>
               <div class="seat-buttons"><button onclick="removeSeat('${station.id}')">${Theme.text('kiosk.remove')}</button></div>`
            : `<div class="dim">${Theme.text(listening ? 'kiosk.seatSwipeNow' : 'kiosk.seatEmpty')}</div>
               <div class="seat-buttons">
                   <button onclick="listenForCard('${station.id}')">${Theme.text('kiosk.swipeCard')}</button>
                   <button onclick="addGuest('${station.id}')">${Theme.text('kiosk.playAsGuest')}</button>
               </div>`;

        return `
            <div class="choice seat-card ${listening ? 'is-listening' : ''}" style="--chip-color: ${station.color}">
                <h3 style="color: ${station.color}">${UI.escape(station.label)}</h3>
                ${content}
            </div>`;
    }).join('');

    const devHelper = new URLSearchParams(location.search).get('dev') === '1'
        ? `<p class="dim" style="text-align:center"><button onclick="simulateCardSwipe()">Simulate card swipe (dev)</button></p>`
        : '';

    return `<h2>${Theme.text('kiosk.playersTitle')}</h2><div class="choice-grid">${cards}</div>${devHelper}`;
}

function teamsStep() {
    const columns = TEAM_IDS.map(teamId => {
        const members = seatedStationIds()
            .filter(id => setup.teamOf[id] === teamId)
            .map(id => {
                const station = room.stations.find(s => s.id === id);
                return `<div class="chip" style="--chip-color: ${station.color}" onclick="switchTeam('${id}')">
                            <div class="chip-name">${UI.escape(setup.seats[id].name)}</div>
                            <div class="chip-detail">${UI.escape(station.label)}</div>
                        </div>`;
            }).join('');

        return `<div class="choice"><h3>${Theme.text(`teams.${teamId}`)}</h3><div class="chip-row">${members || `<span class="dim">${Theme.text('kiosk.nobodyYet')}</span>`}</div></div>`;
    }).join('');

    return `<h2>${Theme.text('kiosk.teamsTitle')}</h2><div class="choice-grid">${columns}</div>`;
}

function categoryStep() {
    const categoryCards = categories.map(c => `
        <div class="choice ${setup.categoryId === c.id ? 'is-selected' : ''}" onclick="chooseCategory(${c.id})">
            <h3>${UI.escape(c.name)}</h3>
        </div>`).join('');

    const difficultyCards = DIFFICULTIES.map(d => `
        <div class="choice ${setup.difficulty === d ? 'is-selected' : ''}" onclick="chooseDifficulty('${d}')">
            <h3>${d}</h3>
        </div>`).join('');

    return `
        <h2>${Theme.text('kiosk.categoryTitle')}</h2><div class="choice-grid">${categoryCards}</div>
        <h2 style="margin-top: 4vh">${Theme.text('kiosk.difficultyTitle')}</h2><div class="choice-grid">${difficultyCards}</div>`;
}

function confirmStep() {
    const mode = MODES.find(m => m.id === setup.mode);
    const category = categories.find(c => c.id === setup.categoryId);
    const lineup = mode.usesTeams
        ? TEAM_IDS.map(teamId => `<p><b>${Theme.text(`teams.${teamId}`)}:</b> ${namesOn(teamId)}</p>`).join('')
        : `<p>${seatedStationIds().map(id => UI.escape(setup.seats[id].name)).join(', ')}</p>`;

    return `
        <div class="center-stack" style="height:auto">
            <h2>${Theme.text('kiosk.confirmTitle')}</h2>
            <p class="subtitle">${Theme.text(`modes.${mode.id}.name`)} &middot; ${UI.escape(category?.name)} &middot; ${setup.difficulty}</p>
            <div class="subtitle">${lineup}</div>
        </div>`;
}

function renderBusy(match) {
    const progress = match.phase === 'Intro'
        ? Theme.text('kiosk.busyIntro', {}, match.matchId)
        : match.phase === 'Results'
            ? Theme.text('kiosk.busyResults', {}, match.matchId)
            : Theme.text('kiosk.busyQuestion', { n: match.roundNumber, total: match.totalRounds });

    document.getElementById('view-busy').innerHTML = `
        <div class="center-stack">
            <h1 class="big-title">${Theme.text('kiosk.busyTitle', {}, match.matchId)}</h1>
            <p class="subtitle">${progress}</p>
            <p class="subtitle">${Theme.text(`modes.${match.mode}.name`)} &middot; ${UI.escape(match.category)}</p>
            <p class="dim">${Theme.text('kiosk.busyWait', {}, match.matchId)}</p>
        </div>`;
}

// ------------------------------------------------------------------ User actions

function chooseMode(modeId) {
    setup.mode = modeId;
    renderStep();
}

function listenForCard(stationId) {
    listeningStationId = stationId;
    renderStep();
}

function addGuest(stationId) {
    setup.seats[stationId] = { name: '', rfidTag: null };
    listeningStationId = null;
    renderStep();
}

function removeSeat(stationId) {
    delete setup.seats[stationId];
    delete setup.teamOf[stationId];
    renderStep();
}

function nameTyped(stationId, value) {
    setup.seats[stationId].name = value;
    updateFooter(); // don't re-render: that would close the on-screen keyboard
}

async function cardSwiped(rfidTag) {
    if (step !== 'players') return;

    if (Object.values(setup.seats).some(seat => seat.rfidTag === rfidTag)) {
        showProblems([Theme.plain('kiosk.cardAlreadyIn')]);
        return;
    }

    // Use the slot that asked for a card, or else the first empty one.
    const stationId = listeningStationId || room.stations.map(s => s.id).find(id => !setup.seats[id]);
    if (!stationId) {
        showProblems([Theme.plain('kiosk.stationsFull')]);
        return;
    }

    const response = await fetch(`/api/players/rfid/${encodeURIComponent(rfidTag)}`);
    const known = response.ok ? await response.json() : null;

    setup.seats[stationId] = { name: known ? known.displayName : '', rfidTag };
    listeningStationId = null;
    showProblems([]);
    renderStep();
}

function simulateCardSwipe() {
    const tag = prompt('Card id to simulate:', 'TEST' + Math.floor(Math.random() * 10000));
    if (tag) cardSwiped(tag);
}

function switchTeam(stationId) {
    const current = TEAM_IDS.indexOf(setup.teamOf[stationId]);
    setup.teamOf[stationId] = TEAM_IDS[(current + 1) % TEAM_IDS.length];
    renderStep();
}

function chooseCategory(categoryId) {
    setup.categoryId = categoryId;
    renderStep();
}

function chooseDifficulty(difficulty) {
    setup.difficulty = difficulty;
    renderStep();
}

// Suggest a split (first half of the seated stations vs the rest) that players can change.
// Only fills in players who don't have a team yet.
function assignDefaultTeams() {
    const seated = seatedStationIds();
    seated.forEach((id, index) => {
        if (!setup.teamOf[id]) setup.teamOf[id] = index < Math.ceil(seated.length / 2) ? TEAM_IDS[0] : TEAM_IDS[1];
    });
}

// ------------------------------------------------------------------ Starting the match

async function startMatch() {
    const nextButton = document.getElementById('next-button');
    nextButton.disabled = true;
    showProblems([]);

    try {
        const seats = [];
        for (const stationId of seatedStationIds()) {
            const seat = setup.seats[stationId];
            const playerProfileId = seat.rfidTag ? await registerCard(seat) : null;
            seats.push({ stationId, playerName: seat.name.trim(), playerProfileId });
        }

        const mode = MODES.find(m => m.id === setup.mode);
        const teams = mode.usesTeams
            ? TEAM_IDS.map(teamId => ({
                teamId,
                name: Theme.plain(`teams.${teamId}`), // the theme's team name is what's saved and shown
                stationIds: seatedStationIds().filter(id => setup.teamOf[id] === teamId),
            }))
            : [];

        const response = await fetch('/api/matches', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ mode: setup.mode, categoryId: setup.categoryId, difficulty: setup.difficulty, seats, teams }),
        });

        if (!response.ok) {
            const body = await response.json();
            throw new Error((body.problems || [body.message || 'Could not start the match.']).join(' '));
        }
        // Success: the server pushes the new match state and the kiosk switches to "Battle in progress".
    } catch (err) {
        showProblems([err.message]);
        nextButton.disabled = false;
    }
}

/** Creates/updates the profile for a swiped card and returns its id. */
async function registerCard(seat) {
    const response = await fetch('/api/players', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ rfidTag: seat.rfidTag, displayName: seat.name.trim() }),
    });
    const body = await response.json();
    if (!response.ok) throw new Error(body.problems.join(' '));
    return body.id;
}

// ------------------------------------------------------------------ Helpers

/** Seated stations, in room order. */
function seatedStationIds() {
    return room.stations.map(s => s.id).filter(id => setup.seats[id]);
}

function namesOn(teamId) {
    return seatedStationIds()
        .filter(id => setup.teamOf[id] === teamId)
        .map(id => UI.escape(setup.seats[id].name))
        .join(' &amp; ');
}

function showProblems(problems) {
    document.getElementById('problems').textContent = problems.join(' ');
}
