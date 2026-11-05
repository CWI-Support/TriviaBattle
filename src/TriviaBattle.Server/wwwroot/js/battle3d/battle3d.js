// Battle3D: the station's fight drawn in 3D (the default; ?view=2d on a station for the CSS version), in the "3D that reads like a painting" style (think
// Hearthstone). The page draws its HTML exactly as before and then hands the same state over with
// Battle3D.show({ match, you, color }). The HTML marks where the scene goes with data-stage: "fight"
// on a battle beat (DungeonBattle.scene), "portrait" on the encounter's stage while a question is up
// (DungeonEncounter.view: the monster big in its room, turned to face you).
// The words, damage numbers, flash, HP bars and the floor curtain stay HTML (css/dungeon-battle.css);
// css/battle3d.css hides the drawn room, sprites and sparks this replaces.
//
// Like the CSS version, it's all timed from the beat's server start, and nothing here is a game rule:
// it only shows what the snapshot says happened.
//
// Needs the import map for 'three' (see screens/dev-battle3d.html), and loads as a module:
//   Battle3D.start(container, clock)   once; clock() = the server's time now (ms)
//   Battle3D.show({ match, you, color, colors }) after every redraw (colors: each station's colour, by id)
//   HeroPortrait.render(faceUrl, color, options)  a hero (wearing a hero photo's face) as a canvas (js/battle3d/portrait.js)

import * as THREE from 'three';
import { Stage } from './stage.js';
import { Actor } from './actor.js';
import { Effects } from './effects.js';
import { showBeat, showIdle, showWalkIn, walkAt, mix, easeInOut } from './beats.js';
import { ROOM_LENGTH } from './models.js';
import { heroPortrait } from './portrait.js';
import { Others } from './others.js';
import { Pack, PACK_MAX } from './pack.js';

// Where the fighters stand. The fight is staged on a slant (the knight nearer the camera, the monster
// further back) so a tall, narrow station screen can show both big: side by side they'd be tiny.
const HERO_HOME = { x: -1.3, z: 1.0 };
const FOE_HOME = { x: 1.5, z: -1.0 };
const zAt = x => HERO_HOME.z + (FOE_HOME.z - HERO_HOME.z) * (x - HERO_HOME.x) / (FOE_HOME.x - HERO_HOME.x);
const HOMES = { hero: HERO_HOME.x, foe: FOE_HOME.x };

/**
 * Each faces the other, turned towards the camera: the monster a little, the knight a lot (three quarters
 * on, Hearthstone style), so we see its face: the knight's own, or the player's (a hero photo).
 */
const HERO_FACING = Math.atan2(FOE_HOME.x - HERO_HOME.x, FOE_HOME.z - HERO_HOME.z) - 1.45;
const FOE_FACING = Math.atan2(HERO_HOME.x - FOE_HOME.x, HERO_HOME.z - FOE_HOME.z) + 0.35;

const FOV = 32;
const PORTRAIT_MAX_HEIGHT = 2.1; // a question's shot fits at most this much of the monster (the top of it): a boss looms
const FIGHT_RIGHT_EDGE = 2.45;   // the right edge of the usual fight shot (span 5.2 round x -0.15)
const KILLS = ['kill', 'floor', 'boss'];
const STEP_MS = 500;        // a waiting monster stepping up to the front between beats (the poison killed the one there)
const POISON_DEATH_MS = 900;

class Battle {
    constructor(container, clock) {
        this.clock = clock;
        this.stage = new Stage(container);
        this.fx = new Effects(this.stage.scene);
        this.view = null;
        this.hero = null;
        this.heroKey = null;   // the hero being shown (or loading): the player's pick
        this.foe = null;
        this.foeKey = null;
        this.foeLoadingKey = null;
        this.foeArrivedAt = null;
        this.foeYaw = FOE_FACING;
        this.foesMet = 0;      // counts the monsters, so each wears a rival's face in turn
        this.others = new Others(this.stage.scene, { zAt, homes: HOMES, facing: HERO_FACING }); // teammates, and rivals dropping in
        this.pack = new Pack(this.stage.scene, FOE_HOME, FOE_FACING, clock); // the monsters waiting their turn
        this.roomSeen = null;  // the room (floor:room) last seen between beats: a kill that emptied it has no pack to step up
        this.deathsSeen = null; // the front monster's last deaths seen: { beat (a kill beat's start), poison (the poison kill's hit number) }
        this.foeStep = null;   // the monster in front stepping up from the pack between beats: { from: { x, z, scale }, at }
        this.poisonDeath = null; // the one the poison killed between beats, going up in smoke: { at, chest }
        this.arrival = null;   // a kill beat that emptied the room: the next room's monster, dropping in before it ends: { beat, key, actor }
        this.lastFrame = performance.now();
        requestAnimationFrame(time => this.frame(time));
    }

    show(view) {
        this.view = view;
        const key = view.match?.players?.find(p => p.stationId === view.you?.stationId)?.hero || 'tank';
        if (key === this.heroKey) return;
        this.heroKey = key;
        Actor.create(key, HERO_FACING, view.color).then(hero => {
            if (this.heroKey !== key) return hero.dispose(this.stage.scene); // another hero was picked meanwhile
            this.hero?.dispose(this.stage.scene);
            this.hero = this.place(hero, HERO_HOME);
        });
    }

    place(actor, home) {
        actor.zAt = zAt;
        actor.root.position.set(home.x, 0, home.z);
        this.stage.scene.add(actor.root);
        return actor;
    }

    /**
     * The monster to show in front: during a beat, the one in it (the one you hit may already be dead in
     * the snapshot, replaced by the next); otherwise the one you're facing now. died: the one in front just
     * died ('beat' or 'poison', see frontDied), so the next takes its place even if it looks the same.
     * The next one steps up from the pack when it was waiting in the room; otherwise it's loaded in the
     * background and then swapped in, dropping into the room.
     */
    wantFoe(you, now, playing, died) {
        const beat = you.beat;
        const delve = you.delve;
        const hurt = beat?.kind === 'hurt' || beat?.kind === 'downed';
        const key = !playing ? delve.monster.sprite
            : hurt ? delve.lastTaken?.monsterSprite || delve.monster.sprite
            : DungeonBattle.beatHit(you)?.monsterSprite || delve.monster.sprite; // the one in this beat (a poison kill: the party's hit)
        if (died === 'poison' && this.foe) this.poisonDeath = { at: now, chest: this.foe.chest() };
        if (!died && (key === this.foeKey || key === this.foeLoadingKey)) return;
        if (died === 'beat' && this.arrival?.key === key && this.arrival.actor) return this.welcome(key);

        const next = died ? this.pack.take(key, now) : null;
        if (next) return this.promote(next, key, now);

        this.foeLoadingKey = key;
        Actor.create(key, FOE_FACING).then(foe => {
            if (this.foeLoadingKey !== key) return foe.dispose(this.stage.scene);  // something newer was asked for
            this.foeArrivedAt = this.foe ? this.clock() : null;  // the first monster is just there
            this.foe?.dispose(this.stage.scene);
            this.foe = this.place(foe, FOE_HOME);
            this.foe.rivalTurn = this.foesMet++;
            this.foeKey = key;
            this.foeLoadingKey = null;
        });
    }

    /**
     * A kill beat that emptied the room (the walk: DungeonBattle.walks): the next room's first monster, to come in
     * with the corridor as the hero runs on, so the question comes back with the new fight already there.
     * { beat (its start), key }, or null. delve.monster is already the next one.
     */
    arrivalOf(you, playing) {
        if (!playing || !DungeonBattle.walks(you)) return null;
        return { beat: you.beat.startedAtMs, key: you.delve.monster.sprite };
    }

    /** Loads the arriving monster as soon as the beat starts (it isn't in the scene until the next room comes into view). */
    prepareArrival(wanted) {
        if (!wanted || this.arrival?.beat === wanted.beat) return;
        this.dropArrival();
        const arrival = { ...wanted, actor: null };
        this.arrival = arrival;
        Actor.create(wanted.key, FOE_FACING).then(actor => {
            if (this.arrival !== arrival) return actor.dispose(this.stage.scene); // that beat's gone
            actor.zAt = zAt;
            arrival.actor = actor;
        });
    }

    dropArrival() {
        this.arrival?.actor?.dispose(this.stage.scene);
        this.arrival = null;
    }

    /** The beat that emptied the room is over: the monster that came in with the next room is the one in front now. */
    welcome(key) {
        const actor = this.arrival.actor;
        this.arrival = null;
        if (!actor.root.parent) this.place(actor, FOE_HOME);
        this.foe?.dispose(this.stage.scene);
        this.foe = actor;
        this.foe.rivalTurn = this.foesMet++;
        this.foeKey = key;
        this.foeLoadingKey = null;
        this.foeArrivedAt = null; // it has already landed
    }

    /**
     * A waiting monster takes the front. After a kill beat it has already walked there (beats.js); after
     * the poison killed the one in front between beats it walks there now, from where it stood.
     */
    promote(member, key, now) {
        this.foe?.dispose(this.stage.scene);
        this.foe = member.actor;
        this.foe.zAt = zAt;
        this.foe.rivalTurn = this.foesMet++;
        this.foeKey = key;
        this.foeLoadingKey = null;
        this.foeArrivedAt = null;
        this.foeStep = { from: member.drawn ?? member.base, at: now };    }

    /**
     * Whether the monster in front has just died: its kill beat is over ('beat'), or between beats the
     * poison killed it ('poison'). Remembered by the beat's start and the hit's number, so each counts once,
     * and what was already over when the screen opened doesn't count. Waits while a beat plays.
     */
    frontDied(you, now, playing) {
        if (playing) return null;
        const beat = you.beat;
        const hit = you.delve.lastHit;
        const old = this.deathsSeen;
        this.deathsSeen = {
            beat: beat && KILLS.includes(beat.kind) && now >= beat.endsAtMs ? beat.startedAtMs : old?.beat ?? null,
            poison: hit?.poison && hit.killed ? hit.number : old?.poison ?? null,
        };
        if (!old) return null;
        if (this.deathsSeen.beat !== old.beat) return 'beat';
        if (this.deathsSeen.poison !== old.poison) return 'poison';
        return null;
    }

    /**
     * Who waits behind the one in front (delve.foes after the first), for js/battle3d/pack.js, and which of
     * them steps up during this beat (stepUp: their place in the pack, -1 for none). During a beat the
     * snapshot is already after the hit, so the pack is worked back to what it was when the blow landed:
     * a Wizard's splash lists every one it hit (even the ones it killed); after a kill the next one in front
     * is still waiting, unless the kill emptied the room (the snapshot is already in the next one: a new
     * floor, the boss's room (bossAhead), or simply a room we saw change).
     */
    packView(you, playing, arriving = false) {
        const delve = you.delve;
        const foes = delve.foes?.length ? delve.foes : [delve.monster];
        const room = `${delve.floor}:${delve.room}`;
        const waiting = list => list.slice(0, PACK_MAX).map(f => ({ key: f.sprite, poison: f.poisonStacks || 0, splash: null }));
        const hurt = you.beat?.kind === 'hurt' || you.beat?.kind === 'downed';
        const hit = playing && !hurt ? DungeonBattle.beatHit(you) : null;
        if (!playing) this.roomSeen = room;

        if (!hit) return { wanted: waiting(foes.slice(1)), stepUp: -1 };
        // On the walk, once the next room is coming into view: its pack (this room's are all dead and gone by then).
        if (arriving) return { wanted: waiting(foes.slice(1)), stepUp: -1 };
        if (hit.splashes?.length) {
            const wanted = hit.splashes.slice(0, PACK_MAX).map(s => ({ key: s.monsterSprite, poison: 0, splash: s }));
            return { wanted, stepUp: hit.killed && !hit.floorCleared ? wanted.findIndex(w => !w.splash.killed) : -1 };
        }
        if (!hit.killed) return { wanted: waiting(foes.slice(1)), stepUp: -1 };
        // The kill emptied the room: nobody waits here (the next room's pack comes in on the walk, above).
        if (hit.floorCleared || hit.bossAhead || (this.roomSeen !== null && this.roomSeen !== room)) return { wanted: [], stepUp: -1 };
        const wanted = waiting(foes);
        return { wanted, stepUp: wanted.length ? 0 : -1 };
    }

    /** The poison stacks on the monster drawn in front (during a beat, the one in it: none if it died). */
    foePoison(you, playing) {
        const delve = you.delve;
        const hurt = you.beat?.kind === 'hurt' || you.beat?.kind === 'downed';
        if (playing && !hurt && DungeonBattle.beatHit(you)?.killed) return 0;
        return delve.monster.poisonStacks || 0;
    }

    frame(time) {
        requestAnimationFrame(next => this.frame(next));
        const dt = Math.min(0.1, (time - this.lastFrame) / 1000);
        this.lastFrame = time;
        const now = this.clock();
        const you = this.view?.you;

        let lean = { zoom: 1, focus: 0 };
        const { flinch: foeFlinch, youHealedAt } = this.others.update(this.view, now, this.foe);
        if (you?.delve) {
            const beat = you.beat;
            const playing = !!beat && now >= beat.startedAtMs && now < beat.endsAtMs;
            // The walk to the next room (a kill that emptied this one): how far along it is, 0 to 1.
            const arrival = this.arrivalOf(you, playing);
            this.prepareArrival(arrival);
            const walk = arrival ? walkAt(now - beat.startedAtMs) : 0;
            const arriving = walk > 0 && !!this.arrival?.actor;   // the next room is coming into view
            const { wanted, stepUp } = this.packView(you, playing, arriving);
            this.wantFoe(you, now, playing, this.frontDied(you, now, playing));  // before the pack lets go of the one stepping up
            if (this.arrival && !(playing && this.arrival.beat === beat.startedAtMs)) this.dropArrival(); // its beat ended without it
            this.pack.sync(wanted, now, { dropIn: !arriving }); // the next room's pack walks in with it, it doesn't fall in
            this.stage.scrollRoom(-ROOM_LENGTH * walk);
            if (this.hero && this.foe) {
                const cast = {
                    hero: this.hero, foe: this.foe, fx: this.fx, stage: this.stage, homes: HOMES, seconds: now / 1000,
                    front: FOE_HOME, pack: this.pack.standing(now), foePoison: this.foePoison(you, playing),
                };
                lean = playing
                    ? showBeat(beatInfo(beat, you, stepUp), now - beat.startedAtMs, cast)
                    : showIdle(cast, {
                        heroDown: you.delve.downedUntilMs > now,
                        foeArrivedMs: this.foeArrivedAt === null ? null : now - this.foeArrivedAt,
                        foeFlinch,
                        heroHealMs: youHealedAt === null ? null : now - youHealedAt,
                        charge: chargeOf(you.delve),
                    });
                if (arriving) {
                    // The next room's monsters stand in their spots, still as far off as the corridor has left to scroll.
                    const ahead = ROOM_LENGTH * (1 - walk);
                    if (!this.arrival.actor.root.parent) this.place(this.arrival.actor, FOE_HOME);
                    showWalkIn(this.arrival.actor, cast);
                    this.arrival.actor.root.position.x += ahead;
                    for (const member of this.pack.standing(now)) member.actor.root.position.x += ahead;
                }
                this.stepUp(now);
                this.showPoisonDeath(now);
            }
        }
        this.wearFace();
        this.maskFoe();
        this.turnFoe(dt);
        this.aim(lean);
        this.stage.draw(now / 1000, dt);
    }

    /** The monster just promoted from the pack walks from where it stood to the front (and grows to full size). */
    stepUp(now) {
        if (!this.foeStep) return;
        const p = (now - this.foeStep.at) / STEP_MS;
        if (p >= 1) {
            this.foeStep = null;
            return;
        }
        const e = easeInOut(p);
        const { from } = this.foeStep;
        const at = this.foe.root.position;
        at.x = mix(from.x, at.x, e);
        at.z = mix(from.z, at.z, e);
        this.foe.holder.scale.multiplyScalar(mix(from.scale, 1, e));
    }

    /** The monster the poison killed between beats, going up in smoke where it stood. */
    showPoisonDeath(now) {
        if (!this.poisonDeath) return;
        const age = now - this.poisonDeath.at;
        if (age > POISON_DEATH_MS) this.poisonDeath = null;
        else this.fx.showPoisonDeath(this.poisonDeath.chest, age / 1000, 3);
    }

    /** Your hero photo on the knight's face (js/hero-photo.js has it once it has loaded); the knight's own face without one. */
    wearFace() {
        const view = this.view;
        const url = view?.you ? HeroPhoto.url(view.match, view.you.stationId, 'face') : null;
        if (this.hero && url !== this.hero.faceUrl) this.hero.wearFace(url);
    }

    /**
     * In a race or a battle, the monster wears a rival's face: the other racers, or the other team, with a
     * hero photo, one per monster in turn. Teammates and players without a photo aren't used.
     */
    maskFoe() {
        const view = this.view;
        if (!this.foe || !view?.you) return;
        const rivals = (view.match.players || []).filter(p =>
            p.stationId !== view.you.stationId && p.photo > 0 && (!view.you.teamId || p.teamId !== view.you.teamId));
        const rival = rivals.length ? rivals[this.foe.rivalTurn % rivals.length] : null;
        const url = rival ? HeroPhoto.url(view.match, rival.stationId, 'face') : null;
        if (url !== this.foe.maskUrl) this.foe.wearMask(url);
    }

    /** While a question is up the monster turns to face you (the player is the one answering); in a fight it faces the knight. */
    turnFoe(dt) {
        if (!this.foe) return;
        const wanted = this.stage.mode() === 'portrait' ? 0.25 : FOE_FACING;
        this.foeYaw += (wanted - this.foeYaw) * (1 - Math.exp(-dt / 0.15));
        this.foe.face(this.foeYaw);
        this.pack.face(this.foeYaw);  // the waiting ones turn with it
    }

    /**
     * Points the camera: the whole fight, or (while a question is up) the monster in its room. The scene fills
     * the screen with words floating over it, so the subject goes in the gap they leave (Stage.focus): the
     * camera is raised or lowered by however far that gap's middle is from the box's.
     */
    aim({ zoom, focus }) {
        const aspect = this.stage.aspect();
        const tan = Math.tan(THREE.MathUtils.degToRad(FOV / 2));
        const clear = this.stage.focus();
        const clearShare = clear.bottom - clear.top;
        // Moves the shot so a point aimed at lands in the gap, a share 'at' of the way down it (0.5: its middle): at
        // this distance the box shows 2 x distance x tan of height, and that spot is (spot - 0.5) of it below the box's middle.
        const intoGap = (distance, at = 0.5) =>
            new THREE.Vector3(0, (clear.top + (clear.bottom - clear.top) * at - 0.5) * 2 * distance * tan, 0);

        if (this.stage.mode() === 'portrait' && this.foe) {
            const spec = this.foe.spec;
            const height = spec.height || 2;
            // The monster big in the gap, seen from straight on and a little below its eyes. A tall one (a boss) is
            // framed from the knees up rather than all of it: fitting the whole of it backed the camera off so far
            // the knight and the room's props came into the shot, and it looked smaller than the rest, not bigger.
            const framed = Math.min(height, PORTRAIT_MAX_HEIGHT);
            const top = (spec.hover || 0) + height;
            const target = new THREE.Vector3(FOE_HOME.x, top - framed * 0.48, FOE_HOME.z);
            const distance = Math.max((framed * 0.62) / (tan * clearShare), (this.foe.width * 0.95) / (tan * aspect));
            const shift = intoGap(distance);
            this.stage.frame(target.clone().add(new THREE.Vector3(-0.08 * distance, 0.12 * distance, distance)).add(shift), target.clone().add(shift), FOV);
            return;
        }

        // Both fighters across the box, seen a little from above, aimed a touch left of centre: the knight
        // is nearer the camera, so he looks further out than the monster. The HP bar and the Host's line go
        // under them.
        // A monster whose pose reaches out past the usual shot's right edge (the orc's arms and club) widens it to fit.
        const reach = this.foe ? Math.max(0, FOE_HOME.x + this.foe.poseWidth / 2 + 0.3 - FIGHT_RIGHT_EDGE) : 0;
        const span = 5.2 + reach;
        const distance = ((span / 2) / (tan * Math.min(aspect, 1.2))) * zoom;
        const target = new THREE.Vector3(focus - 0.15 + reach / 2, 0.75, 0);
        const shift = intoGap(distance, 0.6); // a little low in the fight's space: the words go above them
        this.stage.frame(target.clone().add(new THREE.Vector3(0, distance * 0.36, distance)).add(shift), target.clone().add(shift), FOV);
    }
}

/**
 * What kind of blow the beat is, from the snapshot. seed: the same number for the whole beat (picks the
 * swing, the sparks). stepUp: which waiting monster steps up to the front during it (packView).
 */
function beatInfo(beat, you, stepUp) {
    const delve = you.delve;
    const hurt = beat.kind === 'hurt' || beat.kind === 'downed';
    const hit = DungeonBattle.beatHit(you); // a poison kill's beat: the party's hit, the poison's
    const taken = hurt ? delve.lastTaken : null;
    const isYou = ownerId => !!ownerId && (ownerId === you.stationId || ownerId === you.teamId); // 2v2 Group: the team answers as one
    return {
        kind: beat.kind,
        hurt,
        byPoison: !!beat.byPoison,                            // the Healer's poison killed it: no blow
        watching: !!beat.by,                                  // a teammate's blow killed it: your knight stays home
        walks: DungeonBattle.walks(you),                      // the room's empty: on to the next one
        crit: !!hit?.crit,
        combo: !!hit?.combo,
        comboCount: hit?.combo ? (hit.comboCount || 3) : 0,  // a Pugilist's streak
        partyCombo: !!hit?.partyCombo,
        fireball: !!hit?.fireball,
        blocked: !!taken?.blocked,
        tank: hurt && delve.heroClass === 'tank',             // takes every hit on the shield
        stepUp,
        poisoned: (hit?.poisonStacks || 0) > 0,               // a Healer's fast answer added a stack
        patronPrice: (hurt ? taken?.patronPrice : hit?.patronPrice) || 0,
        healedYou: isYou(hit?.healedOwnerId),                 // a teammate healed by it: js/battle3d/others.js
        charged: !!taken?.charged && !taken.blocked,          // a boss's full-charge blow
        staggered: !!hit?.staggered,                          // a right answer at a boss's full charge
        seed: Math.abs(Math.floor(beat.startedAtMs / 7)),
    };
}

/**
 * A boss's charge for your question now, as the 3D shows it: { level (0..1, how far round its cycle: the
 * first step already glows a little), full (the last step) }, or null when you aren't fighting a boss.
 */
function chargeOf(delve) {
    const steps = delve.bossChargeSteps || 0;
    if (steps < 2) return null;
    const step = Math.max(0, Math.min(steps - 1, delve.bossCharge || 0));
    return { level: (step + 1) / steps, full: step === steps - 1 };
}

let battle = null;
// The knight wearing a player's face, for the screens that draw in 2D (js/hero-camera.js makes it after a hero photo).
window.HeroPortrait = { render: heroPortrait };

window.Battle3D = {
    start(container, clock) {
        battle ??= new Battle(container, clock);
    },
    show(view) {
        battle?.show(view);
    },
};
