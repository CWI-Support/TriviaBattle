// The 9:16 frame around a station screen (screens/portrait-frame.html).
// The real page is this address plus /screen, keeping the query (?room=, ?view=2d, ?camera=...), so
// /display/player/2?room=B shows /display/player/2/screen?room=B. That page can also be opened directly
// to see it fill the window.

const frame = document.getElementById('screen');
frame.src = location.pathname.replace(/\/$/, '') + '/screen' + location.search;

// The demo keypad (js/station-keypad.js) listens inside the frame, so keep the keyboard there: after it
// loads, and after a click on the black bars.
frame.addEventListener('load', () => {
    frame.focus();
    document.title = frame.contentDocument?.title || document.title;
});
document.addEventListener('mousedown', () => setTimeout(() => frame.focus()));

// A key that reaches this page anyway (the frame lost focus) is passed in, so no press goes missing.
document.addEventListener('keydown', event => {
    const inner = frame.contentDocument;
    if (!inner) return;
    inner.dispatchEvent(new KeyboardEvent('keydown', {
        key: event.key, code: event.code, repeat: event.repeat,
        ctrlKey: event.ctrlKey, altKey: event.altKey, metaKey: event.metaKey, shiftKey: event.shiftKey,
        bubbles: true, cancelable: true,
    }));
    frame.focus();
});
