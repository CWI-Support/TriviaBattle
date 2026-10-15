// Small helpers shared by every screen. Plain DOM, no framework.

const UI = {
    ANSWER_LETTERS: ['A', 'B', 'C', 'D'],

    /** The element fitToScreen last sized, so it can be re-fitted on resize. */
    lastFitted: null,

    /** Server-clock source for countdowns, set by startCountdowns. */
    getServerNow: null,

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

    /**
     * Shrinks an element's text until its content fits inside it, so long questions, pictures or a
     * landscape player screen never push answers off the bottom. Lowers the --fit CSS variable,
     * which the sizes in screens.css are multiplied by. Call it after the element is drawn and visible.
     * It runs again when a question picture finishes loading, and on window resize.
     */
    fitToScreen(element, smallestScale = 0.45) {
        if (!element) return;
        UI.lastFitted = element;
        UI.updateCountdowns(); // a countdown is empty until its first tick, and the number adds height

        const fitsAt = scale => {
            element.style.setProperty('--fit', scale);
            return element.scrollHeight <= element.clientHeight + 1;
        };

        if (!fitsAt(1)) {
            // Binary search for the biggest scale that fits: about 6 layouts, no visible flicker.
            let fits = smallestScale, tooBig = 1;
            for (let i = 0; i < 6; i++) {
                const middle = (fits + tooBig) / 2;
                if (fitsAt(middle)) fits = middle; else tooBig = middle;
            }
            fitsAt(fits);
        }

        // A picture still loading has no height yet: fit again once it has one.
        element.querySelectorAll('img, video').forEach(media => {
            const loaded = media.tagName === 'VIDEO' ? media.readyState >= 1 : media.complete;
            if (!loaded) {
                media.addEventListener(media.tagName === 'VIDEO' ? 'loadedmetadata' : 'load',
                    () => UI.fitToScreen(element, smallestScale), { once: true });
            }
        });
    },

    /** "1st", "2nd", "3rd", "4th"... */
    ordinal(n) {
        const suffix = (n % 100 >= 11 && n % 100 <= 13) ? 'th' : ({ 1: 'st', 2: 'nd', 3: 'rd' }[n % 10] || 'th');
        return n + suffix;
    },

    /**
     * Keeps countdowns ticking without re-rendering. Any element on the page can opt in:
     *   <span data-ends-at="1730000000000"></span>         -> shows whole seconds left
     *   <span data-ends-at=".." data-hurry-at="10"></span> -> ...and gets class "is-hurry" for the last 10
     *   <div class="timer-bar" data-starts-at=".." data-ends-at="..">
     *       <div class="timer-bar-fill"></div></div>     -> fill shrinks as time runs out
     * Times are server-clock milliseconds; getServerNow converts.
     */
    startCountdowns(getServerNow) {
        UI.getServerNow = getServerNow;
        setInterval(UI.updateCountdowns, 100);
    },

    /** Brings every countdown on the page up to date (see startCountdowns). */
    updateCountdowns() {
        if (!UI.getServerNow) return;
        const now = UI.getServerNow();

        document.querySelectorAll('[data-ends-at]:not(.timer-bar)').forEach(el => {
            const secondsLeft = Math.max(0, Math.ceil((Number(el.dataset.endsAt) - now) / 1000));
            el.textContent = secondsLeft;
            if (el.dataset.hurryAt) el.classList.toggle('is-hurry', secondsLeft <= Number(el.dataset.hurryAt));
        });

        document.querySelectorAll('.timer-bar[data-ends-at]').forEach(bar => {
            const start = Number(bar.dataset.startsAt);
            const end = Number(bar.dataset.endsAt);
            const fractionLeft = Math.min(1, Math.max(0, (end - now) / (end - start)));
            bar.querySelector('.timer-bar-fill').style.width = (fractionLeft * 100) + '%';
            bar.classList.toggle('timer-bar-low', fractionLeft < 0.25);
        });
    },
};

// Re-fit when the window changes size, and once the display font has loaded (it changes text widths).
window.addEventListener('resize', () => UI.fitToScreen(UI.lastFitted));
document.fonts?.ready.then(() => UI.fitToScreen(UI.lastFitted));
