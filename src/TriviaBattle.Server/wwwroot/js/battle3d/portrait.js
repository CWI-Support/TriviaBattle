// The hero's portrait for the screens that draw in 2D (the station plate, the main screen's lanes and
// line-up): the knight's head and shoulders wearing the player's face, drawn once by the station that
// took the hero photo and sent to the server with it. Lit like the battle room, on a see-through
// background, so it sits in any frame.

import * as THREE from 'three';
import { Actor } from './actor.js';

/** A size x size canvas of the knight (in the station's colour) wearing this face (a URL of a FaceCutout face). */
export async function heroPortrait(faceUrl, color, size = 256) {
    const actor = await Actor.create('hero', 0, color);
    await actor.wearFace(faceUrl);
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

    // Framed tight on the helmet, from the height of the face, so the brim doesn't shade the eyes.
    actor.root.updateMatrixWorld(true);
    const head = new THREE.Box3().expandByObject(actor.model.getObjectByName('Knight_Helmet') || actor.model, true);
    const centre = head.getCenter(new THREE.Vector3());
    const height = head.getSize(new THREE.Vector3()).y;
    centre.y -= height * 0.08;
    const camera = new THREE.PerspectiveCamera(28, 1, 0.01, 100);
    const distance = (height * 0.56) / Math.tan(THREE.MathUtils.degToRad(14));
    camera.position.set(centre.x, centre.y, centre.z + distance);
    camera.lookAt(centre);

    const renderer = new THREE.WebGLRenderer({ alpha: true, antialias: true, preserveDrawingBuffer: true });
    try {
        renderer.setSize(size, size, false);
        renderer.outputColorSpace = THREE.SRGBColorSpace;
        renderer.toneMapping = THREE.ACESFilmicToneMapping;
        renderer.toneMappingExposure = 1.0;
        renderer.setClearColor(0x000000, 0);
        renderer.render(scene, camera);
        const portrait = document.createElement('canvas');
        portrait.width = portrait.height = size;
        portrait.getContext('2d').drawImage(renderer.domElement, 0, 0);
        return portrait;
    } finally {
        actor.dispose(scene);
        renderer.dispose();
        renderer.forceContextLoss();
    }
}
