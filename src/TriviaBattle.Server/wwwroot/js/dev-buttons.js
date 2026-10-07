// Dev buttons page: one pad of 4 buttons per station, wired to the keyboard map.
// Joins as a main screen just to show what the match is doing.

let keyMap = []; // [{ key, buttonId, stationId, answer }]

const connection = new ScreenConnection({ role: 'main', onState: render });
connection.start().then(loadKeyMap);
enableDevKeys(connection, flashKey);

async function loadKeyMap() {
    const response = await fetch('/api/dev/keyboard');
    if (!response.ok) {
        document.getElementById('pads').innerHTML = '<p class="problems">Dev tools are turned off (DevTools:Enabled).</p>';
        return;
    }
    keyMap = await response.json();
    renderPads();
}

let lastState = null;
function render(state) {
    lastState = state;
    const match = state.match;
    document.getElementById('match-status').textContent = match
        ? `${match.modeName} · ${match.phase} · question ${match.roundNumber}/${match.totalRounds}`
        : 'No match running. Start one from the kiosk.';
    if (keyMap.length) renderPads();
}

function renderPads() {
    const stations = lastState?.room.stations || [];
    document.getElementById('pads').innerHTML = stations.map(station => {
        const buttons = keyMap
            .filter(k => k.stationId === station.id)
            .map(k => `
                <button class="dev-button" data-letter="${k.answer}" data-key="${UI.escape(k.key)}"
                        onclick="pressKey('${UI.escape(k.key)}')">
                    ${k.answer}<small>key ${UI.escape(k.key.toUpperCase())} &middot; #${k.buttonId}</small>
                </button>`)
            .join('');
        return `
            <div class="dev-pad" style="--chip-color: ${station.color}">
                <h3>${UI.escape(station.label)}</h3>
                <div class="dev-buttons">${buttons}</div>
            </div>`;
    }).join('');
}

function pressKey(key) {
    connection.pressKey(key).then(() => flashKey(key));
}

function flashKey(key) {
    const button = document.querySelector(`.dev-button[data-key="${CSS.escape(key)}"]`);
    if (!button) return;
    button.classList.add('is-pressed');
    setTimeout(() => button.classList.remove('is-pressed'), 150);
}
