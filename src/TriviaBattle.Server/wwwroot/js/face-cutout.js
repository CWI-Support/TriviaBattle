// The face finder for hero photos: from a camera picture, just the player's face, cut out along its
// outline and laid out the same way every time: eyes level, a set distance apart, and eyes, mouth and
// chin at set heights (a gentle stretch per face). Anything that uses a face can then rely on where its
// features are; the knight's head does (js/battle3d/face.js).
//
// MediaPipe's Face Landmarker (lib/mediapipe, vendored because the venue network may be offline) finds
// 478 points on the face; this uses the outline, the pupils, the mouth, the top of the forehead and the
// chin. It all runs on the station PC: only the finished cut-out leaves it.
//
//   FaceCutout.load()        starts loading the finder (about 17 MB; call it early, e.g. once a camera is found)
//   FaceCutout.find(picture) { face, turn }: the face (as cut) and how far the head is turned (0 = straight on)
//   FaceCutout.cut(picture)  a SIZE x SIZE canvas with only the face (see-through elsewhere), or null if no
//                            face was found. picture: a playing <video>, a loaded image or a canvas. It's
//                            mirrored first, as the player saw themselves in the camera window.

const FaceCutout = {
    SIZE: 256,
    // Where every face's features end up, as shares of SIZE.
    EYE_SPAN: 0.30, // pupil to pupil
    TOP: 0.04,      // the top of the forehead
    EYES: 0.42,
    MOUTH: 0.69,
    CHIN: 0.88,
    // A face turned less than this (find's turn) counts as looking straight at the camera. Looking at a camera a
    // little to one side measured about 0.2; a head turned enough to come out stretched, 0.4.
    STRAIGHT: 0.25,

    finder: null,   // a promise of the Face Landmarker

    load() {
        FaceCutout.finder ??= (async () => {
            const { FilesetResolver, FaceLandmarker } = await import('/lib/mediapipe/vision_bundle.mjs');
            const files = await FilesetResolver.forVisionTasks('/lib/mediapipe/wasm');
            const finder = await FaceLandmarker.createFromOptions(files, {
                baseOptions: { modelAssetPath: '/lib/mediapipe/face_landmarker.task', delegate: 'CPU' },
                runningMode: 'IMAGE',
                numFaces: 1,
            });
            return { finder, outline: FaceCutout.inOrder(FaceLandmarker.FACE_LANDMARKS_FACE_OVAL) };
        })().catch(err => {
            FaceCutout.finder = null; // try again next time
            throw err;
        });
        return FaceCutout.finder;
    },

    async cut(picture) {
        return (await FaceCutout.find(picture))?.face ?? null;
    },

    /**
     * Like cut, with how far the head is turned: { face, turn }, or null if no face was found. turn is 0 for a
     * face looking straight at the camera, up to 1 for one seen side on (from the nose's distance to each side
     * of the face). A turned face, laid out like a straight one, comes out squashed on one side and stretched
     * on the other, so a camera should wait for one under STRAIGHT if it can (js/hero-camera.js).
     */
    async find(picture) {
        const { finder, outline } = await FaceCutout.load();
        const width = picture.videoWidth || picture.naturalWidth || picture.width;
        const height = picture.videoHeight || picture.naturalHeight || picture.height;
        const frame = FaceCutout.canvas(width, height);
        const f = frame.getContext('2d');
        f.translate(width, 0);
        f.scale(-1, 1);
        f.drawImage(picture, 0, 0, width, height);

        const points = finder.detect(frame).faceLandmarks[0];
        if (!points) return null;
        const S = FaceCutout.SIZE;
        const at = i => new DOMPoint(points[i].x * width, points[i].y * height);

        // 1. One turn and one scale: the eyes level, the set distance apart, centred, with room above and below.
        const [left, right] = [at(468), at(473)].sort((a, b) => a.x - b.x);
        const degrees = (Math.atan2(right.y - left.y, right.x - left.x) * 180) / Math.PI;
        const scale = (FaceCutout.EYE_SPAN * S) / Math.hypot(right.x - left.x, right.y - left.y);
        const level = new DOMMatrix()
            .translate(S / 2, S)
            .scale(scale)
            .rotate(-degrees)
            .translate(-(left.x + right.x) / 2, -(left.y + right.y) / 2);
        const place = i => at(i).matrixTransform(level);

        const levelled = FaceCutout.canvas(S, 2 * S); // the eyes at S, half way down
        const g = levelled.getContext('2d');
        g.setTransform(level);
        g.filter = `brightness(${FaceCutout.brightnessFix(frame, points, width, height)}) saturate(1.1)`;
        g.drawImage(frame, 0, 0);
        g.resetTransform();
        g.filter = 'none';
        g.globalCompositeOperation = 'destination-in';
        g.drawImage(FaceCutout.faceMask(outline.map(place), S), 0, 0);

        // 2. Each band (forehead, eyes to mouth, mouth to chin) stretched a little, so the features land at the set heights.
        const mouth = (place(13).y + place(14).y) / 2;
        const from = [place(10).y, S, mouth, place(152).y];
        const to = [FaceCutout.TOP, FaceCutout.EYES, FaceCutout.MOUTH, FaceCutout.CHIN].map(share => share * S);
        const face = FaceCutout.canvas(S, S);
        FaceCutout.stretchRows(levelled, face, y => FaceCutout.between(y, to, from));

        // How far it's turned: the nose tip (1) sits half way between the sides of the face (234, 454) when
        // it looks straight on, and nearer one side the more it turns.
        const nose = place(1).x;
        const [toOne, toOther] = [Math.abs(nose - place(234).x), Math.abs(place(454).x - nose)];
        const turn = Math.abs(toOne - toOther) / Math.max(1, toOne + toOther);
        return { face, turn };
    },

    // ---- helpers (also used by js/battle3d/face.js: stretchRows, between, canvas)

    /** MediaPipe gives the outline as separate lines (start to end); this puts its points in order round the face. */
    inOrder(connections) {
        const next = new Map(connections.map(c => [c.start, c.end]));
        const order = [connections[0].start];
        while (order.length < connections.length && next.get(order[order.length - 1]) !== order[0])
            order.push(next.get(order[order.length - 1]));
        return order;
    },

    /** The face's outline filled white, a little inside the real edge and blurred, so the cut is soft. */
    faceMask(points, size) {
        const mask = FaceCutout.canvas(size, 2 * size);
        const m = mask.getContext('2d');
        const middle = points.reduce((sum, p) => ({ x: sum.x + p.x / points.length, y: sum.y + p.y / points.length }), { x: 0, y: 0 });
        m.filter = `blur(${size * 0.015}px)`;
        m.fillStyle = '#fff';
        m.beginPath();
        points.forEach(p => m.lineTo(middle.x + (p.x - middle.x) * 0.96, middle.y + (p.y - middle.y) * 0.96));
        m.fill();
        return mask;
    },

    /** A dim arcade makes a dark photo: brighten the face (inside its points' box) towards a set level. */
    brightnessFix(frame, points, width, height) {
        const xs = points.map(p => p.x * width);
        const ys = points.map(p => p.y * height);
        const x = Math.max(0, Math.min(...xs));
        const y = Math.max(0, Math.min(...ys));
        const pixels = frame.getContext('2d').getImageData(x, y, Math.max(1, Math.max(...xs) - x), Math.max(1, Math.max(...ys) - y)).data;
        let sum = 0;
        for (let i = 0; i < pixels.length; i += 4) sum += 0.3 * pixels[i] + 0.59 * pixels[i + 1] + 0.11 * pixels[i + 2];
        const mean = sum / (pixels.length / 4) || 128;
        return Math.min(1.6, Math.max(0.85, 140 / mean));
    },

    /**
     * Redraws source into target one row at a time: target row y comes from source row sourceY(y). Used to
     * stretch a face band by band (a straight scale can't put three features at three set heights).
     */
    stretchRows(source, target, sourceY) {
        const t = target.getContext('2d');
        for (let y = 0; y < target.height; y++) {
            const top = sourceY(y);
            t.drawImage(source, 0, top, source.width, Math.max(0.01, sourceY(y + 1) - top), 0, y, target.width, 1);
        }
    },

    /** Piecewise straight line through (stops[i], values[i]), carried on past both ends. */
    between(x, stops, values) {
        let i = 0;
        while (i < stops.length - 2 && x > stops[i + 1]) i++;
        return values[i] + ((x - stops[i]) * (values[i + 1] - values[i])) / (stops[i + 1] - stops[i]);
    },

    canvas(width, height) {
        const canvas = document.createElement('canvas');
        canvas.width = width;
        canvas.height = height;
        return canvas;
    },
};
