// RFID card reader support for the kiosk.
//
// Assumes a USB "keyboard wedge" reader: swiping a card types the card id very quickly and
// then presses Enter. We tell a swipe apart from a person typing by speed: a reader's keys
// arrive a few milliseconds apart, a human's much slower.
//
// If the venue's reader works differently (e.g. serial port), only this file needs to change.

const CARD_READER = {
    MAX_GAP_MS: 60,      // longer pause between keys = a person typing, start over
    MIN_LENGTH: 4,       // ignore very short bursts
};

function listenForCardSwipes(onSwipe) {
    let buffer = '';
    let lastKeyAt = 0;

    document.addEventListener('keydown', event => {
        // Let people type into fields normally (names on a desktop keyboard).
        if (event.target.matches('input, textarea')) return;

        const now = Date.now();
        if (now - lastKeyAt > CARD_READER.MAX_GAP_MS) buffer = '';
        lastKeyAt = now;

        if (event.key === 'Enter') {
            if (buffer.length >= CARD_READER.MIN_LENGTH) onSwipe(buffer);
            buffer = '';
        } else if (event.key.length === 1) {
            buffer += event.key;
        }
    });
}
