// Hero photos on the station (Dungeon format): the camera. The server says when a photo is coming
// (you.photoAtMs). By default that's everyone's, from the start of the intro; a venue can ask first instead
// ("Put your face on your hero? Press ACTION", you.photoOfferUntilMs). The camera comes on in a window over
// the screen with an outline to put your face in and "Press ACTION to skip". The server plays a "3, 2, 1" on
// the station's speaker once the camera is on (it's told: POST .../camera). On the last beat the face is
// found and cut out (js/face-cutout.js), the knight is drawn wearing it (js/battle3d/portrait.js) and shown
// as the reveal, and both are sent to the server (PUT /api/photos/{matchId}/{stationId}). The camera goes off
// straight away. If no face can be found, nothing is sent. Pressing ACTION in the intro says no: the
// snapshot's photoAtMs goes, and the camera stops (or, if the photo was taken, the knight's own face comes
// back). The rules are on the server: this only does what the snapshot says.
//
// The station PC needs a camera, and the page must be allowed to use it. Browsers only give cameras to
// https or localhost pages, so a station on the venue LAN starts Chrome with
// --unsafely-treat-insecure-origin-as-secure=http://<server>:5000 (README, "Hero photos"). When one PC
// drives several stations, each station's page names its own camera with ?camera=<part of its name>.
// Without a camera nothing is shown, and nothing is heard. Whenever a photo can't be taken, the station
// says why in the server's log (and the admin's Live log): not an https/localhost page, no camera,
// the camera refused or busy, no face found.
// Needs ui.js, theme.js and face-cutout.js on the page. Styles: css/hero-photo.css.

const HeroCamera = {
    WARM_UP_MS: 800,   // a camera's first frames are dark while it sets its exposure
    SEARCH_MS: 2500,   // how long to keep looking for a face after the countdown
    COUNT_FROM: 3,     // the digits show for the last seconds only (Game:Dungeon:PhotoCountdownSeconds, 3 by default)

    hasCamera: false,
    noCamera: null,    // why there's no camera, once checked (for the log, and the hint on an insecure page)
    report: () => {},  // tells the server's log why a photo couldn't be taken
    taken: null,       // `${matchId}/${stationId}` of the photo taken (or tried), so it's only done once
    running: null,     // { key, cancelled } while the camera window is up
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
        redraw();
        if (HeroCamera.hasCamera) FaceCutout.load().catch(err => HeroCamera.report(`the face finder didn't load: ${err.message || err}`));
    },

    /** On an http page away from localhost: a quiet line in the intro, so a test session knows why there's no photo. */
    hint(you) {
        if (!you?.photoAtMs || window.isSecureContext) return '';
        return `<p class="photo-hint">${Theme.text('dungeon.photoNoHttps')}</p>`;
    },

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
     * Call on every redraw. When a photo is coming, takes it (once per match); when the player has said no
     * (photoAtMs gone), stops. Not after a reload of the page once it has arrived, or long after its moment.
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

    /** look: the station's colour and the player's hero, for the portrait. */
    async run(matchId, stationId, takeAtMs, clock, look) {
        const run = (HeroCamera.running = { key: `${matchId}/${stationId}`, cancelled: false });
        const box = HeroCamera.overlay();
        const video = box.querySelector('video');
        const line = box.querySelector('.photo-line');
        const seed = `${matchId}:${stationId}`;
        line.innerHTML = Theme.text('dungeon.photoLook', {}, seed);
        box.querySelector('.photo-skip').innerHTML = Theme.text('dungeon.photoSkip');
        box.className = 'photo-camera is-open';
        try {
            HeroCamera.stream = await navigator.mediaDevices.getUserMedia({ video: await HeroCamera.whichCamera(), audio: false });
            video.srcObject = HeroCamera.stream;
            await video.play();
            box.classList.add('is-live');
            HeroCamera.cameraOn(matchId, stationId);
            await HeroCamera.countDown(box, run, Math.max(takeAtMs, clock() + HeroCamera.WARM_UP_MS), clock);
            if (run.cancelled) return await HeroCamera.skipped(box, video, line);
            box.classList.add('is-flash');

            const face = await HeroCamera.findFace(video, run);
            HeroCamera.stop(video);
            if (run.cancelled) return await HeroCamera.skipped(box, video, line);
            if (!face) {
                HeroCamera.report(`none at ${stationId}: no face found in the camera picture`);
                line.innerHTML = Theme.text('dungeon.photoNoFace');
                box.classList.add('is-failed');
                await HeroCamera.wait(2200);
                return;
            }
            const faceUrl = face.toDataURL('image/png');
            const portrait = window.HeroPortrait ? await HeroPortrait.render(faceUrl, look.color, { hero: look.hero }) : face;
            if (run.cancelled) return await HeroCamera.skipped(box, video, line);
            box.querySelector('.photo-result').src = portrait.toDataURL('image/png');
            line.innerHTML = Theme.text('dungeon.photoDone', {}, seed);
            box.classList.add('is-taken');
            await HeroCamera.upload(matchId, stationId, face, portrait);
            await HeroCamera.wait(1800);
        } catch (err) {
            if (!run.cancelled) HeroCamera.report(`failed at ${stationId}: ${err.name || 'Error'}: ${err.message || err}`);
            HeroCamera.stop(video);
            line.innerHTML = Theme.text(run.cancelled ? 'dungeon.photoSkipped' : 'dungeon.photoFailed');
            box.classList.add('is-failed');
            await HeroCamera.wait(2000);
        } finally {
            box.className = 'photo-camera';
            HeroCamera.running = null;
        }
    },

    /** They pressed ACTION to say no: the camera goes off, with a word to say so. */
    async skipped(box, video, line) {
        HeroCamera.stop(video);
        line.innerHTML = Theme.text('dungeon.photoSkipped');
        box.classList.add('is-failed');
        await HeroCamera.wait(1500);
    },

    /** Tells the server the camera is on, so it plays the countdown on this station's speaker. */
    cameraOn(matchId, stationId) {
        fetch(`/api/photos/${encodeURIComponent(matchId)}/${encodeURIComponent(stationId)}/camera`, { method: 'POST' })
            .catch(() => {}); // only the blips depend on it
    },

    /** Looks for the face in the camera picture until one is found, or SEARCH_MS has gone by (someone looked away). */
    async findFace(video, run) {
        const giveUpAt = Date.now() + HeroCamera.SEARCH_MS;
        do {
            const face = await FaceCutout.cut(video);
            if (face || run.cancelled) return face;
            await HeroCamera.wait(200);
        } while (Date.now() < giveUpAt);
        return null;
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

    /** Both pictures, as a form (see PhotosController). */
    async upload(matchId, stationId, face, portrait) {
        const png = canvas => new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
        const form = new FormData();
        form.append('face', await png(face), 'face.png');
        form.append('portrait', await png(portrait), 'portrait.png');
        const response = await fetch(`/api/photos/${encodeURIComponent(matchId)}/${encodeURIComponent(stationId)}`, { method: 'PUT', body: form });
        if (!response.ok) throw new Error(`the server said ${response.status}`);
    },

    /** Turns the camera off. */
    stop(video) {
        HeroCamera.stream?.getTracks().forEach(track => track.stop());
        HeroCamera.stream = null;
        video.srcObject = null;
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
