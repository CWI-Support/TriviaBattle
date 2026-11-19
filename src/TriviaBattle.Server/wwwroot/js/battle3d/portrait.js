// A hero drawn as a picture, lit like the battle room on a see-through background, so it sits in any
// frame. Two uses:
//   - a hero photo's portrait: the player's hero, head and shoulders, wearing their face. The station
//     that took the photo draws it once and sends it to the server, for every screen that draws in 2D
//     (the plate, the main screen's lanes and line-up). With it goes its animated sheet (heroSheet).
//   - the heroes' own pictures in media/heroes (the lobby, and players without a photo), made with
//     /dev/heroes, and their animated sheets.
//
// The animated sheet: the head-and-shoulders portrait as a strip of frames per row, which screens play as a CSS
// sprite animation (css/hero-photo.css, js/hero-photo.js). SHEET is its layout; HeroPhoto.SHEET is the same (keep
// them in step). Rows:
//   idle   two loops of the hero's idle, the face blinking in the last frame
//   cheer  the hero's cheer, grinning
//   hurt   the hero's hit reaction, wincing, flashing red as the blow lands
// A face (a hero photo) changes expression in them (js/battle3d/expressions.js); a hero's own face is its model's.

import * as THREE from 'three';
import { Actor } from './actor.js';

/** What a hero card shows, in the room's units: from just under the feet to above the wizard's hat. */
const BODY_FRAME = { bottom: -0.15, top: 3.2 };

/** The animated sheet: frames along each row (square, frame pixels each), and its rows, top to bottom. */
export const SHEET = { frame: 160, frames: 16, rows: ['idle', 'cheer', 'hurt'] };

/**
 * A canvas of a hero in the station's colour, wearing this face (a URL of a FaceCutout face; null: its own).
 * options: hero ('tank', 'wizard'...), framing ('head': head and shoulders, square; 'body': all of it,
 * 3 wide by 4 high), size (the width, in pixels), expression (the face's: js/battle3d/expressions.js).
 */
export async function heroPortrait(faceUrl, color, { hero = 'tank', framing = 'head', size = 256, expression = null } = {}) {
    const height = framing === 'body' ? Math.round((size * 4) / 3) : size;
    const studio = await Studio.open(faceUrl, color, hero, framing, size, height);
    try {
        const portrait = canvasOf(size, height);
        studio.draw(portrait.getContext('2d'), 0, 0, { clip: studio.actor.spec.idle, time: 0 }, expression);
        return portrait;
    } finally {
        studio.close();
    }
}

/**
 * The hero's animated sheet (see SHEET): a canvas of SHEET.frames frames across by one row each of SHEET.rows,
 * head and shoulders, wearing this face (null: its own). frame: each frame's size in pixels.
 */
export async function heroSheet(faceUrl, color, { hero = 'tank', frame = SHEET.frame } = {}) {
    const studio = await Studio.open(faceUrl, color, hero, 'head', frame, frame);
    try {
        const { actor } = studio;
        const spec = actor.spec;
        const sheet = canvasOf(frame * SHEET.frames, frame * SHEET.rows.length);
        const g = sheet.getContext('2d');
        const n = SHEET.frames;
        const idleLength = actor.duration(spec.idle) || 1.6;
        const cheer = actor.has(spec.cheer) ? spec.cheer : spec.idle;
        const cheerLength = Math.min(actor.duration(cheer) || 1.4, 1.6);
        const hurtLength = Math.min(actor.duration(spec.hurt) || 0.8, 1.2);
        SHEET.rows.forEach((row, r) => {
            for (let i = 0; i < n; i++) {
                const t = i / n;
                let pose, expression = null;
                if (row === 'idle') {
                    pose = { clip: spec.idle, time: t * idleLength * 2 };
                    expression = i === n - 1 ? 'blink' : null;
                } else if (row === 'cheer') {
                    pose = { clip: cheer, time: (i / (n - 1)) * cheerLength };
                    expression = 'grin';
                } else {
                    pose = actor.has(spec.hurt) ? { clip: spec.hurt, time: (i / (n - 1)) * hurtLength } : { clip: spec.idle, time: 0 };
                    pose = { ...pose, flash: i < 4 ? 0.8 * (1 - i / 4) : 0, flashColor: 0xff2020 };
                    expression = i < Math.round(n * 0.75) ? 'wince' : null;
                }
                studio.draw(g, i * frame, r * frame, pose, expression);
            }
        });
        return sheet;
    } finally {
        studio.close();
    }
}

/** The hero in its little photo studio: the actor, the lights, the camera and a renderer, drawn pose by pose. */
class Studio {
    static async open(faceUrl, color, hero, framing, width, height) {
        const actor = await Actor.create(hero, 0, color);
        if (faceUrl) await actor.wearFace(faceUrl);
        actor.apply({ clip: actor.spec.idle, time: 0 });
        actor.shadow.visible = false;
        return new Studio(actor, framing, width, height);
    }

    constructor(actor, framing, width, height) {
        this.actor = actor;
        this.scene = new THREE.Scene();
        this.scene.add(actor.root);
        this.scene.add(new THREE.HemisphereLight(0xffe4c0, 0x2a1820, 1.1));
        const key = new THREE.DirectionalLight(0xfff0d8, 2.0);
        key.position.set(2, 3, 8);
        this.scene.add(key);
        const fill = new THREE.DirectionalLight(0x8fa6ff, 0.6);
        fill.position.set(-6, 2, 4);
        this.scene.add(fill);

        // Head: framed tight on the face, from its own height, so a brim doesn't shade the eyes (framed on the idle's
        // first frame, and held there, so an animated sheet's head bobs within it). Body: every hero through the same
        // camera (feet on the floor, BODY_FRAME tall, from a little below the middle, the way a hero card looks up at
        // its hero), so flipping through the cards their feet stay put and a hat is taller.
        actor.root.updateMatrixWorld(true);
        const camera = new THREE.PerspectiveCamera(28, width / height, 0.01, 100);
        const tan = Math.tan(THREE.MathUtils.degToRad(14));
        if (framing === 'body') {
            const middle = BODY_FRAME.top / 2 + BODY_FRAME.bottom / 2;
            const distance = ((BODY_FRAME.top - BODY_FRAME.bottom) / 2) / tan;
            camera.position.set(0, middle * 0.8, distance);
            camera.lookAt(0, middle, 0);
        } else {
            const spec = actor.spec.face;
            const eyes = new THREE.Vector3(0, spec.eyes - 0.03, 0.4).applyMatrix4(actor.model.matrixWorld);
            const distance = 0.62 / tan;
            camera.position.set(eyes.x, eyes.y, eyes.z + distance);
            camera.lookAt(eyes);
        }
        this.camera = camera;

        this.renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true });
        this.renderer.setSize(width, height, false);
        this.renderer.outputColorSpace = THREE.SRGBColorSpace;
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 1.0;
        this.renderer.setClearColor(0x000000, 0);
    }

    /** Poses the hero (an Actor pose) with this expression, renders it, and copies it into g at (x, y). */
    draw(g, x, y, pose, expression) {
        this.actor.apply(pose);
        this.actor.express(expression);
        this.renderer.render(this.scene, this.camera);
        g.drawImage(this.renderer.domElement, x, y);
    }

    close() {
        this.actor.dispose(this.scene);
        this.renderer.dispose();
        this.renderer.forceContextLoss();
    }
}

function canvasOf(width, height) {
    const canvas = document.createElement('canvas');
    canvas.width = width;
    canvas.height = height;
    return canvas;
}
