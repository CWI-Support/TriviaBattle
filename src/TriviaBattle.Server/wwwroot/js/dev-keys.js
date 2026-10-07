// Dev/demo only: turns keyboard keys into button presses (see config/keyboard.json).
// Loaded by /dev/buttons, and by any screen opened with ?devkeys=1. That makes a one-window
// demo possible, since only the focused window receives keystrokes.

function enableDevKeys(connection, onPressed = () => {}) {
    document.addEventListener('keydown', event => {
        const typingInAField = event.target.matches('input, textarea, select');
        if (event.repeat || typingInAField || event.ctrlKey || event.altKey || event.metaKey) return;

        connection.pressKey(event.key)
            .then(wasMapped => { if (wasMapped) onPressed(event.key.toLowerCase()); })
            .catch(err => console.warn('Dev key press failed:', err));
    });
}

function devKeysRequested() {
    return new URLSearchParams(location.search).get('devkeys') === '1';
}
