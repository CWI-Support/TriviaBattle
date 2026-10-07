// Connects a screen (main, player, kiosk) to the game server and keeps it up to date.
//
// Usage:
//   const connection = new ScreenConnection({
//       role: 'player', stationNumber: 2,
//       onState: state => render(state),     // called with every new ScreenState
//       onEvent: event => animate(event),    // optional: one-off moments (AnswerLocked, ...)
//   });
//   connection.start();
//
// What it handles so the screen code doesn't have to:
//   - joining the right group for this screen, and re-joining after any reconnect
//   - reconnecting forever (venue screens must recover on their own)
//   - ignoring out-of-date states (each match state has a version number)
//   - clock sync: serverNow() gives the server's time, so countdowns match on every screen
//   - themes: loads the active theme before drawing, and redraws when the admin switches theme
//     (needs theme.js on the page)

class ScreenConnection {
    constructor({ role, stationNumber = null, onState, onEvent = () => {} }) {
        this.role = role;
        this.stationNumber = stationNumber;
        this.roomId = new URLSearchParams(location.search).get('room'); // null = server picks the first room
        this.onState = onState;
        this.onEvent = onEvent;

        this.clockOffsetMs = 0;  // server time minus local time
        this.lastMatchId = null;
        this.lastVersion = -1;
        this.lastState = null;
        this.queue = Promise.resolve(); // handles incoming messages strictly one after another

        this.hub = new signalR.HubConnectionBuilder()
            .withUrl('/gamehub')
            .withAutomaticReconnect({ nextRetryDelayInMilliseconds: () => 2000 }) // retry every 2s, forever
            .build();

        this.hub.on('State', state => this.receiveState(state));
        this.hub.on('Theme', name => this.inOrder(() => this.applyTheme(name)));
        this.hub.on('Event', event => this.onEvent(event));
        this.hub.onreconnecting(() => this.showStatus('Reconnecting…'));
        this.hub.onreconnected(() => this.joinAndSync());
    }

    async start() {
        // The first connection doesn't auto-retry in SignalR, so keep trying until the server is up.
        while (true) {
            try {
                await Theme.load();
                await this.hub.start();
                await this.joinAndSync();
                setInterval(() => this.syncClock(), 30000);
                return;
            } catch (err) {
                // Usually the server isn't up yet; a bad URL (e.g. unknown station) also lands here.
                this.showStatus('Waiting for server… ' + (err.message || ''));
                if (this.hub.state !== signalR.HubConnectionState.Disconnected) await this.hub.stop();
                await new Promise(resolve => setTimeout(resolve, 2000));
            }
        }
    }

    /** The current time on the server's clock, in ms since 1970. Use this for all countdowns. */
    serverNow() {
        return Date.now() + this.clockOffsetMs;
    }

    /** Dev only: simulate a physical button via the keyboard map (config/keyboard.json). */
    pressKey(key) {
        return this.hub.invoke('PressKey', key);
    }

    /** Dev only: this player screen presses one of its own station's buttons (0-3 = A-D). */
    pressStationButton(answerIndex) {
        return this.hub.invoke('PressStationButton', this.roomId, this.stationNumber, answerIndex);
    }

    async joinAndSync() {
        const state = await this.hub.invoke('Join', this.role, this.roomId, this.stationNumber);
        this.lastVersion = -1; // after a (re)join, always accept the fresh state
        this.receiveState(state);
        await this.syncClock();
        this.showStatus(null);
    }

    receiveState(state) {
        const match = state.match;
        if (match) {
            const sameMatch = match.matchId === this.lastMatchId;
            if (sameMatch && match.version <= this.lastVersion) return; // stale, a newer one already arrived
            this.lastMatchId = match.matchId;
            this.lastVersion = match.version;
        } else {
            this.lastMatchId = null;
            this.lastVersion = -1;
        }

        this.inOrder(async () => {
            await Theme.load(state.theme); // no-op unless the theme changed
            this.lastState = state;
            this.onState(state);
        });
    }

    /** The admin switched themes: load it and redraw what's on screen. */
    async applyTheme(name) {
        const changed = await Theme.load(name);
        if (changed && this.lastState) this.onState(this.lastState);
    }

    // Loading a theme takes a moment; this keeps messages from overtaking each other meanwhile.
    inOrder(work) {
        this.queue = this.queue.then(work).catch(err => console.error(err));
    }

    // Asks the server for its time a few times and keeps the most accurate answer
    // (the one with the shortest round trip), assuming the reply took half the round trip.
    async syncClock() {
        let best = null;
        for (let i = 0; i < 6; i++) {
            const sentAt = Date.now();
            const serverTime = await this.hub.invoke('Ping', sentAt);
            const receivedAt = Date.now();
            const roundTrip = receivedAt - sentAt;
            if (!best || roundTrip < best.roundTrip) {
                best = { roundTrip, offset: serverTime - (sentAt + roundTrip / 2) };
            }
        }
        this.clockOffsetMs = best.offset;
    }

    // Small banner in the corner when the screen isn't connected; null hides it.
    showStatus(message) {
        let banner = document.getElementById('connection-status');
        if (!banner) {
            banner = document.createElement('div');
            banner.id = 'connection-status';
            document.body.appendChild(banner);
        }
        banner.textContent = message || '';
        banner.hidden = !message;
    }
}
