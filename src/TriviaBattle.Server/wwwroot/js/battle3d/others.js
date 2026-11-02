// The other players in your fight (Dungeon format, 3D), so a group game feels like one game:
//   - Teammates (a party, or your team in a battle) stand in your room with you: their heroes, wearing
//     their faces and their stations' colours. When one answers right (their delve's lastDealt goes up)
//     their hero dashes in and strikes your monster, and it flinches. Hit, they reel; downed, they lie on
//     the floor until a right answer gets them back up.
//   - Rivals (the other racers, or the other team) fight their own monsters, so they only drop in for a
//     moment: while the caption says one overtook you or slew a boss (js/dungeon-feed.js), their hero
//     pops up beside your monster and cheers.
// None of this is a beat: their moments are timed from when this screen saw them, play alongside your own
// fight and never hold up your question. Nothing here is a rule; it only shows what the snapshot says.

import { Actor } from './actor.js';
import { mix, easeOut, easeInOut, pulse, kick } from './beats.js';

// Where teammates stand, in joining order: behind and beside your hero. x is along the floor; back is how
// far behind the slanted line the fight is staged on (your hero and the monster stand on it); reach is how
// far from the monster's side they strike from (each their own, so two blows at once don't overlap).
const SLOTS = [{ x: -2.1, back: 2.5, reach: 0.5 }, { x: 0.4, back: 2.7, reach: 1.2 }, { x: -0.9, back: 4.0, reach: 0.8 }];
const STRIKE_AT = 260;     // a teammate's blow lands this long after it was seen (ms)
const STRIKE_ENDS = 1000;  // and they're back in their place by then
const RIVAL_SIDE = 0.7;    // a rival's cameo: this far out from your monster's right side,
const RIVAL_BACK = 1.0;    // a step behind it (heroes are taller than most monsters: nearer, they'd fill the picture),
const RIVAL_FACING = -0.4; // turned to the camera
const CAMEO_MS = 2600;     // as long as the caption shows (DungeonFeed.SHOW_MS)

export class Others {
    /** zAt: the fight's slanted line (x to z); homes: where your hero and the monster stand (x); facing: how heroes turn. */
    constructor(scene, { zAt, homes, facing }) {
        this.scene = scene;
        this.zAt = zAt;
        this.homes = homes;
        this.facing = facing;
        this.allies = new Map();  // stationId -> { slot, actor, dealt, taken, struckAt, swings, hurtAt, upAt, wasDown }
        this.cameo = null;        // { key, stationId, from, actor }
        this.matchId = null;
    }

    /**
     * Every frame. view: what Battle3D.show was given ({ match, you, colors }); now: the server clock (ms);
     * foe: your monster (null while it loads). Returns how hard a teammate's blow is landing on the monster
     * right now (0..1), for its idle pose to flinch.
     */
    update(view, now, foe) {
        const match = view?.match;
        const you = view?.you;
        if (match?.matchId !== this.matchId) {
            this.clear();
            this.matchId = match?.matchId ?? null;
        }
        if (!match || !you) return 0;

        // Teammates with a delve of their own (in 2v2 Group the team is one adventurer: nobody else to show).
        const teammates = match.players
            .filter(p => p.stationId !== you.stationId && p.delve && you.teamId && p.teamId === you.teamId)
            .slice(0, SLOTS.length);
        for (const stationId of [...this.allies.keys()])
            if (!teammates.some(p => p.stationId === stationId)) this.remove(stationId);

        let flinch = 0;
        teammates.forEach((p, i) => {
            const ally = this.allies.get(p.stationId) ?? this.add(p, SLOTS[i], view.colors?.[p.stationId]);
            if (!ally.actor) return; // still loading
            this.notice(ally, p, now);
            flinch = Math.max(flinch, this.pose(ally, p, now, foe));
            wearFaceOf(ally.actor, match, p.stationId);
        });
        this.showCameo(view, foe);
        return flinch;
    }

    add(player, slot, color) {
        const ally = {
            slot,
            actor: null,
            dealt: player.delve.lastDealt?.number || 0,   // what was already there isn't a new blow
            taken: player.delve.lastTaken?.number || 0,
            struckAt: null,
            swings: 0,
            hurtAt: null,
            upAt: null,
            wasDown: false,
        };
        this.allies.set(player.stationId, ally);
        Actor.create(player.hero || 'knight', this.facing, color).then(actor => {
            if (this.allies.get(player.stationId) !== ally) return actor.dispose(this.scene); // gone meanwhile
            actor.zAt = x => this.zAt(x) - slot.back;
            actor.root.position.set(slot.x, 0, actor.zAt(slot.x));
            this.scene.add(actor.root);
            ally.actor = actor;
        });
        return ally;
    }

    remove(stationId) {
        this.allies.get(stationId)?.actor?.dispose(this.scene);
        this.allies.delete(stationId);
    }

    /** A new hit of theirs on the monster, or on them. */
    notice(ally, player, now) {
        const dealt = player.delve.lastDealt?.number || 0;
        if (dealt > ally.dealt) {
            ally.struckAt = now;
            ally.swings++;
        }
        ally.dealt = dealt;
        const taken = player.delve.lastTaken?.number || 0;
        if (taken > ally.taken) ally.hurtAt = now;
        ally.taken = taken;
    }

    /** One frame of a teammate. Returns how hard their blow is landing (0..1). */
    pose(ally, player, now, foe) {
        const { actor, slot } = ally;
        const spec = actor.spec;
        const idle = { clip: spec.idle, time: now / 1000 + slot.x * 0.37 }; // not breathing in step with you

        // Downed: they fall, and stay on the floor until they're revived; then they get back up.
        const down = player.delve.downedUntilMs > now;
        if (ally.wasDown && !down) ally.upAt = now;
        ally.wasDown = down;
        if (down && spec.death) {
            actor.apply({ x: slot.x, clip: spec.death, time: ally.hurtAt ? (now - ally.hurtAt) / 1000 : 99 });
            return 0;
        }

        // Their right answer: dash in beside the monster, swing so the blow lands at STRIKE_AT, hop back.
        const s = ally.struckAt === null ? Infinity : now - ally.struckAt;
        if (s < STRIKE_ENDS && foe) {
            const swing = spec.attacks[ally.swings % spec.attacks.length];
            const swingAt = ms => ({ clip: swing.clip, time: swing.contact + (ms - STRIKE_AT) / 1000 });
            const strikeX = this.homes.foe - (foe.width / 2 + slot.reach);
            let x = strikeX, lift = 0;
            if (s < 200) x = mix(slot.x, strikeX, easeOut(s / 200));
            else if (s >= 650) {
                const p = (s - 650) / 350;
                x = mix(strikeX, slot.x, easeInOut(p));
                lift = Math.sin(Math.PI * p) * 0.5;
            }
            const clip = s < 100 ? { ...swingAt(s), from: idle, fade: s / 100 }
                : s < 700 ? swingAt(s)
                : { ...idle, from: swingAt(700), fade: Math.min(1, (s - 700) / 200) };
            actor.apply({ x, lift, ...clip });
            return pulse(s - STRIKE_AT, 200);
        }

        // Hit: they reel back, flashing red.
        const h = ally.hurtAt === null ? Infinity : now - ally.hurtAt;
        const hurtMs = actor.duration(spec.hurt) * 1000;
        if (h < hurtMs + 200) {
            const clip = h < hurtMs
                ? { clip: spec.hurt, time: h / 1000, from: idle, fade: Math.min(1, h / 60) }
                : { ...idle, from: { clip: spec.hurt, time: hurtMs / 1000 }, fade: (h - hurtMs) / 200 };
            actor.apply({ x: slot.x - 0.3 * kick(h, 60, 500), flash: pulse(h, 260), flashColor: 0xff2020, ...clip });
            return 0;
        }

        // Back on their feet after being downed, or just standing ready.
        const u = ally.upAt === null ? Infinity : now - ally.upAt;
        actor.apply(u < 400 && spec.death
            ? { x: slot.x, ...idle, from: { clip: spec.death, time: 99 }, fade: u / 400 }
            : { x: slot.x, ...idle });
        return 0;
    }

    /** A rival's moment in the caption: their hero pops up beside your monster and cheers until it ends. */
    showCameo(view, foe) {
        const moment = typeof DungeonFeed === 'undefined' ? null : DungeonFeed.showing; // a page's const, not on window
        const age = moment ? Date.now() - moment.from : Infinity;
        const wanted = moment?.rival && age < CAMEO_MS ? moment : null;
        const key = wanted ? `${wanted.stationId}:${wanted.from}` : null;
        if (key !== (this.cameo?.key ?? null)) {
            this.cameo?.actor?.dispose(this.scene);
            this.cameo = wanted ? this.addCameo(key, wanted, view) : null;
        }
        const actor = this.cameo?.actor;
        if (!actor || !foe) return;

        const scale = age < 200 ? easeOut(age / 200) : Math.min(1, (CAMEO_MS - age) / 250);
        const x = this.homes.foe + foe.width / 2 + RIVAL_SIDE;
        actor.apply({ x, scale, visible: scale > 0.01, clip: actor.spec.cheer || actor.spec.idle, time: age / 1000 });
        wearFaceOf(actor, view.match, wanted.stationId);
    }

    addCameo(key, moment, view) {
        const cameo = { key, actor: null };
        const hero = view.match.players.find(p => p.stationId === moment.stationId)?.hero || 'knight';
        Actor.create(hero, RIVAL_FACING, view.colors?.[moment.stationId]).then(actor => {
            if (this.cameo !== cameo) return actor.dispose(this.scene); // its moment is over already
            actor.zAt = () => this.zAt(this.homes.foe) - RIVAL_BACK; // level with the monster, not on the slant
            actor.root.visible = false;
            this.scene.add(actor.root);
            cameo.actor = actor;
        });
        return cameo;
    }

    /** Everyone out (a new match, or none). */
    clear() {
        for (const stationId of [...this.allies.keys()]) this.remove(stationId);
        this.cameo?.actor?.dispose(this.scene);
        this.cameo = null;
    }
}

/** Their hero photo on their hero's face (js/hero-photo.js has it once loaded); their hero's own face without one. */
function wearFaceOf(actor, match, stationId) {
    const url = HeroPhoto.url(match, stationId, 'face');
    if (url !== (actor.faceUrl ?? null)) actor.wearFace(url);
}
