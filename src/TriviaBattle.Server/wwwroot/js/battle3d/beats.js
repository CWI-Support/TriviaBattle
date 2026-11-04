// The battle beats in 3D: what the hero, the monster, the effects and the camera do at each moment of a
// beat. showBeat is called every frame with ms = how long ago the beat started (server clock), and works
// the whole frame out from that alone, so it can be jumped into at any point (a redraw, a late frame,
// /dev/battle3d?at=) and always shows the same thing.
//
// The moments match css/dungeon-battle.css (the table at its top), which times the words, the damage
// number, the flash and the HP bar laid over this scene in HTML. Change both together:
//   hit     0 the knight dashes in, 190 the blade lands (the server's sound plays at 0: the blow lands early)
//   kill    the hit, then 720 the monster goes down, 1000 it bursts into smoke and gold as SLAIN! slams in
//   floor / boss   the kill; the HTML curtain covers the scene from 1280-1330
//   hurt    0 the monster lunges, 240 it strikes; the knight reels
//   downed  the hurt, then 640 the knight falls (and stays down)
// The hero classes' moments, on the same clock:
//   splash  (Wizard) 70 a spell leaves the hero for each waiting monster, 190 it lands with the blow: they
//           flinch; one it killed goes down at 720 and bursts at 1000, with the one in front
//   kill in a pack   1250-1750 the next monster steps up to the front (it's there before the beat ends)
//   poison  (Healer) 190 a green puff as the stack lands; the bubbles and the tint grow with the stacks
//   heal    (Healer) 300 the heal sparkles on whoever it healed (you, or a teammate in your room)
//   patron  (Warlock) 420 the patron's price: purple drained out of the hero (on a hit or a hurt)
//   tank    a hurt takes the blow on the shield (its block clip) instead of reeling
//   combo   (Pugilist) bigger with each answer in the streak; from 3 the combo swing and a second cut at 240
// A boss's charge (css/dungeon-battle.css has the same moments):
//   charged   a miss at full charge (a hurt or downed beat): 0 the boss winds up burning red and leaps, 240 it
//             slams down (a longer hit-stop, dust, a bigger shockwave and shake, 620 a second shake); the
//             knight is thrown further back
//   staggered a right answer at full charge (a hit or a kill): 190 the blow lands big; the boss stumbles back
//             and sways (to ~1350), dazed stars circling its head 420-1370
//   While a question is up (showIdle) a boss glows and crackles with its charge, more with each step, and
//   burns red, trembling, at full charge.
//
// Hit-stop: on impact everything but the camera shake freezes for a moment (60-110 ms). It's the cheapest
// way to make a blow feel heavy. The freeze is built into the timeline (see stopped()), so it seeks too.
// Only the blow itself runs on the stopped clock; the later moments (the kill, the fall) stay on the
// beat's own clock, in step with the HTML laid over them.

export const HIT_IMPACT = 190;
export const HURT_IMPACT = 240;
const KILL_FALLS = 720;
const KILL_BURSTS = 1000;
const DOWNED_FALLS = 640;
const CHEER_FROM = 1050;
const DOWNED_SLIDE = 0.9;  // how far in the knight lies when he's down (he falls backwards, out of the picture)
const BOLT_LEAVES = 70;    // a Wizard's splash: the spells leave the hero (and land at HIT_IMPACT)
const STEP_FROM = 1250;    // a kill in a pack: the next monster steps up to the front,
const STEP_TO = 1750;      // and is there before the beat fades (kill beats are 2 s)
export const HEAL_AT = 300;       // a Healer's heal sparkles on whoever it healed
const PATRON_AT = 420;     // a Warlock's slow answer: the patron takes its price
const COMBO_CROSS = 240;   // a Pugilist's combo of 3 or more: the second cut
const POISON_GREEN = 0x4dff6a;
const PATRON_PURPLE = 0xa040ff;
const CHARGE_ORANGE = 0xff7a1a;   // a boss building its charge
const CHARGE_RED = 0xff1a10;      // at full charge, and its charged blow
const CHARGED_SECOND_SHAKE = 620; // the charged blow's aftershock (the CSS shakes again then too)
const DAZE_FROM = 420;            // a stagger: the dazed stars appear (effects.js's Daze lasts 0.95 s: to ~1370)
const STUMBLE_BACK = 0.8;         // how far a staggered boss stumbles back (world units)

/**
 * One frame of a beat. beat = { kind, hurt, crit, combo, comboCount, partyCombo, fireball, blocked, tank,
 * stepUp, poisoned, patronPrice, healedYou, seed } (battle3d.js's beatInfo).
 * cast = { hero, foe, fx, stage, homes: { hero, foe } (x), front: { x, z } (the monster in front's spot),
 * pack (the waiting monsters, js/battle3d/pack.js, each with what the splash did to it), foePoison (the
 * stacks on the monster in front), seconds (for idle loops) }.
 * Returns how the camera should lean: { zoom (1 = normal, less = closer), focus (x shift) }.
 * beat.charged: a boss's full-charge blow (on a hurt); beat.staggered: a right answer at its full charge.
 */
export function showBeat(beat, ms, cast) {
    cast.fx.hideAll();
    const lean = beat.hurt ? monsterStrikes(beat, ms, cast) : heroStrikes(beat, ms, cast);
    showPack(cast, beat, ms);
    showPatron(beat, ms, cast);
    return lean;
}

/**
 * Between beats (or while a question is up): both stand at home, breathing, the pack waiting behind.
 * foeFlinch (0..1): a teammate's blow landing on the monster (js/battle3d/others.js), so it flashes and
 * rocks back. heroHealMs: how long ago a teammate's heal landed on you (null: none). charge: a boss's charge
 * for your question now ({ level 0..1, full }, battle3d.js's chargeOf), or null: it glows and crackles with it.
 */
export function showIdle(cast, { heroDown, foeArrivedMs, foeFlinch = 0, heroHealMs = null, charge = null }) {
    const { hero, foe, fx, stage, homes, seconds } = cast;
    fx.hideAll();
    stage.shake.set(0, 0);

    // Downed: the knight stays on the floor until they're back up.
    hero.apply(heroDown && hero.spec.death
        ? { x: homes.hero + DOWNED_SLIDE, clip: hero.spec.death, time: 99 }
        : { x: homes.hero, clip: hero.spec.idle, time: seconds });
    if (heroHealMs !== null) fx.showHeal(hero.chest(), heroHealMs / 1000);

    // A new monster drops in from above and lands with a squash.
    const { lift, squash } = dropIn(foeArrivedMs);
    // At full charge the boss trembles, as if it can barely hold the blow in.
    const tremble = charge?.full ? 0.025 * Math.sin(seconds * 70) : 0;
    foe.apply({
        x: homes.foe + 0.3 * foeFlinch + tremble, lift, squash, tilt: -0.15 * foeFlinch, flash: foeFlinch, flashColor: 0xffffff,
        ...strongerGlow(poisonGlow(cast.foePoison, seconds), chargeGlow(charge, seconds)),
        clip: foe.spec.idle, time: seconds + 0.37,
    });
    fx.showPoison(0, foe, seconds, cast.foePoison);
    if (charge) fx.showCharge(foe, seconds, charge.level, charge.full);
    showPack(cast, null, 0);
    return { zoom: 1, focus: 0 };
}

/** A monster arriving: it falls from above and lands with a squash. ms: since it arrived (null: it was just there). */
function dropIn(ms) {
    let lift = 0, squash = 0;
    if (ms !== null && ms < 600) {
        const fall = Math.min(1, ms / 300);
        lift = 4 * (1 - fall) * (1 - fall);
        const landed = ms - 300;
        if (landed > 0) squash = 0.3 * Math.exp(-landed / 90) * Math.cos(landed / 45);
    }
    return { lift, squash };
}

/**
 * A boss's charge glowing on it while a question is up: a faint orange that grows with each step, then at
 * full charge a strong red, pulsing fast (on the server clock, like the idle loops).
 */
function chargeGlow(charge, seconds) {
    if (!charge) return {};
    if (charge.full) return { glow: 0.55 + 0.25 * Math.sin(seconds * 9), glowColor: CHARGE_RED };
    return { glow: (0.06 + 0.2 * charge.level) * (0.85 + 0.15 * Math.sin(seconds * 5)), glowColor: CHARGE_ORANGE };
}

/** Of two tints (poison's, a boss's charge), the stronger: a model can only glow one colour. */
function strongerGlow(a, b) {
    return (a.glow || 0) >= (b.glow || 0) ? a : b;
}

/** Poison's green tint: stronger with more stacks, gently pulsing (on the server clock, like the idle loops). */
function poisonGlow(stacks, seconds) {
    if (!stacks) return {};
    return { glow: Math.min(0.55, 0.15 + 0.08 * stacks) * (0.8 + 0.2 * Math.sin(seconds * 4)), glowColor: POISON_GREEN };
}

/**
 * The monsters waiting in the room. beat null: between beats, they idle (dropping in when they've just
 * arrived). In a beat: a Wizard's splash reaches each at the impact (and kills the ones it says), and after
 * a kill in a pack the next one steps up to the front, so it's standing there when the beat ends.
 */
function showPack(cast, beat, ms) {
    const { hero, fx, pack, front, seconds } = cast;
    const now = seconds * 1000;
    const a = beat && !beat.hurt ? stopped(ms, HIT_IMPACT, hitStop(beat)) : ms;
    const t = a - HIT_IMPACT;
    for (const member of pack) {
        const { actor, base, slot, want } = member;
        const idle = { clip: actor.spec.idle, time: seconds + 0.6 * slot + 0.2 };  // not breathing in step with the others
        let x = base.x, z = base.z, scale = base.scale, flash = 0, tilt = 0, squash = 0, pose = idle;
        const { lift, squash: landing } = dropIn(member.arrivedAt === null ? null : now - member.arrivedAt);
        squash = landing;

        const splash = beat && !beat.hurt ? want.splash : null;
        if (splash) {
            const at = chestOf(actor, scale, x, z);
            fx.showBolt(slot, hero.chest(), at, (a - BOLT_LEAVES) / (HIT_IMPACT - BOLT_LEAVES));
            if (t >= 0) {
                x += 0.35 * kick(t, 60, 480);
                flash = pulse(t, 220);
                tilt = -0.15 * Math.exp(-t / 260) * Math.cos(t / 90);
                const hurt = actor.spec.hurt;
                const hurtMs = actor.duration(hurt) * 1000;
                if (!actor.has(hurt)) squash = 0.25 * Math.exp(-t / 140) * Math.cos(t / 40);
                else if (t < hurtMs) pose = { clip: hurt, time: t / 1000, from: idle, fade: Math.min(1, t / 60) };
                else pose = { ...idle, from: { clip: hurt, time: hurtMs / 1000 }, fade: Math.min(1, (t - hurtMs) / 200) };
                fx.showSpellHit(slot, at, t / 1000, beat.seed + slot);
            }
            if (splash.killed && ms >= KILL_FALLS) {
                const d = ms - KILL_FALLS;
                if (actor.has(actor.spec.death)) pose = { clip: actor.spec.death, time: d / 1000, from: idle, fade: Math.min(1, d / 100) };
                else tilt = -0.6 * Math.min(1, d / 220);
                flash = Math.max(flash, pulse(d, 220));
                if (ms >= KILL_BURSTS) {
                    const b = ms - KILL_BURSTS;
                    scale *= b < 60 ? 1 + 0.3 * (b / 60) : Math.max(0, 1.3 * (1 - (b - 60) / 100));  // swells, then pops
                    fx.showPackDeath(slot, at, b / 1000, beat.seed + 3 + slot);
                }
            }
        }

        // The next one steps up to where the one in front stood.
        if (beat && beat.stepUp === slot && ms >= STEP_FROM) {
            const p = easeInOut((ms - STEP_FROM) / (STEP_TO - STEP_FROM));
            x = mix(x, front.x, p);
            z = mix(z, front.z, p);
            scale = mix(scale, 1, p);
        }

        actor.root.position.z = z;
        actor.apply({ x, lift, tilt, squash, scale, visible: scale > 0.01, flash, flashColor: 0xbfe8ff, ...poisonGlow(want.poison, seconds), ...pose });
        fx.showPoison(slot + 1, actor, seconds, want.poison);
        member.drawn = { x, z, scale };
    }
}

/** A Warlock's slow answer: the patron drains its price out of the hero, a purple glow and wisps rising off him. */
function showPatron(beat, ms, { hero, fx }) {
    if (!beat.patronPrice) return;
    const d = ms - PATRON_AT;
    if (d < 0) return;
    fx.showDrain(hero.chest(), d / 1000, beat.seed + 9);
}

/** The patron's purple on the hero (0..1): up quickly, then fading. */
function patronGlow(beat, ms) {
    if (!beat.patronPrice) return {};
    const d = ms - PATRON_AT;
    if (d < 0 || d > 800) return {};
    return { glow: 0.75 * (d < 100 ? d / 100 : 1 - (d - 100) / 700), glowColor: PATRON_PURPLE };
}

/** How long the hit-stop holds: longer for a big blow. */
function hitStop(beat) {
    const killed = beat.kind === 'kill' || beat.kind === 'floor' || beat.kind === 'boss';
    return beat.crit || beat.combo || beat.partyCombo || beat.fireball || beat.staggered || killed ? 110 : 60;
}

/** Where a hit lands on a waiting monster drawn at this size and place. */
function chestOf(actor, scale, x, z) {
    const height = (actor.spec.height || 2) * scale;
    return actor.chest().set(x, (actor.spec.hover || 0) + height * 0.55, z + 0.3);
}

function heroStrikes(beat, ms, { hero, foe, fx, stage, homes, seconds, foePoison }) {
    const killed = beat.kind === 'kill' || beat.kind === 'floor' || beat.kind === 'boss';
    const big = beat.crit || beat.combo || beat.partyCombo || beat.fireball || beat.staggered;
    // A Pugilist's combo grows with the streak: 2 a little bigger, every answer more, up to nearly twice.
    const power = beat.comboCount >= 2 ? Math.min(1.8, 1 + 0.15 * (beat.comboCount - 1)) : 1;
    const bigCombo = beat.comboCount >= 3;
    const a = stopped(ms, HIT_IMPACT, hitStop(beat));
    const t = a - HIT_IMPACT;  // since the blow landed (held still during the hit-stop)
    const heroIdle = { clip: hero.spec.idle, time: seconds };
    const foeIdle = { clip: foe.spec.idle, time: seconds + 0.37 };

    // The knight: dash in, swing so the blade lands on the impact, hold the follow-through, hop back.
    // A big combo leaps in.
    const swing = pickSwing(hero.spec, beat);
    const swingAt = time => ({ clip: swing.clip, time: swing.contact + (time - HIT_IMPACT) / 1000 });
    const strikeX = homes.foe - (foe.width / 2 + 1.1);
    let x = homes.hero, lift = 0;
    if (a < 160) {
        x = mix(homes.hero, strikeX, easeOut(a / 160));
        if (bigCombo) lift = Math.sin(Math.PI * a / 190) * 0.25 * power;
    }
    else if (a < 650) x = strikeX;
    else if (a < 1000) {
        const p = (a - 650) / 350;
        x = mix(strikeX, homes.hero, easeInOut(p));
        lift = Math.sin(Math.PI * p) * 0.6;
    }
    let heroPose;
    if (a < 100) heroPose = { ...swingAt(a), from: heroIdle, fade: a / 100 };
    else if (a < 700) heroPose = swingAt(a);
    else if (!killed || ms < CHEER_FROM || !hero.spec.cheer) heroPose = { ...heroIdle, from: swingAt(700), fade: Math.min(1, (a - 700) / 200) };
    else heroPose = { clip: hero.spec.cheer, time: (ms - CHEER_FROM) / 1000, from: heroIdle, fade: Math.min(1, (ms - CHEER_FROM) / 150) };
    hero.apply({ x, lift, ...patronGlow(beat, ms), ...heroPose });
    if (beat.healedYou) fx.showHeal(hero.chest(), (ms - HEAL_AT) / 1000);

    // The monster: knocked back and wobbling, flashing white. A kill: it goes down, then bursts.
    // Poisoned by this hit: the new stack shows from the impact, with a green puff.
    let foePose = foeIdle, squash = 0, tilt = 0, scale = 1;
    // Staggered: it stumbles well back and sways there, dazed, before it squares up again.
    let foeX = beat.staggered ? homes.foe + STUMBLE_BACK * stumble(t) : homes.foe + (big ? 0.8 : 0.45) * power * kick(t, 60, 520);
    const stacks = beat.poisoned && t < 0 ? foePoison - 1 : foePoison;
    if (t >= 0) {
        const hurt = foe.spec.hurt;
        const hurtMs = foe.duration(hurt) * 1000;
        if (!foe.has(hurt)) squash = 0.3 * Math.exp(-t / 140) * Math.cos(t / 40);  // no hurt clip: squash and stretch
        else if (t < hurtMs) foePose = { clip: hurt, time: t / 1000, from: foeIdle, fade: Math.min(1, t / 60) };
        else foePose = { ...foeIdle, from: { clip: hurt, time: hurtMs / 1000 }, fade: Math.min(1, (t - hurtMs) / 200) };
        tilt = beat.staggered ? -0.32 * Math.exp(-t / 650) * Math.sin(t / 110 + 1.2) : -0.2 * Math.exp(-t / 260) * Math.cos(t / 90);
    }
    let flash = pulse(t, beat.staggered ? 260 : 150);
    if (killed && ms >= KILL_FALLS) {
        const d = ms - KILL_FALLS;
        if (foe.has(foe.spec.death)) foePose = { clip: foe.spec.death, time: d / 1000, from: foeIdle, fade: Math.min(1, d / 100) };
        else tilt = -0.6 * Math.min(1, d / 220);
        flash = Math.max(flash, pulse(d, 220));
        if (ms >= KILL_BURSTS) {
            const b = ms - KILL_BURSTS;
            scale = b < 60 ? 1 + 0.3 * (b / 60) : Math.max(0, 1.3 * (1 - (b - 60) / 100));  // swells, then pops
        }
    }
    foe.apply({ x: foeX, tilt, squash, scale, visible: scale > 0.01, flash, flashColor: 0xffffff, ...poisonGlow(stacks, seconds), ...foePose });

    const chest = foe.chest();
    const tiltSlash = beat.seed % 2 ? 0.3 : -0.3;
    fx.showSlash(chest, (a - (HIT_IMPACT - 40)) / 1000, tiltSlash, (big ? 1.35 : 1) * power);
    if (bigCombo) fx.showSlash(chest, (a - COMBO_CROSS) / 1000, tiltSlash, 1.35 * power, true);
    fx.showSparks(chest, t / 1000, beat.seed, big || killed);
    fx.showShockwave(foe.root.position, t / 1000, big || killed, power);
    fx.showPoison(0, foe, seconds, stacks);
    if (beat.poisoned) fx.showPoisonPuff(chest, t / 1000, beat.seed + 5);
    if (killed) fx.showDeath(chest, (ms - KILL_BURSTS) / 1000, beat.seed);
    if (beat.staggered && !killed) fx.showDaze(headOf(foe), (ms - DAZE_FROM) / 1000);

    // The camera: a jolt on the blow (and on the burst), a punch in for a big hit, a lean in for the kill.
    shake(stage, ms, jolt(ms - HIT_IMPACT, (big || killed ? 0.16 : 0.07) * power) + (killed ? jolt(ms - KILL_BURSTS, 0.14) : 0));
    let zoom = 1 - (big ? 0.12 : 0.04) * power * punch(ms - HIT_IMPACT, 600);
    if (killed && ms > KILL_FALLS)
        zoom -= 0.06 * easeInOut(Math.min(1, (ms - KILL_FALLS) / 400));
    return { zoom, focus: 0 };
}

function monsterStrikes(beat, ms, { hero, foe, fx, stage, homes, seconds, foePoison }) {
    const charged = beat.charged;  // a boss's full-charge blow: everything about it is heavier
    const a = stopped(ms, HURT_IMPACT, charged ? 130 : 70);
    const t = a - HURT_IMPACT;
    const heroIdle = { clip: hero.spec.idle, time: seconds };
    const foeIdle = { clip: foe.spec.idle, time: seconds + 0.37 };

    // The monster: lunges at the knight, strikes on the impact, looms, backs off home.
    const attack = foe.spec.attack;
    const attackAt = time => (attack ? { clip: attack.clip, time: attack.contact + (time - HURT_IMPACT) / 1000 } : foeIdle);
    const strikeX = homes.hero + (hero.width / 2 + foe.width / 2 + 0.5);
    let foeX = homes.foe, tilt = 0;
    if (a < 200) foeX = mix(homes.foe, strikeX, easeOut(a / 200));
    else if (a < 650) foeX = strikeX;
    else if (a < 1100) foeX = mix(strikeX, homes.foe, easeInOut((a - 650) / 450));
    if (!attack) tilt = 0.35 * pulse(t + 120, 400);  // no attack clip: it throws itself forward
    // Charged: it leaps (up and down again by the impact) and lands with a squash, burning red until the
    // blow is spent.
    let lift = 0, squash = 0, glow = {};
    if (charged) {
        if (t < 0) lift = 1.1 * Math.sin(Math.PI * Math.min(1, a / HURT_IMPACT));
        else squash = 0.28 * Math.exp(-t / 110) * Math.cos(t / 40);
        glow = { glow: t < 0 ? 0.5 + 0.4 * (a / HURT_IMPACT) : 0.9 * pulse(t, 450), glowColor: CHARGE_RED };
    }
    let foePose;
    if (a < 100) foePose = { ...attackAt(a), from: foeIdle, fade: a / 100 };
    else if (a < 800) foePose = attackAt(a);
    else foePose = { ...foeIdle, from: attackAt(800), fade: Math.min(1, (a - 800) / 200) };
    foe.apply({ x: foeX, tilt, lift, squash, ...strongerGlow(poisonGlow(foePoison, seconds), glow), ...foePose });
    fx.showPoison(0, foe, seconds, foePoison);

    // The knight: reels back, flashing red (or, behind a Shield, takes it on the shield). A Tank takes every
    // hit on the shield (it takes less of it), still flashing red. Downed: falls, and stays down.
    const onShield = (beat.blocked || beat.tank) && hero.has(hero.spec.block);
    const reel = onShield ? hero.spec.block : hero.spec.hurt;
    let heroPose = heroIdle;
    const hurtMs = hero.duration(reel) * 1000;
    if (t >= 0 && t < hurtMs) heroPose = { clip: reel, time: t / 1000, from: heroIdle, fade: Math.min(1, t / 60) };
    else if (t >= hurtMs) heroPose = { ...heroIdle, from: { clip: reel, time: hurtMs / 1000 }, fade: Math.min(1, (t - hurtMs) / 200) };
    if (beat.kind === 'downed' && ms >= DOWNED_FALLS)
        heroPose = { clip: hero.spec.death, time: (ms - DOWNED_FALLS) / 1000, from: { clip: reel, time: (DOWNED_FALLS - HURT_IMPACT) / 1000 }, fade: Math.min(1, (ms - DOWNED_FALLS) / 100) };
    // He falls backwards, out towards the edge of the picture: slide him in a little as he goes down.
    const fallIn = beat.kind === 'downed' ? DOWNED_SLIDE * easeOut((ms - DOWNED_FALLS) / 400) : 0;
    // How far the blow throws him: a charged one much further (and off his feet), even behind a Tank's shield.
    const knock = beat.blocked ? 0.15 : charged ? (beat.tank ? 0.6 : 1.0) : beat.tank ? 0.22 : 0.4;
    hero.apply({
        x: homes.hero - knock * kick(t, 60, charged ? 800 : 500) + fallIn,
        lift: charged && t >= 0 && t < 300 ? 0.35 * Math.sin(Math.PI * t / 300) : 0,
        flash: beat.blocked ? 0.5 * pulse(t, 160) : charged ? pulse(t, 380) : beat.tank ? 0.7 * pulse(t, 220) : pulse(t, 260),
        flashColor: beat.blocked ? 0xbfe4ff : 0xff2020,
        ...patronGlow(beat, ms),
        ...heroPose,
    });

    const chest = hero.chest();
    if (beat.blocked) {
        fx.showSparks(chest, t / 1000, beat.seed, true);  // it glances off the shield
    } else if (onShield) {
        fx.showSparks(chest, t / 1000, beat.seed, false); // a Tank: it lands on the shield, and still hurts
        fx.showHurt(chest, t / 1000, beat.seed);
    } else {
        fx.showClaws(chest, t / 1000);
        fx.showHurt(chest, t / 1000, beat.seed);
    }
    fx.showShockwave(hero.root.position, t / 1000, charged, charged ? 1.6 : 1);
    if (charged) {
        fx.showSparks(chest, t / 1000, beat.seed + 1, true);
        const landed = hero.chest();  // the dust goes up on the floor between them, where the boss came down
        landed.set(mix(hero.root.position.x, foe.root.position.x, 0.5), 0.2, hero.root.position.z);
        fx.showSlamDust(landed, t / 1000, beat.seed + 2);
    }

    shake(stage, ms, charged
        ? jolt(ms - HURT_IMPACT, 0.34) + jolt(ms - CHARGED_SECOND_SHAKE, 0.12)
        : jolt(ms - HURT_IMPACT, 0.13));
    const zoom = 1 - (charged ? 0.15 : 0.06) * punch(ms - HURT_IMPACT, charged ? 700 : 500);
    return { zoom, focus: -0.5 * punch(ms - HURT_IMPACT, 900) };
}

/**
 * Which swing: a crit, a Pugilist's combo of 3 or more and a party combo have their own; a plain hit (and a
 * combo of 2, only made bigger) picks one by the beat's seed.
 */
function pickSwing(spec, beat) {
    if (beat.partyCombo || beat.comboCount >= 3) return spec.comboAttack;
    if (beat.crit) return spec.critAttack;
    return spec.attacks[beat.seed % spec.attacks.length];
}

/**
 * A staggered boss's stumble (0..1 of STUMBLE_BACK), t ms after the blow: knocked back fast, swaying where it
 * ends up, then back to its spot by about 1150 ms (the hit beat is 1.5 s).
 */
function stumble(t) {
    if (t < 0) return 0;
    if (t < 160) return easeOut(t / 160);
    if (t < 700) return 1 - 0.12 * Math.sin(((t - 160) / 540) * Math.PI * 2);
    return Math.max(0, 1 - easeInOut((t - 700) / 450));
}

/** A little over a monster's head, where dazed stars circle. */
function headOf(actor) {
    const at = actor.chest();
    at.y = (actor.spec.hover || 0) + (actor.spec.height || 2) * actor.holder.scale.y * 0.9;  // round its brow: higher goes out of the tall frame
    return at;
}

/** The beat's clock with the hit-stop in it: time stands still for length ms at the impact. */
function stopped(ms, impact, length) {
    if (ms < impact) return ms;
    return ms < impact + length ? impact : ms - length;
}

/** The camera shake: a fast wobble whose size is amount (world units). */
function shake(stage, ms, amount) {
    stage.shake.set(amount * Math.sin(ms * 0.13), amount * 0.7 * Math.cos(ms * 0.17));
}

/** A shake that's strongest the moment t passes 0 and dies away in about a quarter of a second. */
function jolt(t, size) {
    return t < 0 ? 0 : size * Math.exp(-t / 90);
}

/** 1 the moment t passes 0, fading to 0 over length ms. */
export function pulse(t, length) {
    return t < 0 || t >= length ? 0 : 1 - t / length;
}

/** Up quickly (80 ms), then back down by length ms: for the camera's punch in. */
function punch(t, length) {
    if (t < 0 || t >= length) return 0;
    return t < 80 ? t / 80 : 1 - (t - 80) / (length - 80);
}

/** A knockback: out in rise ms, then eased back home by back ms. */
export function kick(t, rise, back) {
    if (t < 0) return 0;
    if (t < rise) return easeOut(t / rise);
    return Math.max(0, 1 - easeInOut((t - rise) / back));
}

export function mix(a, b, p) {
    return a + (b - a) * p;
}

export function easeOut(p) {
    return 1 - Math.pow(1 - Math.min(1, Math.max(0, p)), 3);
}

export function easeInOut(p) {
    p = Math.min(1, Math.max(0, p));
    return p < 0.5 ? 2 * p * p : 1 - Math.pow(-2 * p + 2, 2) / 2;
}
