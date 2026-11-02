// One character in the fight (the hero or a monster): its model, its animations, a soft round shadow,
// and a flash when it's hit. It doesn't decide anything: every frame the beat timeline (beats.js) hands
// it a pose (where it stands, which clip at which moment, how bright the flash) and it shows exactly
// that. Clips are set to a time, never left running, so a redraw or a slow frame can't put a fight out
// of step with the beat's clock (or with the sound the server played at its start).

import * as THREE from 'three';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';
import { loadModel } from './stage.js';
import { MODELS } from './models.js';
import { paintFace } from './face.js';

export class Actor {
    /**
     * Builds the actor for a model key (a hero's, 'knight', or a monster's sprite key, 'goblin', ...). facing: which way it turns (radians round
     * the vertical; 0 = towards the camera). color: the station's colour, for the hero's tinted part.
     */
    static async create(key, facing, color) {
        const spec = MODELS[key];
        const actor = new Actor(key, spec);
        if (spec) await actor.buildModel(spec, facing, color);
        else await actor.buildPicture(key);
        actor.addShadow();
        return actor;
    }

    constructor(key, spec) {
        this.key = key;
        this.spec = spec || {};
        this.root = new THREE.Group();     // stands on the floor: the beat moves it left and right
        this.holder = new THREE.Group();   // inside it: lifted, tipped and squashed by the beat
        this.root.add(this.holder);
        this.materials = [];
        this.actions = new Map();
        this.mixer = null;
        this.width = 1;
        this.zAt = null;   // the fight is staged on a slant: given x, how far back it stands (set by battle3d.js)
    }

    async buildModel(spec, facing, color) {
        const gltf = await loadModel(spec.file);
        const model = SkeletonUtils.clone(gltf.scene);
        this.clips = new Map(gltf.animations.map(clip => [clip.name, clip]));
        this.mixer = new THREE.AnimationMixer(model);

        model.traverse(part => {
            if (!part.isMesh) return;
            // Gear the hero isn't wearing yet stays hidden (see HEROES in models.js).
            if (spec.allGear?.includes(part.name)) part.visible = spec.gear.includes(part.name);
            // Each actor gets its own materials, so a flash or a tint only touches this one.
            part.material = part.material.clone();
            if (part.name === spec.tinted && color) part.material.color.set(color).lerp(new THREE.Color(0xffffff), 0.25);
            part.frustumCulled = false; // skinned parts can swing outside their resting box
            this.materials.push(part.material);
        });

        // Stand it up at its height, feet on the floor, centred, facing the other fighter.
        this.pose({ clip: spec.idle, time: 0 }, model);
        const box = visibleBox(model);
        const size = box.getSize(new THREE.Vector3());
        const scale = spec.height / Math.max(size.y, 0.001);
        model.scale.setScalar(scale);
        const centre = box.getCenter(new THREE.Vector3());
        const turn = facing + (spec.turn || 0);
        const pivot = new THREE.Group();
        model.position.set(-centre.x * scale, -box.min.y * scale, -centre.z * scale);
        pivot.add(model);
        pivot.rotation.y = turn;
        this.holder.add(pivot);
        this.pivot = pivot;
        this.model = model;
        this.width = Math.max(size.x, size.z) * scale;
    }

    /** No model for this monster yet: its flat 2D picture, standing in the room like a cut-out. */
    async buildPicture(key) {
        const isPicture = /^(\/|https?:)/.test(key || '');
        const url = isPicture ? key : PixelSprites.url(key);
        const texture = await new THREE.TextureLoader().loadAsync(url);
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.magFilter = isPicture ? THREE.LinearFilter : THREE.NearestFilter; // pixel art stays crisp
        const height = 2;
        const aspect = texture.image.width / texture.image.height;
        const material = new THREE.MeshStandardMaterial({ map: texture, transparent: true, alphaTest: 0.1, side: THREE.DoubleSide });
        const plane = new THREE.Mesh(new THREE.PlaneGeometry(height * aspect, height), material);
        plane.position.y = height / 2;
        this.holder.add(plane);
        this.materials.push(material);
        this.width = height * aspect;
    }

    addShadow() {
        const shadow = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({ map: shadowTexture(), transparent: true, depthWrite: false }));
        shadow.rotation.x = -Math.PI / 2;
        shadow.position.y = 0.06;
        shadow.scale.set(this.width * 1.3, this.width * 0.8, 1);
        this.root.add(shadow);
        this.shadow = shadow;
    }

    /**
     * Shows one moment. pose = { x, lift, tilt, squash, scale, visible, clip, time, from, fade, flash, flashColor }:
     *   clip/time    the animation and how far into it (seconds; loops for idle, holds the last frame otherwise)
     *   from/fade    the clip it's blending out of ({ clip, time }) and how far the blend has got (0..1)
     *   flash        0..1, how strongly it glows flashColor (hit, or hurt)
     */
    apply(pose) {
        this.root.visible = pose.visible !== false;
        this.root.position.x = pose.x ?? this.root.position.x;
        if (this.zAt) this.root.position.z = this.zAt(this.root.position.x);
        this.holder.position.y = (this.spec.hover || 0) + (pose.lift || 0);
        this.holder.rotation.z = pose.tilt || 0;
        const scale = pose.scale ?? 1;
        const squash = pose.squash || 0;
        this.holder.scale.set(scale * (1 + squash), scale * (1 - squash), scale * (1 + squash));
        this.shadow.material.opacity = Math.max(0, 0.7 - (pose.lift || 0) * 0.3) * Math.min(1, scale);

        const flash = pose.flash || 0;
        for (const material of this.materials) {
            if (!material.emissive) continue;
            material.emissive.set(pose.flashColor || 0xffffff);
            material.emissiveIntensity = flash * 0.9;
        }
        if (this.mixer && pose.clip) this.pose(pose, this.model);
    }

    /** Sets the skeleton to a clip at a time, blended from another clip while fade < 1. */
    pose({ clip, time, from, fade = 1 }, model) {
        for (const action of this.actions.values()) action.setEffectiveWeight(0);
        const main = this.action(clip, model);
        if (!main) return;
        const old = from && fade < 1 ? this.action(from.clip, model) : null;
        main.setEffectiveWeight(old ? fade : 1);
        main.time = clipTime(main, time, clip === this.spec.idle);
        if (old) {
            old.setEffectiveWeight(1 - fade);
            old.time = clipTime(old, from.time, from.clip === this.spec.idle);
        }
        this.mixer.update(0);
    }

    action(name, model) {
        if (!name || !this.clips?.has(name)) return null;
        if (!this.actions.has(name)) {
            const action = this.mixer.clipAction(this.clips.get(name), model);
            action.play();
            this.actions.set(name, action);
        }
        return this.actions.get(name);
    }

    /**
     * A player's hero photo on the hero: their cut-out face painted onto the front of its head, under the
     * helmet (js/battle3d/face.js). url: the face from js/face-cutout.js; null takes it off again.
     */
    async wearFace(url) {
        if (!this.spec.face || !this.model) return;
        this.faceUrl = url;
        if (this.painted) {
            this.materials = this.materials.filter(m => m !== this.painted.mesh.material);
            this.painted.takeOff();
            this.painted = null;
        }
        if (!url) return;

        const face = new Image();
        face.src = url;
        await face.decode();
        if (this.faceUrl !== url) return; // another face (or none) was asked for meanwhile
        this.painted = paintFace(this.model, this.spec.face, face);
        this.materials.push(this.painted.mesh.material); // it flashes with the rest when hit
    }

    /**
     * A rival's face on a monster (in a race or a battle, the monsters you fight wear your opponents'
     * faces): their cut-out face (js/face-cutout.js) as a mask, always turned to the camera and drawn over
     * the monster, fixed to its head bone so it follows every bite and swing. url null takes it off.
     */
    async wearMask(url) {
        this.maskUrl = url;
        if (this.mask) {
            this.mask.removeFromParent();
            this.mask.material.map.dispose();
            this.mask.material.dispose();
            this.mask = null;
        }
        if (!url || !this.model) return;

        const face = new Image();
        face.src = url;
        await face.decode();
        if (this.maskUrl !== url) return; // another face (or none) was asked for meanwhile

        const texture = new THREE.Texture(face);
        texture.colorSpace = THREE.SRGBColorSpace;
        texture.needsUpdate = true;
        const mask = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true, depthTest: false }));
        mask.renderOrder = 10;
        const size = (this.spec.height || 2) * (this.spec.mask ?? 0.5);
        const head = findHead(this.model);
        if (head) {
            this.model.updateMatrixWorld(true);
            const at = head.getWorldPosition(new THREE.Vector3()).add(new THREE.Vector3(0, size * 0.15, 0));
            mask.position.copy(head.worldToLocal(at));
            mask.scale.setScalar(size / head.getWorldScale(new THREE.Vector3()).x);
            head.add(mask);
        } else {
            mask.position.set(0, (this.spec.height || 2) * 0.7, 0);
            mask.scale.setScalar(size);
            this.holder.add(mask);
        }
        this.mask = mask;
    }

    /** Turns it to face this way (radians round the vertical; 0 = the camera). A cut-out always faces the camera. */
    face(angle) {
        if (this.pivot) this.pivot.rotation.y = angle + (this.spec.turn || 0);
    }

    /** True if the model has this clip (monsters without a hurt or death clip get knocked back or burst instead). */
    has(clip) {
        return !!clip && !!this.clips?.has(clip);
    }

    /** A clip's length in seconds (0 if the model doesn't have it). */
    duration(clip) {
        return this.has(clip) ? this.clips.get(clip).duration : 0;
    }

    /** Where a hit lands on it (its chest), for sparks and the slash. */
    chest() {
        const height = this.spec.height || 2;
        return new THREE.Vector3(this.root.position.x, (this.spec.hover || 0) + height * 0.55, this.root.position.z + 0.3);
    }

    dispose(scene) {
        scene.remove(this.root);
        for (const material of this.materials) material.dispose();
    }
}

/** The model's head bone ("Head" or "head" in every rig so far), or null. */
function findHead(model) {
    let head = null;
    model.traverse(part => {
        if (!head && part.isBone && /^head$/i.test(part.name)) head = part;
    });
    return head;
}

/** A looping clip wraps round; any other clip holds its last frame. */
function clipTime(action, time, loop) {
    const duration = action.getClip().duration;
    if (loop) return ((time % duration) + duration) % duration;
    return Math.min(Math.max(time, 0), duration - 0.001);
}

/** The box round the parts that are showing (hidden gear would make the knight look taller than it is). */
function visibleBox(model) {
    model.updateMatrixWorld(true);
    const box = new THREE.Box3();
    model.traverse(part => {
        if (part.isMesh && part.visible) box.expandByObject(part, true);
    });
    return box;
}

let shadowCanvas = null;
/** A soft dark blob, drawn once and shared. */
function shadowTexture() {
    if (!shadowCanvas) {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 64;
        const g = canvas.getContext('2d');
        const gradient = g.createRadialGradient(32, 32, 0, 32, 32, 32);
        gradient.addColorStop(0, 'rgba(0,0,0,0.85)');
        gradient.addColorStop(1, 'rgba(0,0,0,0)');
        g.fillStyle = gradient;
        g.fillRect(0, 0, 64, 64);
        shadowCanvas = new THREE.CanvasTexture(canvas);
    }
    return shadowCanvas;
}
