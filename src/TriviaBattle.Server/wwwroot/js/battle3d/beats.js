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

/**
 * One frame of a beat. beat = { kind, hurt, crit, combo, partyCombo, seed }.
 * cast = { hero, foe, fx, stage, homes: { hero, foe } (x), seconds (for idle loops) }.
 * Returns how the camera should lean: { zoom (1 = normal, less = closer), focus (x shift) }.
 */
export function showBeat(beat, ms, cast) {
    cast.fx.hideAll();
    return beat.hurt ? monsterStrikes(beat, ms, cast) : heroStrikes(beat, ms, cast);
}

/** Between beats (or while a question is up): both stand at home, breathing. */
export function showIdle(cast, { heroDown, foeArrivedMs }) {
    const { hero, foe, fx, stage, homes, seconds } = cast;
    fx.hideAll();
    stage.shake.set(0, 0);

    // Downed: the knight stays on the floor until they're back up.
    hero.apply(heroDown && hero.spec.death
        ? { x: homes.hero + DOWNED_SLIDE, clip: hero.spec.death, time: 99 }
        : { x: homes.hero, clip: hero.spec.idle, time: seconds });

    // A new monster drops in from above and lands with a squash.
    let lift = 0, squash = 0;
    if (foeArrivedMs !== null && foeArrivedMs < 600) {
        const fall = Math.min(1, foeArrivedMs / 300);
        lift = 4 * (1 - fall) * (1 - fall);
        const landed = foeArrivedMs - 300;
        if (landed > 0) squash = 0.3 * Math.exp(-landed / 90) * Math.cos(landed / 45);
    }
    foe.apply({ x: homes.foe, lift, squash, clip: foe.spec.idle, time: seconds + 0.37 });
    return { zoom: 1, focus: 0 };
}

function heroStrikes(beat, ms, { hero, foe, fx, stage, homes, seconds }) {
    const killed = beat.kind === 'kill' || beat.kind === 'floor' || beat.kind === 'boss';
    const big = beat.crit || beat.combo || beat.partyCombo || beat.fireball;
    const a = stopped(ms, HIT_IMPACT, big || killed ? 110 : 60);
    const t = a - HIT_IMPACT;  // since the blow landed (held still during the hit-stop)
    const heroIdle = { clip: hero.spec.idle, time: seconds };
    const foeIdle = { clip: foe.spec.idle, time: seconds + 0.37 };

    // The knight: dash in, swing so the blade lands on the impact, hold the follow-through, hop back.
    const swing = pickSwing(hero.spec, beat);
    const swingAt = time => ({ clip: swing.clip, time: swing.contact + (time - HIT_IMPACT) / 1000 });
    const strikeX = homes.foe - (foe.width / 2 + 1.1);
    let x = homes.hero, lift = 0;
    if (a < 160) x = mix(homes.hero, strikeX, easeOut(a / 160));
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
    hero.apply({ x, lift, ...heroPose });

    // The monster: knocked back and wobbling, flashing white. A kill: it goes down, then bursts.
    let foePose = foeIdle, squash = 0, tilt = 0, scale = 1;
    let foeX = homes.foe + (big ? 0.8 : 0.45) * kick(t, 60, 520);
    if (t >= 0) {
        const hurt = foe.spec.hurt;
        const hurtMs = foe.duration(hurt) * 1000;
        if (!foe.has(hurt)) squash = 0.3 * Math.exp(-t / 140) * Math.cos(t / 40);  // no hurt clip: squash and stretch
        else if (t < hurtMs) foePose = { clip: hurt, time: t / 1000, from: foeIdle, fade: Math.min(1, t / 60) };
        else foePose = { ...foeIdle, from: { clip: hurt, time: hurtMs / 1000 }, fade: Math.min(1, (t - hurtMs) / 200) };
        tilt = -0.2 * Math.exp(-t / 260) * Math.cos(t / 90);
    }
    let flash = pulse(t, 150);
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
    foe.apply({ x: foeX, tilt, squash, scale, visible: scale > 0.01, flash, flashColor: 0xffffff, ...foePose });

    const chest = foe.chest();
    fx.showSlash(chest, (a - (HIT_IMPACT - 40)) / 1000, beat.seed % 2 ? 0.3 : -0.3, big);
    fx.showSparks(chest, t / 1000, beat.seed, big || killed);
    fx.showShockwave(foe.root.position, t / 1000, big || killed);
    if (killed) fx.showDeath(chest, (ms - KILL_BURSTS) / 1000, beat.seed);

    // The camera: a jolt on the blow (and on the burst), a punch in for a big hit, a lean in for the kill.
    shake(stage, ms, jolt(ms - HIT_IMPACT, big || killed ? 0.16 : 0.07) + (killed ? jolt(ms - KILL_BURSTS, 0.14) : 0));
    let zoom = 1 - (big ? 0.12 : 0.04) * punch(ms - HIT_IMPACT, 600);
    if (killed && ms > KILL_FALLS)
        zoom -= 0.06 * easeInOut(Math.min(1, (ms - KILL_FALLS) / 400));
    return { zoom, focus: 0 };
}

function monsterStrikes(beat, ms, { hero, foe, fx, stage, homes, seconds }) {
    const a = stopped(ms, HURT_IMPACT, 70);
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
    let foePose;
    if (a < 100) foePose = { ...attackAt(a), from: foeIdle, fade: a / 100 };
    else if (a < 800) foePose = attackAt(a);
    else foePose = { ...foeIdle, from: attackAt(800), fade: Math.min(1, (a - 800) / 200) };
    foe.apply({ x: foeX, tilt, ...foePose });

    // The knight: reels back, flashing red (or, behind a Shield, takes it on the shield). Downed: falls, and stays down.
    const reel = beat.blocked && hero.has(hero.spec.block) ? hero.spec.block : hero.spec.hurt;
    let heroPose = heroIdle;
    const hurtMs = hero.duration(reel) * 1000;
    if (t >= 0 && t < hurtMs) heroPose = { clip: reel, time: t / 1000, from: heroIdle, fade: Math.min(1, t / 60) };
    else if (t >= hurtMs) heroPose = { ...heroIdle, from: { clip: reel, time: hurtMs / 1000 }, fade: Math.min(1, (t - hurtMs) / 200) };
    if (beat.kind === 'downed' && ms >= DOWNED_FALLS)
        heroPose = { clip: hero.spec.death, time: (ms - DOWNED_FALLS) / 1000, from: { clip: hero.spec.hurt, time: (DOWNED_FALLS - HURT_IMPACT) / 1000 }, fade: Math.min(1, (ms - DOWNED_FALLS) / 100) };
    // He falls backwards, out towards the edge of the picture: slide him in a little as he goes down.
    const fallIn = beat.kind === 'downed' ? DOWNED_SLIDE * easeOut((ms - DOWNED_FALLS) / 400) : 0;
    hero.apply({
        x: homes.hero - (beat.blocked ? 0.15 : 0.4) * kick(t, 60, 500) + fallIn,
        flash: beat.blocked ? 0.5 * pulse(t, 160) : pulse(t, 260),
        flashColor: beat.blocked ? 0xbfe4ff : 0xff2020,
        ...heroPose,
    });

    const chest = hero.chest();
    if (beat.blocked) {
        fx.showSparks(chest, t / 1000, beat.seed, true);  // it glances off the shield
    } else {
        fx.showClaws(chest, t / 1000);
        fx.showHurt(chest, t / 1000, beat.seed);
    }
    fx.showShockwave(hero.root.position, t / 1000, false);

    shake(stage, ms, jolt(ms - HURT_IMPACT, 0.13));
    return { zoom: 1 - 0.06 * punch(ms - HURT_IMPACT, 500), focus: -0.5 * punch(ms - HURT_IMPACT, 900) };
}

/** Which swing: a crit, a combo and a party combo have their own; a plain hit picks one by the beat's seed. */
function pickSwing(spec, beat) {
    if (beat.partyCombo || beat.combo) return spec.comboAttack;
    if (beat.crit) return spec.critAttack;
    return spec.attacks[beat.seed % spec.attacks.length];
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
function pulse(t, length) {
    return t < 0 || t >= length ? 0 : 1 - t / length;
}

/** Up quickly (80 ms), then back down by length ms: for the camera's punch in. */
function punch(t, length) {
    if (t < 0 || t >= length) return 0;
    return t < 80 ? t / 80 : 1 - (t - 80) / (length - 80);
}

/** A knockback: out in rise ms, then eased back home by back ms. */
function kick(t, rise, back) {
    if (t < 0) return 0;
    if (t < rise) return easeOut(t / rise);
    return Math.max(0, 1 - easeInOut((t - rise) / back));
}

function mix(a, b, p) {
    return a + (b - a) * p;
}

function easeOut(p) {
    return 1 - Math.pow(1 - Math.min(1, Math.max(0, p)), 3);
}

function easeInOut(p) {
    p = Math.min(1, Math.max(0, p));
    return p < 0.5 ? 2 * p * p : 1 - Math.pow(-2 * p + 2, 2) / 2;
}
