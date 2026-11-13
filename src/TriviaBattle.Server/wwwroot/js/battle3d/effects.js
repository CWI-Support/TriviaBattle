// The fight's effects: sparks, the sword's slash, the claw marks, the shockwave on the floor, the puff of
// smoke and the shower of gold when a monster dies, and the hero classes' own: the Wizard's spell reaching
// the monsters waiting in the room, the Healer's poison bubbling on a monster and its heal sparkling on a
// hero, the Warlock's patron draining HP; and a boss's: its rune circle glowing on the floor under it, its charge
// crackling round it while a question is up,
// the dazed stars when a right answer at full charge staggers it. Cartoon, never gory (the design doc): getting hurt
// throws out stars, not blood. Like the actors, they're shown at a moment, not
// played: every frame starts with everything hidden, then the beat asks for each effect at its age
// ("sparks, 120 ms after the impact"). Particle paths come from the beat's seed, so the same moment
// always looks the same, however often the screen redraws.

import * as THREE from 'three';

/** One burst of particles flying out from a point and falling, then fading. */
export class Burst {
    constructor(scene, { count, color, size, speed, rise, gravity, life, additive = true }) {
        this.count = count;
        this.speed = speed;
        this.rise = rise;
        this.gravity = gravity;
        this.life = life;
        this.positions = new Float32Array(count * 3);
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3));
        this.points = new THREE.Points(geometry, new THREE.PointsMaterial({
            color, size, map: dotTexture(), transparent: true, depthWrite: false,
            blending: additive ? THREE.AdditiveBlending : THREE.NormalBlending,
        }));
        this.points.frustumCulled = false;
        this.points.visible = false;
        scene.add(this.points);
    }

    /** Shows the burst age seconds after it went off at origin. */
    show(origin, age, seed) {
        if (age < 0 || age > this.life) return;
        const random = seededRandom(seed);
        for (let i = 0; i < this.count; i++) {
            // Each particle: a direction round the circle (mostly sideways and up, as it faces the camera)
            // and a speed, both from the seed.
            const angle = random() * Math.PI * 2;
            const speed = this.speed * (0.45 + random() * 0.75);
            const vx = Math.cos(angle) * speed;
            const vy = Math.sin(angle) * speed * 0.8 + this.rise * (0.5 + random());
            const vz = (random() - 0.3) * speed * 0.6;
            this.positions[i * 3] = origin.x + vx * age;
            this.positions[i * 3 + 1] = Math.max(0.05, origin.y + vy * age - 0.5 * this.gravity * age * age);
            this.positions[i * 3 + 2] = origin.z + vz * age;
        }
        this.points.geometry.attributes.position.needsUpdate = true;
        this.points.material.opacity = 1 - Math.pow(age / this.life, 2);
        this.points.visible = true;
    }

    hide() {
        this.points.visible = false;
    }
}

export class Effects {
    constructor(scene) {
        this.impact = new Burst(scene, { count: 26, color: 0xfff1b0, size: 0.16, speed: 7, rise: 1.5, gravity: 9, life: 0.45 });
        this.bigImpact = new Burst(scene, { count: 44, color: 0xffd23f, size: 0.22, speed: 10, rise: 2, gravity: 9, life: 0.6 });
        this.hurtStars = new Burst(scene, { count: 20, color: 0xff7a3b, size: 0.18, speed: 6, rise: 1.5, gravity: 10, life: 0.5 });
        this.smoke = new Burst(scene, { count: 24, color: 0x9a8a9c, size: 0.9, speed: 1.6, rise: 1.2, gravity: -0.5, life: 0.9, additive: false });
        this.gold = new Burst(scene, { count: 30, color: 0xffc83a, size: 0.26, speed: 5, rise: 6, gravity: 16, life: 1.1 });

        this.slash = flatMesh(scene, new THREE.RingGeometry(1.2, 1.4, 32, 1, -Math.PI * 0.42, Math.PI * 0.84), 0xffe9b0);
        this.shockwave = flatMesh(scene, new THREE.RingGeometry(0.75, 1, 40), 0xffe6a0);
        this.shockwave.rotation.x = -Math.PI / 2;
        this.claws = [0, 1, 2].map(() => flatMesh(scene, new THREE.PlaneGeometry(2.2, 0.12), 0xff2a2a));
        // A Pugilist's combo of 3 or more cuts back across the first slash.
        this.crossSlash = flatMesh(scene, new THREE.RingGeometry(1.2, 1.4, 32, 1, -Math.PI * 0.42, Math.PI * 0.84), 0xffd080);

        // The pack in the room (js/battle3d/pack.js): one of each per waiting monster.
        this.bolts = [0, 1, 2].map(() => new Bolt(scene));
        this.spellHits = [0, 1, 2].map(() => new Burst(scene, { count: 18, color: 0x9fe0ff, size: 0.2, speed: 5, rise: 1.5, gravity: 6, life: 0.45 }));
        this.packSmoke = [0, 1, 2].map(() => new Burst(scene, { count: 14, color: 0x9a8a9c, size: 0.7, speed: 1.4, rise: 1.1, gravity: -0.5, life: 0.9, additive: false }));
        this.packGold = [0, 1, 2].map(() => new Burst(scene, { count: 14, color: 0xffc83a, size: 0.22, speed: 4, rise: 5, gravity: 16, life: 1.0 }));
        // Poison: bubbles on the monster in front (0) and on each waiting one (1-3); a green puff on the hit that adds a stack.
        this.bubbles = [0, 1, 2, 3].map(() => new Bubbles(scene));
        this.poisonPuff = new Burst(scene, { count: 22, color: 0x5dff6e, size: 0.3, speed: 3, rise: 1.2, gravity: -1, life: 0.7 });
        this.heal = new HealSparkle(scene);
        this.drain = new Burst(scene, { count: 28, color: 0xb050ff, size: 0.24, speed: 1.1, rise: 2.2, gravity: -3, life: 0.8 });
        // A boss: its charge crackling round it, and the stars round its head when it's staggered.
        this.embers = new Embers(scene);
        this.daze = new Daze(scene);
        this.slamDust = new Burst(scene, { count: 30, color: 0xb9a48c, size: 0.7, speed: 3.2, rise: 0.6, gravity: 1, life: 0.7, additive: false });
        this.bossCircle = new BossCircle(scene);

        this.all = [this.slash, this.crossSlash, this.shockwave, ...this.claws];
        this.bursts = [this.impact, this.bigImpact, this.hurtStars, this.smoke, this.gold, this.poisonPuff, this.drain,
            ...this.bolts, ...this.spellHits, ...this.packSmoke, ...this.packGold, ...this.bubbles, this.heal, this.embers, this.daze, this.slamDust, this.bossCircle];
    }

    /** Called first every frame: nothing shows unless the beat asks for it again. */
    hideAll() {
        for (const mesh of this.all) mesh.visible = false;
        for (const burst of this.bursts) burst.hide();
    }

    /**
     * The sword's arc sweeping across the monster: age in seconds, from when the swing starts to cut. size: 1
     * for a plain hit, more for a big one. cross: the second cut of a big combo, sweeping back the other way.
     */
    showSlash(at, age, tilt, size, cross = false) {
        const life = 0.22;
        if (age < 0 || age > life) return;
        const p = age / life;
        const slash = cross ? this.crossSlash : this.slash;
        slash.position.copy(at);
        slash.rotation.set(0, 0, cross ? -tilt + 1.6 - p * 2.2 : tilt - 1.4 + p * 2.2);  // it sweeps round as it fades
        slash.scale.setScalar(size);
        slash.material.opacity = 1 - p * p;
        slash.visible = true;
    }

    /** A ring racing out across the floor from under whoever was hit. power: how much further it goes (a combo). */
    showShockwave(at, age, big, power = 1) {
        const life = 0.4;
        if (age < 0 || age > life) return;
        const p = age / life;
        this.shockwave.position.set(at.x, 0.08, at.z);
        this.shockwave.scale.setScalar((big ? 2.8 : 2) * power * (0.2 + p));
        this.shockwave.material.opacity = 0.5 * (1 - p);
        this.shockwave.visible = true;
    }

    /** Three claw marks raked across the hero, in a hurry, then fading. */
    showClaws(at, age) {
        const life = 0.5;
        if (age < 0 || age > life) return;
        const draw = Math.min(1, age / 0.08);  // they rake across in 80 ms
        this.claws.forEach((claw, i) => {
            claw.position.set(at.x + (i - 1) * 0.3, at.y + (1 - i) * 0.32, at.z + 0.6);
            claw.rotation.set(0, 0, -0.75);
            claw.scale.set(draw, 1, 1);
            claw.material.opacity = 1 - Math.max(0, (age - 0.15) / (life - 0.15));
            claw.visible = true;
        });
    }

    showSparks(at, age, seed, big) {
        (big ? this.bigImpact : this.impact).show(at, age, seed);
    }

    showHurt(at, age, seed) {
        this.hurtStars.show(at, age, seed);
    }

    /** A monster bursting: a puff of smoke and a shower of gold (loot, as the design doc wants power to feel earned). */
    showDeath(at, age, seed) {
        this.smoke.show(at, age, seed);
        this.gold.show(at, age, seed + 1);
    }

    /** The poison killing the monster in front between beats: it goes up in smoke and a green puff (no beat, so no loot). */
    showPoisonDeath(at, age, seed) {
        this.smoke.show(at, age, seed);
        this.poisonPuff.show(at, age, seed + 1);
    }

    /** The Wizard's spell flying to waiting monster i: p goes 0 (leaves from) to 1 (lands on to). */
    showBolt(i, from, to, p) {
        this.bolts[i]?.show(from, to, p);
    }

    /** The spell landing on waiting monster i: blue sparks. */
    showSpellHit(i, at, age, seed) {
        this.spellHits[i]?.show(at, age, seed);
    }

    /** Waiting monster i killed by the spell: a smaller puff and shower than the one in front's. */
    showPackDeath(i, at, age, seed) {
        this.packSmoke[i]?.show(at, age, seed);
        this.packGold[i]?.show(at, age, seed + 1);
    }

    /** Poison bubbling up on a monster (0 = the one in front, 1-3 the waiting ones): more and bigger with more stacks. */
    showPoison(i, actor, seconds, stacks) {
        this.bubbles[i]?.show(actor, seconds, stacks);
    }

    /** The hit that poisons: a green puff on the monster. */
    showPoisonPuff(at, age, seed) {
        this.poisonPuff.show(at, age, seed);
    }

    /** A Healer's heal landing on your hero. */
    showHeal(at, age) {
        this.heal.show(at, age);
    }

    /** The Warlock's patron taking its price: purple wisps drawn up out of the hero. */
    showDrain(at, age, seed) {
        this.drain.show(at, age, seed);
    }

    /** A boss's charge crackling up round it (level 0..1; full: at full charge, red and thick). */
    showCharge(actor, seconds, level, full) {
        this.embers.show(actor, seconds, level, full);
    }

    /** A staggered boss: stars circling over its head (at), age seconds after they appear. */
    showDaze(at, age) {
        this.daze.show(at, age);
    }

    /** A boss's charged blow slamming down: a ring of dust thrown up round where it lands. */
    showSlamDust(at, age, seed) {
        this.slamDust.show(at, age, seed);
    }

    /** A boss's rune circle glowing on the floor under it (level: its charge, 0..1; it burns brighter as it builds). */
    showBossCircle(actor, seconds, level) {
        this.bossCircle.show(actor, seconds, level);
    }
}

/**
 * A boss's rune circle: a ring of runes glowing on the floor under it, turning slowly and breathing, brighter as its
 * charge builds (from the server clock's seconds, like the embers, so a redraw doesn't jump it). Hidden with the boss.
 */
class BossCircle {
    constructor(scene) {
        this.mesh = new THREE.Mesh(new THREE.PlaneGeometry(1, 1), new THREE.MeshBasicMaterial({
            map: runeCircleTexture(), color: 0xff4a1a, transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
        }));
        this.mesh.rotation.x = -Math.PI / 2;
        this.mesh.visible = false;
        this.mesh.renderOrder = 1;
        scene.add(this.mesh);
    }

    show(actor, seconds, level) {
        if (!actor?.root.visible) return;
        const size = Math.max(2.2, actor.width * 2.4) * actor.holder.scale.x;
        const { x, z } = actor.root.position;
        this.mesh.position.set(x, 0.07, z); // just over the floor tiles (the shadows are at 0.06)
        this.mesh.scale.set(size, size, 1);
        this.mesh.rotation.z = seconds * 0.25;
        this.mesh.material.opacity = (0.35 + 0.45 * level) * (0.8 + 0.2 * Math.sin(seconds * 2.2));
        this.mesh.visible = true;
    }

    hide() {
        this.mesh.visible = false;
    }
}

/**
 * A boss's charge: embers crackling up round it, on a loop (from the server clock's seconds, so a redraw
 * doesn't restart them). More, bigger and redder as the charge rises; at full charge thick and red, flickering.
 */
class Embers {
    constructor(scene) {
        this.max = 44;
        this.positions = new Float32Array(this.max * 3);
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3));
        this.points = new THREE.Points(geometry, new THREE.PointsMaterial({
            color: 0xff8a2a, size: 0.16, map: dotTexture(), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
        }));
        this.points.frustumCulled = false;
        this.points.visible = false;
        scene.add(this.points);
        const random = seededRandom(91);
        this.embers = Array.from({ length: this.max }, () => ({ period: 0.45 + random() * 0.6, phase: random(), angle: random() * Math.PI * 2, wobble: random() * 6 }));
    }

    show(actor, seconds, level, full) {
        if (!level || !actor?.root.visible) return;
        const count = Math.min(this.max, Math.round(8 + 36 * level));
        const scale = actor.holder.scale.y;
        const height = (actor.spec.height || 2) * scale;
        const base = actor.spec.hover || 0;
        const { x, z } = actor.root.position;
        const radius = actor.width * 0.55 * scale;
        this.embers.slice(0, count).forEach((e, i) => {
            const p = ((seconds / e.period + e.phase) % 1 + 1) % 1;
            // Each rises round the body, drawn in towards it as it goes (like heat being pulled in).
            const r = radius * (1.1 - 0.5 * p);
            const angle = e.angle + Math.sin(seconds * 2 + e.wobble) * 0.3;
            this.positions[i * 3] = x + Math.cos(angle) * r;
            this.positions[i * 3 + 1] = base + p * height * 1.05;
            this.positions[i * 3 + 2] = z + 0.2 + Math.sin(angle) * r * 0.6;
        });
        this.points.geometry.setDrawRange(0, count);
        this.points.geometry.attributes.position.needsUpdate = true;
        this.points.material.color.set(full ? 0xff2a1a : 0xff8a2a);
        this.points.material.size = full ? 0.3 : 0.14 + 0.1 * level;
        // Full charge flickers fast, like it can barely hold it in.
        this.points.material.opacity = full ? 0.75 + 0.25 * Math.sin(seconds * 40) : 0.5 + 0.4 * level;
        this.points.visible = true;
    }

    hide() {
        this.points.visible = false;
    }
}

/** Dazed stars circling over a staggered boss's head, fading in and out. Shown at an age, like the bursts. */
class Daze {
    constructor(scene) {
        this.count = 4;
        this.life = 0.95;
        this.positions = new Float32Array(this.count * 3);
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3));
        this.points = new THREE.Points(geometry, new THREE.PointsMaterial({
            color: 0xfff3a0, size: 0.55, map: starTexture(), transparent: true, depthWrite: false,
        }));
        this.points.frustumCulled = false;
        this.points.visible = false;
        this.points.renderOrder = 11;
        scene.add(this.points);
    }

    show(at, age) {
        if (age < 0 || age > this.life) return;
        for (let i = 0; i < this.count; i++) {
            const angle = (age / 0.7) * Math.PI * 2 + (i / this.count) * Math.PI * 2;
            this.positions[i * 3] = at.x + Math.cos(angle) * 0.6;
            this.positions[i * 3 + 1] = at.y + Math.sin(angle) * 0.08;
            this.positions[i * 3 + 2] = at.z + Math.sin(angle) * 0.35;
        }
        this.points.geometry.attributes.position.needsUpdate = true;
        const p = age / this.life;
        this.points.material.opacity = p < 0.1 ? p / 0.1 : p > 0.85 ? (1 - p) / 0.15 : 1;
        this.points.visible = true;
    }

    hide() {
        this.points.visible = false;
    }
}

/**
 * A spell flying on an arc from one point to another: a bright head with a short trail behind it (points
 * a little further back along the same arc). Shown at a point of its flight, like everything here.
 */
class Bolt {
    constructor(scene) {
        this.count = 9;
        this.positions = new Float32Array(this.count * 3);
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3));
        this.points = new THREE.Points(geometry, new THREE.PointsMaterial({
            color: 0x4fa8ff, size: 0.4, map: dotTexture(), transparent: true, depthWrite: false, blending: THREE.AdditiveBlending,
        }));
        this.points.frustumCulled = false;
        this.points.visible = false;
        this.points.renderOrder = 10;
        scene.add(this.points);
    }

    show(from, to, p) {
        if (p < 0 || p > 1) return;
        for (let k = 0; k < this.count; k++) {
            const q = Math.max(0, p - k * 0.04);
            this.positions[k * 3] = from.x + (to.x - from.x) * q;
            this.positions[k * 3 + 1] = from.y + (to.y - from.y) * q + Math.sin(Math.PI * q) * 0.9;  // it arcs over
            this.positions[k * 3 + 2] = from.z + (to.z - from.z) * q;
        }
        this.points.geometry.attributes.position.needsUpdate = true;
        this.points.visible = true;
    }

    hide() {
        this.points.visible = false;
    }
}

/**
 * Poison bubbles rising round a monster, on a loop (from the server clock's seconds, so a redraw doesn't
 * restart them). Each bubble has its own speed and place from a fixed seed; more stacks show more bubbles,
 * a little bigger.
 */
class Bubbles {
    constructor(scene) {
        this.max = 26;
        this.positions = new Float32Array(this.max * 3);
        const geometry = new THREE.BufferGeometry();
        geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3));
        this.points = new THREE.Points(geometry, new THREE.PointsMaterial({
            color: 0x6dff5a, size: 0.16, map: bubbleTexture(), transparent: true, depthWrite: false, opacity: 0.9,
        }));
        this.points.frustumCulled = false;
        this.points.visible = false;
        scene.add(this.points);
        const random = seededRandom(77);
        this.bubbles = Array.from({ length: this.max }, () => ({ period: 1.1 + random() * 0.9, phase: random(), x: random() - 0.5, z: random() - 0.5, wobble: random() * 6 }));
    }

    /** actor: the monster (where it stands and how big it is this frame); stacks 0 shows nothing. */
    show(actor, seconds, stacks) {
        if (!stacks || !actor?.root.visible) return;
        const count = Math.min(this.max, 4 + stacks * 5);
        const scale = actor.holder.scale.y;
        const height = (actor.spec.height || 2) * scale;
        const base = (actor.spec.hover || 0) + height * 0.2;
        const { x, z } = actor.root.position;
        this.bubbles.slice(0, count).forEach((b, i) => {
            const p = ((seconds / b.period + b.phase) % 1 + 1) % 1;
            this.positions[i * 3] = x + b.x * actor.width * 0.9 * scale + Math.sin(seconds * 3 + b.wobble) * 0.06;
            this.positions[i * 3 + 1] = base + p * height * 0.95;
            this.positions[i * 3 + 2] = z + 0.25 + b.z * 0.5;
        });
        this.points.geometry.setDrawRange(0, count);
        this.points.geometry.attributes.position.needsUpdate = true;
        this.points.material.size = Math.min(0.32, 0.14 + stacks * 0.03);
        this.points.visible = true;
    }

    hide() {
        this.points.visible = false;
    }
}

/**
 * A heal landing on a hero: green sparkles drifting up out of them and a soft green ring rising from their
 * feet to their chest. Exported for the teammates in your room (js/battle3d/others.js), one each.
 */
export class HealSparkle {
    constructor(scene) {
        this.life = 1.0;
        this.scene = scene;
        // Normal blending, not additive: added light turns green white on the warm, bright room.
        this.sparks = new Burst(scene, { count: 30, color: 0x38f070, size: 0.26, speed: 1.5, rise: 1.6, gravity: -1.5, life: this.life, additive: false });
        this.ring = flatMesh(scene, new THREE.RingGeometry(0.5, 0.78, 40), 0x30ff70);
        this.ring.rotation.x = -Math.PI / 2;
    }

    /** at: the hero's chest; age in seconds since the heal landed. */
    show(at, age) {
        this.hide();
        if (age < 0 || age > this.life) return;
        this.sparks.show(at, age, 31);
        const p = age / this.life;
        const rise = 1 - Math.pow(1 - Math.min(1, p * 1.6), 2);
        this.ring.position.set(at.x, 0.1 + (at.y - 0.1) * rise, at.z - 0.3);
        this.ring.scale.setScalar(1 + 0.3 * p);
        this.ring.material.opacity = 0.7 * (1 - p);
        this.ring.visible = true;
    }

    hide() {
        this.sparks.hide();
        this.ring.visible = false;
    }

    dispose() {
        this.scene.remove(this.sparks.points, this.ring);
    }
}

/** A flat shape that glows (adds light), always drawn over the room. */
function flatMesh(scene, geometry, color) {
    const mesh = new THREE.Mesh(geometry, new THREE.MeshBasicMaterial({
        color, transparent: true, depthWrite: false, side: THREE.DoubleSide, blending: THREE.AdditiveBlending,
    }));
    mesh.visible = false;
    mesh.renderOrder = 10;
    scene.add(mesh);
    return mesh;
}

/** A small random-number generator that gives the same numbers for the same seed (mulberry32). */
export function seededRandom(seed) {
    let a = Math.floor(seed) >>> 0;
    return () => {
        a = (a + 0x6d2b79f5) >>> 0;
        let t = a;
        t = Math.imul(t ^ (t >>> 15), t | 1);
        t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
        return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    };
}

let bubble = null;
/** A bubble: a thin ring with a glint, drawn once. */
function bubbleTexture() {
    if (!bubble) {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 32;
        const g = canvas.getContext('2d');
        g.beginPath();
        g.arc(16, 16, 12, 0, Math.PI * 2);
        g.fillStyle = 'rgba(255,255,255,0.3)';
        g.fill();
        g.strokeStyle = 'rgba(255,255,255,0.95)';
        g.lineWidth = 3;
        g.stroke();
        g.beginPath();
        g.arc(11, 11, 3, 0, Math.PI * 2);
        g.fillStyle = 'rgba(255,255,255,1)';
        g.fill();
        bubble = new THREE.CanvasTexture(canvas);
    }
    return bubble;
}

let star = null;
/** A five-pointed cartoon star with a dark edge, drawn once (the dazed stars). */
function starTexture() {
    if (!star) {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 64;
        const g = canvas.getContext('2d');
        g.beginPath();
        for (let i = 0; i < 10; i++) {
            const r = i % 2 ? 12 : 28;
            const a = -Math.PI / 2 + (i * Math.PI) / 5;
            g.lineTo(32 + Math.cos(a) * r, 34 + Math.sin(a) * r);
        }
        g.closePath();
        g.fillStyle = '#ffffff';
        g.fill();
        g.lineWidth = 4;
        g.strokeStyle = 'rgba(60, 40, 0, 0.9)';
        g.stroke();
        star = new THREE.CanvasTexture(canvas);
    }
    return star;
}

let dot = null;
/** A soft round dot for particles, drawn once. */
function dotTexture() {
    if (!dot) {
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = 32;
        const g = canvas.getContext('2d');
        const gradient = g.createRadialGradient(16, 16, 0, 16, 16, 16);
        gradient.addColorStop(0, 'rgba(255,255,255,1)');
        gradient.addColorStop(0.4, 'rgba(255,255,255,0.9)');
        gradient.addColorStop(1, 'rgba(255,255,255,0)');
        g.fillStyle = gradient;
        g.fillRect(0, 0, 32, 32);
        dot = new THREE.CanvasTexture(canvas);
    }
    return dot;
}

let runeCircle = null;

/** The boss's rune circle, drawn once: a soft glow in the middle, two rings, and runes between them. */
function runeCircleTexture() {
    if (!runeCircle) {
        const size = 256, c = size / 2;
        const canvas = document.createElement('canvas');
        canvas.width = canvas.height = size;
        const g = canvas.getContext('2d');
        const glow = g.createRadialGradient(c, c, 0, c, c, c);
        glow.addColorStop(0, 'rgba(255,255,255,0.35)');
        glow.addColorStop(0.7, 'rgba(255,255,255,0.08)');
        glow.addColorStop(1, 'rgba(255,255,255,0)');
        g.fillStyle = glow;
        g.fillRect(0, 0, size, size);
        g.strokeStyle = 'rgba(255,255,255,0.95)';
        g.lineWidth = 5;
        for (const r of [0.92, 0.72]) {
            g.beginPath();
            g.arc(c, c, c * r, 0, Math.PI * 2);
            g.stroke();
        }
        // Runes round the band between the rings: little angular marks, each turned to face the middle.
        g.lineWidth = 4;
        const marks = 12;
        for (let i = 0; i < marks; i++) {
            g.save();
            g.translate(c, c);
            g.rotate((i / marks) * Math.PI * 2);
            g.translate(0, -c * 0.82);
            g.beginPath();
            const kind = i % 3;
            if (kind === 0) { g.moveTo(-8, 8); g.lineTo(0, -8); g.lineTo(8, 8); }
            else if (kind === 1) { g.moveTo(0, -9); g.lineTo(0, 9); g.moveTo(-7, -2); g.lineTo(7, 4); }
            else { g.moveTo(-7, -7); g.lineTo(7, 7); g.moveTo(7, -7); g.lineTo(-7, 7); }
            g.stroke();
            g.restore();
        }
        runeCircle = new THREE.CanvasTexture(canvas);
    }
    return runeCircle;
}
