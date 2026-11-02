// Insights tab: the design doc's indicators from the match history (GET /api/admin/insights). Time the
// attraction was in use, matches and seats filled, and for card holders (guests can't be followed): how
// long a visit lasts, how many matches it holds, and how many players came back. One row per day below.

const InsightsTab = {
    init() {
        document.getElementById('in-days').addEventListener('change', () => InsightsTab.load());
    },

    async load() {
        try {
            const days = document.getElementById('in-days').value;
            const r = await AdminApi.get(`/api/admin/insights?days=${days}`);
            const percent = (part, whole) => whole ? `${Math.round((part / whole) * 100)}%` : '–';
            const hours = minutes => minutes >= 60 ? `${(minutes / 60).toFixed(1)} h` : `${Math.round(minutes)} min`;

            const tile = (label, value, note = '') => `
                <div class="insight-tile"><b>${value}</b><span>${label}</span>${note ? `<small>${note}</small>` : ''}</div>`;
            document.getElementById('in-tiles').innerHTML = [
                tile('Time in use', hours(r.minutesInUse), 'any game running; side-by-side games count once'),
                tile('Matches', r.matches),
                tile('Seats filled', r.plays, `${r.guestPlays} by guests`),
                tile('Card holders', r.cardHolders),
                tile('Came back', `${r.returningCardHolders} (${percent(r.returningCardHolders, r.cardHolders)})`, 'played on more than one day'),
                tile('Average visit', hours(r.averageDwellMinutes), `${r.visits} visits by card holders`),
                tile('Matches per visit', r.averageMatchesPerVisit),
            ].join('');

            const busiest = Math.max(1, ...r.days.map(d => d.minutesInUse));
            document.getElementById('in-rows').innerHTML = r.days.length === 0
                ? '<tr><td colspan="4" class="dim">No matches in this period.</td></tr>'
                : r.days.slice().reverse().map(d => `
                    <tr>
                        <td>${new Date(`${d.date}T12:00:00`).toLocaleDateString(undefined, { weekday: 'short', day: 'numeric', month: 'short' })}</td>
                        <td>${d.matches}</td>
                        <td>${d.plays}</td>
                        <td><div class="insight-bar" style="width: ${(d.minutesInUse / busiest) * 60}%"></div>${hours(d.minutesInUse)}</td>
                    </tr>`).join('');
        } catch (err) {
            showError(err);
        }
    },
};
