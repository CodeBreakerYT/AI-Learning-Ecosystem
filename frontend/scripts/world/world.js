import * as THREE from "three";
import * as CANNON from "cannon-es";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { Sky } from "three/addons/objects/Sky.js";
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

const CREATURES = [
  { file: "Flamingo.glb", targetSize: 0.6 },
  { file: "Parrot.glb", targetSize: 0.32 },
  { file: "Stork.glb", targetSize: 0.7 },
  { file: "Horse.glb", targetSize: 1.5 }
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
let crates = []; // { mesh, body, home }
let scoreboard = null;
let keysDown = new Set();
let disposed = true;

const keyQuaternion = new THREE.Quaternion();
const keyDirection = new THREE.Vector3();

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
  sunLight.position.copy(sun).multiplyScalar(50);
  worldGroup.add(sunLight);
}

function buildTerrain() {
  const ground = new THREE.Mesh(
    new THREE.PlaneGeometry(80, 80, 1, 1),
    new THREE.MeshStandardMaterial({ map: buildGrassTexture(), roughness: 1 })
  );
  ground.rotation.x = -Math.PI / 2;
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

function loadCreatures() {
  const loader = new GLTFLoader();
  const spotCount = CREATURES.length;

  CREATURES.forEach(({ file, targetSize }, i) => {
    loader.load(
      `/assets/models/world/${file}`,
      (gltf) => {
        if (disposed) return; // navigated away before the download finished
        const model = gltf.scene;
        fitAndGround(model, targetSize);
        const angle = (i / spotCount) * Math.PI * 2 + Math.random() * 0.4;
        const dist = 8 + Math.random() * 6;
        model.position.x += Math.cos(angle) * dist;
        model.position.z += Math.sin(angle) * dist;
        model.rotation.y = Math.random() * Math.PI * 2;
        worldGroup.add(model);

        if (gltf.animations?.length) {
          const mixer = new THREE.AnimationMixer(model);
          mixer.clipAction(gltf.animations[0]).play();
          mixers.push(mixer);
        }
      },
      undefined,
      (err) => console.warn(`Couldn't load ${file}:`, err.message)
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
  worldGroup.add(ramp);

  const rampBody = new CANNON.Body({ type: CANNON.Body.STATIC });
  rampBody.addShape(new CANNON.Box(new CANNON.Vec3(0.8, 0.04, 0.5)));
  rampBody.position.copy(ramp.position);
  rampBody.quaternion.copy(ramp.quaternion);
  physics.world.addBody(rampBody);
}

// Wires a mesh + cannon body into the grab system: while held, the body goes
// kinematic and physics stops driving the mesh (grabSystem's own followHand
// takes over instead); on release, the body snaps to the mesh's current
// pose, goes dynamic again, and inherits the hand's real release velocity —
// real engine-driven gravity/collision/restitution on every throw.
function makeGrabbableProp(mesh, body) {
  worldGroup.add(mesh);
  physics.addBody(mesh, body);

  grab.add(mesh, {
    onGrab: () => {
      physics.setSync(mesh, false);
      body.type = CANNON.Body.KINEMATIC;
      body.velocity.setZero();
      body.angularVelocity.setZero();
    },
    onRelease: (obj, releaseVelocity) => {
      body.position.copy(mesh.position);
      body.quaternion.copy(mesh.quaternion);
      body.type = CANNON.Body.DYNAMIC;
      body.velocity.copy(releaseVelocity);
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
      const x = PLAYGROUND_CENTER.x + (i - (count - 1) / 2) * (CRATE_HALF * 2 + 0.02);
      const home = new THREE.Vector3(x, y, PLAYGROUND_CENTER.z + 1.6);
      mesh.position.copy(home);

      const body = new CANNON.Body({ mass: 3 });
      body.addShape(new CANNON.Box(new CANNON.Vec3(CRATE_HALF, CRATE_HALF, CRATE_HALF)));
      body.position.copy(home);
      makeGrabbableProp(mesh, body);
      crates.push({ mesh, body, home });
    }
    y += CRATE_HALF * 2;
  });

  for (let i = 0; i < 2; i++) {
    const mesh = buildBarrel();
    const home = new THREE.Vector3(PLAYGROUND_CENTER.x + 1.4 + i * 0.6, BARREL_HEIGHT / 2, PLAYGROUND_CENTER.z + 0.6);
    mesh.position.copy(home);

    const body = new CANNON.Body({ mass: 4 });
    body.addShape(new CANNON.Cylinder(BARREL_RADIUS, BARREL_RADIUS * 0.92, BARREL_HEIGHT, 12));
    body.position.copy(home);
    makeGrabbableProp(mesh, body);
  }
}

function resetPyramid() {
  crates.forEach(({ mesh, body, home }) => {
    mesh.position.copy(home);
    mesh.quaternion.set(0, 0, 0, 1);
    body.position.copy(home);
    body.quaternion.set(0, 0, 0, 1);
    body.velocity.setZero();
    body.angularVelocity.setZero();
    body.type = CANNON.Body.DYNAMIC;
    body.wakeUp();
  });
}

function buildUI() {
  scoreboard = createTextPanel({ width: 1.6, height: 0.5, fontSize: 34 });
  scoreboard.position.set(PLAYGROUND_CENTER.x, 2.1, PLAYGROUND_CENTER.z + 2.6);
  scoreboard.lookAt(PLAYGROUND_CENTER.x, 1.5, PLAYGROUND_CENTER.z + 4);
  worldGroup.add(scoreboard);

  const resetBtn = createButton3D("↻ Reset crates", { width: 0.5, height: 0.16, accent: "#f472b6", fontSize: 40 });
  resetBtn.position.set(PLAYGROUND_CENTER.x + 0.9, 1.5, PLAYGROUND_CENTER.z + 2.6);
  resetBtn.lookAt(PLAYGROUND_CENTER.x + 0.9, 1.5, PLAYGROUND_CENTER.z + 4);
  worldGroup.add(resetBtn);
  interaction.add(resetBtn, {
    onSelect: () => resetPyramid(),
    onHoverStart: resetBtn.userData.onHoverStart,
    onHoverEnd: resetBtn.userData.onHoverEnd
  });
}

function refreshScoreboard() {
  if (!scoreboard) return;
  const knocked = crates.filter(({ mesh, home }) => mesh.position.distanceTo(home) > 1.0).length;
  scoreboard.userData.setText([
    { text: "Knock the crates off the platform!", bold: true, size: 32 },
    { text: `Scattered: ${knocked}/${crates.length}`, size: 26, color: "#34d399" },
    { text: "WASD / thumbstick to walk, grip to grab & throw", size: 22, color: "#8fa3c8" }
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

  const floor = scene.getObjectByName("floor");
  const grid = scene.getObjectByName("grid");
  const demoCube = scene.getObjectByName("demoCube");
  if (floor) floor.visible = false;
  if (grid) grid.visible = false;
  if (demoCube) demoCube.visible = false;

  worldGroup = new THREE.Group();
  worldGroup.name = "worldRoot";
  scene.add(worldGroup);

  physics = createPhysicsWorld();
  grab = createGrabSystem({ renderer: xrState.renderer, camera: xrState.camera });
  interaction = createInteractionManager({ renderer: xrState.renderer, camera: xrState.camera });
  xrState.grabSystem = grab;

  buildSky();
  buildTerrain();
  loadCreatures();
  buildRamp();
  buildPyramid();
  buildUI();
  refreshScoreboard();

  document.addEventListener("keydown", handleKeyDown);
  document.addEventListener("keyup", handleKeyUp);

  updateFn = (delta) => {
    interaction.update();
    grab.update(delta);
    physics.step(delta);
    mixers.forEach((m) => m.update(delta));
    applyKeyboardLocomotion(delta);
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
  setStatus("");
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
  crates = [];
  scoreboard = null;

  physics?.dispose();
  physics = null;

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
