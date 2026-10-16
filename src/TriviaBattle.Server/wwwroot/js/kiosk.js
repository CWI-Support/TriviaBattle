// Kiosk outside the room: players swipe in, enter names, and pick mode, teams, categories and difficulty.
// This page only collects choices. The server validates them and runs the match.
// Several groups can play in the room at once: stations already in a game show as busy (with the
// time left), and the next group sets up on the free ones. Only when every station is busy does
// the kiosk show "battle in progress".
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
let busy = {};             // stationId -> the match being played there
let busyKey = '';          // which stations are busy, to redraw only when that changes
let step = 'mode';
let setup = emptySetup();
let listeningStationId = null; // the player slot waiting for a card swipe

function emptySetup() {
    return {
        mode: null,
        seats: {},     // stationId -> { name, rfidTag (null for guests) }
        teamOf: {},    // stationId -> teamId
        categoryIds: [],   // one or more; questions from all of them are mixed
        difficulty: 'Medium',
    };
}

// ------------------------------------------------------------------ Start-up

const connection = new ScreenConnection({ role: 'kiosk', onState: receiveState });
connection.start();
UI.startCountdowns(() => connection.serverNow()); // seconds left in the running match
loadCategories();
listenForCardSwipes(cardSwiped);
setUpIdleReset();

document.getElementById('back-button').addEventListener('click', goBack);
document.getElementById('next-button').addEventListener('click', goNext);

async function loadCategories() {
    try {
        const response = await fetch('/api/categories');
        if (!response.ok) throw new Error(`HTTP ${response.status}`);
        categories = await response.json();
        if (step === 'category') renderStep();
    } catch {
        setTimeout(loadCategories, 3000); // server not reachable yet: keep trying, or the category step stays empty
    }
}

function receiveState(state) {
    const firstState = room === null;
    room = state.room;

    busy = {};
    for (const match of state.matches)
        for (const player of match.players) busy[player.stationId] = match;

    // States arrive with every answer in every running game; only redraw when the busy stations change.
    const key = Object.entries(busy).map(([id, m]) => `${id}:${m.matchId}:${m.phase}`).sort().join(',');
    if (!firstState && key === busyKey) return;
    busyKey = key;

    // Someone else's game started on a station this group had picked: free that seat.
    for (const stationId of Object.keys(setup.seats).filter(id => busy[id]))
        removeSeat(stationId);

    if (room.stations.every(s => busy[s.id])) {
        if (onScreenKeyboard?.isOpen) onScreenKeyboard.hideKeyboard();
        renderAllBusy(state.matches);
        UI.showView('view-busy');
    } else {
        UI.showView('view-setup');
        renderStep();
    }
}

// ------------------------------------------------------------------ Step navigation

// The last step's button starts the match (no separate confirm screen: one less tap per group).
function steps() {
    const mode = MODES.find(m => m.id === setup.mode);
    return mode?.usesTeams
        ? ['mode', 'players', 'teams', 'category']
        : ['mode', 'players', 'category'];
}

function isLastStep() {
    const list = steps();
    return list.indexOf(step) === list.length - 1;
}

function goNext() {
    if (isLastStep()) {
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
    loadCategories(); // pick up any category changes made in admin since the last group
}

function setUpIdleReset() {
    let timer;
    const restart = () => {
        clearTimeout(timer);
        timer = setTimeout(() => { if (step !== 'mode') resetKiosk(); }, RESET_AFTER_IDLE_MS);
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
            // Guests may skip typing (they play as "Player 2" etc.); a new card needs a name to register.
            const mode = MODES.find(m => m.id === setup.mode);
            const cardsNamed = seated.every(id => !setup.seats[id].rfidTag || setup.seats[id].name.trim().length > 0);
            return cardsNamed && seated.length >= (mode.usesTeams ? 2 : 1);
        }
        case 'teams':
            return TEAM_IDS.every(teamId => seated.some(id => setup.teamOf[id] === teamId));
        case 'category':
            return setup.categoryIds.length > 0;
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
    next.textContent = Theme.plain(isLastStep() ? 'kiosk.start' : 'kiosk.next');
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
    }
    updateFooter();
    keepKeyboardOnScreenInput();
}

/**
 * Redrawing replaces the name boxes, so an open keyboard would be typing into a box that's gone.
 * Point it at the same seat's new box, or close it if that box isn't on screen any more
 * (another step, or the seat was removed). Otherwise it covers the next step's buttons.
 */
function keepKeyboardOnScreenInput() {
    if (!onScreenKeyboard?.isOpen || onScreenKeyboard.activeInput?.isConnected) return;

    const stationId = onScreenKeyboard.activeInput?.dataset.stationId;
    const replacement = stationId && document.querySelector(`.keyboard-input[data-station-id="${stationId}"]`);
    if (replacement) onScreenKeyboard.showKeyboard(replacement);
    else onScreenKeyboard.hideKeyboard();
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
        if (busy[station.id]) return busyStationCard(station, busy[station.id]);

        const seat = setup.seats[station.id];
        const listening = listeningStationId === station.id;

        // A guest who doesn't type a name plays as the station's label, shown as the placeholder.
        const placeholder = seat && !seat.rfidTag ? UI.escape(station.label) : Theme.text('kiosk.namePlaceholder');

        const content = seat
            ? `<input class="keyboard-input" data-station-id="${station.id}" maxlength="${MAX_NAME_LENGTH}" placeholder="${placeholder}"
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

/** A station another group is playing on: shown, but it can't be picked until their game ends. */
function busyStationCard(station, match) {
    const freeAt = match.phase === 'Playing' ? match.phaseEndsAtMs : null;
    const detail = freeAt
        ? Theme.text('kiosk.seatInGame', { seconds: Theme.raw(`<span data-ends-at="${freeAt}"></span>`) })
        : Theme.text('kiosk.seatInGameSoon');
    return `
        <div class="choice seat-card is-busy" style="--chip-color: ${station.color}">
            <h3 style="color: ${station.color}">${UI.escape(station.label)}</h3>
            <div class="dim">${detail}</div>
        </div>`;
}

function teamsStep() {
    const columns = TEAM_IDS.map(teamId => {
        const members = seatedStationIds()
            .filter(id => setup.teamOf[id] === teamId)
            .map(id => {
                const station = room.stations.find(s => s.id === id);
                return `<div class="chip" style="--chip-color: ${station.color}" onclick="switchTeam('${id}')">
                            <div class="chip-name">${UI.escape(seatName(id))}</div>
                            <div class="chip-detail">${UI.escape(station.label)}</div>
                        </div>`;
            }).join('');

        return `<div class="choice"><h3>${Theme.text(`teams.${teamId}`)}</h3><div class="chip-row">${members || `<span class="dim">${Theme.text('kiosk.nobodyYet')}</span>`}</div></div>`;
    }).join('');

    return `<h2>${Theme.text('kiosk.teamsTitle')}</h2><div class="choice-grid">${columns}</div>`;
}

// Compact cards so all categories and the difficulties fit on a 1080p kiosk without scrolling.
// Any number of categories can be picked; the chosen ones are listed under the title.
function categoryStep() {
    const categoryCards = categories.map(c => `
        <div class="choice ${setup.categoryIds.includes(c.id) ? 'is-selected' : ''}" onclick="chooseCategory(${c.id})">
            <h3>${UI.escape(c.name)}</h3>
        </div>`).join('');

    const chosen = categories.filter(c => setup.categoryIds.includes(c.id)).map(c => c.name);
    const allChosen = categories.length > 0 && chosen.length === categories.length;
    const chosenLine = allChosen ? Theme.text('kiosk.categoriesAllChosen')
        : chosen.length ? Theme.text('kiosk.categoriesChosen', { list: chosen.join(' · ') })
        : Theme.text('kiosk.categoryHint');
    const allButton = `<button class="big-button secondary small" onclick="chooseAllCategories()">${Theme.text(allChosen ? 'kiosk.categoriesClear' : 'kiosk.categoriesAll')}</button>`;

    const difficultyCards = DIFFICULTIES.map(d => `
        <div class="choice ${setup.difficulty === d ? 'is-selected' : ''}" onclick="chooseDifficulty('${d}')">
            <h3>${d}</h3>
        </div>`).join('');

    return `
        <h2>${Theme.text('kiosk.categoryTitle')}</h2>
        <div class="category-bar"><p class="subtitle category-list">${chosenLine}</p>${allButton}</div>
        <div class="choice-grid compact">${categoryCards}</div>
        <h2 class="spaced">${Theme.text('kiosk.difficultyTitle')}</h2><div class="choice-grid compact">${difficultyCards}</div>`;
}

/** Every station is in a game: show each game and how long it has left. */
function renderAllBusy(matches) {
    const lines = matches.map(match => {
        const who = match.players.map(p => UI.escape(p.name)).join(' &middot; ');
        const progress = match.phase === 'Intro'
            ? Theme.text('kiosk.busyIntro', {}, match.matchId)
            : match.phase === 'Results'
                ? Theme.text('kiosk.busyResults', {}, match.matchId)
                : Theme.text('kiosk.busyPlaying', { seconds: Theme.raw(`<span data-ends-at="${match.phaseEndsAtMs}"></span>`) });
        return `<p class="subtitle"><b>${who}</b> &middot; ${progress}</p>`;
    }).join('');

    document.getElementById('view-busy').innerHTML = `
        <div class="center-stack">
            <h1 class="big-title">${Theme.text('kiosk.busyTitle', {}, matches[0]?.matchId)}</h1>
            ${lines}
            <p class="dim">${Theme.text('kiosk.busyWait', {}, matches[0]?.matchId)}</p>
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
    if (!setup.seats[stationId]) return; // seat was removed while the keyboard was open
    setup.seats[stationId].name = value;
    updateFooter(); // don't re-render: that would close the on-screen keyboard
}

async function cardSwiped(rfidTag) {
    if (step !== 'players') return;

    if (Object.values(setup.seats).some(seat => seat.rfidTag === rfidTag)) {
        showProblems([Theme.plain('kiosk.cardAlreadyIn')]);
        return;
    }

    // Use the slot that asked for a card, or else the first free one.
    const stationId = listeningStationId || room.stations.map(s => s.id).find(id => !setup.seats[id] && !busy[id]);
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

    // A new card needs a name before it can continue, so open the keyboard straight away.
    if (!known) onScreenKeyboard?.showKeyboard(document.querySelector(`.keyboard-input[data-station-id="${stationId}"]`));
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

/** Categories toggle: tap to add, tap again to take it out. */
function chooseCategory(categoryId) {
    setup.categoryIds = setup.categoryIds.includes(categoryId)
        ? setup.categoryIds.filter(id => id !== categoryId)
        : [...setup.categoryIds, categoryId];
    renderStep();
}

/** "All categories" picks every one; with every one picked, the same button clears them. */
function chooseAllCategories() {
    setup.categoryIds = setup.categoryIds.length === categories.length ? [] : categories.map(c => c.id);
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
            seats.push({ stationId, playerName: seatName(stationId), playerProfileId });
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
            body: JSON.stringify({ mode: setup.mode, categoryIds: setup.categoryIds, difficulty: setup.difficulty, seats, teams }),
        });

        if (!response.ok) {
            // Our own problems list, or ASP.NET's validation errors (e.g. a field it couldn't read).
            const body = await response.json().catch(() => ({}));
            const problems = body.problems || Object.values(body.errors || {}).flat();
            throw new Error((problems.length ? problems : [body.message || `Could not start the match (${response.status}).`]).join(' '));
        }

        // They're off. Ready for the next group straight away (on whatever stations are still free).
        resetKiosk();
        showNotice(Theme.plain('kiosk.started'));
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

/** The name a seat plays under: what they typed, or the station's label for a guest who skipped it. */
function seatName(stationId) {
    return setup.seats[stationId].name.trim() || room.stations.find(s => s.id === stationId).label;
}

function showProblems(problems) {
    document.getElementById('problems').textContent = problems.join(' ');
}

/** A short, friendly message in the footer (e.g. "You're on!") that clears itself. */
function showNotice(text) {
    const notice = document.getElementById('notice');
    notice.textContent = text;
    notice.hidden = false;
    clearTimeout(showNotice.timer);
    showNotice.timer = setTimeout(() => { notice.hidden = true; }, 5000);
}
