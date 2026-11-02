// One character in the fight (the hero or a monster): its model, its animations, a soft round shadow,
// and a flash when it's hit. It doesn't decide anything: every frame the beat timeline (beats.js) hands
// it a pose (where it stands, which clip at which moment, how bright the flash) and it shows exactly
// that. Clips are set to a time, never left running, so a redraw or a slow frame can't put a fight out
// of step with the beat's clock (or with the sound the server played at its start).

import * as THREE from 'three';
import * as SkeletonUtils from 'three/addons/utils/SkeletonUtils.js';
import { loadModel } from './stage.js';
import { MODELS } from './models.js';

export class Actor {
    /**
     * Builds the actor for a sprite key ('hero', 'goblin', ...). facing: which way it turns (radians round
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
            // Gear the hero isn't wearing yet stays hidden (see MODELS.hero.gear).
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
     * A player's hero photo on the hero, as a bobblehead: their round pixel face in a ring of their colour,
     * as big as the head it replaces, fixed to the head bone (so it follows every swing, hit and fall) and
     * always turned to the camera. url null takes it off and puts the head and helmet back.
     */
    async wearPhoto(url, color) {
        const spec = this.spec.photo;
        if (!spec || !this.model) return;
        this.photoUrl = url;
        if (this.photoSprite) {
            this.photoSprite.removeFromParent();
            this.photoSprite.material.map.dispose();
            this.photoSprite.material.dispose();
            this.photoSprite = null;
        }
        const parts = spec.replaces.map(name => this.model.getObjectByName(name)).filter(Boolean);
        if (!url) {
            parts.forEach(part => (part.visible = true));
            return;
        }

        const canvas = await framedPhoto(url, color);
        if (this.photoUrl !== url) return; // another photo (or none) was asked for meanwhile

        // Measured with the head showing, at the pose the model was built in.
        const bone = this.model.getObjectByName(spec.bone);
        this.model.updateMatrixWorld(true);
        const head = new THREE.Box3();
        parts.forEach(part => head.expandByObject(part, true));
        const size = head.getSize(new THREE.Vector3());
        const boneScale = bone.getWorldScale(new THREE.Vector3()).x;

        const texture = new THREE.CanvasTexture(canvas);
        texture.colorSpace = THREE.SRGBColorSpace;
        const sprite = new THREE.Sprite(new THREE.SpriteMaterial({ map: texture, transparent: true }));
        sprite.position.copy(bone.worldToLocal(head.getCenter(new THREE.Vector3())));
        sprite.scale.setScalar((Math.max(size.x, size.y) * spec.size) / boneScale);
        bone.add(sprite);
        parts.forEach(part => (part.visible = false));
        this.photoSprite = sprite;
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

/** The round photo in a ring of the player's colour, big enough to stay sharp on screen. Its pixels stay square. */
async function framedPhoto(url, color) {
    const image = new Image();
    image.src = url;
    await image.decode();
    const size = 256;
    const ring = 16;
    const canvas = document.createElement('canvas');
    canvas.width = canvas.height = size;
    const g = canvas.getContext('2d');
    const disc = (radius, fill) => {
        g.fillStyle = fill;
        g.beginPath();
        g.arc(size / 2, size / 2, radius, 0, Math.PI * 2);
        g.fill();
    };
    disc(size / 2, '#000');
    disc(size / 2 - 4, color || '#fff');
    disc(size / 2 - ring, '#000');
    g.imageSmoothingEnabled = false;
    g.drawImage(image, ring + 2, ring + 2, size - 2 * (ring + 2), size - 2 * (ring + 2));
    return canvas;
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
