// A hero drawn as a picture, lit like the battle room on a see-through background, so it sits in any
// frame. Two uses:
//   - a hero photo's portrait: the player's hero, head and shoulders, wearing their face. The station
//     that took the photo draws it once and sends it to the server, for every screen that draws in 2D
//     (the plate, the main screen's lanes and line-up).
//   - the heroes' own pictures in media/heroes (the lobby, and players without a photo), made with
//     /dev/heroes.

import * as THREE from 'three';
import { Actor } from './actor.js';

/**
 * A canvas of a hero in the station's colour, wearing this face (a URL of a FaceCutout face; null: its own).
 * options: hero ('tank', 'wizard'...), framing ('head': head and shoulders, square; 'body': all of it,
 * 3 wide by 4 high), size (the width, in pixels).
 */
export async function heroPortrait(faceUrl, color, { hero = 'tank', framing = 'head', size = 256 } = {}) {
    const actor = await Actor.create(hero, 0, color);
    if (faceUrl) await actor.wearFace(faceUrl);
    actor.apply({ clip: actor.spec.idle, time: 0 });
    actor.shadow.visible = false;

    const scene = new THREE.Scene();
    scene.add(actor.root);
    scene.add(new THREE.HemisphereLight(0xffe4c0, 0x2a1820, 1.1));
    const key = new THREE.DirectionalLight(0xfff0d8, 2.0);
    key.position.set(2, 3, 8);
    scene.add(key);
    const fill = new THREE.DirectionalLight(0x8fa6ff, 0.6);
    fill.position.set(-6, 2, 4);
    scene.add(fill);

    // Head: framed tight on the face, from its own height, so a brim doesn't shade the eyes. Body: all of it,
    // from a little below its middle, the way a hero card looks up at its hero.
    actor.root.updateMatrixWorld(true);
    const width = size;
    const height = framing === 'body' ? Math.round((size * 4) / 3) : size;
    const camera = new THREE.PerspectiveCamera(28, width / height, 0.01, 100);
    const tan = Math.tan(THREE.MathUtils.degToRad(14));
    if (framing === 'body') {
        const box = new THREE.Box3().expandByObject(actor.model, true);
        const centre = box.getCenter(new THREE.Vector3());
        const distance = (box.getSize(new THREE.Vector3()).y * 0.58) / tan;
        camera.position.set(centre.x, centre.y * 0.8, centre.z + distance);
        camera.lookAt(centre);
    } else {
        const spec = actor.spec.face;
        const eyes = new THREE.Vector3(0, spec.eyes - 0.03, 0.4).applyMatrix4(actor.model.matrixWorld);
        const distance = 0.62 / tan;
        camera.position.set(eyes.x, eyes.y, eyes.z + distance);
        camera.lookAt(eyes);
    }

    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true });
    try {
        renderer.setSize(width, height, false);
        renderer.outputColorSpace = THREE.SRGBColorSpace;
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 1.0;
        renderer.setClearColor(0x000000, 0);
        renderer.render(scene, camera);
        const portrait = document.createElement('canvas');
        portrait.width = width;
        portrait.height = height;
        portrait.getContext('2d').drawImage(renderer.domElement, 0, 0);
        return portrait;
    } finally {
        actor.dispose(scene);
        renderer.dispose();
        renderer.forceContextLoss();
    }
}
