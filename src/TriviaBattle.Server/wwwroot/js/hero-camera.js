// Hero photos on the station (Dungeon format): the camera. Two places take one:
//   - the lobby, right after choosing a hero (the Photo step: lobby seat's photoTakeAtMs; none while a venue
//     that asks first is still asking, js/arcade-lobby.js shows the question). The camera comes
//     on with an outline to put your face in, counts "3, 2, 1" and takes it; then you see your hero wearing
//     your face (the "3, 2, 1" and the shutter come from the station's speaker once it says its camera is on:
//     POST .../lobby/{stationId}/camera). ACTION keeps it, B takes another, D goes on without. It's sent to the server
//     (PUT /api/photos/lobby/{stationId}) and goes into your game when it starts.
//   - a kiosk game's intro (you.photoAtMs): by default everyone's, unless they press ACTION to skip; a venue
//     can ask first instead ("Put your face on your hero? Press ACTION", you.photoOfferUntilMs). The server
//     plays a "3, 2, 1" on the station's speaker once the camera is on (POST .../camera). Sent with
//     PUT /api/photos/{matchId}/{stationId}.
// Either way the face is found and cut out (js/face-cutout.js), the hero is drawn wearing it
// (js/battle3d/portrait.js), and the camera goes off straight away. If no face can be found, nothing is
// sent. The rules are on the server: this only does what the snapshot says.
//
// The station PC needs a camera, and the page must be allowed to use it: browsers only give cameras to
// https or localhost pages (the server has an https address for this: README, "Hero photos"). When one PC
// drives several stations, each station's page names its own camera with ?camera=<part of its name>.
// Without a camera there's no photo step and nothing is heard. Whenever a photo can't be taken, the station
// says why in the server's log (and the admin's Live log): not an https/localhost page, no camera, the
// camera refused or busy, no face found.
// Needs ui.js, theme.js and face-cutout.js on the page. Styles: css/hero-photo.css.

const HeroCamera = {
    WARM_UP_MS: 800,   // a camera's first frames are dark while it sets its exposure
    SEARCH_MS: 2500,   // how long to keep looking for a face after the countdown
    COUNT_FROM: 3,     // the digits show for the last seconds only

    checked: false,    // the camera check has finished
    hasCamera: false,
    noCamera: null,    // why there's no camera, once checked (for the log, and the hint on an insecure page)
    report: () => {},  // tells the server's log why a photo couldn't be taken
    taken: null,       // `${matchId}/${stationId}` of the intro photo taken (or tried), so it's only done once
    running: null,     // the intro's { key, cancelled } while its camera window is up
    lobbyRun: null,    // the lobby's { takeAtMs, cancelled } while its camera window is up
    skipped: false,    // this station (with no camera) has already skipped the lobby photo step it's on
    stream: null,      // the camera, while it's on

    /**
     * Finds out once whether this PC has a camera the page may use; then starts loading the face finder and
     * redraws. report(text) puts a line in the server's log (the station's connection.reportError).
     */
    async check(redraw, report) {
        HeroCamera.report = text => report(`Hero photo: ${text}`);
        if (!window.isSecureContext) {
            HeroCamera.noCamera = 'this page isn\'t https or localhost, so the browser won\'t give it the camera';
        } else {
            try {
                const devices = await navigator.mediaDevices.enumerateDevices();
                HeroCamera.hasCamera = devices.some(d => d.kind === 'videoinput');
                if (!HeroCamera.hasCamera) HeroCamera.noCamera = 'no camera found on this PC';
            } catch (err) {
                HeroCamera.noCamera = `the browser won't list cameras (${err.name})`;
            }
        }
        HeroCamera.checked = true;
        redraw();
        if (HeroCamera.hasCamera) FaceCutout.load().catch(err => HeroCamera.report(`the face finder didn't load: ${err.message || err}`));
    },

    /** On an http page away from localhost: a quiet line in the intro, so a test session knows why there's no photo. */
    hint(you) {
        if (!you?.photoAtMs || window.isSecureContext) return '';
        return `<p class="photo-hint">${Theme.text('dungeon.photoNoHttps')}</p>`;
    },

    /** The server's https address for this same screen, once known (an http station page can switch to it). */
    secureUrl: null,

    /**
     * A station page opened over http by the server's network address can't use its camera. If the server
     * has an https address (GET /api/display: httpsPort), go there straight away when this browser already
     * trusts it; otherwise offer it (showSecureOffer), since the first visit needs a click through the
     * certificate warning. Call once when the page starts.
     */
    async goSecure() {
        if (window.isSecureContext) return;
        try {
            const { httpsPort } = await (await fetch('/api/display')).json();
            if (!httpsPort) return;
            // The station page sits in a 9:16 frame (js/portrait-frame.js). The frame's page must switch too:
            // a page counts as secure only when the page around it is.
            const page = window.top.location;
            const origin = `https://${page.hostname}:${httpsPort}`;
            HeroCamera.secureUrl = origin + page.pathname + page.search;
            await fetch(`${origin}/api/display`, { mode: 'no-cors' }); // fails until this browser trusts the address
            page.replace(HeroCamera.secureUrl);
        } catch {
            if (HeroCamera.secureUrl) HeroCamera.showSecureOffer(); // not trusted yet (or not reachable)
        }
    },

    /**
     * A bar along the bottom of a station page that can't use its camera: a link to the https address, and
     * what to click there. Kept outside the screen's redrawn HTML, so it never changes the game's layout.
     */
    showSecureOffer() {
        const bar = document.createElement('a');
        bar.className = 'photo-secure';
        bar.href = HeroCamera.secureUrl;
        bar.target = '_top'; // the whole page, frame and all (see goSecure)
        bar.innerHTML = `<b>${Theme.text('dungeon.photoSecureButton')}</b><small>${Theme.text('dungeon.photoSecureSteps')}</small>`;
        document.body.appendChild(bar);
    },

    // ------------------------------------------------------------ The lobby: right after choosing a hero

    /**
     * Call on every redraw, with this station's lobby seat (null in a game). On the Photo step this station
     * takes its photo (a new one for each retake); leaving the step (kept, skipped, or the player walked
     * away) closes the camera window. A station without a camera skips the step.
     */
    followLobby(seat, clock, color) {
        const onStep = seat?.step === 'Photo';
        const takeAtMs = onStep ? seat.photoTakeAtMs : null;
        if (HeroCamera.lobbyRun && HeroCamera.lobbyRun.takeAtMs !== takeAtMs) {
            HeroCamera.lobbyRun.cancelled = true;
            HeroCamera.lobbyRun = null;
            HeroCamera.close();
        }
        if (!onStep) HeroCamera.skipped = false; // the next visit to the step may skip again
        if (!onStep || !HeroCamera.checked) return;

        // No camera here: skip the step as soon as it starts (asked first too: a yes would get nothing). The
        // server refuses if another screen open as this station has a camera; that one takes the photo.
        if (!HeroCamera.hasCamera) {
            if (HeroCamera.skipped) return;
            HeroCamera.skipped = true;
            if (HeroCamera.noCamera) HeroCamera.report(`none at ${seat.stationId} (lobby): ${HeroCamera.noCamera}`);
            fetch(`/api/photos/lobby/${encodeURIComponent(seat.stationId)}/skip`, { method: 'POST' }).catch(() => {});
            return;
        }
        // Still asking; or this take's photo is already in (a reloaded page shows it rather than taking another).
        if (!takeAtMs || HeroCamera.lobbyRun || seat.photo > 0) return;
        HeroCamera.lobbyRun = { takeAtMs, cancelled: false };
        HeroCamera.runLobby(HeroCamera.lobbyRun, seat, clock, color);
    },

    /** The lobby's photo: taken, shown on the hero, sent; the window stays up until the player keeps it, retakes or skips. */
    async runLobby(run, seat, clock, color) {
        const box = HeroCamera.open(Theme.text('arcade.photoTitle'), Theme.text('arcade.photoKeysTaking'));
        try {
            const face = await HeroCamera.shoot(box, run, run.takeAtMs, clock, () =>
                fetch(`/api/photos/lobby/${encodeURIComponent(seat.stationId)}/camera`, { method: 'POST' }).catch(() => {})); // the server's "3, 2, 1"
            if (run.cancelled) return;
            if (!face) {
                HeroCamera.report(`none at ${seat.stationId} (lobby): no face found in the camera picture`);
                HeroCamera.failed(box, Theme.text('dungeon.photoNoFace'), Theme.text('arcade.photoKeysFailed'));
                return;
            }
            const portrait = await HeroCamera.portraitOf(face, { color, hero: seat.hero });
            if (run.cancelled) return;
            HeroCamera.reveal(box, portrait, Theme.text('dungeon.photoDone', {}, `${seat.stationId}:${run.takeAtMs}`));
            await HeroCamera.send(`/api/photos/lobby/${encodeURIComponent(seat.stationId)}`, face, portrait, { takeAtMs: run.takeAtMs });
            // What to press now, once the server has it (a keep pressed before it lands wouldn't count).
            if (!run.cancelled) box.querySelector('.photo-skip').innerHTML = Theme.text('arcade.photoKeysTaken');
        } catch (err) {
            if (run.cancelled) return;
            HeroCamera.report(`failed at ${seat.stationId} (lobby): ${err.name || 'Error'}: ${err.message || err}`);
            HeroCamera.failed(box, Theme.text('dungeon.photoFailed'), Theme.text('arcade.photoKeysFailed'));
        }
    },

    // ------------------------------------------------------------ A kiosk game's intro

    /** Photos asked for first: the offer, for the intro screen. Nothing without a camera, or once it has passed (it fades out then by itself). */
    offer(you, serverNow) {
        if (!HeroCamera.hasCamera || !(you.photoOfferUntilMs > serverNow)) return '';
        return `
            <div class="photo-offer" style="${UI.animateAt(you.photoOfferUntilMs)}">
                <div class="photo-offer-title">${Theme.text('dungeon.photoOffer')}</div>
                <div class="photo-offer-press">${Theme.text('dungeon.photoPress')}</div>
            </div>`;
    },

    /**
     * Call on every redraw of a game. When a photo is coming, takes it (once per match); when the player has
     * said no (photoAtMs gone), stops. Not after a reload of the page once it has arrived, or long after its moment.
     */
    follow(match, you, clock, color) {
        if (!you) return;
        const key = `${match.matchId}/${you.stationId}`;
        if (HeroCamera.running?.key === key && !you.photoAtMs)
            HeroCamera.running.cancelled = true;

        const player = match.players.find(p => p.stationId === you.stationId);
        if (!you.photoAtMs || HeroCamera.taken === key || player?.photo > 0 || clock() - you.photoAtMs > 10000) return;
        HeroCamera.taken = key;
        if (!HeroCamera.hasCamera) {
            if (HeroCamera.noCamera) HeroCamera.report(`none at ${you.stationId}: ${HeroCamera.noCamera}`);
            return;
        }
        HeroCamera.run(match.matchId, you.stationId, you.photoAtMs, clock, { color, hero: player?.hero });
    },

    /** The intro's photo: taken, shown on the hero for a moment, sent. look: the station's colour and the player's hero. */
    async run(matchId, stationId, takeAtMs, clock, look) {
        const run = (HeroCamera.running = { key: `${matchId}/${stationId}`, cancelled: false });
        const box = HeroCamera.open(Theme.text('dungeon.photoLook', {}, `${matchId}:${stationId}`), Theme.text('dungeon.photoSkip'));
        try {
            const face = await HeroCamera.shoot(box, run, takeAtMs, clock, () => HeroCamera.cameraOn(matchId, stationId));
            if (run.cancelled) return await HeroCamera.skipped(box);
            if (!face) {
                HeroCamera.report(`none at ${stationId}: no face found in the camera picture`);
                HeroCamera.failed(box, Theme.text('dungeon.photoNoFace'));
                await HeroCamera.wait(2200);
                return;
            }
            const portrait = await HeroCamera.portraitOf(face, look);
            if (run.cancelled) return await HeroCamera.skipped(box);
            HeroCamera.reveal(box, portrait, Theme.text('dungeon.photoDone', {}, `${matchId}:${stationId}`));
            await HeroCamera.send(`/api/photos/${encodeURIComponent(matchId)}/${encodeURIComponent(stationId)}`, face, portrait);
            await HeroCamera.wait(1800);
        } catch (err) {
            if (!run.cancelled) HeroCamera.report(`failed at ${stationId}: ${err.name || 'Error'}: ${err.message || err}`);
            HeroCamera.failed(box, Theme.text(run.cancelled ? 'dungeon.photoSkipped' : 'dungeon.photoFailed'));
            await HeroCamera.wait(2000);
        } finally {
            HeroCamera.close();
            HeroCamera.running = null;
        }
    },

    /** They pressed ACTION to say no: the camera goes off, with a word to say so. */
    async skipped(box) {
        HeroCamera.failed(box, Theme.text('dungeon.photoSkipped'));
        await HeroCamera.wait(1500);
    },

    /** Tells the server the camera is on, so it plays the intro's countdown on this station's speaker. */
    cameraOn(matchId, stationId) {
        fetch(`/api/photos/${encodeURIComponent(matchId)}/${encodeURIComponent(stationId)}/camera`, { method: 'POST' })
            .catch(() => {}); // only the blips depend on it
    },

    // ------------------------------------------------------------ The camera itself

    /**
     * Turns the camera on, counts down to takeAtMs and finds the face: the cut-out face, or null when there's
     * no face to be found. The camera is off again afterwards. onLive: called once the camera is on.
     */
    async shoot(box, run, takeAtMs, clock, onLive = () => {}) {
        const video = box.querySelector('video');
        const stream = await navigator.mediaDevices.getUserMedia({ video: await HeroCamera.whichCamera(), audio: false });
        try {
            if (run.cancelled) return null; // a retake (or a no) came while the camera was coming on
            HeroCamera.stream = stream;
            video.srcObject = stream;
            await video.play();
            box.classList.add('is-live');
            onLive();
            await HeroCamera.countDown(box, run, Math.max(takeAtMs, clock() + HeroCamera.WARM_UP_MS), clock);
            if (run.cancelled) return null;
            box.classList.add('is-flash');
            return await HeroCamera.findFace(video, run);
        } finally {
            // Only this run's camera: after a quick retake, the next run's may already be on.
            stream.getTracks().forEach(track => track.stop());
            if (HeroCamera.stream === stream) HeroCamera.stop();
        }
    },

    /** The hero wearing the face, drawn in 3D (where the station can), or just the face. */
    async portraitOf(face, look) {
        return window.HeroPortrait ? HeroPortrait.render(face.toDataURL('image/png'), look.color, { hero: look.hero }) : face;
    },

    /**
     * Looks for the face in the camera picture until one looks straight at the camera, or SEARCH_MS has gone
     * by (someone looked away, or at the screen beside it): then the straightest one seen, or null if none.
     * A turned head would come out squashed on one side on every hero.
     */
    async findFace(video, run) {
        const giveUpAt = Date.now() + HeroCamera.SEARCH_MS;
        let best = null;
        do {
            const found = await FaceCutout.find(video);
            if (run.cancelled) return null;
            if (found && (!best || found.turn < best.turn)) best = found;
            if (best && best.turn <= FaceCutout.STRAIGHT) break;
            await HeroCamera.wait(150);
        } while (Date.now() < giveUpAt);
        return best?.face ?? null;
    },

    /**
     * The station's own camera. With ?camera= (one PC driving several stations, a camera each), the one
     * whose name has that in it, and no other: better no photo than one of the player at the next station.
     */
    async whichCamera() {
        const video = { facingMode: 'user', width: { ideal: 1280 }, height: { ideal: 720 } };
        const wanted = new URLSearchParams(location.search).get('camera')?.toLowerCase();
        if (!wanted) return video;
        const devices = await navigator.mediaDevices.enumerateDevices();
        const camera = devices.find(d => d.kind === 'videoinput' && d.label.toLowerCase().includes(wanted));
        if (!camera) throw new Error(`no camera called "${wanted}"`);
        return { ...video, deviceId: { exact: camera.deviceId } };
    },

    /** "3, 2, 1" over the camera picture until the moment to take it (or until they say no). */
    countDown(box, run, takeAtMs, clock) {
        const count = box.querySelector('.photo-count');
        return new Promise(resolve => {
            const tick = () => {
                const left = takeAtMs - clock();
                if (left <= 0 || run.cancelled) return resolve();
                count.textContent = left <= HeroCamera.COUNT_FROM * 1000 ? Math.ceil(left / 1000) : '';
                setTimeout(tick, Math.min(100, left));
            };
            tick();
        });
    },

    /** Both pictures, as a form (see PhotosController), with any other fields it wants (the lobby's takeAtMs). */
    async send(url, face, portrait, fields = {}) {
        const png = canvas => new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
        const form = new FormData();
        form.append('face', await png(face), 'face.png');
        form.append('portrait', await png(portrait), 'portrait.png');
        for (const [name, value] of Object.entries(fields)) form.append(name, String(value));
        const response = await fetch(url, { method: 'PUT', body: form });
        if (!response.ok) throw new Error(`the server said ${response.status}`);
    },

    /** Turns the camera off. */
    stop() {
        HeroCamera.stream?.getTracks().forEach(track => track.stop());
        HeroCamera.stream = null;
        const video = document.querySelector('#photo-camera video');
        if (video) video.srcObject = null;
    },

    // ------------------------------------------------------------ The camera window

    /** Opens the window: the camera picture to come, a line to say what's happening, and the keys. */
    open(line, keys) {
        const box = HeroCamera.overlay();
        box.querySelector('.photo-line').innerHTML = line;
        box.querySelector('.photo-skip').innerHTML = keys;
        box.querySelector('.photo-count').textContent = '';
        box.className = 'photo-camera is-open';
        return box;
    },

    /** The photo as the hero wears it, with a word about it (and, in the lobby, what to press now). */
    reveal(box, portrait, line, keys = '') {
        box.querySelector('.photo-result').src = portrait.toDataURL('image/png');
        box.querySelector('.photo-line').innerHTML = line;
        box.querySelector('.photo-skip').innerHTML = keys;
        box.classList.add('is-taken');
    },

    /** No photo: says why (and, in the lobby, what to press now). */
    failed(box, line, keys = '') {
        HeroCamera.stop();
        box.querySelector('.photo-line').innerHTML = line;
        box.querySelector('.photo-skip').innerHTML = keys;
        box.classList.add('is-failed');
    },

    close() {
        HeroCamera.stop();
        const box = document.getElementById('photo-camera');
        if (box) box.className = 'photo-camera';
    },

    /** The camera window, made once and kept outside the screen's redrawn HTML (a redraw would cut the live picture). */
    overlay() {
        let box = document.getElementById('photo-camera');
        if (box) return box;
        box = document.createElement('div');
        box.id = 'photo-camera';
        box.className = 'photo-camera';
        box.innerHTML = `
            <div class="photo-window">
                <video muted playsinline></video>
                <div class="photo-guide"></div>
                <div class="photo-count"></div>
                <img class="photo-result" alt="">
                <div class="photo-flash"></div>
            </div>
            <div class="photo-line"></div>
            <div class="photo-skip"></div>`;
        document.body.appendChild(box);
        return box;
    },

    wait(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    },
};
