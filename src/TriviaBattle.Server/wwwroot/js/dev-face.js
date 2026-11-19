// Dev pages only: a face drawn in js/face-cutout.js's layout (eyes with whites and pupils, brows, a nose, lips), to
// see hero photos and their expressions (js/battle3d/expressions.js) without a real photo: /dev/faces?expressions and
// /dev/battle3d?face=drawn. A real face looks different; check one with ?photo= or ?camera before trusting a change.
// Needs js/face-cutout.js.

const DevFace = {
    /** The drawn face, as a PNG data URL. */
    drawn() {
        const S = FaceCutout.SIZE;
        const face = FaceCutout.canvas(S, S);
        const g = face.getContext('2d');
        const y = share => share * S;
        g.fillStyle = '#e9b48f';
        g.beginPath();
        g.ellipse(S / 2, (y(FaceCutout.TOP) + y(FaceCutout.CHIN)) / 2, S * 0.36, (y(FaceCutout.CHIN) - y(FaceCutout.TOP)) / 2, 0, 0, Math.PI * 2);
        g.fill();
        for (const side of [-1, 1]) {
            const x = S / 2 + (side * FaceCutout.EYE_SPAN * S) / 2;
            g.fillStyle = '#fff';
            g.beginPath(); g.ellipse(x, y(FaceCutout.EYES), S * 0.06, S * 0.032, 0, 0, Math.PI * 2); g.fill();
            g.fillStyle = '#5a3a22';
            g.beginPath(); g.arc(x, y(FaceCutout.EYES), S * 0.026, 0, Math.PI * 2); g.fill();
            g.fillStyle = '#111';
            g.beginPath(); g.arc(x, y(FaceCutout.EYES), S * 0.012, 0, Math.PI * 2); g.fill();
            g.strokeStyle = '#5b3b26'; g.lineWidth = S * 0.018; g.lineCap = 'round';
            g.beginPath();
            g.moveTo(x - S * 0.06, y(FaceCutout.EYES) - S * 0.065);
            g.quadraticCurveTo(x, y(FaceCutout.EYES) - S * 0.085, x + S * 0.06, y(FaceCutout.EYES) - S * 0.065);
            g.stroke();
        }
        g.strokeStyle = 'rgba(150, 90, 60, 0.6)'; g.lineWidth = S * 0.012;
        g.beginPath(); g.moveTo(S / 2, y(0.5)); g.lineTo(S / 2 - S * 0.02, y(0.6)); g.lineTo(S / 2 + S * 0.015, y(0.61)); g.stroke();
        g.fillStyle = '#b5524f';
        g.beginPath(); g.ellipse(S / 2, y(FaceCutout.MOUTH), S * 0.085, S * 0.022, 0, 0, Math.PI * 2); g.fill();
        g.strokeStyle = '#6e2a2a'; g.lineWidth = S * 0.008;
        g.beginPath(); g.moveTo(S / 2 - S * 0.085, y(FaceCutout.MOUTH)); g.lineTo(S / 2 + S * 0.085, y(FaceCutout.MOUTH)); g.stroke();
        return face.toDataURL('image/png');
    },
};
