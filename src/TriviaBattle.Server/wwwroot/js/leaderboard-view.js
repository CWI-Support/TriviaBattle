// High-score boards, drawn from GET /api/leaderboards.
// Used by the main screen's lobby (between matches) and the stand-alone /display/leaderboard page.
//
//   LeaderboardView.startRotation(element)  -> cycles through every mode's board, today then all-time,
//                                              re-fetching each cycle so new scores show up on their own.

const LeaderboardView = {
    SECONDS_PER_BOARD: 8,
    PERIODS: [
        { id: 'Today', titleKey: 'lobby.leaderboardToday' },
        { id: 'AllTime', titleKey: 'lobby.leaderboardAllTime' },
    ],

    startRotation(element) {
        let slides = [];
        let index = 0;

        const showNext = async () => {
            if (index >= slides.length) {
                slides = await LeaderboardView.loadSlides();
                index = 0;
            }
            element.innerHTML = slides.length
                ? LeaderboardView.render(slides[index++])
                : `<p class="subtitle">${Theme.text('lobby.leaderboardEmpty')}</p>`;
        };

        showNext();
        setInterval(showNext, LeaderboardView.SECONDS_PER_BOARD * 1000);
    },

    /** Every non-empty board, for each period: [{ periodTitleKey, board }]. */
    async loadSlides() {
        const slides = [];
        try {
            for (const period of LeaderboardView.PERIODS) {
                const boards = await (await fetch(`/api/leaderboards?period=${period.id}`)).json();
                boards.filter(b => b.entries.length > 0)
                      .forEach(board => slides.push({ periodTitleKey: period.titleKey, board }));
            }
        } catch (err) {
            console.warn('Could not load leaderboards', err);
        }
        return slides;
    },

    render({ periodTitleKey, board }) {
        const rows = board.entries.map(entry => `
            <div class="score-row">
                <span class="rank">${UI.ordinal(entry.rank)}</span>
                <span>${UI.escape(entry.names)}<div class="members">${UI.escape(entry.category)} &middot; ${UI.escape(entry.difficulty)}</div></span>
                <span class="score">${entry.score.toLocaleString()}</span>
            </div>`).join('');

        return `
            <div class="leaderboard">
                <h2>${Theme.text(`modes.${board.mode}.name`)} &middot; ${Theme.text(periodTitleKey)}</h2>
                <div class="scoreboard">${rows}</div>
            </div>`;
    },
};
