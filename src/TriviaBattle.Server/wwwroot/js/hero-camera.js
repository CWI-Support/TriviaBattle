// Hero photos on the station (Dungeon format): the camera. In the intro the station offers it ("Put your
// face on your hero? Press ACTION"); nothing is filmed until the player says yes. The server then says when
// to take it (you.photoAtMs, after a "3, 2, 1" whose blips it plays on the station's speaker). The camera
// comes on in a window over the screen with an outline to put your face in. On the last beat the face is
// found and cut out (js/face-cutout.js), the knight is drawn wearing it (js/battle3d/portrait.js) and shown
// as the reveal, and both are sent to the server (PUT /api/photos/{matchId}/{stationId}). The camera goes off
// straight away. If no face can be found, nothing is sent. The rules (when it's on offer, once per match)
// are on the server: this only does what the snapshot says.
//
// The station PC needs a camera, and the page must be allowed to use it. Browsers only give cameras to
// https or localhost pages, so a station on the venue LAN starts Chrome with
// --unsafely-treat-insecure-origin-as-secure=http://<server>:5000 (README, "Hero photos"). When one PC
// drives several stations, each station's page names its own camera with ?camera=<part of its name>.
// Without a camera the offer simply isn't shown.
// Needs ui.js, theme.js and face-cutout.js on the page. Styles: css/hero-photo.css.

const HeroCamera = {
    WARM_UP_MS: 800,   // a camera's first frames are dark while it sets its exposure
    SEARCH_MS: 2500,   // how long to keep looking for a face after the countdown

    hasCamera: false,
    taken: null,       // `${matchId}/${stationId}` of the photo taken (or tried), so it's only done once
    stream: null,      // the camera, while it's on

    /** Finds out once whether this PC has a camera the page may use; then starts loading the face finder and redraws. */
    async check(redraw) {
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            HeroCamera.hasCamera = devices.some(d => d.kind === 'videoinput');
        } catch {
            HeroCamera.hasCamera = false; // no mediaDevices: not an https/localhost page, or an old browser
        }
        if (!HeroCamera.hasCamera) return;
        FaceCutout.load().catch(err => console.warn('Face finder:', err));
        redraw();
    },

    /** The offer, for the intro screen. Nothing without a camera, or once it has passed (it fades out then by itself). */
    offer(you, serverNow) {
        if (!HeroCamera.hasCamera || !(you.photoOfferUntilMs > serverNow)) return '';
        return `
            <div class="photo-offer" style="${UI.animateAt(you.photoOfferUntilMs)}">
                <div class="photo-offer-title">${Theme.text('dungeon.photoOffer')}</div>
                <div class="photo-offer-press">${Theme.text('dungeon.photoPress')}</div>
            </div>`;
    },

    /**
     * Call on every redraw. Once the player has said yes, takes their photo (once per match). Not after
     * a reload of the page once it has arrived, or long after the yes: the moment has passed.
     */
    follow(match, you, clock, color) {
        if (!you?.photoAtMs || !HeroCamera.hasCamera) return;
        const arrived = match.players.find(p => p.stationId === you.stationId)?.photo > 0;
        const key = `${match.matchId}/${you.stationId}`;
        if (HeroCamera.taken === key || arrived || clock() - you.photoAtMs > 10000) return;
        HeroCamera.taken = key;
        HeroCamera.run(match.matchId, you.stationId, you.photoAtMs, clock, color);
    },

    async run(matchId, stationId, takeAtMs, clock, color) {
        const box = HeroCamera.overlay();
        const video = box.querySelector('video');
        const line = box.querySelector('.photo-line');
        const seed = `${matchId}:${stationId}`;
        line.innerHTML = Theme.text('dungeon.photoLook', {}, seed);
        box.className = 'photo-camera is-open';
        try {
            HeroCamera.stream = await navigator.mediaDevices.getUserMedia({ video: await HeroCamera.whichCamera(), audio: false });
            video.srcObject = HeroCamera.stream;
            await video.play();
            box.classList.add('is-live');
            await HeroCamera.countDown(box, Math.max(takeAtMs, clock() + HeroCamera.WARM_UP_MS), clock);
            box.classList.add('is-flash');

            const face = await HeroCamera.findFace(video);
            HeroCamera.stop(video);
            if (!face) {
                line.innerHTML = Theme.text('dungeon.photoNoFace');
                box.classList.add('is-failed');
                await HeroCamera.wait(2200);
                return;
            }
            const faceUrl = face.toDataURL('image/png');
            const portrait = window.HeroPortrait ? await HeroPortrait.render(faceUrl, color) : face;
            box.querySelector('.photo-result').src = portrait.toDataURL('image/png');
            line.innerHTML = Theme.text('dungeon.photoDone', {}, seed);
            box.classList.add('is-taken');
            await HeroCamera.upload(matchId, stationId, face, portrait);
            await HeroCamera.wait(1800);
        } catch (err) {
            console.warn('Hero photo:', err);
            HeroCamera.stop(video);
            line.innerHTML = Theme.text('dungeon.photoFailed');
            box.classList.add('is-failed');
            await HeroCamera.wait(2000);
        } finally {
            box.className = 'photo-camera';
        }
    },

    /** Looks for the face in the camera picture until one is found, or SEARCH_MS has gone by (someone looked away). */
    async findFace(video) {
        const giveUpAt = Date.now() + HeroCamera.SEARCH_MS;
        do {
            const face = await FaceCutout.cut(video);
            if (face) return face;
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

    /** "3, 2, 1" over the camera picture until the moment to take it. */
    countDown(box, takeAtMs, clock) {
        const count = box.querySelector('.photo-count');
        return new Promise(resolve => {
            const tick = () => {
                const left = takeAtMs - clock();
                if (left <= 0) return resolve();
                count.textContent = Math.ceil(left / 1000);
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
            <div class="photo-line"></div>`;
        document.body.appendChild(box);
        return box;
    },

    wait(ms) {
        return new Promise(resolve => setTimeout(resolve, ms));
    },
};
