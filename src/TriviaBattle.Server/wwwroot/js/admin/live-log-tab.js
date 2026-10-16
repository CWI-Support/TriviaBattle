// Live log tab: is anything going wrong right now? Shows which screens are connected (a missing one is
// red) and the server log as it happens, newest first: matches, buttons, sound, rejected requests, and
// errors the screens themselves report. Refreshes every second while the tab is open.

const LiveLogTab = {
    timer: null,
    serverId: null,
    lastId: 0,
    entries: [],   // newest last, at most MAX_ENTRIES
    MAX_ENTRIES: 500,

    init() {
        ['ll-level', 'll-search'].forEach(id => document.getElementById(id).addEventListener('input', () => LiveLogTab.renderEntries()));
        document.getElementById('ll-clear').addEventListener('click', () => {
            LiveLogTab.entries = [];
            LiveLogTab.renderEntries();
        });
    },

    load() {
        LiveLogTab.refresh();
        clearInterval(LiveLogTab.timer);
        LiveLogTab.timer = setInterval(LiveLogTab.refresh, 1000);
    },

    leave() {
        clearInterval(LiveLogTab.timer);
    },

    async refresh() {
        if (document.getElementById('ll-pause').checked) return;
        try {
            const data = await AdminApi.get(`/api/admin/live-log?after=${LiveLogTab.lastId}`);
            if (data.serverId !== LiveLogTab.serverId) {
                // First load, or the server restarted (its line numbers start again): take everything it has.
                const restarted = LiveLogTab.serverId !== null;
                LiveLogTab.serverId = data.serverId;
                LiveLogTab.lastId = 0;
                if (restarted) {
                    LiveLogTab.entries.push({ id: 0, time: new Date().toISOString(), level: 'Warning', source: 'Live log', message: 'The server restarted.' });
                    LiveLogTab.refresh();
                    return;
                }
            }
            document.getElementById('ll-status').textContent = '';
            LiveLogTab.renderScreens(data.screens);
            if (data.entries.length) {
                LiveLogTab.entries.push(...data.entries);
                LiveLogTab.entries.splice(0, Math.max(0, LiveLogTab.entries.length - LiveLogTab.MAX_ENTRIES));
                LiveLogTab.lastId = data.lastId;
                LiveLogTab.renderEntries();
            }
        } catch (err) {
            document.getElementById('ll-status').textContent = `Can't reach the server: ${err.message}`;
        }
    },

    renderScreens(screens) {
        const rooms = [...new Set(screens.map(s => s.room))];
        document.getElementById('ll-screens').innerHTML = screens.map(s => {
            const count = s.connections.length;
            const detail = count === 0
                ? 'Not connected'
                : s.connections.map(c => `${UI.escape(c.address || '?')} since ${new Date(c.connectedAt).toLocaleTimeString()}`).join(', ');
            const room = rooms.length > 1 ? `${UI.escape(s.room)} · ` : '';
            return `
                <div class="screen-chip ${count ? (count > 1 ? 'is-warning' : 'is-ok') : 'is-missing'}" title="${count > 1 ? 'Open in more than one place' : ''}">
                    <b>${room}${UI.escape(s.name)}</b>
                    <span>${detail}</span>
                </div>`;
        }).join('');
    },

    renderEntries() {
        const minimum = document.getElementById('ll-level').value;
        const search = document.getElementById('ll-search').value.trim().toLowerCase();
        const levels = ['Verbose', 'Debug', 'Information', 'Warning', 'Error', 'Fatal'];

        const shown = LiveLogTab.entries
            .filter(e => levels.indexOf(e.level) >= levels.indexOf(minimum))
            .filter(e => !search || `${e.source} ${e.message} ${e.exception || ''}`.toLowerCase().includes(search))
            .slice().reverse();

        document.getElementById('ll-rows').innerHTML = shown.map(e => `
            <tr class="log-${e.level.toLowerCase()}">
                <td class="nowrap">${new Date(e.time).toLocaleTimeString()}</td>
                <td>${e.level === 'Information' ? 'Info' : UI.escape(e.level)}</td>
                <td class="dim">${UI.escape(e.source)}</td>
                <td>${UI.escape(e.message)}${e.exception ? `<details><summary>Details</summary><pre>${UI.escape(e.exception)}</pre></details>` : ''}</td>
            </tr>`).join('') || '<tr><td colspan="4" class="dim">Nothing yet.</td></tr>';

        const problems = LiveLogTab.entries.filter(e => e.level === 'Warning' || e.level === 'Error' || e.level === 'Fatal').length;
        document.getElementById('ll-count').textContent = `${LiveLogTab.entries.length} lines, ${problems} warnings/errors`;
    },
};
