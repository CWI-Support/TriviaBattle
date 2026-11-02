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
import { showBeat, showIdle } from './beats.js';
import { heroPortrait } from './portrait.js';
import { Others } from './others.js';

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
        this.lastFrame = performance.now();
        requestAnimationFrame(time => this.frame(time));
    }

    show(view) {
        this.view = view;
        const key = view.match?.players?.find(p => p.stationId === view.you?.stationId)?.hero || 'knight';
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
     * The monster to show: during a beat, the one in it (the one you hit may already be dead in the
     * snapshot, replaced by the next); otherwise the one you're facing now. A new one is loaded in the
     * background and then swapped in, dropping into the room.
     */
    wantFoe(you, now) {
        const beat = you.beat;
        const delve = you.delve;
        const playing = beat && now < beat.endsAtMs;
        const hurt = beat?.kind === 'hurt' || beat?.kind === 'downed';
        const key = !playing ? delve.monster.sprite
            : hurt ? delve.lastTaken?.monsterSprite || delve.monster.sprite
            : delve.lastDealt?.monsterSprite || delve.monster.sprite;
        if (key === this.foeKey || key === this.foeLoadingKey) return;

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

    frame(time) {
        requestAnimationFrame(next => this.frame(next));
        const dt = Math.min(0.1, (time - this.lastFrame) / 1000);
        this.lastFrame = time;
        const now = this.clock();
        const you = this.view?.you;

        let lean = { zoom: 1, focus: 0 };
        const foeFlinch = this.others.update(this.view, now, this.foe);
        if (you?.delve) {
            this.wantFoe(you, now);
            if (this.hero && this.foe) {
                const cast = { hero: this.hero, foe: this.foe, fx: this.fx, stage: this.stage, homes: HOMES, seconds: now / 1000 };
                const beat = you.beat;
                lean = beat && now >= beat.startedAtMs && now < beat.endsAtMs
                    ? showBeat(beatInfo(beat, you.delve), now - beat.startedAtMs, cast)
                    : showIdle(cast, {
                        heroDown: you.delve.downedUntilMs > now,
                        foeArrivedMs: this.foeArrivedAt === null ? null : now - this.foeArrivedAt,
                        foeFlinch,
                    });
            }
        }
        this.wearFace();
        this.maskFoe();
        this.turnFoe(dt);
        this.aim(lean);
        this.stage.draw(now / 1000, dt);
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
    }

    /** Points the camera: the whole fight, or (while a question is up) the monster in its room. */
    aim({ zoom, focus }) {
        const aspect = this.stage.aspect();
        const tan = Math.tan(THREE.MathUtils.degToRad(FOV / 2));

        if (this.stage.mode() === 'portrait' && this.foe) {
            const spec = this.foe.spec;
            const height = spec.height || 2;
            // The monster big in the middle of its room, seen from straight on and a little below its eyes.
            const target = new THREE.Vector3(FOE_HOME.x, (spec.hover || 0) + height * 0.52, FOE_HOME.z);
            const distance = Math.max((height * 0.62) / tan, (this.foe.width * 0.95) / (tan * aspect));
            this.stage.frame(target.clone().add(new THREE.Vector3(-0.08 * distance, 0.12 * distance, distance)), target, FOV);
            return;
        }

        // Both fighters across the box, seen a little from above, aimed a touch left of centre: the knight
        // is nearer the camera, so he looks further out than the monster. The HP bar and the Host's line go
        // under them.
        const span = 5.8;
        const distance = ((span / 2) / (tan * Math.min(aspect, 1.2))) * zoom;
        const target = new THREE.Vector3(focus - 0.15, 0.75, 0);
        this.stage.frame(target.clone().add(new THREE.Vector3(0, distance * 0.36, distance)), target, FOV);
    }
}

/** What kind of blow the beat is, from the snapshot. seed: the same number for the whole beat (picks the swing, the sparks). */
function beatInfo(beat, delve) {
    const hurt = beat.kind === 'hurt' || beat.kind === 'downed';
    const hit = hurt ? null : delve.lastDealt;
    return {
        kind: beat.kind,
        hurt,
        crit: !!hit?.crit,
        combo: !!hit?.combo,
        partyCombo: !!hit?.partyCombo,
        fireball: !!hit?.fireball,
        blocked: !!(hurt && delve.lastTaken?.blocked),
        seed: Math.abs(Math.floor(beat.startedAtMs / 7)),
    };
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
