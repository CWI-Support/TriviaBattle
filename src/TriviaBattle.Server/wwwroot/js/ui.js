// Small helpers shared by every screen. Plain DOM, no framework.

const UI = {
    ANSWER_LETTERS: ['A', 'B', 'C', 'D'],

    /** Escapes text for safe use in innerHTML and attribute values (player names are typed by the public!). */
    escape(text) {
        const replacements = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };
        return String(text ?? '').replace(/[&<>"']/g, char => replacements[char]);
    },

    /** Shows the element with this id and hides every other `.view` on the page. */
    showView(viewId) {
        document.querySelectorAll('.view').forEach(view => {
            view.hidden = view.id !== viewId;
        });
    },

    /** A question's picture or video (set in the admin editor), or '' if it has none. Videos loop silently. */
    questionMedia(url) {
        if (!url) return '';
        const safeUrl = UI.escape(url);
        return /\.(mp4|webm)$/i.test(url)
            ? `<video class="question-media" src="${safeUrl}" autoplay muted loop playsinline></video>`
            : `<img class="question-media" src="${safeUrl}" alt="">`;
    },

    /** "1st", "2nd", "3rd", "4th"... */
    ordinal(n) {
        const suffix = (n % 100 >= 11 && n % 100 <= 13) ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' }[n % 10] || 'th');
        return n + suffix;
    },

    /**
     * Keeps countdowns ticking without re-rendering. Any element on the page can opt in:
     *   <span data-ends-at="1730000000000"></span>         -> shows whole seconds left
     *   <div class="timer-bar" data-starts-at=".." data-ends-at="..">
     *       <div class="timer-bar-fill"></div></div>     -> fill shrinks as time runs out
     * Times are server-clock milliseconds; getServerNow converts.
     */
    startCountdowns(getServerNow) {
        setInterval(() => {
            const now = getServerNow();

            document.querySelectorAll('[data-ends-at]:not(.timer-bar)').forEach(el => {
                const secondsLeft = Math.max(0, Math.ceil((Number(el.dataset.endsAt) - now) / 1000));
                el.textContent = secondsLeft;
            });

            document.querySelectorAll('.timer-bar[data-ends-at]').forEach(bar => {
                const start = Number(bar.dataset.startsAt);
                const end = Number(bar.dataset.endsAt);
                const fractionLeft = Math.min(1, Math.max(0, (end - now) / (end - start)));
                bar.querySelector('.timer-bar-fill').style.width = (fractionLeft * 100) + '%';
                bar.classList.toggle('timer-bar-low', fractionLeft < 0.25);
            });
        }, 100);
    },
};
