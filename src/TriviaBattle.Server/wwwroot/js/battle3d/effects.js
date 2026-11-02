// The fight's effects: sparks, the sword's slash, the claw marks, the shockwave on the floor, the puff of
// smoke and the shower of gold when a monster dies. Cartoon, never gory (the design doc): getting hurt
// throws out stars, not blood. Like the actors, they're shown at a moment, not
// played: every frame starts with everything hidden, then the beat asks for each effect at its age
// ("sparks, 120 ms after the impact"). Particle paths come from the beat's seed, so the same moment
// always looks the same, however often the screen redraws.

import * as THREE from 'three';

/** One burst of particles flying out from a point and falling, then fading. */
class Burst {
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
        this.all = [this.slash, this.shockwave, ...this.claws];
        this.bursts = [this.impact, this.bigImpact, this.hurtStars, this.smoke, this.gold];
    }

    /** Called first every frame: nothing shows unless the beat asks for it again. */
    hideAll() {
        for (const mesh of this.all) mesh.visible = false;
        for (const burst of this.bursts) burst.hide();
    }

    /** The sword's arc sweeping across the monster: age in seconds, from when the swing starts to cut. */
    showSlash(at, age, tilt, big) {
        const life = 0.22;
        if (age < 0 || age > life) return;
        const p = age / life;
        this.slash.position.copy(at);
        this.slash.rotation.set(0, 0, tilt - 1.4 + p * 2.2);  // it sweeps round as it fades
        this.slash.scale.setScalar(big ? 1.35 : 1);
        this.slash.material.opacity = 1 - p * p;
        this.slash.visible = true;
    }

    /** A ring racing out across the floor from under whoever was hit. */
    showShockwave(at, age, big) {
        const life = 0.4;
        if (age < 0 || age > life) return;
        const p = age / life;
        this.shockwave.position.set(at.x, 0.08, at.z);
        this.shockwave.scale.setScalar((big ? 2.8 : 2) * (0.2 + p));
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
