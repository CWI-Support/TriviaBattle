// Demo/testing without the physical buttons: a player screen acts as its own 4-button keypad.
//
//   keys 1 2 3 4  (top row or numpad)  or  A B C D   -> this station's answers A-D
//   tapping / clicking an answer on the screen        -> the same
//   Space                                             -> a card swipe at this station (takes a continue
//                                                        when "Continue?" is up), like its card reader
//
// Presses go through the server's normal button path (button id -> ButtonRouter), so the game
// can't tell them apart from the real buttons. Only switched on when the server has dev tools
// enabled (the default when running with `dotnet run`); on a production server this does nothing.

/** stationId: a function giving this station's id (e.g. 'A1') once the screen knows it. */
async function enableStationKeypad(connection, stationId) {
    const { devTools } = await (await fetch('/api/display')).json();
    if (!devTools) return;

    document.body.classList.add('keypad-enabled');

    const press = answerIndex =>
        connection.pressStationButton(answerIndex).catch(err => console.warn('Keypad press failed:', err));

    // The same call as the Swipe button on /dev/buttons. Nothing on offer: the server says no, and that's fine.
    const swipe = async () => {
        if (!stationId()) return;
        const response = await fetch(`/api/dev/continue/${encodeURIComponent(stationId())}`, { method: 'POST' });
        if (!response.ok) console.info('Swipe: no continue on offer at this station right now.');
    };

    const KEY_TO_ANSWER = { '1': 0, '2': 1, '3': 2, '4': 3, a: 0, b: 1, c: 2, d: 3 };
    const NUMPAD_TO_ANSWER = { Numpad1: 0, Numpad2: 1, Numpad3: 2, Numpad4: 3 }; // works with NumLock off too

    document.addEventListener('keydown', event => {
        if (event.repeat || event.ctrlKey || event.altKey || event.metaKey) return;
        if (event.code === 'Space') {
            event.preventDefault(); // no page scroll
            swipe();
            return;
        }
        const answer = NUMPAD_TO_ANSWER[event.code] ?? KEY_TO_ANSWER[event.key.toLowerCase()];
        if (answer !== undefined) press(answer);
    });

    document.addEventListener('click', event => {
        const answer = event.target.closest('.answer[data-letter]');
        if (answer) press(UI.ANSWER_LETTERS.indexOf(answer.dataset.letter));
    });
}
