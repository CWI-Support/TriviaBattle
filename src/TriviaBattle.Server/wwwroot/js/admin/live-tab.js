// Live tab: theme switcher, hardware health and running matches (with the answers, for the operator).
// Hardware and matches refresh every 2 seconds.

const LiveTab = {
    timer: null,

    init() {
        document.getElementById('l-theme').addEventListener('change', LiveTab.showPreviewLinks);
        document.getElementById('l-theme-apply').addEventListener('click', async () => {
            try {
                await AdminApi.put('/api/admin/display/theme', { theme: document.getElementById('l-theme').value });
                await LiveTab.loadThemes();
            } catch (err) {
                showError(err);
            }
        });
    },

    async loadThemes() {
        const { theme, themes } = await (await fetch('/api/display')).json();
        const select = document.getElementById('l-theme');
        select.innerHTML = themes.map(t => `<option value="${UI.escape(t)}">${UI.escape(t)}${t === theme ? ' (in use)' : ''}</option>`).join('');
        select.value = theme;
        LiveTab.showPreviewLinks();
    },

    /** Links that open a screen in the chosen theme without changing the room (?theme=...). */
    showPreviewLinks() {
        const theme = encodeURIComponent(document.getElementById('l-theme').value);
        const links = [['Main', '/display/main'], ['Player 1', '/display/player/1'], ['Kiosk', '/kiosk'], ['Leaderboard', '/display/leaderboard']]
            .map(([label, path]) => `<a href="${path}?theme=${theme}" target="_blank">${label}</a>`).join(' · ');
        document.getElementById('l-theme-preview').innerHTML = `Preview: ${links}`;
    },

    load() {
        LiveTab.loadThemes().catch(showError);
        LiveTab.refresh();
        clearInterval(LiveTab.timer);
        LiveTab.timer = setInterval(LiveTab.refresh, 2000);
    },

    leave() {
        clearInterval(LiveTab.timer);
    },

    async refresh() {
        try {
            const [hardware, matches] = await Promise.all([
                fetch('/api/hardware/status').then(r => r.json()),
                AdminApi.get('/api/matches'),
            ]);
            document.getElementById('l-hardware').innerHTML = LiveTab.renderHardware(hardware.plc);
            document.getElementById('l-matches').innerHTML = matches.length
                ? matches.map(LiveTab.renderMatch).join('')
                : '<p class="dim">No matches running.</p>';

            document.querySelectorAll('[data-abort]').forEach(button =>
                button.addEventListener('click', () => LiveTab.abort(button.dataset.abort)));
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
        const q = match.question;
        const question = q
            ? `<p>Q${match.roundNumber}/${match.totalRounds}: ${UI.escape(q.text)}<br>
               <span class="dim">Answer: ${UI.ANSWER_LETTERS[q.correctIndex]} &ndash; ${UI.escape(q.answers[q.correctIndex])}</span></p>`
            : '';
        const standings = match.standings.map(s => `${s.rank}. ${UI.escape(s.name)} ${s.score.toLocaleString()}`).join(' &middot; ');

        return `
            <div class="live-match">
                <header>
                    <strong>${UI.escape(match.modeName)} &middot; ${UI.escape(match.category)} &middot; ${UI.escape(match.difficulty)}</strong>
                    <span class="status-pill">${match.phase}</span>
                    <span class="spacer"></span>
                    <button class="danger" data-abort="${match.matchId}">Abort match</button>
                </header>
                ${question}
                <p>${standings}</p>
            </div>`;
    },

    async abort(matchId) {
        if (!confirm('Abort this match? Players will be sent back to the lobby and it won\'t count for leaderboards.')) return;
        try {
            await AdminApi.post(`/api/matches/${matchId}/abort`);
            await LiveTab.refresh();
        } catch (err) {
            showError(err);
        }
    },
};
