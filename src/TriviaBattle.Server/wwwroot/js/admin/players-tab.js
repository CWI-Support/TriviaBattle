// Players tab: RFID player profiles (rename offensive names, forget cards).

const PlayersTab = {
    init() {
        let searchTimer;
        document.getElementById('p-search').addEventListener('input', () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => PlayersTab.load(), 300);
        });
    },

    async load() {
        try {
            const search = document.getElementById('p-search').value.trim();
            const players = await AdminApi.get(`/api/admin/players${search ? `?search=${encodeURIComponent(search)}` : ''}`);
            const tbody = document.getElementById('p-rows');

            tbody.innerHTML = players.length === 0
                ? '<tr><td colspan="5" class="dim">No players with cards yet.</td></tr>'
                : players.map(p => `
                    <tr data-id="${p.id}">
                        <td><input class="p-name" value="${UI.escape(p.displayName)}" maxlength="16"></td>
                        <td>${UI.escape(p.rfidTag)}</td>
                        <td>${formatDate(p.createdAt)}</td>
                        <td>${formatDate(p.lastSeenAt)}</td>
                        <td><button class="p-save">Save</button> <button class="p-delete danger">Forget card</button></td>
                    </tr>`).join('');

            tbody.querySelectorAll('tr[data-id]').forEach(row => {
                const id = row.dataset.id;
                row.querySelector('.p-save').addEventListener('click', () =>
                    AdminApi.put(`/api/admin/players/${id}`, { displayName: row.querySelector('.p-name').value })
                        .then(PlayersTab.load).catch(showError));
                row.querySelector('.p-delete').addEventListener('click', () => {
                    if (confirm('Forget this card? Their past scores stay on record; the card will be treated as new next time.'))
                        AdminApi.del(`/api/admin/players/${id}`).then(PlayersTab.load).catch(showError);
                });
            });
        } catch (err) {
            showError(err);
        }
    },
};
