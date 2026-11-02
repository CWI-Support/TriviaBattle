// A player's face on the knight (hero photos). Their cut-out face (js/face-cutout.js lays every face out
// the same way) becomes the front of the knight's own head, under its helmet. It's bound to the same
// skeleton, so it bends with every animation exactly as the head does, flashes when hit, and is lit like the
// rest of the model.
//
// How: the knight's own eyes, brows, nose and mouth are bumps and dents in its head, and under a photo
// they'd look like beads and ridges. So while a face is worn, that knight's head (its own copy: other
// knights keep theirs) is split in two. The front of the face is smoothed to the face's curve and becomes
// the "face" mesh, which shows the photo on the knight's skin colour. The rest of the head stays as it was.
// Nothing lies under the photo, so nothing can show through it.
//
// The knight is a chibi (a big head, a short lower face inside the helmet), so the face is redrawn to fit
// it first (the "face map"): the player's eyes, mouth and chin at set heights on the head, which squashes
// the lower face a little, like a cartoon. The numbers are in MODELS.hero.face.
//
// Needs js/face-cutout.js on the page (its layout numbers and helpers).

import * as THREE from 'three';

/**
 * Paints the face (an image or canvas from FaceCutout) onto the model's head. Returns { mesh, takeOff }:
 * mesh is the painted face (its material flashes with the rest), takeOff() puts the knight's own face back.
 */
export function paintFace(model, spec, face) {
    const head = model.getObjectByName(spec.mesh);
    const ownHead = head.geometry;
    const all = ownHead.index ? ownHead.toNonIndexed() : ownHead.clone();
    const faceTriangles = smooth(all, spec.surface);
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
 * are the ones whose middle is in surface.area, whole: the head is low-poly, and a big triangle half on
 * the face would cover part of the photo. Every corner they use is moved onto the face's curve,
 * z = front - curve * x * x, facing straight out of it; so is the same corner in the triangles round the
 * edge, so the two parts still meet. Returns the face's triangles (their first points' indexes).
 */
function smooth(geometry, { front, curve, area }) {
    const position = geometry.attributes.position;
    const normal = geometry.attributes.normal;
    const at = i => `${position.getX(i).toFixed(4)},${position.getY(i).toFixed(4)},${position.getZ(i).toFixed(4)}`;

    const faceTriangles = new Set();
    const corners = new Set();
    for (let i = 0; i < position.count; i += 3) {
        const x = (position.getX(i) + position.getX(i + 1) + position.getX(i + 2)) / 3;
        const y = (position.getY(i) + position.getY(i + 1) + position.getY(i + 2)) / 3;
        const z = (position.getZ(i) + position.getZ(i + 1) + position.getZ(i + 2)) / 3;
        if (z < area.from || Math.abs(x) > area.halfWidth || y < area.bottom || y > area.top) continue;
        faceTriangles.add(i);
        for (let v = i; v < i + 3; v++) corners.add(at(v));
    }

    const facing = new THREE.Vector3();
    for (let i = 0; i < position.count; i++) {
        if (!corners.has(at(i))) continue;
        const x = position.getX(i);
        position.setZ(i, front - curve * x * x);
        facing.set(2 * curve * x, 0, 1).normalize();
        normal.setXYZ(i, facing.x, facing.y, facing.z);
    }
    return faceTriangles;
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
    return map;
}
