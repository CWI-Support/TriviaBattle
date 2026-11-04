// The monsters waiting their turn in the room (Dungeon format, 3D). A room often holds a pack of two or
// three; the party fights the one in front (battle3d.js draws it as the foe) and the rest stand behind it,
// a little smaller, idling, until it dies and the next steps up.
//
// This file only keeps track of who stands where: which waiting monster is in which spot, loading their
// models (each .glb loads once, js/battle3d/stage.js), and gliding one to its new spot when the one ahead
// of it leaves. What they do at each moment of a beat (flinch at a Wizard's spell, die, step up to the
// front) is in beats.js (showPack), timed from the beat's start like everything else.
//
// Nothing here is a rule: battle3d.js says who is waiting (from the snapshot's delve.foes) every frame.

import { Actor } from './actor.js';
import { mix, easeInOut } from './beats.js';

export const PACK_MAX = 3;  // the most waiting monsters shown (and loaded) at once

// Where the waiting monsters stand, from the one in front: behind it and to the side, so each still shows
// round it, in both the fight (seen from the hero's side) and the portrait (seen from straight on).
// dx/dz are from the front monster's spot; scale is how big they look next to it.
const SPOTS = [
    { dx: 1.25, dz: -1.4, scale: 0.85 },
    { dx: -0.6, dz: -2.6, scale: 0.8 },
    { dx: 1.9, dz: -3.2, scale: 0.75 },
];
const SLIDE_MS = 500;  // a waiting monster moving up a spot (the one ahead of it left)
const SETTLED_MS = 900; // only a monster that has been standing here this long can step up to the front

export class Pack {
    /** front: where the monster in front stands ({ x, z }); facing: how they turn; clock(): the server's time (ms). */
    constructor(scene, front, facing, clock) {
        this.scene = scene;
        this.front = front;
        this.facing = facing;
        this.clock = clock;
        this.members = [];   // in spot order: { key, want, slot, actor, joinedAt, arrivedAt, from, movedAt, base, drawn }
        this.synced = false; // the first monsters are just there; later ones drop in
    }

    /**
     * Every frame: who should be waiting, in order (wanted = [{ key, poison, splash }], at most PACK_MAX).
     * A monster already standing here with the same key keeps its actor (gliding to its new spot if it
     * moved up); new ones are loaded, and ones no longer wanted leave.
     */
    sync(wanted, now) {
        const unused = [...this.members];
        const next = wanted.slice(0, PACK_MAX).map((want, slot) => {
            const same = unused.find(m => m.key === want.key && m.slot === slot) ?? unused.find(m => m.key === want.key);
            if (!same) return this.join(want, slot, now);
            unused.splice(unused.indexOf(same), 1);
            if (same.slot !== slot) {
                same.from = same.drawn ?? this.spot(same.slot);
                same.movedAt = now;
                same.slot = slot;
            }
            same.want = want;
            return same;
        });
        for (const gone of unused) gone.actor?.dispose(this.scene);
        this.members = next;
        this.synced = true;
    }

    join(want, slot, now) {
        const member = { key: want.key, want, slot, actor: null, joinedAt: now, arrivedAt: null, from: null, movedAt: null, drawn: null };
        const dropIn = this.synced;
        Actor.create(want.key, this.facing).then(actor => {
            if (!this.members.includes(member)) return actor.dispose(this.scene); // it left while loading
            const at = this.base(member, this.clock());
            actor.root.position.set(at.x, 0, at.z);
            this.scene.add(actor.root);
            member.actor = actor;
            member.arrivedAt = dropIn ? this.clock() : null;
        });
        return member;
    }

    /**
     * The waiting monster that steps up to the front (the one in front died): the nearest one with this
     * key that has stood here a while, taken out of the pack (battle3d.js makes it the foe). Null if there
     * isn't one: a new room's monster is loaded and drops in instead.
     */
    take(key, now) {
        const member = this.members.find(m => m.key === key && m.actor && !m.want.splash?.killed && now - m.joinedAt >= SETTLED_MS);
        if (!member) return null;
        this.members.splice(this.members.indexOf(member), 1);
        return member;
    }

    /**
     * The ones loaded, each given where it stands this frame (base: { x, z, scale }) for beats.js to pose.
     * beats.js writes back what it drew (drawn), so a monster moving up a spot starts from where it was.
     */
    standing(now) {
        const loaded = this.members.filter(m => m.actor);
        for (const m of loaded) m.base = this.base(m, now);
        return loaded;
    }

    /** Where a member stands now: its spot, or on its way there from the last one. */
    base(member, now) {
        const spot = this.spot(member.slot);
        if (!member.from) return spot;
        const p = easeInOut((now - member.movedAt) / SLIDE_MS);
        return { x: mix(member.from.x, spot.x, p), z: mix(member.from.z, spot.z, p), scale: mix(member.from.scale, spot.scale, p) };
    }

    spot(slot) {
        const s = SPOTS[slot] ?? SPOTS[SPOTS.length - 1];
        return { x: this.front.x + s.dx, z: this.front.z + s.dz, scale: s.scale };
    }

    /** Turns them all, with the monster in front (towards the hero in a fight, towards you while a question is up). */
    face(angle) {
        for (const m of this.members) m.actor?.face(angle);
    }
}
