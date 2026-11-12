// A player's face on their hero (hero photos). Their cut-out face (js/face-cutout.js lays every face out
// the same way) becomes the front of the hero's own head, under its helmet, hat or hood. It's bound to the
// same skeleton, so it bends with every animation exactly as the head does, flashes when hit, and is lit like
// the rest of the model.
//
// How: the hero's own eyes, brows, nose and mouth are bumps and dents in its head, and under a photo
// they'd look like beads and ridges. So while a face is worn, that hero's head (its own copy: other
// heroes keep theirs) is split in two. The front of the face, only its skin and what that surrounds (read from
// the head's own picture, so hair, a hood or a beard stays), is smoothed to the face's curve and becomes the
// "face" mesh, which shows the photo on the hero's skin colour. The rest of the head stays as it was.
// Nothing lies under the photo, so nothing can show through it. /dev/faces shows it on every hero.
//
// The heroes are chibis (a big head, a short lower face), so the face is redrawn to fit
// it first (the "face map"): the player's eyes, mouth and chin at set heights on the head, which squashes
// the lower face a little, like a cartoon. The numbers are in each hero's face (HEROES in models.js).
//
// Needs js/face-cutout.js on the page (its layout numbers and helpers).

import * as THREE from 'three';

// Which of the head's triangles are the face (smooth): skin is within SKIN_TOLERANCE (red + green + blue
// differences, 0-255 each) of the hero's skin colour; rows of the face are FACE_ROW high; a non-skin triangle
// (an eye) is face when there's skin within FACE_REACH on both sides of it. In the model's own units.
// DARK: an eye or the mouth's line (red + green + blue under it). WARM: a brow's brown (red over green by more
// than this; a hood's green isn't).
const SKIN_TOLERANCE = 90;
const DARK = 150;
const WARM = 30;
const FACE_ROW = 0.04;
const FACE_REACH = 0.3;
// How far round the bottom of the face the photo fades into the skin (faceMap), in the model's own units.
const CHIN_FADE = 0.045;

/**
 * Paints the face (an image or canvas from FaceCutout) onto the model's head. Returns { mesh, takeOff }:
 * mesh is the painted face (its material flashes with the rest), takeOff() puts the knight's own face back.
 */
export function paintFace(model, spec, face) {
    const head = model.getObjectByName(spec.mesh);
    const ownHead = head.geometry;
    const all = ownHead.index ? ownHead.toNonIndexed() : ownHead.clone();
    const faceTriangles = smooth(all, spec, looksOf(all, head.material.map, spec));
    head.geometry = triangles(all, i => !faceTriangles.has(i));

    const texture = new THREE.CanvasTexture(faceMap(face, spec));
    texture.colorSpace = THREE.SRGBColorSpace;
    texture.anisotropy = 4;
    const material = head.material.clone(); // the head's own shading
    material.map = texture;
    material.color.set(0xffffff);

    // Flat, the bumps' sides that faced away (under the nose, behind the eyes) are turned over and would
    // show dark; the ones that faced forwards already cover the whole face, so those are left out.
    const geometry = triangles(all, i => faceTriangles.has(i) && facesForwards(all, i));
    const { left, bottom, side } = spec.area;
    const position = geometry.attributes.position;
    const uv = new Float32Array(position.count * 2);
    for (let v = 0; v < position.count; v++) {
        uv[v * 2] = (position.getX(v) - left) / side;
        uv[v * 2 + 1] = (position.getY(v) - bottom) / side;
    }
    geometry.setAttribute('uv', new THREE.BufferAttribute(uv, 2));

    const mesh = new THREE.SkinnedMesh(geometry, material);
    mesh.name = 'HeroFace';
    mesh.frustumCulled = false;
    mesh.position.copy(head.position);
    mesh.quaternion.copy(head.quaternion);
    mesh.scale.copy(head.scale);
    head.parent.add(mesh);
    mesh.bind(head.skeleton, head.bindMatrix);

    const takeOff = () => {
        mesh.removeFromParent();
        geometry.dispose();
        texture.dispose();
        material.dispose();
        head.geometry.dispose();
        head.geometry = ownHead;
    };
    return { mesh, takeOff };
}

/**
 * Smooths the front of the face (geometry has no index: three points per triangle). The face's triangles
 * are whole ones (the head is low-poly, and a big triangle half on the face would cover part of the photo)
 * whose middle is in surface.area, and of those only the face itself: the skin, what the skin has on both
 * sides along its row (the eyes, the mouth: on a helmeted face anything, on an open face only what's dark,
 * since a hood can sit between skin too), and the brows (spec.brows: anything warm-coloured in their band, from
 * bottom to top and within halfWidth of the middle; a brow's faces come in the hair's brown as well as its own,
 * so colour alone can't tell them apart). Hair, a hood or a beard round the edge isn't face: it stays the hero's own, over the photo's
 * edges, so every head keeps its look. Every corner of the face is moved onto the face's curve,
 * z = front - curve * x * x (an open face also curves back under its chin, see below), facing straight out of
 * it, except where it meets hair (those stay put, see below), so the face still meets the rest of the head.
 * Returns its triangles (their first points' indexes). looks: what each triangle is (looksOf).
 */
function smooth(geometry, { surface: { front, curve, chin, area, enclosed }, brows }, { isSkin, isDark, isWarm }) {
    const position = geometry.attributes.position;
    const normal = geometry.attributes.normal;
    const at = i => `${position.getX(i).toFixed(4)},${position.getY(i).toFixed(4)},${position.getZ(i).toFixed(4)}`;
    const middle = (i, axis) => (position.getComponent(i, axis) + position.getComponent(i + 1, axis) + position.getComponent(i + 2, axis)) / 3;

    // The triangles in the area, and where its skin is, row by row.
    const inArea = [];
    const skinRows = new Map(); // row -> the x of each skin triangle's middle
    const rowOf = y => Math.round(y / FACE_ROW);
    for (let i = 0; i < position.count; i += 3) {
        const x = middle(i, 0), y = middle(i, 1), z = middle(i, 2);
        if (z < area.from || Math.abs(x) > area.halfWidth || y < area.bottom || y > area.top) continue;
        const skin = isSkin(i);
        inArea.push({ i, x, y, skin });
        if (skin) skinRows.set(rowOf(y), [...(skinRows.get(rowOf(y)) ?? []), x]);
    }
    // Skin close by on both sides, in its row or the ones either side (a low-poly row is ragged).
    const between = ({ x, y }) => {
        const near = [-1, 0, 1].flatMap(d => skinRows.get(rowOf(y) + d) ?? []);
        return near.some(s => s < x && x - s <= FACE_REACH) && near.some(s => s > x && s - x <= FACE_REACH);
    };

    const faceTriangles = new Set();
    const corners = new Set();
    const isFace = t => t.skin
        || (between(t) && (enclosed === 'any' || isDark(t.i)))
        || (brows && Math.abs(t.x) <= brows.halfWidth && t.y >= brows.bottom && t.y <= brows.top && isWarm(t.i));
    for (const triangle of inArea) {
        if (!isFace(triangle)) continue;
        faceTriangles.add(triangle.i);
        for (let v = triangle.i; v < triangle.i + 3; v++) corners.add(at(v));
    }
    // A corner the face shares with hair, a hood or a beard stays where it is: moved, it would pull their edge
    // in with it, and the skin behind would show through in jags. Where the face meets skin (the chin) the
    // corners move as before, so the head's own mouth crease is smoothed away rather than showing dark.
    for (let i = 0; i < position.count; i += 3) {
        if (faceTriangles.has(i) || isSkin(i)) continue;
        for (let v = i; v < i + 3; v++) corners.delete(at(v));
    }

    // An open face (chin set) also curves back under the chin: below chin.from it falls back by
    // chin.curve * (chin.from - y) squared, as the head's own chin does.
    const below = y => (chin && y < chin.from ? chin.from - y : 0);
    const facing = new THREE.Vector3();
    for (let i = 0; i < position.count; i++) {
        if (!corners.has(at(i))) continue;
        const x = position.getX(i);
        const down = below(position.getY(i));
        position.setZ(i, front - curve * x * x - (chin ? chin.curve * down * down : 0));
        facing.set(2 * curve * x, chin ? -2 * chin.curve * down : 0, 1).normalize();
        normal.setXYZ(i, facing.x, facing.y, facing.z);
    }
    return faceTriangles;
}

/**
 * What each triangle of the head (geometry without an index, by its first point) is, from the colour of the
 * head's own picture at its middle: isSkin(i) (near spec.skin), isDark(i) (an eye, a pupil, the mouth's line)
 * and isWarm(i) (reddish brown, like a brow or hair; not a hood's green). Without a picture every triangle
 * counts as skin (the whole area is face). The picture's pixels are read once and kept.
 */
function looksOf(geometry, map, spec) {
    const pixels = map?.image && pixelsOf(map.image);
    const uv = geometry.attributes.uv;
    if (!pixels || !uv) return { isSkin: () => true, isDark: () => false, isWarm: () => false };
    const colourAt = i => {
        const u = (uv.getX(i) + uv.getX(i + 1) + uv.getX(i + 2)) / 3;
        const v = (uv.getY(i) + uv.getY(i + 1) + uv.getY(i + 2)) / 3;
        // glTF pictures aren't flipped: v runs down the picture (texture.flipY is false).
        const column = Math.min(pixels.width - 1, Math.max(0, Math.floor((u - Math.floor(u)) * pixels.width)));
        const row = Math.min(pixels.height - 1, Math.max(0, Math.floor((v - Math.floor(v)) * pixels.height)));
        const k = (row * pixels.width + column) * 4;
        return [pixels.data[k], pixels.data[k + 1], pixels.data[k + 2]];
    };
    const hex = new THREE.Color(spec.skin).getHex(); // as written (sRGB, like the picture's pixels), not three's linear r, g, b
    const skin = [(hex >> 16) & 255, (hex >> 8) & 255, hex & 255];
    return {
        isSkin: i => colourAt(i).reduce((sum, c, n) => sum + Math.abs(c - skin[n]), 0) <= SKIN_TOLERANCE,
        isDark: i => colourAt(i).reduce((sum, c) => sum + c, 0) < DARK,
        isWarm: i => colourAt(i)[0] - colourAt(i)[1] > WARM,
    };
}

const pixelCache = new WeakMap();
function pixelsOf(image) {
    if (!pixelCache.has(image)) {
        const width = image.width, height = image.height;
        const canvas = FaceCutout.canvas(width, height);
        const g = canvas.getContext('2d', { willReadFrequently: true });
        g.drawImage(image, 0, 0);
        pixelCache.set(image, { width, height, data: g.getImageData(0, 0, width, height).data });
    }
    return pixelCache.get(image);
}

/** True if the triangle starting at point i is wound anticlockwise seen from the front (+Z), i.e. faces forwards. */
function facesForwards(geometry, i) {
    const p = geometry.attributes.position;
    const ax = p.getX(i + 1) - p.getX(i), ay = p.getY(i + 1) - p.getY(i);
    const bx = p.getX(i + 2) - p.getX(i), by = p.getY(i + 2) - p.getY(i);
    return ax * by - ay * bx > 1e-7;
}

/** A new geometry with just the triangles keep(firstPointIndex) picks, from one without an index. */
function triangles(geometry, keep) {
    const kept = [];
    for (let i = 0; i < geometry.attributes.position.count; i += 3)
        if (keep(i)) kept.push(i, i + 1, i + 2);

    const picked = new THREE.BufferGeometry();
    for (const [name, source] of Object.entries(geometry.attributes)) {
        const values = new Float32Array(kept.length * source.itemSize);
        kept.forEach((v, k) => {
            for (let c = 0; c < source.itemSize; c++) values[k * source.itemSize + c] = source.getComponent(v, c);
        });
        picked.setAttribute(name, new THREE.BufferAttribute(values, source.itemSize));
    }
    return picked;
}

/**
 * The face redrawn over a square of the knight's head (spec.area, seen from the front, in the model's own
 * units), on the knight's skin colour: one straight scale across (pupil to pupil = spec.eyeSpan), and band by
 * band down (forehead, eyes to mouth, mouth to chin), so each feature lands at its height on the head.
 */
function faceMap(face, spec) {
    const S = face.naturalWidth || face.width;
    const size = 512;
    const map = FaceCutout.canvas(size, size);
    const g = map.getContext('2d');
    g.fillStyle = spec.skin;
    g.fillRect(0, 0, size, size);

    const { left, bottom, side } = spec.area;
    const unitsPerFacePixel = spec.eyeSpan / (FaceCutout.EYE_SPAN * S);
    const column = x => ((x - left) / side) * size; // model x -> map column
    const faceLeft = column((0 - S / 2) * unitsPerFacePixel);
    const faceWidth = column((S / 2) * unitsPerFacePixel) - faceLeft;

    // Down the map: model heights (top to bottom, as negatives so they run upwards) -> rows of the face.
    const heights = [spec.top, spec.eyes, spec.mouth, spec.chin].map(y => -y);
    const rows = [FaceCutout.TOP, FaceCutout.EYES, FaceCutout.MOUTH, FaceCutout.CHIN].map(share => share * S);
    const faceRow = mapRow => FaceCutout.between(-(bottom + side - (mapRow / size) * side), heights, rows);
    for (let y = 0; y < size; y++) {
        const top = faceRow(y);
        const height = faceRow(y + 1) - top;
        if (top + height < 0 || top > S) continue;
        g.drawImage(face, 0, top, S, Math.max(0.01, height), faceLeft, y, faceWidth, 1);
    }

    // The face ends at surface.area.bottom (under the mouth, or under an open face's chin) along a zigzag of
    // the head's triangles, where the head's own skin carries on. Fade the photo into the skin there, so a
    // beard or a shadow doesn't end in dark teeth (from CHIN_FADE below that line, all skin, to CHIN_FADE above
    // it, all photo).
    const row = y => ((bottom + side - y) / side) * size; // model y -> map row
    const edge = spec.surface.area.bottom;
    const fade = g.createLinearGradient(0, row(edge + CHIN_FADE), 0, row(edge - CHIN_FADE));
    fade.addColorStop(0, `${spec.skin}00`);
    fade.addColorStop(1, spec.skin);
    g.fillStyle = fade;
    g.fillRect(0, row(edge + CHIN_FADE), size, size);
    return map;
}
