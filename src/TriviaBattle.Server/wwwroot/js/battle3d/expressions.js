// Hero photo expressions: the player's cut-out face redrawn blinking, wincing, grinning, shocked or knocked out, so the
// face on their hero reacts in the fight (js/battle3d/face.js paints each one, and the actor swaps between them) and in
// their animated portrait (js/battle3d/portrait.js). Every face from js/face-cutout.js is laid out the same way (eyes
// level, a set distance apart, eyes and mouth at set heights), so where the eyes and mouth are is known without
// looking. Each expression is drawn over the face cartoon style, in keeping with the chibi heroes, and bold enough
// to read on a small head across the room (a real mouth bent into a smile barely shows):
//   blink   eyes covered with the face's own skin, a lid line across each
//   grin    happy closed eyes (^ ^), a wide open smile with its top teeth showing
//   wince   eyes squeezed shut (> <), gritted teeth
//   shock   eyes wide, the mouth a big open O
//   ko      X X eyes, a dazed little o (downed)
// Needs js/face-cutout.js on the page (its layout numbers and canvas).

export const EXPRESSIONS = ['blink', 'grin', 'wince', 'shock', 'ko'];

const LID = 'rgba(48, 28, 24, 0.92)';   // lid lines, X eyes
const MOUTH_INSIDE = '#5a1a22';          // an open mouth
const LIP_LINE = 'rgba(80, 26, 30, 0.95)'; // a drawn mouth's outline
const TEETH = '#fbf6ee';
const TONGUE = '#d9606a';

/** Every expression of this face (an image or canvas from FaceCutout): { neutral, blink, grin, wince, shock, ko }, each a canvas (neutral: the face itself). */
export function expressionsOf(face) {
    const S = face.naturalWidth || face.width;
    const layout = {
        S,
        eyes: [-1, 1].map(side => ({ x: S / 2 + (side * FaceCutout.EYE_SPAN * S) / 2, y: FaceCutout.EYES * S, side })),
        mouth: { x: S / 2, y: FaceCutout.MOUTH * S },
    };
    const pixels = readPixels(face, S);
    const skin = {
        eyes: layout.eyes.map(e => averageColour(pixels, S, e.x, e.y + 0.11 * S, 0.035 * S)),  // the cheek under each eye
        mouth: averageColour(pixels, S, S / 2, FaceCutout.MOUTH * S + 0.085 * S, 0.03 * S),     // the chin under the mouth
    };
    const make = draw => {
        const canvas = FaceCutout.canvas(S, S);
        const g = canvas.getContext('2d');
        g.drawImage(face, 0, 0, S, S);
        draw(g, layout, skin, face);
        // Only where the face is: a cover or a stroke never spills past its cut-out outline.
        g.globalCompositeOperation = 'destination-in';
        g.drawImage(face, 0, 0, S, S);
        return canvas;
    };
    return {
        neutral: face,
        blink: make((g, l, s) => l.eyes.forEach((e, i) => { coverEye(g, e, s.eyes[i], S); lidArc(g, e, S, 0.03); })),
        grin: make((g, l, s) => {
            l.eyes.forEach((e, i) => { coverEye(g, e, s.eyes[i], S); lidArc(g, e, S, -0.045); });
            coverMouth(g, l.mouth, s.mouth, S);
            smile(g, l.mouth, S);
        }),
        wince: make((g, l, s) => {
            l.eyes.forEach((e, i) => { coverEye(g, e, s.eyes[i], S); chevron(g, e, S); });
            coverMouth(g, l.mouth, s.mouth, S);
            gritted(g, l.mouth, S);
        }),
        shock: make((g, l, s, f) => {
            l.eyes.forEach(e => widenEye(g, f, e, S));
            coverMouth(g, l.mouth, s.mouth, S);
            oMouth(g, l.mouth, S, 0.06, 0.085);
        }),
        ko: make((g, l, s) => {
            l.eyes.forEach((e, i) => { coverEye(g, e, s.eyes[i], S); crossEye(g, e, S); });
            coverMouth(g, l.mouth, s.mouth, S);
            oMouth(g, l.mouth, S, 0.03, 0.026);
        }),
    };
}

/**
 * Whether the face blinks now: for a moment (130 ms) every few seconds, each face on its own beat (seed), worked out
 * from the clock alone so a redraw never stutters it.
 */
export function blinking(seconds, seed = 0) {
    const every = 3.4 + (seed % 7) * 0.23;                 // 3.4-4.8 s between blinks
    const t = (seconds + (seed % 13) * 0.61) % every;
    return t < 0.13;
}

/** A face's own blinking beat, from its station (so two heroes side by side don't blink together). */
export function blinkSeed(stationId) {
    let seed = 0;
    for (const c of stationId || '') seed = (seed * 31 + c.charCodeAt(0)) % 9973;
    return seed;
}

// ---------- Eyes ----------

/** Covers an eye with the face's own skin (sampled from the cheek under it), soft-edged. */
function coverEye(g, eye, skin, S) {
    g.save();
    g.filter = `blur(${(0.012 * S).toFixed(1)}px)`;
    g.fillStyle = skin;
    g.beginPath();
    g.ellipse(eye.x, eye.y, 0.095 * S, 0.06 * S, 0, 0, Math.PI * 2);
    g.fill();
    g.restore();
}

function lidStroke(g, S) {
    g.strokeStyle = LID;
    g.lineWidth = 0.018 * S;
    g.lineCap = 'round';
    g.lineJoin = 'round';
}

/** A lid line across a closed eye: bowed down (bow > 0, a blink: ‿) or up (bow < 0, a happy ^). In shares of S. */
function lidArc(g, eye, S, bow) {
    lidStroke(g, S);
    g.beginPath();
    g.moveTo(eye.x - 0.07 * S, eye.y);
    g.quadraticCurveTo(eye.x, eye.y + bow * S, eye.x + 0.07 * S, eye.y);
    g.stroke();
}

/** A squeezed-shut eye: a chevron pointing at the nose (> on the left eye, < on the right). */
function chevron(g, eye, S) {
    const out = -eye.side; // towards the nose is -side; the chevron's arms open outwards
    lidStroke(g, S);
    g.beginPath();
    g.moveTo(eye.x + out * -0.06 * S, eye.y - 0.04 * S);
    g.lineTo(eye.x + out * 0.05 * S, eye.y);
    g.lineTo(eye.x + out * -0.06 * S, eye.y + 0.04 * S);
    g.stroke();
}

/** Knocked out: an X for the eye. */
function crossEye(g, eye, S) {
    const r = 0.045 * S;
    lidStroke(g, S);
    g.beginPath();
    g.moveTo(eye.x - r, eye.y - r);
    g.lineTo(eye.x + r, eye.y + r);
    g.moveTo(eye.x + r, eye.y - r);
    g.lineTo(eye.x - r, eye.y + r);
    g.stroke();
}

/** Eyes wide in shock: the eye drawn bigger over itself, its edge faded into the face. */
function widenEye(g, face, eye, S) {
    const w = 0.22 * S, h = 0.13 * S, grow = 1.3;
    const piece = FaceCutout.canvas(Math.ceil(w * grow), Math.ceil(h * grow));
    const p = piece.getContext('2d');
    p.drawImage(face, eye.x - w / 2, eye.y - h / 2, w, h, 0, 0, piece.width, piece.height);
    feather(piece);
    g.drawImage(piece, eye.x - piece.width / 2, eye.y - piece.height / 2);
}

// ---------- The mouth ----------

/** Covers the mouth with the face's own skin (sampled from the chin under it), soft-edged, for a drawn one to go on. */
function coverMouth(g, mouth, skin, S) {
    g.save();
    g.filter = `blur(${(0.014 * S).toFixed(1)}px)`;
    g.fillStyle = skin;
    g.beginPath();
    g.ellipse(mouth.x, mouth.y, 0.16 * S, 0.06 * S, 0, 0, Math.PI * 2);
    g.fill();
    g.restore();
}

function mouthLine(g, S) {
    g.strokeStyle = LIP_LINE;
    g.lineWidth = 0.016 * S;
    g.lineJoin = 'round';
    g.lineCap = 'round';
}

/** A wide open smile: corners up, a deep curve below, the top teeth showing and a bit of tongue. */
function smile(g, mouth, S) {
    const w = 0.14 * S, x = mouth.x, y = mouth.y - 0.012 * S;
    const shape = () => {
        g.beginPath();
        g.moveTo(x - w, y - 0.02 * S);
        g.quadraticCurveTo(x, y + 0.012 * S, x + w, y - 0.02 * S);  // the top lip, a gentle curve up at the corners
        g.quadraticCurveTo(x, y + 0.15 * S, x - w, y - 0.02 * S);   // the bottom, deep
        g.closePath();
    };
    g.save();
    shape();
    g.fillStyle = MOUTH_INSIDE;
    g.fill();
    g.clip();
    g.fillStyle = TONGUE;
    g.beginPath();
    g.ellipse(x, y + 0.075 * S, 0.07 * S, 0.035 * S, 0, 0, Math.PI * 2);
    g.fill();
    g.fillStyle = TEETH;
    g.fillRect(x - w, y - 0.03 * S, w * 2, 0.04 * S);
    g.restore();
    mouthLine(g, S);
    shape();
    g.stroke();
}

/** Gritted teeth (a wince): a wide flat row of teeth, the corners pulled down a touch. */
function gritted(g, mouth, S) {
    const w = 0.13 * S, h = 0.05 * S, x = mouth.x, y = mouth.y;
    const shape = () => {
        g.beginPath();
        g.moveTo(x - w, y + 0.012 * S);
        g.quadraticCurveTo(x, y - h * 0.9, x + w, y + 0.012 * S);
        g.quadraticCurveTo(x, y + h * 1.3, x - w, y + 0.012 * S);
        g.closePath();
    };
    g.save();
    shape();
    g.fillStyle = TEETH;
    g.fill();
    g.clip();
    g.strokeStyle = 'rgba(120, 90, 80, 0.8)';
    g.lineWidth = 0.008 * S;
    g.beginPath();
    g.moveTo(x - w, y + 0.006 * S);
    g.lineTo(x + w, y + 0.006 * S); // where the teeth meet
    for (let i = -3; i <= 3; i++) {
        g.moveTo(x + (i * w) / 4, y - h);
        g.lineTo(x + (i * w) / 4, y + h);
    }
    g.stroke();
    g.restore();
    mouthLine(g, S);
    shape();
    g.stroke();
}

/** An open O (shock), or a small dazed o: rx and ry in shares of S. */
function oMouth(g, mouth, S, rx, ry) {
    g.fillStyle = MOUTH_INSIDE;
    mouthLine(g, S);
    g.beginPath();
    g.ellipse(mouth.x, mouth.y + 0.01 * S, rx * S, ry * S, 0, 0, Math.PI * 2);
    g.fill();
    g.stroke();
}

// ---------- Helpers ----------

/** Fades a piece's edges out (an oval, solid in the middle), so it blends into the face it's drawn over. */
function feather(piece) {
    const g = piece.getContext('2d');
    const { width: w, height: h } = piece;
    g.save();
    g.globalCompositeOperation = 'destination-in';
    g.translate(w / 2, h / 2);
    g.scale(w / 2, h / 2);
    const fade = g.createRadialGradient(0, 0, 0, 0, 0, 1);
    fade.addColorStop(0, 'rgba(0,0,0,1)');
    fade.addColorStop(0.6, 'rgba(0,0,0,1)');
    fade.addColorStop(1, 'rgba(0,0,0,0)');
    g.fillStyle = fade;
    g.fillRect(-1, -1, 2, 2);
    g.restore();
}

function readPixels(face, S) {
    const canvas = FaceCutout.canvas(S, S);
    const g = canvas.getContext('2d', { willReadFrequently: true });
    g.drawImage(face, 0, 0, S, S);
    return g.getImageData(0, 0, S, S).data;
}

/** The average colour of the face's opaque pixels in a square round (x, y), as a CSS colour (a mid skin tone if none). */
function averageColour(pixels, S, x, y, r) {
    let red = 0, green = 0, blue = 0, n = 0;
    for (let row = Math.max(0, Math.round(y - r)); row <= Math.min(S - 1, Math.round(y + r)); row++) {
        for (let col = Math.max(0, Math.round(x - r)); col <= Math.min(S - 1, Math.round(x + r)); col++) {
            const k = (row * S + col) * 4;
            if (pixels[k + 3] < 200) continue;
            red += pixels[k]; green += pixels[k + 1]; blue += pixels[k + 2]; n++;
        }
    }
    return n ? `rgb(${Math.round(red / n)}, ${Math.round(green / n)}, ${Math.round(blue / n)})` : 'rgb(222, 180, 150)';
}
