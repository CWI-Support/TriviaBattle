# MediaPipe Tasks Vision 1.1.1 (vendored)

The face finder for hero photos (`js/face-cutout.js`): it finds a face's outline, eyes and mouth in the
station camera's picture, so only the face is kept and it lines up with the knight's.

- `vision_bundle.mjs`, `wasm/vision_wasm_internal.js`, `wasm/vision_wasm_internal.wasm`: from the npm
  package `@mediapipe/tasks-vision@1.1.1` (only the SIMD build: every Chrome and Edge since 2021 has SIMD).
- `face_landmarker.task`: the Face Landmarker model (float16, version 1), from
  `https://storage.googleapis.com/mediapipe-models/face_landmarker/face_landmarker/float16/1/face_landmarker.task`.

Both are © Google LLC, Apache License 2.0 (https://www.apache.org/licenses/LICENSE-2.0).
Vendored rather than loaded from a CDN because the venue network may be offline.
