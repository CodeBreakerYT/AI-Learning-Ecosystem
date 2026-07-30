import * as THREE from "three";
import * as CANNON from "cannon-es";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { Sky } from "three/addons/objects/Sky.js";
import { clone as cloneSkinned } from "three/addons/utils/SkeletonUtils.js";
import { xrState } from "../core/xrState.js";
import { connectVRSession } from "../core/xrSession.js";
import { createInteractionManager } from "../core/interaction.js";
import { createGrabSystem } from "../core/grabSystem.js";
import { createTextPanel, createButton3D, disposeTree } from "../core/textPanel.js";
import { createPhysicsWorld } from "../core/physicsWorld.js";

/**
 * World — a real place to walk around in, instead of a menu of minigames.
 * Unlike Learn's games (whose props are parented to the player rig so they
 * stay in reach no matter where you walk), everything here lives in real
 * scene/world space under one `worldGroup`: a big terrain you actually
 * explore with locomotion, a handful of live animated animals (real glTF
 * models, not primitives), and a physics playground where crates and
 * barrels get real cannon-es gravity, collision and restitution when you
 * grab and throw them — the release velocity from grabSystem becomes actual
 * rigid-body velocity instead of hand-rolled projectile math.
 */

const KEY_SPEED = 2.2; // desktop WASD speed, m/s — thumbstick locomotion (locomotion.js) covers VR
const PLAYGROUND_CENTER = new THREE.Vector3(0, 0, -6);
const CRATE_HALF = 0.22;
const BARREL_RADIUS = 0.22;
const BARREL_HEIGHT = 0.5;
const BALL_RADIUS = 0.13;
const RING_CENTER = new THREE.Vector3(PLAYGROUND_CENTER.x + 3.6, 0, PLAYGROUND_CENTER.z + 1.6);
const RING_RADIUS = 0.35;
const RING_SETTLE_SPEED = 0.35; // below this speed, a prop inside the ring counts as "landed"
const BEST_SCORE_KEY = "ale.world.bestRingScore";
const COLLIDE_SOUND_MIN_SPEED = 1.2; // m/s of impact — below this, skip the thud (resting jitter)
const COLLIDE_SOUND_COOLDOWN = 150; // ms, per body — avoids machine-gun buzz on a resting contact

const CREATURES = [
  { file: "Flamingo.glb", targetSize: 0.6, extra: 1 },
  { file: "Parrot.glb", targetSize: 0.32, extra: 1 },
  { file: "Stork.glb", targetSize: 0.7, extra: 0 },
  { file: "Horse.glb", targetSize: 1.5, extra: 0 }
];

const statusEl = () => document.getElementById("world-status");
const enterVRBtn = () => document.getElementById("world-enter-vr");

let sceneRef = null;
let worldGroup = null;
let physics = null;
let grab = null;
let interaction = null;
let updateFn = null;
let mixers = [];
let props = []; // { mesh, body, home, kind, inRing }
let scoreboard = null;
let ringMarker = null;
let keysDown = new Set();
let disposed = true;
let ringScore = 0;
let bestRingScore = 0;
let audioCtx = null;

const keyQuaternion = new THREE.Quaternion();
const keyDirection = new THREE.Vector3();
const flatPos = new THREE.Vector2();
const ringFlatPos = new THREE.Vector2(RING_CENTER.x, RING_CENTER.z);

function setStatus(message, isError = false) {
  const el = statusEl();
  if (!el) return;
  el.textContent = message;
  el.classList.toggle("is-error", isError);
}

function buildGrassTexture() {
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = 256;
  const ctx = canvas.getContext("2d");
  ctx.fillStyle = "#2f6b3a";
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  // A few large soft tonal patches first, so the texture reads as terrain
  // variation from a distance instead of pure noise...
  for (let i = 0; i < 10; i++) {
    const x = Math.random() * canvas.width;
    const y = Math.random() * canvas.height;
    const r = 30 + Math.random() * 50;
    const gradient = ctx.createRadialGradient(x, y, 0, x, y, r);
    const shade = Math.random() < 0.5 ? "rgba(38,90,44,0.5)" : "rgba(70,130,60,0.4)";
    gradient.addColorStop(0, shade);
    gradient.addColorStop(1, "rgba(0,0,0,0)");
    ctx.fillStyle = gradient;
    ctx.beginPath();
    ctx.arc(x, y, r, 0, Math.PI * 2);
    ctx.fill();
  }
  // ...then fine speckle on top for close-up texture.
  for (let i = 0; i < 900; i++) {
    const x = Math.random() * canvas.width;
    const y = Math.random() * canvas.height;
    ctx.fillStyle = Math.random() < 0.5 ? "rgba(20,60,28,0.35)" : "rgba(120,180,90,0.25)";
    ctx.fillRect(x, y, 2, 2);
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
  texture.repeat.set(24, 24);
  texture.anisotropy = 4;
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

function buildSky() {
  const sky = new Sky();
  sky.scale.setScalar(4500);
  const uniforms = sky.material.uniforms;
  uniforms.turbidity.value = 6;
  uniforms.rayleigh.value = 2;
  uniforms.mieCoefficient.value = 0.006;
  uniforms.mieDirectionalG.value = 0.8;

  const sun = new THREE.Vector3();
  const phi = THREE.MathUtils.degToRad(90 - 35);
  const theta = THREE.MathUtils.degToRad(160);
  sun.setFromSphericalCoords(1, phi, theta);
  uniforms.sunPosition.value.copy(sun);
  worldGroup.add(sky);

  const sunLight = new THREE.DirectionalLight(0xfff2e0, 1.4);
  sunLight.position.copy(sun).multiplyScalar(20).add(PLAYGROUND_CENTER);
  sunLight.target.position.copy(PLAYGROUND_CENTER);
  worldGroup.add(sunLight.target);

  // Shadows are scoped to this route (toggled on the renderer in mount(),
  // off again in unmount()) and the shadow camera frustum is sized to just
  // the playground, not the full 80x80 terrain — keeping the shadow map cheap.
  sunLight.castShadow = true;
  sunLight.shadow.mapSize.set(1024, 1024);
  sunLight.shadow.camera.left = -8;
  sunLight.shadow.camera.right = 8;
  sunLight.shadow.camera.top = 8;
  sunLight.shadow.camera.bottom = -8;
  sunLight.shadow.camera.near = 1;
  sunLight.shadow.camera.far = 40;
  sunLight.shadow.bias = -0.002;
  worldGroup.add(sunLight);
}

function buildTerrain() {
  const ground = new THREE.Mesh(
    new THREE.PlaneGeometry(80, 80, 1, 1),
    new THREE.MeshStandardMaterial({ map: buildGrassTexture(), roughness: 1 })
  );
  ground.rotation.x = -Math.PI / 2;
  ground.receiveShadow = true;
  worldGroup.add(ground);

  // Decorative low-poly hills sit outside the walkable/physics area — purely
  // a backdrop, since locomotion is flat and has no vertical collision.
  const hillMaterial = new THREE.MeshStandardMaterial({ color: 0x3d7a46, roughness: 1 });
  for (let i = 0; i < 10; i++) {
    const angle = (i / 10) * Math.PI * 2;
    const dist = 26 + Math.random() * 8;
    const hill = new THREE.Mesh(new THREE.ConeGeometry(3 + Math.random() * 2, 2.5 + Math.random() * 2, 8), hillMaterial);
    hill.position.set(Math.cos(angle) * dist, 0, Math.sin(angle) * dist);
    worldGroup.add(hill);
  }

  physics.addGroundPlane(0);
}

function fitAndGround(model, targetSize) {
  const box = new THREE.Box3().setFromObject(model);
  const size = box.getSize(new THREE.Vector3());
  const maxDim = Math.max(size.x, size.y, size.z) || 1;
  model.scale.setScalar(targetSize / maxDim);

  const groundedBox = new THREE.Box3().setFromObject(model);
  model.position.y -= groundedBox.min.y;
}

function placeCreature(model, spotIndex, spotCount) {
  const angle = (spotIndex / spotCount) * Math.PI * 2 + Math.random() * 0.4;
  const dist = 8 + Math.random() * 6;
  model.position.x += Math.cos(angle) * dist;
  model.position.z += Math.sin(angle) * dist;
  model.rotation.y = Math.random() * Math.PI * 2;
  model.traverse((node) => { if (node.isMesh) node.castShadow = true; });
  worldGroup.add(model);
}

function loadCreatures() {
  const loader = new GLTFLoader();
  const spotCount = CREATURES.reduce((sum, c) => sum + 1 + (c.extra ?? 0), 0);
  let spotIndex = 0;

  const loadPromises = CREATURES.map(({ file, targetSize, extra = 0 }) =>
    new Promise((resolve) => {
      loader.load(
        `${import.meta.env.BASE_URL}assets/models/world/${file}`,
        (gltf) => {
          if (disposed) { resolve(); return; } // navigated away before the download finished
          const primary = gltf.scene;
          fitAndGround(primary, targetSize);
          placeCreature(primary, spotIndex++, spotCount);
          if (gltf.animations?.length) {
            const mixer = new THREE.AnimationMixer(primary);
            mixer.clipAction(gltf.animations[0]).play();
            mixers.push(mixer);
          }

          // A couple of extra clones per bird for a livelier world without
          // downloading more assets — SkeletonUtils.clone (not plain
          // Object3D.clone) is required so the animated skeleton's bone
          // bindings copy correctly onto the duplicate.
          for (let i = 0; i < extra; i++) {
            const copy = cloneSkinned(primary);
            placeCreature(copy, spotIndex++, spotCount);
            if (gltf.animations?.length) {
              const mixer = new THREE.AnimationMixer(copy);
              mixer.clipAction(gltf.animations[0]).play();
              mixers.push(mixer);
            }
          }
          resolve();
        },
        undefined,
        (err) => { console.warn(`Couldn't load ${file}:`, err.message); resolve(); }
      );
    })
  );

  return Promise.allSettled(loadPromises);
}

function buildCrate() {
  const group = new THREE.Group();
  const boxMat = new THREE.MeshStandardMaterial({ color: 0xb5793b, roughness: 0.85 });
  const trimMat = new THREE.MeshStandardMaterial({ color: 0x8a5a2b, roughness: 0.9 });
  const box = new THREE.Mesh(new THREE.BoxGeometry(CRATE_HALF * 2, CRATE_HALF * 2, CRATE_HALF * 2), boxMat);
  group.add(box);

  // Edge trim battens — the detail that reads as "a built crate" instead of a bare cube.
  const battenGeo = new THREE.BoxGeometry(CRATE_HALF * 2.05, 0.03, 0.03);
  [-1, 1].forEach((sx) => {
    [-1, 1].forEach((sy) => {
      const batten = new THREE.Mesh(battenGeo, trimMat);
      batten.position.set(0, sy * CRATE_HALF * 0.9, sx * CRATE_HALF * 0.9);
      group.add(batten);
      const battenV = new THREE.Mesh(battenGeo, trimMat);
      battenV.rotation.z = Math.PI / 2;
      battenV.position.set(sx * CRATE_HALF * 0.9, sy * CRATE_HALF * 0.9, 0);
      group.add(battenV);
    });
  });

  return group;
}

function buildBarrel() {
  const group = new THREE.Group();
  const body = new THREE.Mesh(
    new THREE.CylinderGeometry(BARREL_RADIUS, BARREL_RADIUS * 0.92, BARREL_HEIGHT, 20),
    new THREE.MeshStandardMaterial({ color: 0x6b4a2f, roughness: 0.8 })
  );
  group.add(body);
  const bandMat = new THREE.MeshStandardMaterial({ color: 0x3a3f47, roughness: 0.5, metalness: 0.4 });
  [-0.16, 0, 0.16].forEach((y) => {
    const band = new THREE.Mesh(new THREE.CylinderGeometry(BARREL_RADIUS * 1.02, BARREL_RADIUS * 1.02, 0.04, 20), bandMat);
    band.position.y = y;
    group.add(band);
  });
  return group;
}

function buildRamp() {
  const ramp = new THREE.Mesh(
    new THREE.BoxGeometry(1.6, 0.08, 1.0),
    new THREE.MeshStandardMaterial({ color: 0x8a6a45, roughness: 0.7 })
  );
  ramp.position.set(PLAYGROUND_CENTER.x - 2.2, 0.25, PLAYGROUND_CENTER.z);
  ramp.rotation.z = THREE.MathUtils.degToRad(18);
  ramp.castShadow = true;
  ramp.receiveShadow = true;
  worldGroup.add(ramp);

  const rampBody = new CANNON.Body({ type: CANNON.Body.STATIC, material: physics.materials.ground });
  rampBody.addShape(new CANNON.Box(new CANNON.Vec3(0.8, 0.04, 0.5)));
  rampBody.position.copy(ramp.position);
  rampBody.quaternion.copy(ramp.quaternion);
  physics.world.addBody(rampBody);
}

function buildBall() {
  const mesh = new THREE.Mesh(
    new THREE.SphereGeometry(BALL_RADIUS, 24, 18),
    new THREE.MeshStandardMaterial({ color: 0xfbbf24, emissive: 0xfbbf24, emissiveIntensity: 0.25, roughness: 0.35 })
  );
  return mesh;
}

function buildRing() {
  const ring = new THREE.Group();
  ring.position.copy(RING_CENTER);
  const glow = new THREE.Mesh(
    new THREE.TorusGeometry(RING_RADIUS, 0.02, 12, 40),
    new THREE.MeshStandardMaterial({ color: 0x22d3ee, emissive: 0x22d3ee, emissiveIntensity: 0.6 })
  );
  glow.rotation.x = Math.PI / 2;
  glow.position.y = 0.02;
  ring.add(glow);
  const well = new THREE.Mesh(
    new THREE.CircleGeometry(RING_RADIUS * 0.9, 32),
    new THREE.MeshBasicMaterial({ color: 0x0d2230, transparent: true, opacity: 0.55, side: THREE.DoubleSide })
  );
  well.rotation.x = -Math.PI / 2;
  well.position.y = 0.005;
  ring.add(well);
  worldGroup.add(ring);
  ringMarker = glow;
}

function flashRing() {
  ringMarker.material.emissiveIntensity = 1.6;
  setTimeout(() => { if (ringMarker) ringMarker.material.emissiveIntensity = 0.6; }, 350);
}

function ensureAudio() {
  if (audioCtx) return audioCtx;
  const Ctx = window.AudioContext || window.webkitAudioContext;
  if (!Ctx) return null;
  audioCtx = new Ctx();
  return audioCtx;
}

// Short synthesized percussive thud — no audio assets. Pitch/decay vary a
// little by prop kind so crates/barrels/the ball don't all sound identical.
const THUD_PROFILE = {
  crate: { freq: 110, decay: 0.14 },
  barrel: { freq: 90, decay: 0.18 },
  ball: { freq: 220, decay: 0.1 }
};

function playThud(kind, strength) {
  const ctx = audioCtx;
  if (!ctx) return;
  const { freq, decay } = THUD_PROFILE[kind] ?? THUD_PROFILE.crate;
  const volume = Math.min(0.5, 0.08 + strength * 0.05);

  const osc = ctx.createOscillator();
  osc.type = "triangle";
  osc.frequency.setValueAtTime(freq, ctx.currentTime);
  osc.frequency.exponentialRampToValueAtTime(freq * 0.6, ctx.currentTime + decay);

  const gain = ctx.createGain();
  gain.gain.setValueAtTime(volume, ctx.currentTime);
  gain.gain.exponentialRampToValueAtTime(0.001, ctx.currentTime + decay);

  osc.connect(gain).connect(ctx.destination);
  osc.start();
  osc.stop(ctx.currentTime + decay);
}

// Wires a mesh + cannon body into the grab system: while held, the body goes
// kinematic and physics stops driving the mesh (grabSystem's own followHand
// takes over instead); on release, the body snaps to the mesh's current
// pose, goes dynamic again, and inherits the hand's real release velocity
// (boosted a bit so throws feel punchier) — real engine-driven gravity,
// collision and restitution on every throw. Also wires a synthesized impact
// thud off cannon-es's own 'collide' event, throttled per body so a resting
// contact doesn't machine-gun the sound every physics step.
function makeGrabbableProp(mesh, body, { kind = "crate", throwBoost = 1.3 } = {}) {
  worldGroup.add(mesh);
  physics.addBody(mesh, body);
  body.linearDamping = 0.05;
  body.angularDamping = 0.15;

  let lastThud = 0;
  body.addEventListener("collide", (event) => {
    const impactSpeed = Math.abs(event.contact.getImpactVelocityAlongNormal());
    if (impactSpeed < COLLIDE_SOUND_MIN_SPEED) return;
    const now = performance.now();
    if (now - lastThud < COLLIDE_SOUND_COOLDOWN) return;
    lastThud = now;
    playThud(kind, impactSpeed);
  });

  grab.add(mesh, {
    onGrab: () => {
      ensureAudio(); // first grab is a user gesture — safe point to unlock audio
      physics.setSync(mesh, false);
      body.type = CANNON.Body.KINEMATIC;
      body.velocity.setZero();
      body.angularVelocity.setZero();
    },
    onRelease: (obj, releaseVelocity) => {
      body.position.copy(mesh.position);
      body.quaternion.copy(mesh.quaternion);
      body.type = CANNON.Body.DYNAMIC;
      body.velocity.copy(releaseVelocity).scale(throwBoost, body.velocity);
      body.wakeUp();
      physics.setSync(mesh, true);
    }
  });
}

function buildPyramid() {
  const rows = [3, 2, 1];
  let y = CRATE_HALF;
  rows.forEach((count) => {
    for (let i = 0; i < count; i++) {
      const mesh = buildCrate();
      mesh.traverse((node) => { if (node.isMesh) { node.castShadow = true; node.receiveShadow = true; } });
      const x = PLAYGROUND_CENTER.x + (i - (count - 1) / 2) * (CRATE_HALF * 2 + 0.02);
      const home = new THREE.Vector3(x, y, PLAYGROUND_CENTER.z + 1.6);
      mesh.position.copy(home);

      const body = new CANNON.Body({ mass: 3, material: physics.materials.crate });
      body.addShape(new CANNON.Box(new CANNON.Vec3(CRATE_HALF, CRATE_HALF, CRATE_HALF)));
      body.position.copy(home);
      makeGrabbableProp(mesh, body, { kind: "crate", throwBoost: 1.25 });
      props.push({ mesh, body, home, kind: "crate", inRing: false });
    }
    y += CRATE_HALF * 2;
  });

  for (let i = 0; i < 2; i++) {
    const mesh = buildBarrel();
    mesh.traverse((node) => { if (node.isMesh) { node.castShadow = true; node.receiveShadow = true; } });
    const home = new THREE.Vector3(PLAYGROUND_CENTER.x + 1.4 + i * 0.6, BARREL_HEIGHT / 2, PLAYGROUND_CENTER.z + 0.6);
    mesh.position.copy(home);

    const body = new CANNON.Body({ mass: 4, material: physics.materials.barrel });
    body.addShape(new CANNON.Cylinder(BARREL_RADIUS, BARREL_RADIUS * 0.92, BARREL_HEIGHT, 12));
    body.position.copy(home);
    makeGrabbableProp(mesh, body, { kind: "barrel", throwBoost: 1.3 });
    props.push({ mesh, body, home, kind: "barrel", inRing: false });
  }

  const ballMesh = buildBall();
  ballMesh.castShadow = true;
  const ballHome = new THREE.Vector3(PLAYGROUND_CENTER.x - 0.6, BALL_RADIUS, PLAYGROUND_CENTER.z + 2.2);
  ballMesh.position.copy(ballHome);
  const ballBody = new CANNON.Body({ mass: 1, material: physics.materials.ball });
  ballBody.addShape(new CANNON.Sphere(BALL_RADIUS));
  ballBody.position.copy(ballHome);
  makeGrabbableProp(ballMesh, ballBody, { kind: "ball", throwBoost: 1.6 });
  props.push({ mesh: ballMesh, body: ballBody, home: ballHome, kind: "ball", inRing: false });
}

function resetProps() {
  props.forEach((prop) => {
    prop.mesh.position.copy(prop.home);
    prop.mesh.quaternion.set(0, 0, 0, 1);
    prop.body.position.copy(prop.home);
    prop.body.quaternion.set(0, 0, 0, 1);
    prop.body.velocity.setZero();
    prop.body.angularVelocity.setZero();
    prop.body.type = CANNON.Body.DYNAMIC;
    prop.body.wakeUp();
    prop.inRing = false;
  });
}

function buildUI() {
  scoreboard = createTextPanel({ width: 1.7, height: 0.56, fontSize: 30 });
  scoreboard.position.set(PLAYGROUND_CENTER.x, 2.1, PLAYGROUND_CENTER.z + 2.6);
  scoreboard.lookAt(PLAYGROUND_CENTER.x, 1.5, PLAYGROUND_CENTER.z + 4);
  worldGroup.add(scoreboard);

  const resetBtn = createButton3D("↻ Reset", { width: 0.5, height: 0.16, accent: "#f472b6", fontSize: 40 });
  resetBtn.position.set(PLAYGROUND_CENTER.x + 0.9, 1.5, PLAYGROUND_CENTER.z + 2.6);
  resetBtn.lookAt(PLAYGROUND_CENTER.x + 0.9, 1.5, PLAYGROUND_CENTER.z + 4);
  worldGroup.add(resetBtn);
  interaction.add(resetBtn, {
    onSelect: () => resetProps(),
    onHoverStart: resetBtn.userData.onHoverStart,
    onHoverEnd: resetBtn.userData.onHoverEnd
  });
}

// Any prop that comes to rest inside the ring scores once; leaving and
// re-landing lets it score again, so it stays a repeatable target rather
// than a one-shot trigger.
function updateRingScoring() {
  let changed = false;
  for (const prop of props) {
    flatPos.set(prop.mesh.position.x, prop.mesh.position.z);
    const inside = flatPos.distanceTo(ringFlatPos) < RING_RADIUS && prop.mesh.position.y < 0.4;
    const settled = prop.body.velocity.length() < RING_SETTLE_SPEED;

    if (inside && settled && !prop.inRing) {
      prop.inRing = true;
      ringScore += 1;
      changed = true;
      flashRing();
    } else if (!inside && prop.inRing) {
      prop.inRing = false;
    }
  }
  if (changed && ringScore > bestRingScore) {
    bestRingScore = ringScore;
    try { sessionStorage.setItem(BEST_SCORE_KEY, String(bestRingScore)); } catch { /* storage unavailable */ }
  }
}

function refreshScoreboard() {
  if (!scoreboard) return;
  const pyramidProps = props.filter((p) => p.kind !== "ball");
  const knocked = pyramidProps.filter((p) => p.mesh.position.distanceTo(p.home) > 1.0).length;
  scoreboard.userData.setText([
    { text: "Knock the crates off, land things in the ring!", bold: true, size: 28 },
    { text: `Scattered: ${knocked}/${pyramidProps.length}   ·   Ring: ${ringScore} (best ${bestRingScore})`, size: 24, color: "#34d399" },
    { text: "WASD / thumbstick to walk, grip to grab & throw", size: 20, color: "#8fa3c8" }
  ]);
}

function handleKeyDown(event) { keysDown.add(event.code); }
function handleKeyUp(event) { keysDown.delete(event.code); }

function applyKeyboardLocomotion(delta) {
  if (xrState.renderer.xr.isPresenting) return; // VR uses locomotion.js's thumbstick instead
  let x = 0, z = 0;
  if (keysDown.has("KeyW") || keysDown.has("ArrowUp")) z -= 1;
  if (keysDown.has("KeyS") || keysDown.has("ArrowDown")) z += 1;
  if (keysDown.has("KeyA") || keysDown.has("ArrowLeft")) x -= 1;
  if (keysDown.has("KeyD") || keysDown.has("ArrowRight")) x += 1;
  if (x === 0 && z === 0) return;

  xrState.camera.getWorldQuaternion(keyQuaternion);
  keyDirection.set(x, 0, z).applyQuaternion(keyQuaternion);
  keyDirection.y = 0;
  if (keyDirection.lengthSq() === 0) return;
  keyDirection.normalize().multiplyScalar(KEY_SPEED * delta);
  xrState.rig.position.add(keyDirection);
}

export function mount(scene) {
  sceneRef = scene;
  disposed = false;
  ringScore = 0;
  try { bestRingScore = Number(sessionStorage.getItem(BEST_SCORE_KEY)) || 0; } catch { bestRingScore = 0; }

  const floor = scene.getObjectByName("floor");
  const grid = scene.getObjectByName("grid");
  const demoCube = scene.getObjectByName("demoCube");
  if (floor) floor.visible = false;
  if (grid) grid.visible = false;
  if (demoCube) demoCube.visible = false;

  // Shadows are scoped to this route only, so Learn/other pages' render
  // cost is unaffected — flipped back off in unmount().
  xrState.renderer.shadowMap.enabled = true;
  xrState.renderer.shadowMap.type = THREE.PCFSoftShadowMap;

  scene.fog = new THREE.Fog(0xdce8f0, 20, 55);

  worldGroup = new THREE.Group();
  worldGroup.name = "worldRoot";
  scene.add(worldGroup);

  physics = createPhysicsWorld();
  grab = createGrabSystem({ renderer: xrState.renderer, camera: xrState.camera });
  interaction = createInteractionManager({ renderer: xrState.renderer, camera: xrState.camera });
  xrState.grabSystem = grab;

  buildSky();
  buildTerrain();
  buildRamp();
  buildPyramid();
  buildRing();
  buildUI();
  refreshScoreboard();

  setStatus("Loading world…");
  loadCreatures().then(() => { if (!disposed) setStatus(""); });

  document.addEventListener("keydown", handleKeyDown);
  document.addEventListener("keyup", handleKeyUp);

  updateFn = (delta) => {
    interaction.update();
    grab.update(delta);
    physics.step(delta);
    mixers.forEach((m) => m.update(delta));
    applyKeyboardLocomotion(delta);
    updateRingScoring();
    refreshScoreboard();
  };
  xrState.updatables.add(updateFn);

  const btn = enterVRBtn();
  if (btn) {
    btn.disabled = false;
    btn.onclick = async () => {
      btn.disabled = true;
      setStatus("Starting VR session…");
      try {
        await connectVRSession(xrState.renderer, {
          onConnected: () => setStatus("In VR! Walk with the thumbstick, squeeze the grip to grab and throw."),
          onEnded: () => { setStatus("VR session ended."); btn.disabled = false; }
        });
      } catch (err) {
        setStatus(err.message, true);
        btn.disabled = false;
      }
    };
  }
}

export function unmount() {
  disposed = true;

  document.removeEventListener("keydown", handleKeyDown);
  document.removeEventListener("keyup", handleKeyUp);
  keysDown.clear();

  const btn = enterVRBtn();
  if (btn) btn.onclick = null;

  xrState.updatables.delete(updateFn);
  updateFn = null;

  xrState.grabSystem = null;
  grab?.dispose();
  grab = null;
  interaction?.dispose();
  interaction = null;

  mixers = [];
  props = [];
  scoreboard = null;
  ringMarker = null;
  ringScore = 0;

  physics?.dispose();
  physics = null;

  audioCtx?.close().catch(() => {});
  audioCtx = null;

  xrState.renderer.shadowMap.enabled = false;
  sceneRef.fog = null;

  const floor = sceneRef.getObjectByName("floor");
  const grid = sceneRef.getObjectByName("grid");
  const demoCube = sceneRef.getObjectByName("demoCube");
  if (floor) floor.visible = true;
  if (grid) grid.visible = true;
  if (demoCube) demoCube.visible = true;

  sceneRef.remove(worldGroup);
  disposeTree(worldGroup);
  worldGroup = null;
  sceneRef = null;
}
