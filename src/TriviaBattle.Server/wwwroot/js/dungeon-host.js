// The Host on the main screen (Dungeon format): Q.U.E.R.I.E., the charismatic, unhinged AI that runs
// the dungeon (docs/DUNGEON-QUIZ-QUEST-experience-design.pdf: think a game-show genie who secretly hates
// you). A portrait and a speech bubble at the foot of the room screen. When something big happens (a
// boss slain, a floor cleared, a revive, a party combo) he has something to say about it for a few
// seconds; the rest of the time he fills the quiet with an idle quip that changes every few seconds.
// Roast the play, never the person. All his lines are in copy.json ("dungeon.host*"), so a theme (or
// a licensed IP) can give the dungeon a different host. The portrait is a placeholder pixel sprite.
// Needs ui.js, theme.js and pixel-sprites.js on the page. Styles: css/dungeon-host.css.

const DungeonHost = {
    said: null, // { html, at }: his comment on the last big moment

    /** How long he talks about a moment before going back to idle chatter, and how often idle lines change. */
    MOMENT_MS: 5000,
    IDLE_MS: 9000,

    /** He comments on a big moment (the line is already filled in and escaped). */
    say(html) {
        DungeonHost.said = { html, at: Date.now() };
    },

    /** The portrait and the bubble. */
    box() {
        const now = Date.now();
        const talking = DungeonHost.said && now - DungeonHost.said.at < DungeonHost.MOMENT_MS;
        const line = talking ? DungeonHost.said.html : Theme.text('dungeon.hostIdle', {}, `idle:${Math.floor(now / DungeonHost.IDLE_MS)}`);
        return `
            <div class="dg-host ${talking ? 'is-talking' : ''}">
                ${PixelSprites.html('host-querie', 'dg-host-face')}
                <div class="dg-host-bubble">
                    <div class="dg-host-name">${Theme.text('dungeon.hostName')}</div>
                    <div class="dg-host-line" style="--t0: ${talking ? DungeonHost.said.at - now : 0}ms">${line}</div>
                </div>
            </div>`;
    },

    /** Forget the last comment (a new match). */
    reset() {
        DungeonHost.said = null;
    },
};

// Q.U.E.R.I.E.: an old CRT monitor with a face, antennae and a glitchy grin. A placeholder until the art exists.
Object.assign(PixelSprites.SPRITES, {
    'host-querie': {
        palette: { k: '#0b0710', c: '#2ef2ff', m: '#ff3df2', g: '#3a3346', G: '#5a5068', w: '#ffffff', y: '#ffe04a' },
        rows: [
            '...m..........m...',
            '....m........m....',
            '.....m......m.....',
            '..GGGGGGGGGGGGGG..',
            '.GgggggggggggggG..',
            '.Ggkkkkkkkkkkkgg..',
            '.Ggkkwwkkkkwwkkg..',
            '.GgkwcckkkkwcckgGG',
            '.GgkwcckkkkwcckgGy',
            '.Ggkkkkkkkkkkkkgg.',
            '.Ggkmkkkkkkkkmkgg.',
            '.Ggkkmmmmmmmmkkgg.',
            '.Ggkkkcwcwcwkkkgg.',
            '.Ggkkkkkkkkkkkkgg.',
            '.GgggggggggggggGG.',
            '..GGGGGGGGGGGGGG..',
            '.....GGGGGGGG.....',
            '...GGGGGGGGGGGG...',
        ],
    },
});
