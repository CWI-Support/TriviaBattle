// Hero photos on the station (Dungeon format): the camera. In the intro the station offers it ("Put your
// face on your hero? Press ACTION"); nothing is filmed until the player says yes. The server then says when
// to take it (you.photoAtMs, after a "3, 2, 1" whose blips it plays on the station's speaker). The camera
// comes on in a circle over the screen, showing exactly the part that will be kept; on the last beat the
// photo is taken, turned into a small round pixel portrait to match the game's art, and sent to the server
// (PUT /api/photos/{matchId}/{stationId}). The camera goes off straight away. The rules (when it's on offer,
// once per match) are on the server: this only does what the snapshot says.
//
// The station PC needs a camera, and the page must be allowed to use it. Browsers only give cameras to
// https or localhost pages, so a station on the venue LAN starts Chrome with
// --unsafely-treat-insecure-origin-as-secure=http://<server>:5000 (README, "Hero photos"). When one PC
// drives several stations, each station's page names its own camera with ?camera=<part of its name>.
// Without a camera the offer simply isn't shown.
// Needs ui.js and theme.js on the page. Styles: css/hero-photo.css.

const HeroCamera = {
    PIXELS: 48,      // the portrait is this many pixels across: the game's pixel-art look
    LEVELS: 8,       // colour steps per channel, for the same look
    CROP: 0.55,      // the circle kept, as a share of the camera picture's height (head and a little shoulder)
    WARM_UP_MS: 800, // a camera's first frames are dark while it sets its exposure

    hasCamera: false,
    taken: null,     // `${matchId}/${stationId}` of the photo taken (or tried), so it's only done once
    stream: null,    // the camera, while it's on

    /** Finds out once whether this PC has a camera the page may use; then calls redraw. */
    async check(redraw) {
        try {
            const devices = await navigator.mediaDevices.enumerateDevices();
            HeroCamera.hasCamera = devices.some(d => d.kind === 'videoinput');
        } catch {
            HeroCamera.hasCamera = false; // no mediaDevices: not an https/localhost page, or an old browser
        }
        if (HeroCamera.hasCamera) redraw();
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
    follow(match, you, clock) {
        if (!you?.photoAtMs || !HeroCamera.hasCamera) return;
        const arrived = match.players.find(p => p.stationId === you.stationId)?.photo > 0;
        const key = `${match.matchId}/${you.stationId}`;
        if (HeroCamera.taken === key || arrived || clock() - you.photoAtMs > 10000) return;
        HeroCamera.taken = key;
        HeroCamera.run(match.matchId, you.stationId, you.photoAtMs, clock);
    },

    async run(matchId, stationId, takeAtMs, clock) {
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

            const portrait = HeroCamera.pixelate(video);
            HeroCamera.stop(video);
            box.querySelector('.photo-result').src = portrait.toDataURL('image/png');
            line.innerHTML = Theme.text('dungeon.photoDone', {}, seed);
            box.classList.add('is-taken');
            await HeroCamera.upload(matchId, stationId, portrait);
            await HeroCamera.wait(1400);
        } catch (err) {
            console.warn('Hero photo:', err);
            HeroCamera.stop(video);
            line.innerHTML = Theme.text('dungeon.photoFailed');
            box.classList.add('is-failed');
            await HeroCamera.wait(2000);
        }
        box.className = 'photo-camera';
    },

    /**
     * The station's own camera. With ?camera= (one PC driving several stations, a camera each), the one
     * whose name has that in it, and no other: better no photo than one of the player at the next station.
     */
    async whichCamera() {
        const video = { facingMode: 'user', width: { ideal: 640 }, height: { ideal: 480 } };
        const wanted = new URLSearchParams(location.search).get('camera')?.toLowerCase();
        if (!wanted) return video;
        const devices = await navigator.mediaDevices.enumerateDevices();
        const camera = devices.find(d => d.kind === 'videoinput' && d.label.toLowerCase().includes(wanted));
        if (!camera) throw new Error(`no camera called "${wanted}"`);
        return { ...video, deviceId: { exact: camera.deviceId } };
    },

    /** "3, 2, 1" in the circle until the moment to take it. */
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

    /**
     * The photo as the game draws it: the circle the player saw (mirrored, as they saw it), shrunk to a
     * few pixels across with a little more contrast and fewer colours, like painted pixel art. Round:
     * the corners are see-through.
     */
    pixelate(video) {
        const size = HeroCamera.PIXELS;
        const width = video.videoWidth || video.width;    // a canvas works too (the dev preview's made-up face)
        const height = video.videoHeight || video.height;
        const crop = Math.min(width, height) * HeroCamera.CROP;
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = size;
        const g = canvas.getContext('2d', { willReadFrequently: true });
        g.imageSmoothingQuality = 'high';
        g.translate(size, 0);
        g.scale(-1, 1);
        g.drawImage(video, (width - crop) / 2, (height - crop) / 2, crop, crop, 0, 0, size, size);

        const image = g.getImageData(0, 0, size, size);
        const step = 255 / (HeroCamera.LEVELS - 1);
        const radius = size / 2;
        for (let i = 0; i < image.data.length; i += 4) {
            for (let c = 0; c < 3; c++) {
                const punchy = (image.data[i + c] - 128) * 1.1 + 128;
                image.data[i + c] = Math.round(Math.min(255, Math.max(0, punchy)) / step) * step;
            }
            const x = (i / 4) % size + 0.5;
            const y = Math.floor(i / 4 / size) + 0.5;
            image.data[i + 3] = Math.hypot(x - radius, y - radius) <= radius ? 255 : 0;
        }
        g.putImageData(image, 0, 0);
        return canvas;
    },

    async upload(matchId, stationId, canvas) {
        const png = await new Promise(resolve => canvas.toBlob(resolve, 'image/png'));
        const response = await fetch(`/api/photos/${encodeURIComponent(matchId)}/${encodeURIComponent(stationId)}`, {
            method: 'PUT',
            headers: { 'Content-Type': 'image/png' },
            body: png,
        });
        if (!response.ok) throw new Error(`the server said ${response.status}`);
    },

    /** Turns the camera off. */
    stop(video) {
        HeroCamera.stream?.getTracks().forEach(track => track.stop());
        HeroCamera.stream = null;
        video.srcObject = null;
    },

    /**
     * The camera window, made once and kept outside the screen's redrawn HTML (a redraw would cut the
     * live picture). The video is scaled so the circle shows exactly the part that's kept (CROP).
     */
    overlay() {
        let box = document.getElementById('photo-camera');
        if (box) return box;
        box = document.createElement('div');
        box.id = 'photo-camera';
        box.className = 'photo-camera';
        box.innerHTML = `
            <div class="photo-window">
                <video muted playsinline style="height: ${100 / HeroCamera.CROP}%"></video>
                <img class="photo-result" alt="">
                <div class="photo-count"></div>
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
