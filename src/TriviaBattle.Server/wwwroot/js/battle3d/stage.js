// The 3D stage: one WebGL canvas over the whole screen, behind the station's HTML. It draws only inside
// the box the page marks with data-stage (the full fight, or the encounter's stage while a question is
// up), so the page's own layout decides where the scene goes and how big it is. When that box moves or
// changes size (the fight giving way to the question), the picture glides after it.
//
// The look aims for "3D that reads like a painting": a warm key light, warm torches, a cool fill, soft
// round shadows under the actors, and a fixed camera, so the models only ever need to look good from one side.

import * as THREE from 'three';
import { GLTFLoader } from 'three/addons/loaders/GLTFLoader.js';
import { ROOM, TORCH_LIGHTS } from './models.js';

const loader = new GLTFLoader();
const loaded = new Map();

/** Loads a .glb once; later calls get the same promise. */
export function loadModel(file) {
    if (!loaded.has(file))
        loaded.set(file, loader.loadAsync(file));
    return loaded.get(file);
}

/** How fast the picture and the camera glide to where they're wanted (seconds to get most of the way). */
const GLIDE_SECONDS = 0.12;

export class Stage {
    constructor(container) {
        this.renderer = new THREE.WebGLRenderer({ antialias: true, alpha: true });
        this.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 2));
        this.renderer.outputColorSpace = THREE.SRGBColorSpace;
        this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
        this.renderer.toneMappingExposure = 0.95;
        this.renderer.setScissorTest(true);
        container.appendChild(this.renderer.domElement);

        this.scene = new THREE.Scene();
        this.scene.background = new THREE.Color(0x140d12);
        this.scene.fog = new THREE.Fog(0x140d12, 16, 34);

        this.camera = new THREE.PerspectiveCamera(30, 1, 0.1, 100);
        this.shot = null;      // where the camera is now: { position, target, fov }
        this.wantedShot = null;
        this.shake = new THREE.Vector2();  // set by the beat each frame, in world units
        this.rect = null;      // the box being drawn in now (glides towards the data-stage element's)

        this.addLights();
        this.addRoom();
        window.addEventListener('resize', () => this.resize());
        this.resize();
    }

    addLights() {
        this.scene.add(new THREE.HemisphereLight(0xffe4c0, 0x2a1820, 0.7));

        const key = new THREE.DirectionalLight(0xfff0d8, 1.7);
        key.position.set(4, 9, 8);
        this.scene.add(key);

        const fill = new THREE.DirectionalLight(0x8fa6ff, 0.6);
        fill.position.set(-7, 3, 3);
        this.scene.add(fill);

        this.torches = TORCH_LIGHTS.map(([x, y, z]) => {
            const light = new THREE.PointLight(0xff8a3a, 30, 14, 1.6);
            light.position.set(x, y, z);
            this.scene.add(light);
            return light;
        });
    }

    async addRoom() {
        for (const [file, x, y, z, turn] of ROOM) {
            const gltf = await loadModel(file);
            const piece = gltf.scene.clone(true);
            piece.position.set(x, y, z);
            piece.rotation.y = turn;
            this.scene.add(piece);
        }
    }

    resize() {
        this.renderer.setSize(window.innerWidth, window.innerHeight, false);
    }

    /** Where the camera should be. It glides there (snaps if it's the first shot). */
    frame(position, target, fov) {
        this.wantedShot = { position: position.clone(), target: target.clone(), fov };
        if (!this.shot)
            this.shot = { position: position.clone(), target: target.clone(), fov };
    }

    /** One frame: follow the data-stage box, glide the camera, flicker the torches, draw. */
    draw(seconds, dt) {
        const renderer = this.renderer;
        const width = window.innerWidth;
        const height = window.innerHeight;

        // Wipe the whole canvas: whatever isn't the stage box stays see-through.
        renderer.setScissor(0, 0, width, height);
        renderer.setViewport(0, 0, width, height);
        renderer.setClearColor(0x000000, 0);
        renderer.clear();

        const box = document.querySelector('[data-stage]')?.getBoundingClientRect();
        if (!box || box.width < 2 || box.height < 2 || !this.wantedShot) {
            this.rect = null;
            return;
        }

        const glide = 1 - Math.exp(-dt / GLIDE_SECONDS);
        const wanted = { x: box.left, y: box.top, w: box.width, h: box.height };
        this.rect = this.rect
            ? { x: lerp(this.rect.x, wanted.x, glide), y: lerp(this.rect.y, wanted.y, glide), w: lerp(this.rect.w, wanted.w, glide), h: lerp(this.rect.h, wanted.h, glide) }
            : wanted;
        this.shot.position.lerp(this.wantedShot.position, glide);
        this.shot.target.lerp(this.wantedShot.target, glide);
        this.shot.fov = lerp(this.shot.fov, this.wantedShot.fov, glide);

        // The torches flicker: two sine waves at odd speeds, so it never looks like a loop.
        this.torches.forEach((light, i) => {
            light.intensity = 30 + 6 * Math.sin(seconds * 9.1 + i * 2) + 4 * Math.sin(seconds * 23.7 + i);
        });

        const camera = this.camera;
        camera.position.copy(this.shot.position);
        camera.position.x += this.shake.x;
        camera.position.y += this.shake.y;
        camera.lookAt(this.shot.target.x + this.shake.x, this.shot.target.y + this.shake.y, this.shot.target.z);
        camera.fov = this.shot.fov;
        camera.aspect = this.rect.w / this.rect.h;
        camera.updateProjectionMatrix();

        // WebGL counts the box from the bottom of the canvas, the page from the top.
        const bottom = height - (this.rect.y + this.rect.h);
        renderer.setScissor(this.rect.x, bottom, this.rect.w, this.rect.h);
        renderer.setViewport(this.rect.x, bottom, this.rect.w, this.rect.h);
        renderer.render(this.scene, camera);
    }

    /** The aspect (width / height) of the box being drawn in, or of the one it's gliding to. */
    aspect() {
        const box = document.querySelector('[data-stage]')?.getBoundingClientRect();
        return box && box.height > 0 ? box.width / box.height : 9 / 16;
    }

    /**
     * The part of the data-stage box left clear for the subject (the page marks it data-focus: the gap between
     * the words floating over the scene), as shares of the box's height: { top, bottom }. The whole box when
     * nothing is marked.
     */
    focus() {
        const box = document.querySelector('[data-stage]')?.getBoundingClientRect();
        const clear = document.querySelector('[data-focus]')?.getBoundingClientRect();
        if (!box || !clear || box.height <= 0) return { top: 0, bottom: 1 };
        const share = y => Math.max(0, Math.min(1, (y - box.top) / box.height));
        const focus = { top: share(clear.top), bottom: share(clear.bottom) };
        return focus.bottom - focus.top > 0.05 ? focus : { top: 0, bottom: 1 };
    }

    /** The data-stage box's kind: 'fight' (the full beat) or 'portrait' (the encounter's stage, a question up). */
    mode() {
        return document.querySelector('[data-stage]')?.dataset.stage || null;
    }
}

function lerp(a, b, t) {
    return a + (b - a) * t;
}
