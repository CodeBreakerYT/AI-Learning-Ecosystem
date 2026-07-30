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
 * scene/world space under one `worldGroup`: a big terrain with a village,
 * a camp, a forest, a river and mountains to explore with locomotion, live
 * animated animals and NPCs that actually roam (not just animate in place),
 * and a physics playground where crates and barrels get real cannon-es
 * gravity, collision and restitution when you grab and throw them — the
 * release velocity from grabSystem becomes actual rigid-body velocity
 * instead of hand-rolled projectile math.
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

// Named places in the world, used both to lay out buildings/trees without
// overlapping and to keep creatures/NPCs roaming near where they belong.
const VILLAGE_CENTER = new THREE.Vector3(20, 0, 12);
const CAMP_CENTER = new THREE.Vector3(-20, 0, 12);
const ZONES = [
  { center: PLAYGROUND_CENTER, radius: 4.5 },
  { center: VILLAGE_CENTER, radius: 7 },
  { center: CAMP_CENTER, radius: 6 }
];

const CREATURES = [
  { file: "Flamingo.glb", targetSize: 0.6, extra: 1 },
  { file: "Parrot.glb", targetSize: 0.32, extra: 1 },
  { file: "Stork.glb", targetSize: 0.7, extra: 0 },
  { file: "Horse.glb", targetSize: 1.5, extra: 0 }
];

const NPC_LINES = [
  ["Welcome to the village!", "Feel free to look around."],
  ["Careful with those crates by the playground —", "I hear they're rigged to a ring target."],
  ["Nice weather for a walk, isn't it?", "Try not to get lost near the mountains."],
  ["The camp folks make a good fire.", "Go say hello."]
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
let roamers = []; // { root, home, target, speed, radius, pauseUntil, actions, current, dialogue, dialoguePanel, dialogueIndex }
let scoreboard = null;
let ringMarker = null;
let campFlames = [];
let river = null;
let keysDown = new Set();
let disposed = true;
let ringScore = 0;
let bestRingScore = 0;
let audioCtx = null;
let elapsed = 0;

const keyQuaternion = new THREE.Quaternion();
const keyDirection = new THREE.Vector3();
const flatPos = new THREE.Vector2();
const ringFlatPos = new THREE.Vector2(RING_CENTER.x, RING_CENTER.z);
const roamDelta = new THREE.Vector3();

function setStatus(message, isError = false) {
  const el = statusEl();
  if (!el) return;
  el.textContent = message;
  el.classList.toggle("is-error", isError);
}

function isFreeSpot(x, z, extraRadius = 0) {
  return ZONES.every((zone) => Math.hypot(x - zone.center.x, z - zone.center.z) > zone.radius + extraRadius);
}

// Picks a random point in an annulus around the origin, retrying a few times
// to avoid landing inside the village/camp/playground footprints.
function randomFreeSpot(minR, maxR, extraRadius = 0) {
  for (let i = 0; i < 12; i++) {
    const angle = Math.random() * Math.PI * 2;
    const r = minR + Math.random() * (maxR - minR);
    const x = Math.cos(angle) * r;
    const z = Math.sin(angle) * r;
    if (isFreeSpot(x, z, extraRadius)) return new THREE.Vector3(x, 0, z);
  }
  const angle = Math.random() * Math.PI * 2;
  return new THREE.Vector3(Math.cos(angle) * maxR, 0, Math.sin(angle) * maxR);
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

function buildWaterTexture() {
  const canvas = document.createElement("canvas");
  canvas.width = 64;
  canvas.height = 256;
  const ctx = canvas.getContext("2d");
  ctx.fillStyle = "#2f7fb8";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  ctx.strokeStyle = "rgba(255,255,255,0.35)";
  ctx.lineWidth = 3;
  for (let i = 0; i < 10; i++) {
    const y = (i / 10) * canvas.height;
    ctx.beginPath();
    ctx.moveTo(0, y);
    ctx.quadraticCurveTo(canvas.width / 2, y + 10, canvas.width, y);
    ctx.stroke();
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = THREE.RepeatWrapping;
  texture.wrapT = THREE.RepeatWrapping;
  texture.repeat.set(1, 6);
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

  buildMountains();
  physics.addGroundPlane(0);
}

// A mountain treeline at the terrain's edge — layered, varied-height cone
// clusters instead of uniform hills, still purely decorative (locomotion is
// flat with no vertical collision, so nothing raised may sit inside the
// walkable radius).
function buildMountains() {
  const rockMaterial = new THREE.MeshStandardMaterial({ color: 0x5b6470, roughness: 1 });
  const treeMaterial = new THREE.MeshStandardMaterial({ color: 0x2f5c38, roughness: 1 });

  for (let i = 0; i < 14; i++) {
    const angle = (i / 14) * Math.PI * 2;
    const dist = 32 + Math.random() * 6;
    const base = new THREE.Vector3(Math.cos(angle) * dist, 0, Math.sin(angle) * dist);

    const cluster = new THREE.Group();
    cluster.position.copy(base);
    cluster.rotation.y = Math.random() * Math.PI * 2;

    const peaks = 2 + Math.floor(Math.random() * 2);
    for (let p = 0; p < peaks; p++) {
      const height = 6 + Math.random() * 6;
      const radius = 3 + Math.random() * 2.5;
      const peak = new THREE.Mesh(new THREE.ConeGeometry(radius, height, 7), rockMaterial);
      peak.position.set((p - (peaks - 1) / 2) * 3.5, height / 2, (Math.random() - 0.5) * 2);
      cluster.add(peak);
    }
    worldGroup.add(cluster);
  }

  // A tree-lined foothill band just inside the mountains, so the transition
  // from grass to peaks doesn't feel abrupt.
  for (let i = 0; i < 16; i++) {
    const spot = randomFreeSpot(27, 31);
    const foothill = new THREE.Mesh(new THREE.ConeGeometry(2.2 + Math.random(), 3 + Math.random() * 1.5, 7), treeMaterial);
    foothill.position.set(spot.x, 1.5, spot.z);
    worldGroup.add(foothill);
  }
}

function buildTree(x, z) {
  const tree = new THREE.Group();
  const trunkHeight = 0.9 + Math.random() * 0.5;
  const trunk = new THREE.Mesh(
    new THREE.CylinderGeometry(0.06, 0.09, trunkHeight, 8),
    new THREE.MeshStandardMaterial({ color: 0x5b4128, roughness: 0.9 })
  );
  trunk.position.y = trunkHeight / 2;
  trunk.castShadow = true;
  tree.add(trunk);

  const foliageMat = new THREE.MeshStandardMaterial({
    color: new THREE.Color(0x2f6b3a).offsetHSL(0, 0, (Math.random() - 0.5) * 0.08),
    roughness: 0.9
  });
  const tiers = 2 + Math.floor(Math.random() * 2);
  let y = trunkHeight;
  for (let i = 0; i < tiers; i++) {
    const size = (1 - i * 0.22) * (0.55 + Math.random() * 0.2);
    const foliage = new THREE.Mesh(new THREE.ConeGeometry(size, size * 1.4, 8), foliageMat);
    foliage.position.y = y + size * 0.6;
    foliage.castShadow = true;
    tree.add(foliage);
    y += size * 0.75;
  }

  tree.position.set(x, 0, z);
  tree.rotation.y = Math.random() * Math.PI * 2;
  return tree;
}

function buildForest() {
  for (let i = 0; i < 30; i++) {
    const spot = randomFreeSpot(9, 26, 1.2);
    worldGroup.add(buildTree(spot.x, spot.z));
  }
}

function buildHouse(x, z, rotationY) {
  const house = new THREE.Group();
  house.position.set(x, 0, z);
  house.rotation.y = rotationY;

  const width = 2.2 + Math.random() * 0.6;
  const depth = 1.8 + Math.random() * 0.5;
  const wallHeight = 1.5;
  const wallColor = [0xd8c9a3, 0xc9b691, 0xd1c4a8][Math.floor(Math.random() * 3)];

  const walls = new THREE.Mesh(
    new THREE.BoxGeometry(width, wallHeight, depth),
    new THREE.MeshStandardMaterial({ color: wallColor, roughness: 0.9 })
  );
  walls.position.y = wallHeight / 2;
  walls.castShadow = true;
  walls.receiveShadow = true;
  house.add(walls);

  const roof = new THREE.Mesh(
    new THREE.ConeGeometry(Math.max(width, depth) * 0.78, 1.1, 4),
    new THREE.MeshStandardMaterial({ color: 0x8a3f34, roughness: 0.8 })
  );
  roof.position.y = wallHeight + 0.55;
  roof.rotation.y = Math.PI / 4;
  roof.castShadow = true;
  house.add(roof);

  const door = new THREE.Mesh(
    new THREE.PlaneGeometry(0.45, 0.9),
    new THREE.MeshStandardMaterial({ color: 0x4a2f1f, roughness: 0.7 })
  );
  door.position.set(0, 0.45, depth / 2 + 0.01);
  house.add(door);

  const windowMat = new THREE.MeshStandardMaterial({
    color: 0xffe9a8,
    emissive: 0xffcf6b,
    emissiveIntensity: 0.6
  });
  [-1, 1].forEach((side) => {
    const win = new THREE.Mesh(new THREE.PlaneGeometry(0.32, 0.32), windowMat);
    win.position.set((side * width) / 3, 0.95, depth / 2 + 0.01);
    house.add(win);
  });

  return house;
}

function buildVillage() {
  const layout = [
    { dx: -2.4, dz: -1.5, ry: 0.4 },
    { dx: 2.2, dz: -1.8, ry: -0.5 },
    { dx: -1.6, dz: 2.2, ry: 2.6 },
    { dx: 2.6, dz: 1.8, ry: -2.3 }
  ];
  layout.forEach(({ dx, dz, ry }) => {
    worldGroup.add(buildHouse(VILLAGE_CENTER.x + dx, VILLAGE_CENTER.z + dz, ry));
  });
}

function buildTent(x, z, rotationY, color) {
  const tent = new THREE.Group();
  tent.position.set(x, 0, z);
  tent.rotation.y = rotationY;

  const body = new THREE.Mesh(
    new THREE.ConeGeometry(0.85, 1.15, 4),
    new THREE.MeshStandardMaterial({ color, roughness: 0.85 })
  );
  body.rotation.y = Math.PI / 4;
  body.position.y = 0.575;
  body.castShadow = true;
  body.receiveShadow = true;
  tent.add(body);

  const flap = new THREE.Mesh(
    new THREE.PlaneGeometry(0.5, 0.75),
    new THREE.MeshStandardMaterial({ color: 0x1c1f26, side: THREE.DoubleSide })
  );
  flap.position.set(0, 0.4, 0.7);
  tent.add(flap);

  return tent;
}

function buildCampfire() {
  const fire = new THREE.Group();
  const logMat = new THREE.MeshStandardMaterial({ color: 0x4a3320, roughness: 0.9 });
  for (let i = 0; i < 5; i++) {
    const angle = (i / 5) * Math.PI * 2;
    const log = new THREE.Mesh(new THREE.CylinderGeometry(0.05, 0.06, 0.55, 8), logMat);
    log.position.set(Math.cos(angle) * 0.12, 0.1, Math.sin(angle) * 0.12);
    log.rotation.z = Math.PI / 2;
    log.rotation.y = angle;
    fire.add(log);
  }

  const flame = new THREE.Mesh(
    new THREE.ConeGeometry(0.14, 0.4, 8),
    new THREE.MeshStandardMaterial({ color: 0xffa23e, emissive: 0xff7a1a, emissiveIntensity: 1.2, transparent: true, opacity: 0.9 })
  );
  flame.position.y = 0.3;
  fire.add(flame);
  campFlames.push(flame);

  const glow = new THREE.PointLight(0xff9a4d, 1.4, 6, 2);
  glow.position.y = 0.4;
  fire.add(glow);

  return fire;
}

function buildCamp() {
  const tentColors = [0xb5493f, 0x3f6b8a, 0x4f7a4a];
  const layout = [
    { dx: -1.8, dz: -1.0, ry: 0.6 },
    { dx: 1.6, dz: -1.4, ry: -0.8 },
    { dx: -1.2, dz: 1.8, ry: 2.4 }
  ];
  layout.forEach(({ dx, dz, ry }, i) => {
    worldGroup.add(buildTent(CAMP_CENTER.x + dx, CAMP_CENTER.z + dz, ry, tentColors[i % tentColors.length]));
  });
  const fire = buildCampfire();
  fire.position.set(CAMP_CENTER.x, 0, CAMP_CENTER.z);
  worldGroup.add(fire);
}

// A winding ribbon of connected quads following a curved path — visual
// water only (walkable over, like the hills/mountains, since locomotion has
// no terrain collision anywhere in the app).
function buildRiver() {
  const points = [
    new THREE.Vector3(-38, 0.02, -2),
    new THREE.Vector3(-20, 0.02, 6),
    new THREE.Vector3(-4, 0.02, 2),
    new THREE.Vector3(10, 0.02, 10),
    new THREE.Vector3(24, 0.02, 4),
    new THREE.Vector3(38, 0.02, 14)
  ];
  const curve = new THREE.CatmullRomCurve3(points);
  const samples = curve.getPoints(48);
  const width = 2.4;

  const positions = [];
  const uvs = [];
  const up = new THREE.Vector3(0, 1, 0);
  for (let i = 0; i < samples.length; i++) {
    const p = samples[i];
    const next = samples[Math.min(i + 1, samples.length - 1)];
    const dir = next.clone().sub(p).normalize();
    const side = new THREE.Vector3().crossVectors(up, dir).normalize().multiplyScalar(width / 2);
    positions.push(p.x - side.x, p.y, p.z - side.z, p.x + side.x, p.y, p.z + side.z);
    const v = i / (samples.length - 1);
    uvs.push(0, v, 1, v);
  }

  const indices = [];
  for (let i = 0; i < samples.length - 1; i++) {
    const a = i * 2, b = i * 2 + 1, c = i * 2 + 2, d = i * 2 + 3;
    indices.push(a, b, c, b, d, c);
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute("uv", new THREE.Float32BufferAttribute(uvs, 2));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();

  const material = new THREE.MeshStandardMaterial({
    map: buildWaterTexture(),
    transparent: true,
    opacity: 0.85,
    roughness: 0.25,
    metalness: 0.1
  });
  river = new THREE.Mesh(geometry, material);
  river.receiveShadow = true;
  worldGroup.add(river);
}

function fitAndGround(model, targetSize) {
  const box = new THREE.Box3().setFromObject(model);
  const size = box.getSize(new THREE.Vector3());
  const maxDim = Math.max(size.x, size.y, size.z) || 1;
  model.scale.setScalar(targetSize / maxDim);

  const groundedBox = new THREE.Box3().setFromObject(model);
  model.position.y -= groundedBox.min.y;
}

// Registers a loaded model as something that wanders a little around a home
// point instead of just animating in place. `actions` is either
// { move: AnimationAction } for a single-clip model (the birds — the clip
// loops for the whole lifetime, translation gives the sense of movement) or
// { idle, walk } for a model with distinct clips (the NPCs), switched based
// on whether it's currently moving toward a target or paused.
function registerRoamer(model, { home, radius, speed, actions, dialogue = null }) {
  roamers.push({
    root: model,
    home: home.clone(),
    target: home.clone(),
    speed,
    radius,
    pauseUntil: 0,
    actions,
    current: null,
    dialogue,
    dialoguePanel: null
  });
}

function pickRoamerAction(roamer, name) {
  if (roamer.current === name) return;
  const next = roamer.actions[name];
  const prev = roamer.current ? roamer.actions[roamer.current] : null;
  prev?.stop();
  next?.reset().play();
  roamer.current = name;
}

function updateRoamers(delta) {
  const now = performance.now();
  for (const roamer of roamers) {
    roamDelta.copy(roamer.target).sub(roamer.root.position);
    roamDelta.y = 0;
    const dist = roamDelta.length();

    if (dist < 0.15) {
      if (now >= roamer.pauseUntil) {
        const angle = Math.random() * Math.PI * 2;
        const r = Math.random() * roamer.radius;
        roamer.target.set(roamer.home.x + Math.cos(angle) * r, roamer.root.position.y, roamer.home.z + Math.sin(angle) * r);
        roamer.pauseUntil = now + 1500 + Math.random() * 2500;
      } else {
        pickRoamerAction(roamer, "idle");
      }
      continue;
    }

    pickRoamerAction(roamer, roamer.actions.idle ? "walk" : "move");
    roamDelta.normalize().multiplyScalar(Math.min(roamer.speed * delta, dist));
    roamer.root.position.add(roamDelta);
    const facing = Math.atan2(roamDelta.x, roamDelta.z);
    roamer.root.rotation.y = facing;
  }
}

function showDialogue(roamer) {
  if (!roamer.dialogue) return;
  if (!roamer.dialoguePanel) {
    const panel = createTextPanel({ width: 1.3, height: 0.4, fontSize: 26, border: "rgba(251, 191, 36, 0.85)" });
    panel.position.set(0, 2.1, 0);
    roamer.root.add(panel);
    roamer.dialoguePanel = panel;
  }
  roamer.dialoguePanel.userData.setText(
    roamer.dialogue.map((text, i) => ({ text, bold: i === 0, size: i === 0 ? 28 : 22, color: i === 0 ? "#fbbf24" : "#e8ecf6" }))
  );
  roamer.dialoguePanel.visible = true;

  clearTimeout(roamer.dialogueTimer);
  roamer.dialogueTimer = setTimeout(() => {
    if (roamer.dialoguePanel) roamer.dialoguePanel.visible = false;
  }, 3200);
}

function loadCreatures() {
  const loader = new GLTFLoader();
  const spotCount = CREATURES.reduce((sum, c) => sum + 1 + (c.extra ?? 0), 0);
  let spotIndex = 0;

  function place(model) {
    const spot = randomFreeSpot(9, 24);
    // x/z only — fitAndGround() already set position.y so the model's feet
    // sit exactly on the ground; overwriting it with spot.y (always 0) would
    // undo that and sink/float the model depending on its rest origin.
    model.position.x = spot.x;
    model.position.z = spot.z;
    spot.y = model.position.y;
    model.rotation.y = Math.random() * Math.PI * 2;
    model.traverse((node) => { if (node.isMesh) node.castShadow = true; });
    worldGroup.add(model);
    return spot;
  }

  const loadPromises = CREATURES.map(({ file, targetSize, extra = 0 }) =>
    new Promise((resolve) => {
      loader.load(
        `${import.meta.env.BASE_URL}assets/models/world/${file}`,
        (gltf) => {
          if (disposed) { resolve(); return; } // navigated away before the download finished
          const primary = gltf.scene;
          fitAndGround(primary, targetSize);
          const home = place(primary);
          spotIndex++;
          let mixer = null;
          if (gltf.animations?.length) {
            mixer = new THREE.AnimationMixer(primary);
            const action = mixer.clipAction(gltf.animations[0]);
            action.play();
            mixers.push(mixer);
          }
          registerRoamer(primary, { home, radius: 4, speed: 0.5, actions: { move: mixer?.clipAction(gltf.animations[0]) } });

          // A couple of extra clones per bird for a livelier world without
          // downloading more assets — SkeletonUtils.clone (not plain
          // Object3D.clone) is required so the animated skeleton's bone
          // bindings copy correctly onto the duplicate.
          for (let i = 0; i < extra; i++) {
            const copy = cloneSkinned(primary);
            const copyHome = place(copy);
            spotIndex++;
            let copyMixer = null;
            if (gltf.animations?.length) {
              copyMixer = new THREE.AnimationMixer(copy);
              copyMixer.clipAction(gltf.animations[0]).play();
              mixers.push(copyMixer);
            }
            registerRoamer(copy, { home: copyHome, radius: 4, speed: 0.5, actions: { move: copyMixer?.clipAction(gltf.animations[0]) } });
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

const NPC_SPOTS = [
  { center: VILLAGE_CENTER, offset: new THREE.Vector3(1.2, 0, -0.6) },
  { center: VILLAGE_CENTER, offset: new THREE.Vector3(-1.4, 0, 1.0) },
  { center: CAMP_CENTER, offset: new THREE.Vector3(0.8, 0, 1.4) },
  { center: CAMP_CENTER, offset: new THREE.Vector3(-1.0, 0, -0.8) }
];

function loadNpcs() {
  const loader = new GLTFLoader();
  return new Promise((resolve) => {
    loader.load(
      `${import.meta.env.BASE_URL}assets/models/world/Soldier.glb`,
      (gltf) => {
        if (disposed) { resolve(); return; }
        const clipFor = (name) => gltf.animations.find((c) => c.name === name);

        NPC_SPOTS.forEach((spot, i) => {
          const model = i === 0 ? gltf.scene : cloneSkinned(gltf.scene);
          fitAndGround(model, 1.7);
          const home = spot.center.clone().add(spot.offset);
          // x/z only — preserve the y fitAndGround() just computed so the
          // NPC's feet sit on the ground instead of snapping to y=0.
          model.position.x = home.x;
          model.position.z = home.z;
          home.y = model.position.y;
          model.rotation.y = Math.random() * Math.PI * 2;
          model.traverse((node) => { if (node.isMesh) { node.castShadow = true; node.receiveShadow = true; } });
          worldGroup.add(model);

          const mixer = new THREE.AnimationMixer(model);
          const idleClip = clipFor("Idle") ?? gltf.animations[0];
          const walkClip = clipFor("Walk") ?? gltf.animations[0];
          const idle = idleClip ? mixer.clipAction(idleClip) : null;
          const walk = walkClip ? mixer.clipAction(walkClip) : null;
          idle?.play();
          mixers.push(mixer);

          registerRoamer(model, { home, radius: 2.5, speed: 0.35, actions: { idle, walk }, dialogue: NPC_LINES[i % NPC_LINES.length] });
          const roamer = roamers[roamers.length - 1];
          roamer.current = "idle";

          interaction.add(model, {
            onSelect: () => showDialogue(roamer),
            onHoverStart: () => { model.scale.multiplyScalar(1.05); },
            onHoverEnd: () => { model.scale.multiplyScalar(1 / 1.05); }
          });
        });
        resolve();
      },
      undefined,
      (err) => { console.warn("Couldn't load Soldier.glb:", err.message); resolve(); }
    );
  });
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
  elapsed = 0;
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
  buildForest();
  buildRiver();
  buildVillage();
  buildCamp();
  buildRamp();
  buildPyramid();
  buildRing();
  buildUI();
  refreshScoreboard();

  setStatus("Loading world…");
  Promise.allSettled([loadCreatures(), loadNpcs()]).then(() => { if (!disposed) setStatus(""); });

  document.addEventListener("keydown", handleKeyDown);
  document.addEventListener("keyup", handleKeyUp);

  updateFn = (delta) => {
    elapsed += delta;
    interaction.update();
    grab.update(delta);
    physics.step(delta);
    mixers.forEach((m) => m.update(delta));
    updateRoamers(delta);
    applyKeyboardLocomotion(delta);
    updateRingScoring();
    refreshScoreboard();

    campFlames.forEach((flame, i) => {
      flame.scale.y = 1 + Math.sin(elapsed * 9 + i) * 0.12;
      flame.material.emissiveIntensity = 1.0 + Math.sin(elapsed * 13 + i * 2) * 0.35;
    });
    if (river) river.material.map?.offset.set(0, -elapsed * 0.05);
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
          onConnected: () => setStatus("In VR! Walk with the thumbstick, squeeze the grip to grab and throw, pull the trigger to talk to people."),
          onWaiting: () => setStatus("Still connecting — put on your headset and look for a prompt there to allow VR."),
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

  roamers.forEach((r) => clearTimeout(r.dialogueTimer));
  roamers = [];
  mixers = [];
  props = [];
  campFlames = [];
  river = null;
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
