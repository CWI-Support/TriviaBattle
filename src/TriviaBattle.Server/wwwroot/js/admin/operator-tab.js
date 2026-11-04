// Operator tab (/operator): what the venue staff change day to day. Room sound (volume sliders, speaker
// tests), game rules, theme, running matches (with the answers) and hardware health.
// Hardware and matches refresh every 2 seconds.

const OperatorTab = {
    timer: null,

    init() {
        document.getElementById('l-rules-penalty').addEventListener('change', OperatorTab.showPenaltyFields);
        document.getElementById('l-rules-format').addEventListener('change', OperatorTab.showSoloFields);
        document.getElementById('l-rules').addEventListener('submit', async event => {
            event.preventDefault();
            await OperatorTab.saveRules();
        });
        document.getElementById('l-theme').addEventListener('change', OperatorTab.showPreviewLinks);
        document.getElementById('l-theme-apply').addEventListener('click', async () => {
            try {
                await AdminApi.put('/api/admin/display/theme', { theme: document.getElementById('l-theme').value });
                await OperatorTab.loadThemes();
            } catch (err) {
                showError(err);
            }
        });
    },

    async loadRules() {
        const rules = await AdminApi.get('/api/admin/game-rules');
        document.getElementById('l-rules-seconds').value = rules.matchSeconds;
        document.getElementById('l-rules-penalty').value = rules.wrongAnswerPenalty;
        document.getElementById('l-rules-lockout').value = rules.lockoutSeconds;
        document.getElementById('l-rules-points').value = rules.wrongAnswerPoints;
        document.getElementById('l-rules-format').value = rules.format;
        document.getElementById('l-rules-solo-start').value = rules.clockStartSeconds;
        document.getElementById('l-rules-solo-kill').value = rules.secondsPerKill;
        document.getElementById('l-rules-solo-boss').value = rules.secondsPerBoss;
        document.getElementById('l-rules-solo-continue').value = rules.continueSeconds;
        document.getElementById('l-rules-solo-countdown').value = rules.continueCountdownSeconds;
        OperatorTab.showPenaltyFields();
        OperatorTab.showSoloFields();
    },

    async saveRules() {
        const status = document.getElementById('l-rules-status');
        try {
            await AdminApi.put('/api/admin/game-rules', {
                matchSeconds: Number(document.getElementById('l-rules-seconds').value),
                wrongAnswerPenalty: document.getElementById('l-rules-penalty').value,
                lockoutSeconds: Number(document.getElementById('l-rules-lockout').value),
                wrongAnswerPoints: Number(document.getElementById('l-rules-points').value),
                format: document.getElementById('l-rules-format').value,
                clockStartSeconds: Number(document.getElementById('l-rules-solo-start').value),
                secondsPerKill: Number(document.getElementById('l-rules-solo-kill').value),
                secondsPerBoss: Number(document.getElementById('l-rules-solo-boss').value),
                continueSeconds: Number(document.getElementById('l-rules-solo-continue').value),
                continueCountdownSeconds: Number(document.getElementById('l-rules-solo-countdown').value),
            });
            status.textContent = 'Saved. The next match uses these rules.';
        } catch (err) {
            showError(err);
        }
    },

    /** Only show the setting that goes with the chosen penalty. */
    showPenaltyFields() {
        const lockout = document.getElementById('l-rules-penalty').value === 'Lockout';
        document.getElementById('l-rules-lockout-field').hidden = !lockout;
        document.getElementById('l-rules-points-field').hidden = lockout;
        document.getElementById('l-rules-status').textContent = '';
    },

    /** The solo clock only exists in Dungeon Quiz Quest. Hidden fields are still saved as they are. */
    showSoloFields() {
        const dungeon = document.getElementById('l-rules-format').value === 'Dungeon';
        document.querySelectorAll('.l-rules-solo').forEach(field => field.hidden = !dungeon);
        document.getElementById('l-rules-status').textContent = '';
    },

    async loadThemes() {
        const { theme, themes } = await (await fetch('/api/display')).json();
        const select = document.getElementById('l-theme');
        select.innerHTML = themes.map(t => `<option value="${UI.escape(t)}">${UI.escape(t)}${t === theme ? ' (in use)' : ''}</option>`).join('');
        select.value = theme;
        OperatorTab.showPreviewLinks();
    },

    /** Links that open a screen in the chosen theme without changing the room (?theme=...). */
    showPreviewLinks() {
        const theme = encodeURIComponent(document.getElementById('l-theme').value);
        const links = [['Main', '/display/main'], ['Player 1', '/display/player/1'], ['Kiosk', '/kiosk'], ['Leaderboard', '/display/leaderboard']]
            .map(([label, path]) => `<a href="${path}?theme=${theme}" target="_blank">${label}</a>`).join(' · ');
        document.getElementById('l-theme-preview').innerHTML = `Preview: ${links}`;
    },

    /**
     * One slider per kind of sound, 0-200%. Changes apply live (the server ramps what's playing)
     * and are saved, so they survive a restart.
     */
    renderVolumes(volumes) {
        const sliders = [
            ['music', 'Room music'], ['attract', 'Attract'], ['stingers', 'Answer stingers'], ['intro', 'Intro sound'], ['sub', 'Sub'],
        ];
        const container = document.getElementById('l-volumes');
        container.innerHTML = sliders.map(([key, label]) => `
            <label class="volume-row">
                <span>${label}</span>
                <input type="range" min="0" max="200" step="5" value="${Math.round(volumes[key] * 100)}" data-volume="${key}">
                <span class="dim" data-volume-label="${key}">${Math.round(volumes[key] * 100)}%</span>
            </label>`).join('');

        let sendTimer = null;
        container.querySelectorAll('[data-volume]').forEach(slider => slider.addEventListener('input', () => {
            container.querySelector(`[data-volume-label="${slider.dataset.volume}"]`).textContent = `${slider.value}%`;
            clearTimeout(sendTimer);
            sendTimer = setTimeout(() => {
                const levels = Object.fromEntries([...container.querySelectorAll('[data-volume]')]
                    .map(s => [s.dataset.volume, Number(s.value) / 100]));
                AdminApi.put('/api/admin/audio/volumes', levels).catch(showError);
            }, 120); // while dragging, send at most every 120 ms
        }));
    },

    /** Each room's sound card status, with a test button per speaker (for checking the wiring). */
    async loadAudio() {
        const audio = await AdminApi.get('/api/admin/audio');
        const container = document.getElementById('l-audio');

        if (!audio.enabled) {
            document.getElementById('l-volumes').innerHTML = '';
            container.innerHTML = '<p><span class="status-pill">Audio off</span> Audio:Enabled is false in appsettings.json.</p>';
            return;
        }
        OperatorTab.renderVolumes(audio.volumes);
        if (audio.rooms.length === 0) {
            container.innerHTML = audio.notPlayingReason
                ? `<p><span class="status-pill bad">No sound</span> ${UI.escape(audio.notPlayingReason)} <button id="l-audio-retry">Refresh</button></p>`
                : '<p class="dim">Starting (decoding the sound files)… <button id="l-audio-retry">Refresh</button></p>';
            document.getElementById('l-audio-retry').addEventListener('click', () => OperatorTab.loadAudio().catch(showError));
            return;
        }

        const output = n => n === null || n === undefined ? '<span class="dim">none</span>' : n;
        container.innerHTML = audio.rooms.map(room => {
            const s = room.speakers;
            const rows = [
                ...s.stations.map(st => [st.label, st.output, st.stationId]),
                ['Room', s.room, 'room'], ['Attract', s.attract, 'attract'], ['Sub', s.sub, 'sub'],
            ];
            return `
                <p><b>Room ${UI.escape(room.roomId)}</b>: ${UI.escape(room.status)}</p>
                <table class="admin-table">
                    <tr><th>Speaker</th><th>Output</th><th></th></tr>
                    ${rows.map(([name, out, id]) => `
                        <tr><td>${UI.escape(name)}</td><td>${output(out)}</td>
                            <td>${out === null || out === undefined ? '' : `<button data-audio-test="${UI.escape(id)}" data-room="${UI.escape(room.roomId)}">Test</button>`}</td></tr>`).join('')}
                </table>`;
        }).join('') + `<p class="dim">Playback devices: ${audio.devices.map(UI.escape).join(' &middot; ') || 'none'}</p>`;

        container.querySelectorAll('[data-audio-test]').forEach(button => button.addEventListener('click', () =>
            AdminApi.post('/api/admin/audio/test', { roomId: button.dataset.room, speaker: button.dataset.audioTest }).catch(showError)));
    },

    load() {
        OperatorTab.loadRules().catch(showError);
        OperatorTab.loadAudio().catch(showError);
        OperatorTab.loadThemes().catch(showError);
        OperatorTab.refresh();
        clearInterval(OperatorTab.timer);
        OperatorTab.timer = setInterval(OperatorTab.refresh, 2000);
    },

    leave() {
        clearInterval(OperatorTab.timer);
    },

    async refresh() {
        try {
            const [hardware, matches] = await Promise.all([
                fetch('/api/hardware/status').then(r => r.json()),
                AdminApi.get('/api/matches'),
            ]);
            document.getElementById('l-hardware').innerHTML = OperatorTab.renderHardware(hardware.plc);
            document.getElementById('l-matches').innerHTML = matches.length
                ? matches.map(OperatorTab.renderMatch).join('')
                : '<p class="dim">No matches running.</p>';

            document.querySelectorAll('[data-abort]').forEach(button =>
                button.addEventListener('click', () => OperatorTab.abort(button.dataset.abort)));
            document.querySelectorAll('[data-continue]').forEach(button =>
                button.addEventListener('click', () => OperatorTab.giveContinue(button.dataset.continue)));
        } catch (err) {
            document.getElementById('l-matches').innerHTML = `<p class="problems">${UI.escape(err.message)}</p>`;
        }
    },

    renderHardware(plc) {
        if (!plc.enabled) {
            return '<p><span class="status-pill">PLC off</span> Using keyboard buttons and console lights (Plc:Enabled is false in config/plc.json).</p>';
        }
        const state = plc.isConnected
            ? `<span class="status-pill ok">PLC connected</span> since ${formatDate(plc.connectedSince)}`
            : `<span class="status-pill bad">PLC not connected</span> ${UI.escape(plc.lastError || '')} (${formatDate(plc.lastErrorAt)})`;
        return `<p>${state} &middot; ${UI.escape(plc.endpoint)} &middot; ${plc.pressesReceived} presses received` +
               `${plc.lastPressAt ? `, last at ${formatDate(plc.lastPressAt)}` : ''}</p>`;
    },

    renderMatch(match) {
        // Where each player/team is up to, with the answer to the question on their screen.
        const nameOf = ownerId => match.teams.find(t => t.id === ownerId)?.name ?? match.players.find(p => p.stationId === ownerId)?.name ?? ownerId;
        const streams = match.phase === 'Playing'
            ? `<ul>${match.streams.map(s => `
                <li><b>${UI.escape(nameOf(s.ownerId))}</b> (${s.correct}/${s.answered} right): Q${s.number} ${UI.escape(s.questionText)}
                    <span class="dim">&ndash; ${UI.escape(s.correctAnswer)}</span></li>`).join('')}</ul>`
            : '';
        const standings = match.standings.map(s => `${s.rank}. ${UI.escape(s.name)} ${s.score.toLocaleString()}`).join(' &middot; ');

        return `
            <div class="live-match">
                <header>
                    <strong>${UI.escape(match.modeName)} &middot; ${UI.escape(match.category)} &middot; ${UI.escape(match.difficulty)}</strong>
                    <span class="status-pill">${match.phase}</span>
                    <span class="spacer"></span>
                    ${match.phase === 'Continue' ? `<button class="primary" data-continue="${match.matchId}">Give a continue</button>` : ''}
                    <button class="danger" data-abort="${match.matchId}">Abort match</button>
                </header>
                ${streams}
                <p>${standings}</p>
            </div>`;
    },

    /** A free continue for a solo player on "Continue?" (a comp, or a swipe that didn't read). */
    async giveContinue(matchId) {
        try {
            await AdminApi.post(`/api/matches/${matchId}/continue`);
            await OperatorTab.refresh();
        } catch (err) {
            showError(err);
        }
    },

    async abort(matchId) {
        if (!confirm('Abort this match? Players will be sent back to the lobby and it won\'t count for leaderboards.')) return;
        try {
            await AdminApi.post(`/api/matches/${matchId}/abort`);
            await OperatorTab.refresh();
        } catch (err) {
            showError(err);
        }
    },
};
