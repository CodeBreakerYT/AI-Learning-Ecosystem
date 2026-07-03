// Shared handles to the live renderer/scene/camera/rig, populated once by app.js
// so route modules (e.g. vrSetup) can reach them without re-threading props.
export const xrState = {
  scene: null,
  camera: null,
  renderer: null,
  rig: null,
  // Latest animation-loop frame delta (seconds); the Devices page uses this
  // as an honest, measured proxy for connection/render quality while an XR
  // session is active — there's no browser API for headset signal strength.
  frameDelta: 0,
  // Per-frame update callbacks (fn(delta)) that route modules register while
  // mounted — e.g. minigame physics — and must remove on unmount.
  updatables: new Set()
};
