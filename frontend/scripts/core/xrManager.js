import * as THREE from "three";
import { XRControllerModelFactory } from "three/addons/webxr/XRControllerModelFactory.js";
import { xrState } from "./xrState.js";

/**
 * Creates the renderer/camera/scene plus a persistent VR room (sky, floor,
 * lighting, ambient dressing, demo cube) and a movable player rig with
 * tracked controllers. The rig is what locomotion.js translates, so the
 * camera and controllers move together with it.
 */
export function createXRApp(canvas) {
  const scene = new THREE.Scene();

  const rig = new THREE.Group();
  rig.name = "playerRig";
  rig.position.set(0, 0, 3);
  scene.add(rig);

  const camera = new THREE.PerspectiveCamera(70, window.innerWidth / window.innerHeight, 0.05, 100);
  camera.position.set(0, 1.6, 0);
  rig.add(camera);

  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  renderer.setSize(window.innerWidth, window.innerHeight);
  // Filmic tone mapping gives every scene a softer highlight rolloff and
  // richer contrast than the flat linear default — the single biggest
  // "does this look like a game or a tech demo" lever available for free.
  renderer.toneMapping = THREE.ACESFilmicToneMapping;
  renderer.toneMappingExposure = 1.05;
  renderer.xr.enabled = true;
  renderer.xr.setReferenceSpaceType("local-floor");

  renderer.xr.addEventListener("sessionstart", () => document.body.classList.add("xr-presenting"));
  renderer.xr.addEventListener("sessionend", () => document.body.classList.remove("xr-presenting"));

  const demoCube = setupEnvironment(scene);
  setupControllers(renderer, rig);

  window.addEventListener("resize", () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  return { scene, camera, renderer, rig, demoCube };
}

const SKY_TOP = 0x0a0e18;
const SKY_HORIZON = 0x1c2740;
const ACCENT_COLORS = [0x5b8cff, 0x22d3ee, 0xa78bfa, 0x34d399, 0xf472b6, 0xfbbf24];

function setupEnvironment(scene) {
  scene.background = new THREE.Color(SKY_HORIZON);

  const fog = new THREE.FogExp2(SKY_HORIZON, 0.045);
  scene.fog = fog;
  scene.userData.baseFog = fog; // other routes (e.g. World) swap scene.fog temporarily and restore this on unmount

  // Everything below is the shared "empty room" backdrop other routes hide
  // (by toggling this group's visibility) when they bring their own scenery.
  const envGroup = new THREE.Group();
  envGroup.name = "baseEnvironment";
  scene.add(envGroup);

  envGroup.add(buildSkyDome());

  const hemiLight = new THREE.HemisphereLight(0x4d6bb0, 0x11141c, 1.15);
  scene.add(hemiLight);

  const dirLight = new THREE.DirectionalLight(0xcfe0ff, 1.3);
  dirLight.position.set(3, 6, 2);
  scene.add(dirLight);

  const rimLight = new THREE.PointLight(0x5b8cff, 0.7, 14, 2);
  rimLight.position.set(0, 2.6, -3);
  envGroup.add(rimLight);

  envGroup.add(buildFloor());
  envGroup.add(buildGlowPool());

  const pylonRing = buildPylonRing();
  envGroup.add(pylonRing);

  envGroup.add(buildDust());

  let envElapsed = 0;
  xrState.updatables.add((delta) => {
    envElapsed += delta;
    pylonRing.children.forEach((pylon, i) => {
      const cap = pylon.userData.cap;
      cap.material.emissiveIntensity = 0.9 + Math.sin(envElapsed * 1.4 + i) * 0.4;
      cap.rotation.y += delta * 0.6;
    });
  });

  // Placeholder for real lesson content — swap for a GLTFLoader call against
  // assets/models/ once a model with animations is ready.
  const demoCube = new THREE.Mesh(
    new THREE.BoxGeometry(0.6, 0.6, 0.6),
    new THREE.MeshStandardMaterial({ color: 0x4f8cff, emissive: 0x1c3a8a, emissiveIntensity: 0.35, roughness: 0.35 })
  );
  demoCube.position.set(0, 1, -1.5);
  demoCube.name = "demoCube";
  scene.add(demoCube);

  return demoCube;
}

// A large inverted sphere painted with a vertical gradient so the room has a
// sense of sky/atmosphere instead of a flat clear color — cheap (one draw
// call, no lights) and the fog blends its base into the horizon seamlessly.
function buildSkyDome() {
  const geometry = new THREE.SphereGeometry(30, 24, 16);
  const material = new THREE.ShaderMaterial({
    uniforms: {
      topColor: { value: new THREE.Color(SKY_TOP) },
      bottomColor: { value: new THREE.Color(SKY_HORIZON) },
      offset: { value: 6 },
      exponent: { value: 0.75 }
    },
    vertexShader: `
      varying vec3 vWorldPosition;
      void main() {
        vec4 worldPosition = modelMatrix * vec4(position, 1.0);
        vWorldPosition = worldPosition.xyz;
        gl_Position = projectionMatrix * modelViewMatrix * vec4(position, 1.0);
      }
    `,
    fragmentShader: `
      uniform vec3 topColor;
      uniform vec3 bottomColor;
      uniform float offset;
      uniform float exponent;
      varying vec3 vWorldPosition;
      void main() {
        float h = normalize(vWorldPosition + vec3(0.0, offset, 0.0)).y;
        gl_FragColor = vec4(mix(bottomColor, topColor, max(pow(max(h, 0.0), exponent), 0.0)), 1.0);
      }
    `,
    side: THREE.BackSide,
    depthWrite: false
  });
  const dome = new THREE.Mesh(geometry, material);
  dome.name = "skyDome";
  dome.renderOrder = -1;
  return dome;
}

// Tileable canvas grid (fine lines + bolder major lines every 4 cells) —
// reads as a lit sci-fi floor instead of the flat gray plane + a default
// THREE.GridHelper wireframe it replaces.
function buildFloorTexture() {
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = 512;
  const ctx = canvas.getContext("2d");
  ctx.fillStyle = "#141a26";
  ctx.fillRect(0, 0, canvas.width, canvas.height);

  const step = 32;
  ctx.strokeStyle = "rgba(120,150,210,0.22)";
  ctx.lineWidth = 1.5;
  for (let x = 0; x <= canvas.width; x += step) {
    ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, canvas.height); ctx.stroke();
  }
  for (let y = 0; y <= canvas.height; y += step) {
    ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(canvas.width, y); ctx.stroke();
  }

  const bigStep = step * 4;
  ctx.strokeStyle = "rgba(91,140,255,0.4)";
  ctx.lineWidth = 2;
  for (let x = 0; x <= canvas.width; x += bigStep) {
    ctx.beginPath(); ctx.moveTo(x, 0); ctx.lineTo(x, canvas.height); ctx.stroke();
  }
  for (let y = 0; y <= canvas.height; y += bigStep) {
    ctx.beginPath(); ctx.moveTo(0, y); ctx.lineTo(canvas.width, y); ctx.stroke();
  }

  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
  texture.repeat.set(5, 5);
  texture.anisotropy = 4;
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

function buildFloor() {
  const floor = new THREE.Mesh(
    new THREE.PlaneGeometry(20, 20),
    new THREE.MeshStandardMaterial({ map: buildFloorTexture(), roughness: 0.55, metalness: 0.2 })
  );
  floor.rotation.x = -Math.PI / 2;
  floor.name = "floor";
  return floor;
}

// A soft additive "spotlight pool" glowing under the play area — the
// vignette that makes the room feel lit and centered instead of an infinite
// gridded plane with no focal point.
function buildGlowPoolTexture() {
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = 512;
  const ctx = canvas.getContext("2d");
  const gradient = ctx.createRadialGradient(256, 256, 0, 256, 256, 256);
  gradient.addColorStop(0, "rgba(91,140,255,0.35)");
  gradient.addColorStop(0.6, "rgba(91,140,255,0.12)");
  gradient.addColorStop(1, "rgba(91,140,255,0)");
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

function buildGlowPool() {
  const pool = new THREE.Mesh(
    new THREE.CircleGeometry(4.5, 48),
    new THREE.MeshBasicMaterial({
      map: buildGlowPoolTexture(),
      transparent: true,
      depthWrite: false,
      blending: THREE.AdditiveBlending
    })
  );
  pool.rotation.x = -Math.PI / 2;
  pool.position.set(0, 0.006, -1);
  pool.name = "glowPool";
  return pool;
}

// A ring of glowing accent pylons around the perimeter — gives the room a
// sense of place/scale beyond a bare floor, without extra real-time lights.
function buildPylon(color) {
  const pylon = new THREE.Group();

  const post = new THREE.Mesh(
    new THREE.CylinderGeometry(0.05, 0.07, 2.0, 10),
    new THREE.MeshStandardMaterial({ color: 0x1b2334, roughness: 0.6, metalness: 0.3 })
  );
  post.position.y = 1.0;
  pylon.add(post);

  const cap = new THREE.Mesh(
    new THREE.OctahedronGeometry(0.09, 0),
    new THREE.MeshStandardMaterial({ color, emissive: color, emissiveIntensity: 1.1, roughness: 0.3 })
  );
  cap.position.y = 2.05;
  pylon.add(cap);
  pylon.userData.cap = cap;

  return pylon;
}

function buildPylonRing() {
  const ring = new THREE.Group();
  ring.name = "pylonRing";
  const count = 8;
  const radius = 6.5;
  for (let i = 0; i < count; i++) {
    const angle = (i / count) * Math.PI * 2;
    const pylon = buildPylon(ACCENT_COLORS[i % ACCENT_COLORS.length]);
    pylon.position.set(Math.cos(angle) * radius, 0, Math.sin(angle) * radius - 1);
    ring.add(pylon);
  }
  return ring;
}

// Slow-drifting dust motes for atmosphere — a single Points cloud, cheap
// enough to always run even when not visible (route swap just hides the
// group; the tick keeps ticking).
function buildDust() {
  const count = 140;
  const spread = 14;
  const ceiling = 3.2;
  const positions = new Float32Array(count * 3);
  const speeds = new Float32Array(count);
  for (let i = 0; i < count; i++) {
    positions[i * 3] = (Math.random() - 0.5) * spread;
    positions[i * 3 + 1] = Math.random() * ceiling;
    positions[i * 3 + 2] = (Math.random() - 0.5) * spread;
    speeds[i] = 0.05 + Math.random() * 0.08;
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  const material = new THREE.PointsMaterial({
    color: 0x8fb3ff,
    size: 0.018,
    transparent: true,
    opacity: 0.35,
    depthWrite: false,
    blending: THREE.AdditiveBlending
  });
  const dust = new THREE.Points(geometry, material);
  dust.name = "envDust";
  dust.frustumCulled = false;

  const posAttr = geometry.attributes.position;
  xrState.updatables.add((delta) => {
    for (let i = 0; i < count; i++) {
      let y = posAttr.array[i * 3 + 1] + speeds[i] * delta;
      if (y > ceiling) y = 0;
      posAttr.array[i * 3 + 1] = y;
    }
    posAttr.needsUpdate = true;
  });

  return dust;
}

export const RAY_COLOR_IDLE = 0x5b8cff;
export const RAY_COLOR_HOVER = 0x34d399;

function setupControllers(renderer, rig) {
  const modelFactory = new XRControllerModelFactory();
  const rayGeometry = new THREE.BufferGeometry().setFromPoints([
    new THREE.Vector3(0, 0, 0),
    new THREE.Vector3(0, 0, -1)
  ]);

  for (let i = 0; i < 2; i++) {
    const controller = renderer.xr.getController(i);

    const rayMaterial = new THREE.LineBasicMaterial({ color: RAY_COLOR_IDLE, transparent: true, opacity: 0.9 });
    const ray = new THREE.Line(rayGeometry, rayMaterial);
    ray.name = "ray";
    controller.add(ray);

    // Small dot that interaction.js slides along the ray to the hit point
    // (or a fixed default distance when nothing's hit) — without it, aiming
    // at small 3D UI is guesswork since there's no crosshair like on desktop.
    const reticle = new THREE.Mesh(
      new THREE.SphereGeometry(0.012, 12, 10),
      new THREE.MeshBasicMaterial({ color: RAY_COLOR_IDLE, transparent: true, opacity: 0.9 })
    );
    reticle.position.z = -1.5;
    controller.add(reticle);

    controller.userData.rayMaterial = rayMaterial;
    controller.userData.reticle = reticle;
    rig.add(controller);

    const grip = renderer.xr.getControllerGrip(i);
    grip.add(modelFactory.createControllerModel(grip));
    rig.add(grip);
  }
}
