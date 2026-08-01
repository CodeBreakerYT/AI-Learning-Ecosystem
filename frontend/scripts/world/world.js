import * as THREE from "three";
import * as CANNON from "cannon-es";
import { GLTFLoader } from "three/addons/loaders/GLTFLoader.js";
import { FBXLoader } from "three/addons/loaders/FBXLoader.js";
import { Sky } from "three/addons/objects/Sky.js";
import { clone as cloneSkinned } from "three/addons/utils/SkeletonUtils.js";
import { xrState } from "../core/xrState.js";
import { connectVRSession } from "../core/xrSession.js";
import { createInteractionManager } from "../core/interaction.js";
import { createGrabSystem } from "../core/grabSystem.js";
import { createTextPanel, createLabel, createButton3D, disposeTree } from "../core/textPanel.js";
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
// The playground used to sit 6m from spawn — visible immediately, with no
// NPC, building, or story reason to be there, which read as a minigame
// dropped in an empty field rather than a real place. Moved to the south
// edge of the village (a "village playground") so the player only reaches
// it by following the guide there, same as the market and kitchen.
const PLAYGROUND_CENTER = new THREE.Vector3(20, 0, -4);
const CRATE_HALF = 0.22;
const BARREL_RADIUS = 0.22;
const BARREL_HEIGHT = 0.5;
const BALL_RADIUS = 0.13;
const RING_CENTER = new THREE.Vector3(PLAYGROUND_CENTER.x + 3.6, 0, PLAYGROUND_CENTER.z + 1.6);
const RING_RADIUS = 0.35;
const RING_SETTLE_SPEED = 0.35; // below this speed, a prop inside the ring counts as "landed"
const RING_GOAL = 3; // lands needed during the "golf" stage before the story advances
const BEST_SCORE_KEY = "ale.world.bestRingScore";
const COLLIDE_SOUND_MIN_SPEED = 1.2; // m/s of impact — below this, skip the thud (resting jitter)
const COLLIDE_SOUND_COOLDOWN = 150; // ms, per body — avoids machine-gun buzz on a resting contact

// Named places in the world, used both to lay out buildings/trees without
// overlapping and to keep creatures/NPCs roaming near where they belong.
const VILLAGE_CENTER = new THREE.Vector3(20, 0, 12);
const CAMP_CENTER = new THREE.Vector3(-20, 0, 12);
const MARKET_CENTER = new THREE.Vector3(8, 0, 5);
const KITCHEN_CENTER = new THREE.Vector3(28, 0, 4);
// A small hamlet tucked into the gap between the playground and camp paths
// (SW of spawn) so the player sees houses right away instead of bare grass.
const SPAWN_HAMLET_CENTER = new THREE.Vector3(-5, 0, -3);
const ZONES = [
  { center: PLAYGROUND_CENTER, radius: 4.5 },
  { center: VILLAGE_CENTER, radius: 9 },
  { center: CAMP_CENTER, radius: 6 },
  { center: MARKET_CENTER, radius: 4 },
  { center: KITCHEN_CENTER, radius: 4 },
  { center: SPAWN_HAMLET_CENTER, radius: 5 }
];

// Optional side content, no bearing on the main quest — gems tucked around
// the world (by the river, near camp, out past the mountains) so there's a
// reason to actually explore instead of only ever walking the direct paths
// between quest markers. NPC flavor lines above hint at a few of these.
const COLLECTIBLE_SPOTS = [
  new THREE.Vector3(-4, 0.5, 3.5),   // river bend
  new THREE.Vector3(-9, 0.5, -6),    // behind the spawn hamlet
  new THREE.Vector3(-13, 0.5, 7),    // deep forest, spawn-to-camp
  new THREE.Vector3(-23, 0.5, 8),    // camp outskirts
  new THREE.Vector3(11, 0.5, 15),    // toward the northern mountains
  new THREE.Vector3(27, 0.5, 19),    // behind the village houses
  new THREE.Vector3(32, 0.5, 9),     // kitchen outskirts
  new THREE.Vector3(24, 0.5, -9)     // near the playground, off to the side
];

// Flamingo/Parrot/Stork all ship with a genuine flying-wingbeat animation
// (that's what they're famous for in three.js's own demos) — flying: true
// makes loadCreatures() actually send them soaring at altitude instead of
// walking them around on the ground like Horse/Fox.
const CREATURES = [
  { file: "Flamingo.glb", targetSize: 0.6, extra: 5, flying: true, altitude: [3, 6], radius: 11, speed: 1.3 },
  { file: "Parrot.glb", targetSize: 0.32, extra: 5, flying: true, altitude: [4, 8], radius: 13, speed: 1.6 },
  { file: "Stork.glb", targetSize: 0.7, extra: 4, flying: true, altitude: [4, 7], radius: 12, speed: 1.4 },
  { file: "Horse.glb", targetSize: 1.5, extra: 3 },
  // Fox.glb's rest pose faces the opposite way from the other models here —
  // without facingOffset it visibly walks backward relative to where it's headed.
  { file: "Fox.glb", targetSize: 0.55, extra: 6, facingOffset: Math.PI },
  { file: "Cow.glb", targetSize: 1.6, extra: 3 },
  { file: "Dog.glb", targetSize: 0.5, extra: 4 },
  { file: "Giraffe.glb", targetSize: 2.2, extra: 2 },
  // Low, slow, close-range flutter instead of a bird's wide soaring circle —
  // butterflies stay near flower height, not up with the storks.
  { file: "ButterflyModel.glb", targetSize: 0.12, extra: 8, flying: true, altitude: [0.3, 0.9], radius: 4, speed: 0.5 }
];

// Cat/Chicken/Ladybug ship with no baked animation clips (unlike the rest of
// CREATURES) — registering them as roamers would just slide them around the
// ground with no walk cycle to play, so they're placed once as static scene
// dressing instead (still get a small idle bob — see updateStaticBobbers()).
// Wagon/Crate are hand-authored props from the Medieval Village MegaKit
// asset pack, added for detail the procedural box-houses can't provide.
const STATIC_PROPS = [
  { file: "Cat.glb", targetSize: 0.32, spots: [VILLAGE_CENTER.clone().add(new THREE.Vector3(1.6, 0, 1.2))] },
  {
    file: "Chicken.glb", targetSize: 0.28, spots: [
      VILLAGE_CENTER.clone().add(new THREE.Vector3(-2.0, 0, -0.3)),
      VILLAGE_CENTER.clone().add(new THREE.Vector3(-1.7, 0, 0.3)),
      VILLAGE_CENTER.clone().add(new THREE.Vector3(-2.2, 0, 0.1))
    ]
  },
  {
    file: "Ladybug.glb", targetSize: 0.04, spots: [
      CAMP_CENTER.clone().add(new THREE.Vector3(0.5, 0, 0.4)),
      VILLAGE_CENTER.clone().add(new THREE.Vector3(2.4, 0, 1.0))
    ]
  },
  { file: "village-kit/Prop_Wagon.gltf", targetSize: 1.4, rotationY: 0.6, spots: [MARKET_CENTER.clone().add(new THREE.Vector3(-1.6, 0, -0.6))] },
  {
    file: "village-kit/Prop_Crate.gltf", targetSize: 0.5, spots: [
      MARKET_CENTER.clone().add(new THREE.Vector3(1.3, 0, -0.8)),
      MARKET_CENTER.clone().add(new THREE.Vector3(1.5, 0, -0.5)),
      VILLAGE_CENTER.clone().add(new THREE.Vector3(3.0, 0, 0.8))
    ]
  }
];

// Each NPC gets several line-sets instead of one static pair — showDialogue()
// cycles through them on repeat interaction (see roamer.dialogueIndex), so
// talking to the same person twice doesn't just repeat itself verbatim.
const NPC_LINES = [
  [
    ["Welcome to the village!", "Feel free to look around."],
    ["Still exploring?", "There's a playground on the south side worth seeing."],
    ["Come back and see me anytime.", "I'm not going anywhere."]
  ],
  [
    ["Careful with those crates by the playground —", "I hear they're rigged to a ring target."],
    ["I heard someone's been throwing things around here.", "...was that you?"],
    ["The reset button's there for a reason.", "Don't be shy about using it."]
  ],
  [
    ["Nice weather for a walk, isn't it?", "Try not to get lost near the mountains."],
    ["I keep meaning to hike up there.", "Never quite get around to it."],
    ["Shiny things catch the light out past the treeline sometimes.", "Might be worth a look."]
  ],
  [
    ["The camp folks make a good fire.", "Go say hello."],
    ["Camp's just west of here.", "They tell better stories than we do."],
    ["Ask them about the river.", "They know it better than anyone."]
  ],
  [
    ["Did you see the storks circling overhead?", "They nest up in the foothills."],
    ["The flamingos showed up a season early this year.", "Nobody's complaining."],
    ["Birds go quiet right before it rains.", "Keep an eye on them."]
  ],
  [
    ["A fox has been sneaking around the crates.", "Harmless — just curious."],
    ["That fox has a favorite napping spot.", "Somewhere sunny, probably."],
    ["Feed the fox and it'll follow you around all day.", "Don't feed the fox."]
  ],
  [
    ["The river's calm this time of day.", "Good spot to sit and think."],
    ["Something glints under the water sometimes.", "Trick of the light, probably."],
    ["Follow the river far enough and it just keeps going.", "Never made it to the end myself."]
  ],
  [
    ["Watch your step near the mountains.", "The path gets steep fast."],
    ["The view from halfway up is worth it.", "Just don't go higher than that."],
    ["Someone said they lost a gem out that way.", "I wouldn't know anything about that."]
  ]
];

// A short linear story: the guide relocates to each location in turn and
// the quest log always shows the current step. Persisted to sessionStorage
// (same pattern as bestRingScore) so leaving and re-entering World resumes
// where the player left off instead of restarting the day.
const QUEST_STAGE_KEY = "ale.world.questStage";
const QUEST_STAGES = {
  intro: {
    title: "Go shopping",
    objective: "Find the market and price out two items for breakfast.",
    guideLocation: () => new THREE.Vector3(2, 0, 2),
    guideLines: ["Morning! Let's get breakfast.", "Head to the market — the vendor needs two things priced out."],
    guideFlavor: ["I'm Shinobu, by the way.", "I'll be around if you need pointing in the right direction."]
  },
  market: {
    title: "Go shopping",
    objective: "Work out each total and drop the right coin in the bowl, twice.",
    guideLocation: () => MARKET_CENTER.clone().add(new THREE.Vector3(-1.2, 0, 0.6)),
    guideLines: ["Check the sign, work out the total,", "and drop the right coin in the bowl."],
    guideFlavor: ["Take your time with the math.", "The vendor's patient — mostly."]
  },
  golf: {
    title: "Prove your aim",
    objective: `Land ${RING_GOAL} throws in the glowing ring at the playground.`,
    guideLocation: () => PLAYGROUND_CENTER.clone().add(new THREE.Vector3(-1.2, 0, 1.2)),
    guideLines: ["Breakfast sorted!", `Now land ${RING_GOAL} throws in that ring — crates, barrels, the ball, anything goes.`],
    guideFlavor: ["I used to be pretty good at this.", "Used to be."]
  },
  kitchen: {
    title: "Cook something",
    objective: "Mix the right ingredients at the kitchen counter.",
    guideLocation: () => KITCHEN_CENTER.clone().add(new THREE.Vector3(-1.2, 0, 0.6)),
    guideLines: ["Great putt!", "One more thing — mix up today's recipe in the kitchen."],
    guideFlavor: ["Don't improvise on the recipe.", "I'm speaking from experience."]
  },
  complete: {
    title: "All done!",
    objective: "You've earned a well-deserved break.",
    guideLocation: () => KITCHEN_CENTER.clone().add(new THREE.Vector3(1.2, 0, 0.6)),
    guideLines: ["You did it — potatoes bought,", "aim proven, and dinner's cooking. Nice work today!"],
    guideFlavor: ["Feel free to keep exploring.", "There might still be a gem or two out there."]
  }
};

const KITCHEN_INGREDIENTS = [
  { name: "Flour", color: 0xf3e5c9 },
  { name: "Water", color: 0x7fd1e0 },
  { name: "Salt", color: 0xffffff },
  { name: "Oil", color: 0xd7c14b }
];
const KITCHEN_RECIPE = { Flour: 2, Water: 1, Salt: 1 };

const statusEl = () => document.getElementById("world-status");
const enterVRBtn = () => document.getElementById("world-enter-vr");
const guidePanelEl = () => document.getElementById("world-guide-panel");
const guideCloseBtn = () => document.getElementById("world-guide-close");
const guideReopenBtn = () => document.getElementById("world-guide-reopen");

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
let butterflies = null; // { group, flutters } — see buildButterflies()
let staticBobbers = []; // { model, baseY, phase } — animation-less props (Cat/Chicken/Ladybug, Wagon/Crate) get a small idle bob instead of standing perfectly frozen
let keysDown = new Set();
let disposed = true;
let ringScore = 0;
let bestRingScore = 0;
let audioCtx = null;
let elapsed = 0;

// Story/quest state
let questStage = "intro";
let questLogPanel = null;
let guideRoamer = null;
// Playground (golf) and kitchen locations/props stay hidden and
// non-interactive until the story actually reaches them, so the player
// encounters one objective at a time by following the guide instead of
// finding every minigame simultaneously visible from the start.
let playgroundRamp = null;
let playgroundRing = null;
let playgroundResetBtn = null;
let kitchenGroup = null;
let marketCoins = []; // { mesh, body, home, value }
let marketCorrectValue = 0;
const marketBowlPos = new THREE.Vector3();
let marketQuestionPanel = null;
let marketFeedbackPanel = null;
let kitchenIngredients = []; // { mesh, body, home, ing }
let kitchenZoneAtoms = []; // { name, mesh }
let kitchenLocked = false;
let kitchenFeedbackPanel = null;
const kitchenZonePos = new THREE.Vector3();
let toastPanel = null;
let toastTimer = null;
// Gems: optional side collectibles, no bearing on the main quest — pure
// reward-and-exploration content. { id, mesh, phase } per uncollected gem.
let collectibles = [];
let gemsFound = 0;

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

// Idyllic Fantasy Nature models ship as Unity assets: materials carry no
// embedded texture reference at all (Unity links textures via its own
// .mat/GUID database, never written into the FBX), so FBXLoader imports
// every mesh with a blank/default material — hence textures are wired up
// by hand here, matched by material name ("Trunk"/"Leaves") for the tree
// or a flat per-model texture for single-material props. Each model also
// ships 3 LOD variants (_LOD0/_LOD1/_LOD2) all present simultaneously in
// the same file — without hiding LOD1/2, all three render stacked on top
// of each other as an oversized, doubled-up blob.
function keepOnlyLOD0(group) {
  const meshes = [];
  group.traverse((node) => { if (node.isMesh) meshes.push(node); });
  if (!meshes.some((m) => /_LOD\d/i.test(m.name))) return; // no LODs to filter
  // Actually remove (not just hide) LOD1/LOD2 — every instance of every
  // scattered tree/rock/bush carries its own copy of these, and merely
  // setting .visible = false still leaves them in the graph for matrix
  // updates, frustum culling, and shadow passes every frame. With 46 real
  // trees this alone was roughly halving the framerate.
  meshes.forEach((m) => {
    if (/_LOD0$/i.test(m.name)) return;
    m.geometry?.dispose();
    m.parent?.remove(m);
  });
}

function loadNatureAssets() {
  const loader = new FBXLoader();
  const texLoader = new THREE.TextureLoader();
  const tex = (name) => {
    const t = texLoader.load(`${import.meta.env.BASE_URL}assets/models/world/idyllic-nature/${name}`);
    t.colorSpace = THREE.SRGBColorSpace;
    return t;
  };

  const barkMap = tex("trees/Bark_Albedo.png");
  const leavesMap = tex("trees/BroadleafTree_Leaves.png");
  const bushMap = tex("bushes/Bush_Branch.png");
  const flowerMap = tex("flowers/FlowerMeadow.png");

  function applyTreeMaterials(group) {
    group.traverse((node) => {
      if (!node.isMesh || !node.visible) return;
      const mats = Array.isArray(node.material) ? node.material : [node.material];
      node.material = mats.map((mat) =>
        /trunk/i.test(mat.name)
          ? new THREE.MeshStandardMaterial({ map: barkMap, roughness: 0.9 })
          // The leaf card texture itself is nearly white — a SpeedTree-style
          // pack authored to be recolored via the material's own color
          // (multiplied with the texture), not a full-color albedo on its own.
          : new THREE.MeshStandardMaterial({ map: leavesMap, color: 0x4f8a3d, roughness: 0.8, alphaTest: 0.15, side: THREE.DoubleSide })
      );
    });
  }

  function applyFlatMaterial(group, map, { alphaCutout = false, tint = null } = {}) {
    group.traverse((node) => {
      if (!node.isMesh || !node.visible) return;
      node.material = new THREE.MeshStandardMaterial({
        map, color: tint ?? 0xffffff, roughness: 0.9,
        transparent: alphaCutout, alphaTest: alphaCutout ? 0.5 : 0,
        side: alphaCutout ? THREE.DoubleSide : THREE.FrontSide
      });
    });
  }

  function load(file) {
    return new Promise((resolve) => {
      loader.load(
        `${import.meta.env.BASE_URL}assets/models/world/idyllic-nature/${file}`,
        (group) => resolve(group),
        undefined,
        (err) => { console.warn(`Couldn't load ${file}:`, err.message); resolve(null); }
      );
    });
  }

  return Promise.all([
    load("trees/BroadleafTree_01.fbx"),
    load("trees/BroadleafTree_02.fbx"),
    load("bushes/Bush_01_01.fbx"),
    load("flowers/FlowerMeadow_Blue.fbx")
  ]).then(([tree1, tree2, bush, flower]) => {
    if (disposed) return null;
    // Shadow-casting is expensive (each caster adds to every shadow-map
    // pass) and this scatter can run into the dozens of instances — only
    // the trees are prominent enough to be worth it; ground-scatter bush/
    // flowers still receive shadows so they don't look like they're
    // floating, they just don't cast their own. The real Cliff rocks were
    // dropped entirely — too large/prominent next to everything else, and
    // the existing small procedural rocks (buildRocks(), still in the
    // scene) already cover that role at the right low-poly scale.
    [tree1, tree2].forEach((g) => { if (g) { keepOnlyLOD0(g); applyTreeMaterials(g); g.traverse((n) => { if (n.isMesh) { n.castShadow = true; n.receiveShadow = true; } }); } });
    if (bush) { keepOnlyLOD0(bush); applyFlatMaterial(bush, bushMap, { alphaCutout: true, tint: 0x4f8a3d }); }
    if (flower) { keepOnlyLOD0(flower); applyFlatMaterial(flower, flowerMap, { alphaCutout: true }); }
    return { trees: [tree1, tree2].filter(Boolean), bush, flower };
  });
}

// Scatters real Idyllic Fantasy Nature trees/rocks/bushes/flowers across
// the walkable area as supplemental decoration alongside the existing
// procedural conifer forest (buildForest()) — adds variety without
// touching the already-working bulk tree coverage. Same fit-once/clone-
// the-fitted-primary pattern used for creatures and static props: the
// first placed instance gets the expensive fitAndGround() bounding-box
// pass, every other instance is a cheap clone reusing that same scale.
function scatterNatureModel(model, count, targetSize, { minDist = 1.2 } = {}) {
  if (!model) return;
  fitAndGround(model, targetSize);
  const groundedY = model.position.y;
  const placed = [];
  for (let i = 0; i < count; i++) {
    let spot = null;
    for (let attempt = 0; attempt < 5; attempt++) {
      const candidate = randomFreeSpot(6, 26, 1.0);
      const tooClose = placed.some((p) => Math.hypot(candidate.x - p.x, candidate.z - p.z) < minDist);
      if (!tooClose) { spot = candidate; break; }
      spot = candidate;
    }
    placed.push(spot);
    const instance = i === 0 ? model : model.clone(true);
    instance.position.set(spot.x, groundedY, spot.z);
    instance.rotation.y = Math.random() * Math.PI * 2;
    worldGroup.add(instance);
  }
}

function loadNatureScatter() {
  return loadNatureAssets().then((assets) => {
    if (!assets || disposed) return;
    // Real trees replaced the old procedural cone forest (mixing the two
    // styles read as "unnatural" — see prior commit). They're much heavier
    // to render than a flat-shaded cone was, though (real geometry +
    // textures + shadows) — 46 of them roughly halved the framerate, so
    // density comes down from the old forest's count to keep things smooth.
    scatterNatureModel(assets.trees[0], 12, 3.2, { minDist: 2.2 });
    scatterNatureModel(assets.trees[1], 12, 3.0, { minDist: 2.2 });
    scatterNatureModel(assets.bush, 6, 0.8, { minDist: 0.8 });
    scatterNatureModel(assets.flower, 5, 0.6, { minDist: 0.8 });
  });
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
  // Houses are up to 2.8 x 2.3 (see buildHouse), so worst-case they need
  // ~3.6m of center-to-center clearance to never overlap regardless of
  // rotation. Evenly ringing them around the village center guarantees that
  // spacing instead of the old hand-picked dx/dz array, which had a pair
  // only ~2m apart and rendered as two houses with fused walls.
  const count = 8;
  const ringRadius = 6.2;
  const jitter = 0.5;
  for (let i = 0; i < count; i++) {
    const angle = (i / count) * Math.PI * 2;
    const r = ringRadius + (Math.random() - 0.5) * jitter;
    const dx = Math.sin(angle) * r;
    const dz = Math.cos(angle) * r;
    // Face roughly toward the village center (doors toward the market)
    // with a little variance so the ring doesn't look robotically uniform.
    const ry = angle + Math.PI + (Math.random() - 0.5) * 0.5;
    worldGroup.add(buildHouse(VILLAGE_CENTER.x + dx, VILLAGE_CENTER.z + dz, ry));
  }
}

// A few houses right near the player's spawn point so the world doesn't
// open on empty grass — same spacing math as buildVillage() (3 houses on a
// 3m ring clears the ~3.6m worst-case footprint with room to spare).
function buildSpawnHamlet() {
  const count = 3;
  const ringRadius = 3;
  const jitter = 0.5;
  for (let i = 0; i < count; i++) {
    const angle = (i / count) * Math.PI * 2;
    const r = ringRadius + (Math.random() - 0.5) * jitter;
    const dx = Math.sin(angle) * r;
    const dz = Math.cos(angle) * r;
    const ry = angle + Math.PI + (Math.random() - 0.5) * 0.5;
    worldGroup.add(buildHouse(SPAWN_HAMLET_CENTER.x + dx, SPAWN_HAMLET_CENTER.z + dz, ry));
  }
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

// For a plain Mesh, raw vertex position × the node's own matrixWorld is the
// true world position, so Box3.setFromObject() works directly. For a
// SkinnedMesh that's not the case — the actual posed shape comes from the
// skeleton's bone transforms, not the mesh node's own (often near-trivial)
// matrixWorld — so measuring it the plain way yields a near-degenerate box.
// Bone world positions give a reliable stand-in for the character's extent.
function measureWorldBox(model) {
  model.updateWorldMatrix(true, true);
  const box = new THREE.Box3();
  const v = new THREE.Vector3();
  let hasBones = false;
  model.traverse((node) => {
    if (node.isBone) {
      hasBones = true;
      box.expandByPoint(node.getWorldPosition(v));
    }
  });
  if (!hasBones) box.setFromObject(model);
  return box;
}

function fitAndGround(model, targetSize) {
  const size = measureWorldBox(model).getSize(new THREE.Vector3());
  const maxDim = Math.max(size.x, size.y, size.z) || 1;
  model.scale.setScalar(targetSize / maxDim);

  model.position.y -= measureWorldBox(model).min.y;
}

// Registers a loaded model as something that wanders a little around a home
// point instead of just animating in place. `actions` is either
// { move: AnimationAction } for a single-clip model (the birds — the clip
// loops for the whole lifetime, translation gives the sense of movement) or
// { idle, walk } for a model with distinct clips (the NPCs), switched based
// on whether it's currently moving toward a target or paused.
function registerRoamer(model, { home, radius, speed, actions, dialogue = null, flying = false, altitudeMin = 0, altitudeMax = 0, facingOffset = 0 }) {
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
    dialogueIndex: 0,
    dialoguePanel: null,
    flying,
    altitudeMin,
    altitudeMax,
    facingOffset
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
    if (!roamer.flying) roamDelta.y = 0;
    const dist = roamDelta.length();

    if (dist < 0.15) {
      if (roamer.flying) {
        // Birds never land or idle mid-air — immediately pick the next
        // waypoint in the circling pattern so the flight looks continuous.
        const angle = Math.random() * Math.PI * 2;
        const r = roamer.radius * (0.5 + Math.random() * 0.5);
        const alt = roamer.altitudeMin + Math.random() * (roamer.altitudeMax - roamer.altitudeMin);
        roamer.target.set(roamer.home.x + Math.cos(angle) * r, alt, roamer.home.z + Math.sin(angle) * r);
      } else if (now >= roamer.pauseUntil) {
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
    // Not every glTF sample model was authored facing the same rest
    // direction — Fox.glb's rest pose faces the opposite way from the other
    // models here, so without this it visibly walks backward. facingOffset
    // lets a per-species correction be applied on top of the shared formula.
    roamer.root.rotation.y = facing + (roamer.facingOffset ?? 0);
  }
}

// A brief rig-attached toast for one-off reward moments (gem picked up,
// ring landed, quest stage cleared) — unlike the old always-on quest HUD
// this stays hidden until something happens and fades itself out a couple
// seconds later, so it reads as feedback rather than clutter.
function showToast(text, color = "#38bdf8") {
  if (!toastPanel) {
    toastPanel = createTextPanel({ width: 1.2, height: 0.3, fontSize: 24, border: "rgba(56, 189, 248, 0.85)" });
    toastPanel.position.set(0, 1.55, -1.4);
    toastPanel.visible = false;
    xrState.rig.add(toastPanel);
  }
  toastPanel.userData.setText([{ text, bold: true, size: 24, color }]);
  toastPanel.visible = true;

  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => { if (toastPanel) toastPanel.visible = false; }, 2600);
}

// roamer.dialogue is a list of line-sets (each a [title, ...detail] array);
// every interaction advances to the next set and wraps around, so repeat
// conversations reveal new flavor instead of replaying the same two lines.
function showDialogue(roamer) {
  if (!roamer.dialogue || roamer.dialogue.length === 0) return;
  const lines = roamer.dialogue[roamer.dialogueIndex % roamer.dialogue.length];
  roamer.dialogueIndex += 1;

  if (!roamer.dialoguePanel) {
    const panel = createTextPanel({ width: 1.3, height: 0.4, fontSize: 26, border: "rgba(251, 191, 36, 0.85)" });
    // Counter-scale — same fix as addQuestMarker() above. This is the
    // actual "unable to interact" bug: clicking an NPC WAS finding and
    // triggering the hit correctly (confirmed via direct instrumentation
    // of interaction.js's pick()), but the panel is a child of roamer.root,
    // and the new FBX characters have a ~0.01 scale (vs. ~1.0 for the old
    // glTF ones) to normalize their hundreds-of-raw-units source size down
    // to 1.7m. A literal "position.y = 2.1" child collapses to ~2cm above
    // the character's origin and the panel itself shrinks to ~1.3cm wide —
    // it WAS opening, just too small and misplaced to ever notice.
    const inv = 1 / (roamer.root.scale.x || 1);
    panel.scale.setScalar(inv);
    panel.position.set(0, 2.1 * inv, 0);
    roamer.root.add(panel);
    roamer.dialoguePanel = panel;
  }
  roamer.dialoguePanel.userData.setText(
    lines.map((text, i) => ({ text, bold: i === 0, size: i === 0 ? 28 : 22, color: i === 0 ? "#fbbf24" : "#e8ecf6" }))
  );
  roamer.dialoguePanel.visible = true;

  clearTimeout(roamer.dialogueTimer);
  roamer.dialogueTimer = setTimeout(() => {
    if (roamer.dialoguePanel) roamer.dialoguePanel.visible = false;
  }, 3200);
}

function setGuideDialogue() {
  if (!guideRoamer) return;
  const stage = QUEST_STAGES[questStage];
  // The objective always comes first (so a first click always tells you
  // what to do), then a bit of personality on the second click — reset to
  // index 0 on every stage change so a fresh objective is never buried
  // behind leftover flavor-line cycling from the previous stage.
  guideRoamer.dialogue = stage.guideFlavor ? [stage.guideLines, stage.guideFlavor] : [stage.guideLines];
  guideRoamer.dialogueIndex = 0;
}

// Teleports the guide to the new stage's location — a walk-over would be
// nicer, but a straight relocation is far more reliable to get right, and
// the player has already moved on to the next place by the time this fires.
function relocateGuide() {
  if (!guideRoamer) return;
  const pos = QUEST_STAGES[questStage].guideLocation();
  guideRoamer.root.position.x = pos.x;
  guideRoamer.root.position.z = pos.z;
  guideRoamer.home.set(pos.x, guideRoamer.root.position.y, pos.z);
  guideRoamer.target.copy(guideRoamer.home);
}

function refreshQuestLog() {
  if (!questLogPanel) return;
  const stage = QUEST_STAGES[questStage];
  questLogPanel.userData.setText([
    { text: stage.title, bold: true, size: 30, color: "#fbbf24" },
    { text: stage.objective, size: 22, color: "#e8ecf6" }
  ]);
}

function advanceQuest(newStage) {
  if (questStage === newStage || !QUEST_STAGES[newStage]) return;
  questStage = newStage;
  try { sessionStorage.setItem(QUEST_STAGE_KEY, questStage); } catch { /* storage unavailable */ }
  setGuideDialogue();
  relocateGuide();
  refreshQuestLog();
  updateStageVisibility();
}

// The order the story unlocks locations in: potatoes at the market first,
// then the playground, then the kitchen. Objects for a not-yet-reached
// location are hidden AND left ungrabbable (grabSystem/interaction both
// skip anything with .visible === false) so the player can only ever act
// on the current objective instead of stumbling onto a later one early.
const STAGE_ORDER = { intro: 0, market: 0, golf: 1, kitchen: 2, complete: 3 };
function updateStageVisibility() {
  const order = STAGE_ORDER[questStage] ?? 0;
  const playgroundUnlocked = order >= 1;
  const kitchenUnlocked = order >= 2;

  if (playgroundRamp) playgroundRamp.visible = playgroundUnlocked;
  if (playgroundRing) playgroundRing.visible = playgroundUnlocked;
  if (playgroundResetBtn) playgroundResetBtn.visible = playgroundUnlocked;
  if (scoreboard) scoreboard.visible = playgroundUnlocked;
  props.forEach((p) => {
    if (p.kind === "crate" || p.kind === "barrel" || p.kind === "ball") p.mesh.visible = playgroundUnlocked;
  });

  if (kitchenGroup) kitchenGroup.visible = kitchenUnlocked;
  kitchenIngredients.forEach((k) => { k.mesh.visible = kitchenUnlocked; });
}

function loadCreatures() {
  const loader = new GLTFLoader();

  // Ground creatures: fitAndGround() already set position.y so the model's
  // feet sit exactly on the ground; only x/z come from the random spot.
  // Flying creatures skip grounding entirely and get a random altitude
  // within their species' band instead, since they're never meant to touch
  // down — landing them via fitAndGround would look like a crash.
  function place(model, flying, altitudeRange) {
    const spot = randomFreeSpot(6, 24);
    model.position.x = spot.x;
    model.position.z = spot.z;
    if (flying) {
      const [min, max] = altitudeRange;
      model.position.y = min + Math.random() * (max - min);
    }
    spot.y = model.position.y;
    model.rotation.y = Math.random() * Math.PI * 2;
    model.traverse((node) => { if (node.isMesh) node.castShadow = true; });
    worldGroup.add(model);
    return spot;
  }

  // Picks the walk/run cycle over an idle/survey pose for multi-clip models
  // like Fox — animations[0] isn't reliably "the one that should loop while
  // moving" once a model has more than a single clip.
  function pickMoveClip(animations) {
    if (!animations?.length) return null;
    const walkLike = animations.find((c) => /walk|run|fly/i.test(c.name));
    if (walkLike) return walkLike;
    const first = animations[0];
    // A clip literally named idle/iddle is a standing pose, not a locomotion
    // cycle — using it as a "move" animation makes the model visibly slide
    // across the ground in place (this is what happened with Giraffe.glb,
    // whose only clip is "iddle"). Better to treat it as having no move clip.
    return /idle|iddle/i.test(first.name) ? null : first;
  }

  function pickIdleClip(animations) {
    if (!animations?.length) return null;
    return animations.find((c) => /idle|iddle/i.test(c.name)) ?? animations[0];
  }

  const loadPromises = CREATURES.map(({ file, targetSize, extra = 0, flying = false, altitude = [0, 0], radius = 4, speed = 0.5, facingOffset = 0 }) =>
    new Promise((resolve) => {
      loader.load(
        `${import.meta.env.BASE_URL}assets/models/world/${file}`,
        (gltf) => {
          if (disposed) { resolve(); return; } // navigated away before the download finished
          const primary = gltf.scene;
          fitAndGround(primary, targetSize);
          const home = place(primary, flying, altitude);
          const moveClip = pickMoveClip(gltf.animations);
          const idleClip = moveClip ? null : pickIdleClip(gltf.animations);
          if (moveClip) {
            const mixer = new THREE.AnimationMixer(primary);
            mixer.clipAction(moveClip).play();
            mixers.push(mixer);
            registerRoamer(primary, {
              home, radius, speed, flying, facingOffset,
              altitudeMin: altitude[0], altitudeMax: altitude[1],
              actions: { move: mixer.clipAction(moveClip) }
            });
          } else if (idleClip) {
            // No genuine walk/run/fly cycle exists (e.g. Giraffe.glb only
            // ships an "iddle" pose) — play the idle clip in place instead
            // of registering as a roamer, which would visibly slide a
            // standing pose across the ground.
            const mixer = new THREE.AnimationMixer(primary);
            mixer.clipAction(idleClip).play();
            mixers.push(mixer);
          }

          // A few extra clones per species for a livelier world without
          // downloading more assets — SkeletonUtils.clone (not plain
          // Object3D.clone) is required so the animated skeleton's bone
          // bindings copy correctly onto the duplicate.
          for (let i = 0; i < extra; i++) {
            const copy = cloneSkinned(primary);
            const copyHome = place(copy, flying, altitude);
            if (moveClip) {
              const copyMixer = new THREE.AnimationMixer(copy);
              copyMixer.clipAction(moveClip).play();
              mixers.push(copyMixer);
              registerRoamer(copy, {
                home: copyHome, radius, speed, flying, facingOffset,
                altitudeMin: altitude[0], altitudeMax: altitude[1],
                actions: { move: copyMixer.clipAction(moveClip) }
              });
            } else if (idleClip) {
              const copyMixer = new THREE.AnimationMixer(copy);
              copyMixer.clipAction(idleClip).play();
              mixers.push(copyMixer);
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

// Loads each STATIC_PROPS entry once and places a clone at every requested
// spot — same fit-once-then-clone-the-fitted-primary pattern as
// loadCreatures(), since the grounding Y offset it bakes in only depends on
// the model's shape and scale, which every clone shares.
function loadStaticProps() {
  const loader = new GLTFLoader();
  const bobbers = [];

  const loadPromises = STATIC_PROPS.map(({ file, targetSize, spots, rotationY }) =>
    new Promise((resolve) => {
      loader.load(
        `${import.meta.env.BASE_URL}assets/models/world/${file}`,
        (gltf) => {
          if (disposed) { resolve(); return; }
          const primary = gltf.scene;
          fitAndGround(primary, targetSize);

          spots.forEach((spot, i) => {
            const model = i === 0 ? primary : cloneSkinned(primary);
            model.position.x = spot.x;
            model.position.z = spot.z;
            model.rotation.y = rotationY ?? Math.random() * Math.PI * 2;
            model.traverse((node) => { if (node.isMesh) { node.castShadow = true; node.receiveShadow = true; } });
            worldGroup.add(model);
            bobbers.push({ model, baseY: model.position.y, phase: Math.random() * Math.PI * 2 });
          });
          resolve();
        },
        undefined,
        (err) => { console.warn(`Couldn't load ${file}:`, err.message); resolve(); }
      );
    })
  );

  return Promise.allSettled(loadPromises).then(() => {
    if (!disposed) staticBobbers = bobbers;
  });
}

// A gentle sine bob for props with no baked animation clip — just enough to
// read as "alive" rather than a frozen prop planted in the grass.
function updateStaticBobbers(elapsed) {
  for (const b of staticBobbers) {
    b.model.position.y = b.baseY + Math.sin(elapsed * 1.6 + b.phase) * 0.015;
  }
}

const NPC_SPOTS = [
  { center: VILLAGE_CENTER, offset: new THREE.Vector3(1.2, 0, -0.6) },
  { center: VILLAGE_CENTER, offset: new THREE.Vector3(-1.4, 0, 1.0) },
  { center: VILLAGE_CENTER, offset: new THREE.Vector3(2.1, 0, 0.4) },
  { center: VILLAGE_CENTER, offset: new THREE.Vector3(-0.6, 0, -1.9) },
  { center: CAMP_CENTER, offset: new THREE.Vector3(0.8, 0, 1.4) },
  { center: CAMP_CENTER, offset: new THREE.Vector3(-1.0, 0, -0.8) },
  { center: CAMP_CENTER, offset: new THREE.Vector3(1.9, 0, -0.3) },
  { center: CAMP_CENTER, offset: new THREE.Vector3(-2.0, 0, 1.1) }
];

function addQuestMarker(model, symbol, color) {
  const marker = createLabel(symbol, { width: 0.3, height: 0.3, fontSize: 200, color });
  // Counter-scale: the marker is a child of `model`, whose own scale varies
  // wildly by source asset — near 1.0 for glTF characters authored in
  // meters, ~0.01 for the FBX characters (authored at hundreds of raw
  // units, e.g. centimeters). Without this, "float 2.3 units above" and
  // the marker's own 0.3-unit size both collapse to an imperceptible
  // fraction of a centimeter once multiplied by a 0.01 parent scale.
  const inv = 1 / (model.scale.x || 1);
  marker.scale.setScalar(inv);
  marker.position.y = 2.3 * inv;
  model.add(marker);
  return marker;
}

// A wandering MutantGolem (Crimson-Valor) roaming the mountain foothills —
// unlike Shinobu/Bob/Neko, this one's mesh and its separately-exported
// walk/idle clips were verified to share the same Mixamo skeleton (37/38
// bone names matched exactly), so the animation actually plays instead of
// freezing in a T-pose. Also the only character candidate whose FBX
// references its textures by filename (Mutant_diffuse/normal.png) rather
// than through Unity's separate .mat/GUID system, so FBXLoader resolves
// them automatically — no manual material wiring needed, unlike the
// Idyllic Fantasy Nature props.
function loadMutantGolem() {
  const dir = "crimson-valor/mutant-golem";
  const loader = new FBXLoader();
  const load = (file) => new Promise((resolve) => {
    loader.load(
      `${import.meta.env.BASE_URL}assets/models/world/${dir}/${file}`,
      (group) => resolve(group),
      undefined,
      (err) => { console.warn(`Couldn't load ${dir}/${file}:`, err.message); resolve(null); }
    );
  });

  return Promise.all([load("MutantGolem.fbx"), load("Mutant_Walking.fbx"), load("Mutant_Idle.fbx")])
    .then(([mesh, walkGroup, idleGroup]) => {
      if (!mesh || disposed) return;
      const walkClip = walkGroup?.animations?.[0] ?? null;
      const idleClip = idleGroup?.animations?.[0] ?? null;

      // Fit the shared template ONCE, then clone it per instance — fitting
      // each instance individually was the bug that shipped: instance 0
      // used `mesh` directly (mutating its scale in place), so instance 1's
      // cloneSkinned(mesh) cloned an already-shrunk mesh and fitAndGround
      // ran a second time on top of that, compounding into a giant.
      fitAndGround(mesh, 2.2);
      const groundedY = mesh.position.y;

      // One instance wandering the mountain foothills, well outside the
      // village/camp/market exclusion zones — a "something's out there"
      // atmosphere touch an NPC line already hints at. Just one (not two)
      // and no shadow-casting — it's a heavy, high-poly mesh, and its
      // AnimationMixer/skinning update runs every frame regardless of
      // whether it's ever on screen.
      const spots = [new THREE.Vector3(26, 0, -22)];
      spots.forEach((home, i) => {
        const model = i === 0 ? mesh : cloneSkinned(mesh);
        model.position.set(home.x, groundedY, home.z);
        model.rotation.y = Math.random() * Math.PI * 2;
        model.traverse((node) => { if (node.isMesh) node.receiveShadow = true; });
        worldGroup.add(model);

        const mixer = new THREE.AnimationMixer(model);
        const idle = idleClip ? mixer.clipAction(idleClip) : null;
        const walk = walkClip ? mixer.clipAction(walkClip) : null;
        idle?.play();
        mixers.push(mixer);

        registerRoamer(model, {
          home, radius: 6, speed: 0.6,
          actions: { idle, walk },
          dialogue: [
            ["A low, rumbling growl.", "It doesn't seem interested in you."],
            ["The ground shakes slightly with each step.", "Best to keep some distance."]
          ]
        });
        const roamer = roamers[roamers.length - 1];
        roamer.current = "idle";

        interaction.add(model, {
          onSelect: () => showDialogue(roamer),
          onHoverStart: () => { model.scale.multiplyScalar(1.05); },
          onHoverEnd: () => { model.scale.multiplyScalar(1 / 1.05); }
        });
      });
    });
}

// Loads a named FBX character where the mesh+skeleton lives in one FBX and
// idle/walk animations are separately exported clip-only FBX files sharing
// the same bone names. Returns a kit shaped like { scene, idleClip,
// walkClip } — AnimationMixer binds tracks by bone name, not by object
// identity, so a clip loaded from a different file animates the mesh fine
// as long as the skeleton naming matches (verified per-character below).
function loadFBXCharacterKit(dir, meshFile, animFiles = {}) {
  const loader = new FBXLoader();
  const load = (file) => new Promise((resolve) => {
    loader.load(
      `${import.meta.env.BASE_URL}assets/models/world/${dir}/${file}`,
      (group) => resolve(group),
      undefined,
      (err) => { console.warn(`Couldn't load ${dir}/${file}:`, err.message); resolve(null); }
    );
  });
  return load(meshFile).then((mesh) => {
    if (!mesh) return null;
    return Promise.all(Object.entries(animFiles).map(([key, file]) =>
      file ? load(file).then((g) => [key, g?.animations?.[0] ?? null]) : Promise.resolve([key, null])
    )).then((entries) => {
      const clips = Object.fromEntries(entries);
      return { scene: mesh, idleClip: clips.idle ?? null, walkClip: clips.walk ?? null };
    });
  });
}

// Every character in the village — Shinobu as the story guide, Bob filling
// the ambient crowd, an Indian food-cart vendor at the market — all real
// named models from proj_sample instead of generic glTF sample mannequins.
// Neko was tried too but its mesh uses a different skeleton than its own
// exported animations (0/38 bone names matched, vs. Shinobu's 33/34 and
// Bob's 43/44) and stood frozen in a T-pose — left out. Bob only ships an
// idle clip (no walk), so he's placed stationary (radius: 0) rather than
// roaming, same as the vendor.
function loadCast() {
  const shinobuPromise = loadFBXCharacterKit("crimson-valor/guide", "Shinobu.fbx", {
    idle: "Shinobu@Idle.fbx",
    walk: "Shinobu@Walking.fbx"
  });
  const bobPromise = loadFBXCharacterKit("crimson-valor/bob", "Bob.fbx", { idle: "Bob_Idle.fbx" });
  const vendorPromise = new Promise((resolve) => {
    new FBXLoader().load(
      `${import.meta.env.BASE_URL}assets/models/world/lumora/food-vendor/FoodVentor.fbx`,
      (group) => resolve({ scene: group, idleClip: null, walkClip: null }),
      undefined,
      (err) => { console.warn("Couldn't load FoodVentor.fbx:", err.message); resolve(null); }
    );
  });

  return Promise.all([shinobuPromise, bobPromise, vendorPromise])
    .then(([shinobuKit, bobKit, vendorKit]) => {
      if (disposed) return;

      function spawnCharacter(spawnPos, { radius = 2.5, speed = 0.35, dialogue = null, marker = null, kit, targetSize = 1.7 } = {}) {
        if (!kit) return null;
        const model = cloneSkinned(kit.scene);
        fitAndGround(model, targetSize);
        // x/z only — preserve the y fitAndGround() just computed so the
        // character's feet sit on the ground instead of snapping to y=0.
        model.position.x = spawnPos.x;
        model.position.z = spawnPos.z;
        const home = new THREE.Vector3(spawnPos.x, model.position.y, spawnPos.z);
        model.rotation.y = Math.random() * Math.PI * 2;
        model.traverse((node) => { if (node.isMesh) { node.castShadow = true; node.receiveShadow = true; } });
        worldGroup.add(model);

        const mixer = new THREE.AnimationMixer(model);
        const idle = kit.idleClip ? mixer.clipAction(kit.idleClip) : null;
        const walk = kit.walkClip ? mixer.clipAction(kit.walkClip) : null;
        idle?.play();
        mixers.push(mixer);

        registerRoamer(model, { home, radius, speed, actions: { idle, walk }, dialogue });
        const roamer = roamers[roamers.length - 1];
        roamer.current = "idle";
        if (marker) roamer.marker = addQuestMarker(model, marker.symbol, marker.color);

        interaction.add(model, {
          onSelect: () => showDialogue(roamer),
          onHoverStart: () => { model.scale.multiplyScalar(1.05); },
          onHoverEnd: () => { model.scale.multiplyScalar(1 / 1.05); }
        });

        return roamer;
      }

      NPC_SPOTS.forEach((spot, i) => {
        spawnCharacter(spot.center.clone().add(spot.offset), {
          radius: 0, speed: 0, kit: bobKit, dialogue: NPC_LINES[i % NPC_LINES.length]
        });
      });

      spawnCharacter(MARKET_CENTER.clone().add(new THREE.Vector3(0, 0, 0.9)), {
        radius: 0,
        speed: 0,
        targetSize: 1.3,
        kit: vendorKit,
        dialogue: [
          ["Fresh potatoes!", "Check the sign for today's price."],
          ["Take your time doing the math.", "I'm not going anywhere."],
          ["Best potatoes this side of the river.", "Trust me."]
        ]
      });

      guideRoamer = spawnCharacter(QUEST_STAGES[questStage].guideLocation(), {
        radius: 0.3,
        speed: 0.2,
        marker: { symbol: "!", color: "#fbbf24" },
        kit: shinobuKit
      });
      if (guideRoamer) setGuideDialogue();
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
  playgroundRamp = ramp;

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
  playgroundRing = ring;
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

// A quick rising arpeggio — sine tones instead of playThud's percussive
// triangle wave, so positive moments (a gem, a ring landing, finishing a
// quest stage) sound distinct from an impact.
const CHIME_PROFILE = {
  gem: [660, 880],
  success: [523, 659, 784]
};

function playChime(kind = "gem") {
  const ctx = ensureAudio();
  if (!ctx) return;
  const notes = CHIME_PROFILE[kind] ?? CHIME_PROFILE.gem;
  notes.forEach((freq, i) => {
    const start = ctx.currentTime + i * 0.09;
    const decay = 0.22;
    const osc = ctx.createOscillator();
    osc.type = "sine";
    osc.frequency.setValueAtTime(freq, start);
    const gain = ctx.createGain();
    gain.gain.setValueAtTime(0.001, start);
    gain.gain.linearRampToValueAtTime(0.22, start + 0.02);
    gain.gain.exponentialRampToValueAtTime(0.001, start + decay);
    osc.connect(gain).connect(ctx.destination);
    osc.start(start);
    osc.stop(start + decay + 0.02);
  });
}

// Wires a mesh + cannon body into the grab system: while held, the body goes
// kinematic and physics stops driving the mesh (grabSystem's own followHand
// takes over instead); on release, the body snaps to the mesh's current
// pose, goes dynamic again, and inherits the hand's real release velocity
// (boosted a bit so throws feel punchier) — real engine-driven gravity,
// collision and restitution on every throw. Also wires a synthesized impact
// thud off cannon-es's own 'collide' event, throttled per body so a resting
// contact doesn't machine-gun the sound every physics step.
function makeGrabbableProp(mesh, body, { kind = "crate", throwBoost = 1.3, onDrop = null } = {}) {
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
      onDrop?.(mesh);
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

function buildMarketCoin() {
  const mesh = new THREE.Group();
  const disc = new THREE.Mesh(
    new THREE.CylinderGeometry(0.08, 0.08, 0.02, 20),
    new THREE.MeshStandardMaterial({ color: 0xffd54a, metalness: 0.6, roughness: 0.3, emissive: 0xffd54a, emissiveIntensity: 0.15 })
  );
  mesh.add(disc);
  const label = createLabel("0", { width: 0.14, height: 0.14, fontSize: 110 });
  label.rotation.x = -Math.PI / 2;
  label.position.y = 0.011;
  mesh.add(label);
  mesh.userData.label = label;
  return mesh;
}

// Breakfast needs two things, not one — the vendor asks two separate
// pricing questions in a row (different item each time) before the story
// moves on, instead of a single drag-and-drop being the whole "quest".
const MARKET_ITEMS = [
  { name: "Potatoes", unit: "kg" },
  { name: "Bread", unit: "loaves" },
  { name: "Apples", unit: "kg" },
  { name: "Cheese", unit: "wheels" }
];
const MARKET_ROUNDS_NEEDED = 2;
let marketRoundsDone = 0;

function resetMarketCoins() {
  marketCoins.forEach((coin) => {
    coin.mesh.position.copy(coin.home);
    coin.mesh.quaternion.set(0, 0, 0, 1);
    coin.body.position.copy(coin.home);
    coin.body.quaternion.set(0, 0, 0, 1);
    coin.body.velocity.setZero();
    coin.body.angularVelocity.setZero();
    coin.body.wakeUp();
  });
}

function generateMarketQuestion() {
  const item = MARKET_ITEMS[Math.floor(Math.random() * MARKET_ITEMS.length)];
  const price = 2 + Math.floor(Math.random() * 5);
  const qty = 2 + Math.floor(Math.random() * 3);
  marketCorrectValue = price * qty;
  marketQuestionPanel.userData.setText([
    { text: `Vendor: "${item.name} are ${price} coins/${item.unit.replace(/s$/, "")}.`, bold: true, size: 24 },
    { text: `I need ${qty} ${item.unit} — how many coins do I owe?"`, size: 22 }
  ]);

  const values = new Set([marketCorrectValue]);
  while (values.size < 4) {
    const offset = (1 + Math.floor(Math.random() * 4)) * (Math.random() < 0.5 ? -1 : 1);
    const candidate = marketCorrectValue + offset;
    if (candidate > 0) values.add(candidate);
  }
  const shuffled = [...values].sort(() => Math.random() - 0.5);
  marketCoins.forEach((coin, i) => {
    coin.value = shuffled[i];
    coin.mesh.userData.label.userData.setText(String(coin.value));
  });
  resetMarketCoins();
}

function handleCoinDrop(mesh) {
  const coin = marketCoins.find((c) => c.mesh === mesh);
  if (!coin) return;
  const worldPos = mesh.getWorldPosition(new THREE.Vector3());
  if (worldPos.distanceTo(marketBowlPos) > 0.22) return; // missed the bowl

  if (coin.value === marketCorrectValue) {
    marketRoundsDone += 1;
    playChime("gem");
    if (marketRoundsDone >= MARKET_ROUNDS_NEEDED) {
      marketFeedbackPanel.userData.setText([{ text: "Correct! That's everything — thanks!", bold: true, size: 26, color: "#34d399" }]);
      showToast("Shopping done! Head to the playground.", "#34d399");
      playChime("success");
      advanceQuest("golf");
    } else {
      marketFeedbackPanel.userData.setText([
        { text: `Correct! (${marketRoundsDone}/${MARKET_ROUNDS_NEEDED})`, bold: true, size: 26, color: "#34d399" },
        { text: "One more item to price out.", size: 20 }
      ]);
      generateMarketQuestion();
    }
  } else {
    marketFeedbackPanel.userData.setText([
      { text: `${coin.value} isn't right —`, size: 24, color: "#f87171" },
      { text: "try again.", size: 22 }
    ]);
    setTimeout(() => {
      coin.mesh.position.copy(coin.home);
      coin.body.position.copy(coin.home);
      coin.body.velocity.setZero();
      coin.body.angularVelocity.setZero();
      coin.body.wakeUp();
    }, 500);
  }
}

// The market — the math quest. A vendor (spawned in loadCast) poses an
// arithmetic problem; grab the correctly-numbered coin off the tray and
// drop it in the payment bowl to advance the story.
function buildMarket() {
  const group = new THREE.Group();
  group.position.set(MARKET_CENTER.x, 0, MARKET_CENTER.z);
  worldGroup.add(group);

  const counter = new THREE.Mesh(
    new THREE.BoxGeometry(1.6, 0.9, 0.6),
    new THREE.MeshStandardMaterial({ color: 0x8a6a45, roughness: 0.8 })
  );
  counter.position.y = 0.45;
  counter.castShadow = true;
  counter.receiveShadow = true;
  group.add(counter);

  const awning = new THREE.Mesh(
    new THREE.BoxGeometry(2.0, 0.08, 0.9),
    new THREE.MeshStandardMaterial({ color: 0xd94f4f, roughness: 0.7 })
  );
  awning.position.set(0, 1.7, -0.1);
  awning.rotation.x = -0.15;
  awning.castShadow = true;
  group.add(awning);

  [-0.9, 0.9].forEach((x) => {
    const post = new THREE.Mesh(
      new THREE.CylinderGeometry(0.04, 0.04, 1.7, 8),
      new THREE.MeshStandardMaterial({ color: 0x5b4128, roughness: 0.8 })
    );
    post.position.set(x, 0.85, -0.25);
    group.add(post);
  });

  const basket = new THREE.Mesh(
    new THREE.CylinderGeometry(0.22, 0.18, 0.25, 12),
    new THREE.MeshStandardMaterial({ color: 0x8a5a2b, roughness: 0.85 })
  );
  basket.position.set(0.5, 0.125, 0.35);
  group.add(basket);
  for (let i = 0; i < 6; i++) {
    const potato = new THREE.Mesh(
      new THREE.SphereGeometry(0.06, 8, 6),
      new THREE.MeshStandardMaterial({ color: 0xc2a15a, roughness: 0.9 })
    );
    potato.scale.set(1, 0.8, 1.1);
    potato.position.set(0.5 + (Math.random() - 0.5) * 0.2, 0.28 + Math.random() * 0.05, 0.35 + (Math.random() - 0.5) * 0.2);
    group.add(potato);
  }

  marketQuestionPanel = createTextPanel({ width: 1.7, height: 0.5, fontSize: 24 });
  marketQuestionPanel.position.set(0, 2.0, -0.9);
  marketQuestionPanel.lookAt(MARKET_CENTER.x, 1.5, MARKET_CENTER.z - 3); // lookAt targets are world-space, unlike position
  group.add(marketQuestionPanel);

  marketFeedbackPanel = createTextPanel({ width: 1.3, height: 0.34, fontSize: 22, border: "rgba(167, 139, 250, 0.8)" });
  marketFeedbackPanel.position.set(1.5, 1.7, -0.4);
  marketFeedbackPanel.rotation.y = -0.5;
  group.add(marketFeedbackPanel);

  const bowlPos = new THREE.Vector3(MARKET_CENTER.x, 0.95, MARKET_CENTER.z - 0.5);
  marketBowlPos.copy(bowlPos);
  const bowlRing = new THREE.Mesh(
    new THREE.TorusGeometry(0.14, 0.015, 10, 30),
    new THREE.MeshStandardMaterial({ color: 0x34d399, emissive: 0x34d399, emissiveIntensity: 0.5 })
  );
  bowlRing.rotation.x = Math.PI / 2;
  bowlRing.position.copy(bowlPos);
  worldGroup.add(bowlRing);

  const coinHomesLocalX = [-0.3, -0.1, 0.1, 0.3];
  marketCoins = coinHomesLocalX.map((dx) => {
    const home = new THREE.Vector3(MARKET_CENTER.x + dx, 0.95, MARKET_CENTER.z + 0.35);
    const mesh = buildMarketCoin();
    mesh.position.copy(home);
    mesh.castShadow = true;

    const body = new CANNON.Body({ mass: 0.3, material: physics.materials.crate });
    body.addShape(new CANNON.Cylinder(0.08, 0.08, 0.02, 20));
    body.position.copy(home);
    makeGrabbableProp(mesh, body, { kind: "coin", throwBoost: 1.1, onDrop: handleCoinDrop });

    return { mesh, body, home, value: 0 };
  });

  marketRoundsDone = 0;
  generateMarketQuestion();
}

function kitchenCounts() {
  const counts = {};
  kitchenZoneAtoms.forEach((a) => { counts[a.name] = (counts[a.name] ?? 0) + 1; });
  return counts;
}

function refreshKitchenFeedback(text, color) {
  kitchenFeedbackPanel.userData.setText([{ text, bold: true, size: 24, color }]);
}

function resetIngredient(entry) {
  setTimeout(() => {
    entry.mesh.position.copy(entry.home);
    entry.mesh.quaternion.set(0, 0, 0, 1);
    entry.body.position.copy(entry.home);
    entry.body.quaternion.set(0, 0, 0, 1);
    entry.body.velocity.setZero();
    entry.body.angularVelocity.setZero();
    entry.body.wakeUp();
  }, 500);
}

function handleIngredientDrop(mesh) {
  const entry = kitchenIngredients.find((e) => e.mesh === mesh);
  if (!entry) return;
  if (kitchenLocked) { resetIngredient(entry); return; }

  const worldPos = mesh.getWorldPosition(new THREE.Vector3());
  if (worldPos.distanceTo(kitchenZonePos) > 0.2) return; // missed the pot

  const wouldBe = (kitchenCounts()[entry.ing.name] ?? 0) + 1;
  if (wouldBe > (KITCHEN_RECIPE[entry.ing.name] ?? 0)) {
    refreshKitchenFeedback(`Too much ${entry.ing.name}!`, "#f87171");
    resetIngredient(entry);
    return;
  }

  kitchenZoneAtoms.push({ name: entry.ing.name, mesh });
  const matches = Object.keys(KITCHEN_RECIPE).every((n) => (kitchenCounts()[n] ?? 0) === KITCHEN_RECIPE[n]);
  if (matches) {
    kitchenLocked = true;
    kitchenFeedbackPanel.userData.setText([
      { text: "Perfect mix!", bold: true, size: 28, color: "#34d399" },
      { text: "Dinner's on its way.", size: 22 }
    ]);
    advanceQuest("complete");
  } else {
    refreshKitchenFeedback(`Added ${entry.ing.name}`, "#e8ecf6");
  }
}

function spawnKitchenIngredient(ing, standWorldPos) {
  const mesh = new THREE.Mesh(
    new THREE.SphereGeometry(0.06, 16, 12),
    new THREE.MeshStandardMaterial({ color: ing.color, emissive: ing.color, emissiveIntensity: 0.2, roughness: 0.4 })
  );
  mesh.castShadow = true;
  const home = standWorldPos.clone().add(new THREE.Vector3(0, 0.32, 0));
  mesh.position.copy(home);

  const body = new CANNON.Body({ mass: 0.2, material: physics.materials.ball });
  body.addShape(new CANNON.Sphere(0.06));
  body.position.copy(home);
  makeGrabbableProp(mesh, body, { kind: "ingredient", throwBoost: 1.1, onDrop: handleIngredientDrop });

  kitchenIngredients.push({ mesh, body, home, ing });
}

// The kitchen — the chemistry quest. Grab the right ingredients off their
// stands and drop them in the pot to match the recipe on the board.
function buildKitchen() {
  const group = new THREE.Group();
  group.position.set(KITCHEN_CENTER.x, 0, KITCHEN_CENTER.z);
  worldGroup.add(group);
  kitchenGroup = group;

  const counter = new THREE.Mesh(
    new THREE.BoxGeometry(2.4, 0.9, 0.7),
    new THREE.MeshStandardMaterial({ color: 0xdad2c3, roughness: 0.8 })
  );
  counter.position.y = 0.45;
  counter.castShadow = true;
  counter.receiveShadow = true;
  group.add(counter);

  const stove = new THREE.Mesh(
    new THREE.BoxGeometry(0.6, 0.5, 0.6),
    new THREE.MeshStandardMaterial({ color: 0x30343d, roughness: 0.6, metalness: 0.3 })
  );
  stove.position.set(0.9, 0.7, 0);
  stove.castShadow = true;
  group.add(stove);

  const pot = new THREE.Mesh(
    new THREE.CylinderGeometry(0.18, 0.15, 0.2, 16),
    new THREE.MeshStandardMaterial({ color: 0x40464f, metalness: 0.5, roughness: 0.4 })
  );
  pot.position.set(0.9, 1.0, 0);
  group.add(pot);
  kitchenZonePos.set(KITCHEN_CENTER.x + 0.9, 1.0, KITCHEN_CENTER.z);

  const zoneRing = new THREE.Mesh(
    new THREE.TorusGeometry(0.14, 0.012, 10, 30),
    new THREE.MeshStandardMaterial({ color: 0xf59e0b, emissive: 0xf59e0b, emissiveIntensity: 0.4 })
  );
  zoneRing.rotation.x = Math.PI / 2;
  zoneRing.position.set(0.9, 1.02, 0);
  group.add(zoneRing);

  const kitchenPanel = createTextPanel({ width: 1.8, height: 0.5, fontSize: 22 });
  kitchenPanel.position.set(0, 2.0, -0.9);
  kitchenPanel.lookAt(KITCHEN_CENTER.x, 1.5, KITCHEN_CENTER.z - 3); // lookAt targets are world-space, unlike position
  const needText = Object.entries(KITCHEN_RECIPE).map(([n, c]) => `${c} × ${n}`).join("  +  ");
  kitchenPanel.userData.setText([
    { text: "Recipe:", bold: true, size: 26, color: "#fbbf24" },
    { text: needText, size: 22 }
  ]);
  group.add(kitchenPanel);

  kitchenFeedbackPanel = createTextPanel({ width: 1.3, height: 0.34, fontSize: 22, border: "rgba(245, 158, 11, 0.8)" });
  kitchenFeedbackPanel.position.set(1.6, 1.7, -0.4);
  kitchenFeedbackPanel.rotation.y = -0.5;
  refreshKitchenFeedback("Mix the ingredients in the pot", "#e8ecf6");
  group.add(kitchenFeedbackPanel);

  KITCHEN_INGREDIENTS.forEach((ing, i) => {
    const standLocal = new THREE.Vector3(-1.0 + i * 0.7, 0, 0.6);
    const stand = new THREE.Group();
    stand.position.copy(standLocal);
    group.add(stand);

    const post = new THREE.Mesh(
      new THREE.CylinderGeometry(0.035, 0.045, 0.3, 12),
      new THREE.MeshStandardMaterial({ color: 0x8a6a45, roughness: 0.7 })
    );
    post.position.y = 0.15;
    stand.add(post);

    const label = createLabel(ing.name, { width: 0.32, height: 0.14, fontSize: 60 });
    label.position.set(0, -0.06, 0.05);
    stand.add(label);

    const standWorld = new THREE.Vector3(KITCHEN_CENTER.x + standLocal.x, 0, KITCHEN_CENTER.z + standLocal.z);
    spawnKitchenIngredient(ing, standWorld);
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
  playgroundResetBtn = resetBtn;
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
      playChime("gem");
      if (questStage === "golf") {
        if (ringScore >= RING_GOAL) {
          showToast(`${RING_GOAL} landed — nice aim! Head to the kitchen.`, "#34d399");
          playChime("success");
          advanceQuest("kitchen");
        } else {
          showToast(`Landed! (${ringScore}/${RING_GOAL})`);
        }
      }
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
    { text: `Knock the crates off, land ${RING_GOAL} things in the ring!`, bold: true, size: 28 },
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

// Desktop-only look-around: right-click drag rotates the view (left-click
// drag is already grabSystem's grab gesture, so this deliberately uses the
// other mouse button to never conflict with it). Without this, a desktop
// player facing whichever way they last walked can never see anything
// that isn't directly ahead — the village/camp/forest/mountains are all
// already built, they're just invisible without a way to turn and look.
let lookActive = false;
let lookLastX = 0;
let lookLastY = 0;
let cameraPitch = 0;
const MOUSE_LOOK_SPEED = 0.0028;
const MAX_PITCH = 1.3; // radians, just short of straight up/down

function handleContextMenu(event) { event.preventDefault(); }

// The "How to play" card starts open on every visit and can be dismissed
// (it was previously a permanent paragraph pinned to the bottom of the
// screen for the whole session) — closing it swaps in a small "?" button
// so the controls/objective explanation stays one click away instead of
// gone for good.
function handleGuideClose() {
  const panel = guidePanelEl();
  const reopen = guideReopenBtn();
  if (panel) panel.hidden = true;
  if (reopen) reopen.hidden = false;
}
function handleGuideReopen() {
  const panel = guidePanelEl();
  const reopen = guideReopenBtn();
  if (panel) panel.hidden = false;
  if (reopen) reopen.hidden = true;
}

function handleLookPointerDown(event) {
  if (event.button !== 2 || xrState.renderer.xr.isPresenting) return;
  lookActive = true;
  lookLastX = event.clientX;
  lookLastY = event.clientY;
}

function handleLookPointerMove(event) {
  if (!lookActive) return;
  const dx = event.clientX - lookLastX;
  const dy = event.clientY - lookLastY;
  lookLastX = event.clientX;
  lookLastY = event.clientY;
  xrState.rig.rotation.y -= dx * MOUSE_LOOK_SPEED;
  cameraPitch = THREE.MathUtils.clamp(cameraPitch - dy * MOUSE_LOOK_SPEED, -MAX_PITCH, MAX_PITCH);
  xrState.camera.rotation.x = cameraPitch;
}

function handleLookPointerUp(event) {
  if (event.button !== 2) return;
  lookActive = false;
}

// ---------------------------------------------------------------------------
// Environmental dressing: the open grass between the village/camp/market was
// bare, so this scatters flowers, grass tufts, bushes and rocks across it
// (each type is one InstancedMesh — a few thousand instances for the cost of
// four draw calls, unlike buildTree's one-Group-per-tree approach), lays
// dirt paths connecting the points of interest, and adds a few butterflies
// for ambient motion. Purely decorative, same flat-world convention as
// buildMountains — nothing here blocks locomotion.
// ---------------------------------------------------------------------------

const dummyMatrix = new THREE.Matrix4();
const dummyPos = new THREE.Vector3();
const dummyQuat = new THREE.Quaternion();
const dummyScale = new THREE.Vector3();
const dummyColor = new THREE.Color();

function scatterInstances(mesh, count, { minR, maxR, extraRadius = 0, minScale = 0.7, maxScale = 1.3, colorVariants = null }) {
  let placed = 0;
  let attempts = 0;
  while (placed < count && attempts < count * 4) {
    attempts++;
    const angle = Math.random() * Math.PI * 2;
    const r = minR + Math.random() * (maxR - minR);
    const x = Math.cos(angle) * r;
    const z = Math.sin(angle) * r;
    if (!isFreeSpot(x, z, extraRadius)) continue;

    dummyPos.set(x, 0, z);
    dummyQuat.setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.random() * Math.PI * 2);
    const s = minScale + Math.random() * (maxScale - minScale);
    dummyScale.set(s, s, s);
    dummyMatrix.compose(dummyPos, dummyQuat, dummyScale);
    mesh.setMatrixAt(placed, dummyMatrix);

    if (colorVariants) {
      dummyColor.set(colorVariants[Math.floor(Math.random() * colorVariants.length)]);
      mesh.setColorAt(placed, dummyColor); // lazily creates mesh.instanceColor on first call
    }
    placed++;
  }
  mesh.count = placed;
  mesh.instanceMatrix.needsUpdate = true;
  if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
}

function buildFlowerPatch() {
  const petalColors = [0xff6b9d, 0xffd54a, 0xf5f5f5, 0xff8a5c, 0xc084fc];
  const geometry = new THREE.ConeGeometry(0.035, 0.09, 5);
  geometry.translate(0, 0.045, 0);
  const material = new THREE.MeshStandardMaterial({ roughness: 0.7, vertexColors: true });
  const mesh = new THREE.InstancedMesh(geometry, material, 420);
  mesh.name = "flowerPatch";
  scatterInstances(mesh, 420, { minR: 3, maxR: 30, minScale: 0.8, maxScale: 1.6, colorVariants: petalColors });
  return mesh;
}

function buildGrassTufts() {
  const geometry = new THREE.ConeGeometry(0.05, 0.22, 4);
  geometry.translate(0, 0.11, 0);
  const material = new THREE.MeshStandardMaterial({ color: 0x3f7a45, roughness: 1 });
  const mesh = new THREE.InstancedMesh(geometry, material, 650);
  mesh.name = "grassTufts";
  scatterInstances(mesh, 650, { minR: 2.5, maxR: 31, minScale: 0.7, maxScale: 1.7 });
  return mesh;
}

function buildBushes() {
  const geometry = new THREE.IcosahedronGeometry(0.26, 0);
  const material = new THREE.MeshStandardMaterial({ color: 0x336640, roughness: 0.95 });
  const mesh = new THREE.InstancedMesh(geometry, material, 110);
  mesh.castShadow = true;
  mesh.name = "bushes";
  scatterInstances(mesh, 110, { minR: 4, maxR: 29, extraRadius: 0.6, minScale: 0.8, maxScale: 1.5 });
  return mesh;
}

function buildRocks() {
  const geometry = new THREE.DodecahedronGeometry(0.16, 0);
  const material = new THREE.MeshStandardMaterial({ color: 0x6b7280, roughness: 1 });
  const mesh = new THREE.InstancedMesh(geometry, material, 90);
  mesh.castShadow = true;
  mesh.name = "rocks";
  scatterInstances(mesh, 90, { minR: 3, maxR: 31, minScale: 0.6, maxScale: 1.8 });
  return mesh;
}

function buildFoliage() {
  worldGroup.add(buildFlowerPatch());
  worldGroup.add(buildGrassTufts());
  worldGroup.add(buildBushes());
  worldGroup.add(buildRocks());
}

function buildDirtTexture() {
  const canvas = document.createElement("canvas");
  canvas.width = canvas.height = 128;
  const ctx = canvas.getContext("2d");
  ctx.fillStyle = "#9c7a4f";
  ctx.fillRect(0, 0, canvas.width, canvas.height);
  for (let i = 0; i < 260; i++) {
    ctx.fillStyle = Math.random() < 0.5 ? "rgba(70,50,25,0.25)" : "rgba(190,160,110,0.25)";
    ctx.fillRect(Math.random() * canvas.width, Math.random() * canvas.height, 2, 2);
  }
  const texture = new THREE.CanvasTexture(canvas);
  texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}

// A straight dirt-path strip from `from` to `to`, tiled lengthwise so it
// reads as a worn trail rather than a stretched texture. Built as an
// explicit ground-plane quad (same cross-section technique buildRiver()
// uses) rather than a rotated PlaneGeometry, so there's no Euler-order
// ambiguity about which way "flat and facing along the line" ends up.
function buildPathSegment(from, to, width = 1.1) {
  const dx = to.x - from.x;
  const dz = to.z - from.z;
  const length = Math.hypot(dx, dz);
  const dir = new THREE.Vector3(dx, 0, dz).normalize();
  const side = new THREE.Vector3(-dir.z, 0, dir.x).multiplyScalar(width / 2);

  const positions = [
    from.x - side.x, 0.012, from.z - side.z,
    from.x + side.x, 0.012, from.z + side.z,
    to.x - side.x, 0.012, to.z - side.z,
    to.x + side.x, 0.012, to.z + side.z
  ];
  const uvs = [0, 0, 1, 0, 0, 1, 1, 1];
  const indices = [0, 1, 2, 1, 3, 2];

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.Float32BufferAttribute(positions, 3));
  geometry.setAttribute("uv", new THREE.Float32BufferAttribute(uvs, 2));
  geometry.setIndex(indices);
  geometry.computeVertexNormals();

  const texture = buildDirtTexture();
  texture.repeat.set(1, Math.max(1, Math.round(length / width)));
  const material = new THREE.MeshStandardMaterial({ map: texture, roughness: 1, transparent: true, opacity: 0.85 });
  return new THREE.Mesh(geometry, material);
}

function buildPaths() {
  const spawn = new THREE.Vector3(0, 0, 0);
  const destinations = [PLAYGROUND_CENTER, VILLAGE_CENTER, CAMP_CENTER, MARKET_CENTER, KITCHEN_CENTER];
  destinations.forEach((dest) => worldGroup.add(buildPathSegment(spawn, dest)));
}

// A handful of small colorful quads drifting in loose figure-eight loops —
// cheap ambient motion (no physics, no collision) that makes the world feel
// inhabited even where nothing else is happening. Animated from the main
// updateFn tick (registered/deregistered with the rest of World's state in
// mount()/unmount()) rather than self-registering here, since a self-added
// tick would have no owner to remove it and leak on every re-visit to World.
function buildButterflies() {
  const colors = [0xffb703, 0xf72585, 0x4cc9f0, 0xffffff, 0xa78bfa];
  const group = new THREE.Group();
  group.name = "butterflies";
  const flutters = [];

  for (let i = 0; i < 14; i++) {
    const spot = randomFreeSpot(3, 26);
    const wingGeo = new THREE.PlaneGeometry(0.05, 0.04);
    const wingMat = new THREE.MeshBasicMaterial({
      color: colors[i % colors.length],
      side: THREE.DoubleSide,
      transparent: true,
      opacity: 0.95
    });
    const left = new THREE.Mesh(wingGeo, wingMat);
    left.position.x = -0.02;
    const right = new THREE.Mesh(wingGeo, wingMat);
    right.position.x = 0.02;
    const butterfly = new THREE.Group();
    butterfly.add(left, right);
    butterfly.position.set(spot.x, 0.4 + Math.random() * 0.6, spot.z);
    group.add(butterfly);

    flutters.push({
      mesh: butterfly,
      left,
      right,
      center: spot.clone(),
      radius: 0.6 + Math.random() * 1.2,
      speed: 0.4 + Math.random() * 0.5,
      phase: Math.random() * Math.PI * 2,
      baseY: 0.4 + Math.random() * 0.6
    });
  }

  return { group, flutters };
}

function updateButterflies() {
  if (!butterflies) return;
  for (const f of butterflies.flutters) {
    const t = elapsed * f.speed + f.phase;
    f.mesh.position.x = f.center.x + Math.sin(t) * f.radius;
    f.mesh.position.z = f.center.z + Math.sin(t * 2) * f.radius * 0.5;
    f.mesh.position.y = f.baseY + Math.sin(t * 3) * 0.12;
    f.mesh.rotation.y = t;
    const flap = Math.sin(elapsed * 14 + f.phase) * 0.8;
    f.left.rotation.y = flap;
    f.right.rotation.y = -flap;
  }
}

const GEMS_KEY = "ale.world.gemsCollected";
const GEM_PICKUP_RADIUS = 0.9;

// Optional side collectibles — spinning, bobbing gems scattered around the
// world (see COLLECTIBLE_SPOTS). Purely a reward loop for exploring off the
// direct quest path; skips any spot already collected earlier this session.
function buildCollectibles() {
  let savedIds = [];
  try { savedIds = JSON.parse(sessionStorage.getItem(GEMS_KEY) ?? "[]"); } catch { savedIds = []; }
  const savedSet = new Set(savedIds);
  gemsFound = savedSet.size;
  collectibles = [];

  const geometry = new THREE.OctahedronGeometry(0.14, 0);
  const material = new THREE.MeshStandardMaterial({
    color: 0x38bdf8, emissive: 0x38bdf8, emissiveIntensity: 0.7, roughness: 0.2, metalness: 0.3
  });

  COLLECTIBLE_SPOTS.forEach((pos, id) => {
    if (savedSet.has(id)) return; // already found earlier this session
    const mesh = new THREE.Mesh(geometry, material);
    mesh.position.copy(pos);
    mesh.castShadow = true;
    worldGroup.add(mesh);
    collectibles.push({ id, mesh, phase: Math.random() * Math.PI * 2 });
  });
}

function collectGem(gem) {
  worldGroup.remove(gem.mesh);
  gemsFound += 1;
  try {
    const saved = JSON.parse(sessionStorage.getItem(GEMS_KEY) ?? "[]");
    saved.push(gem.id);
    sessionStorage.setItem(GEMS_KEY, JSON.stringify(saved));
  } catch { /* storage unavailable */ }

  playChime(gemsFound >= COLLECTIBLE_SPOTS.length ? "success" : "gem");
  showToast(
    gemsFound >= COLLECTIBLE_SPOTS.length
      ? `All ${COLLECTIBLE_SPOTS.length} gems found — nice exploring!`
      : `Found a gem! (${gemsFound}/${COLLECTIBLE_SPOTS.length})`,
    "#38bdf8"
  );
}

function updateCollectibles() {
  if (collectibles.length === 0) return;
  const rigPos = xrState.rig.position;
  for (let i = collectibles.length - 1; i >= 0; i--) {
    const gem = collectibles[i];
    gem.mesh.rotation.y = elapsed * 1.4 + gem.phase;
    gem.mesh.position.y = 0.5 + Math.sin(elapsed * 2 + gem.phase) * 0.08;
    // Horizontal-only distance — the gem's own bob shouldn't make pickup
    // finickier, and rig.position.y sits at ground level regardless of the
    // gem's floating height.
    const dx = gem.mesh.position.x - rigPos.x;
    const dz = gem.mesh.position.z - rigPos.z;
    if (Math.hypot(dx, dz) < GEM_PICKUP_RADIUS) {
      collectGem(gem);
      collectibles.splice(i, 1);
    }
  }
}

export function mount(scene) {
  sceneRef = scene;
  disposed = false;
  ringScore = 0;
  elapsed = 0;
  try { bestRingScore = Number(sessionStorage.getItem(BEST_SCORE_KEY)) || 0; } catch { bestRingScore = 0; }
  try {
    const saved = sessionStorage.getItem(QUEST_STAGE_KEY);
    questStage = saved && QUEST_STAGES[saved] ? saved : "intro";
  } catch { questStage = "intro"; }
  kitchenLocked = false;
  kitchenZoneAtoms = [];

  const baseEnv = scene.getObjectByName("baseEnvironment");
  const demoCube = scene.getObjectByName("demoCube");
  if (baseEnv) baseEnv.visible = false;
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
  buildFoliage();
  buildPaths();
  buildRiver();
  buildVillage();
  buildSpawnHamlet();
  buildCamp();
  buildRamp();
  buildPyramid();
  buildRing();
  buildUI();
  buildMarket();
  buildKitchen();
  refreshScoreboard();
  updateStageVisibility();

  butterflies = buildButterflies();
  worldGroup.add(butterflies.group);
  buildCollectibles();

  // No permanent on-screen quest HUD — it read as unwanted clutter sitting
  // in front of the camera at all times. The guide NPC's dialogue already
  // carries the current objective (see setGuideDialogue/relocateGuide),
  // which is enough to follow the story without a floating text box.

  setStatus("Loading world…");
  Promise.allSettled([loadCreatures(), loadCast(), loadStaticProps(), loadNatureScatter(), loadMutantGolem()]).then(() => { if (!disposed) setStatus(""); });

  document.addEventListener("keydown", handleKeyDown);
  document.addEventListener("keyup", handleKeyUp);

  // Reset to "open" every visit — a dismissal from a previous session
  // shouldn't carry over and leave a first-time player without it.
  if (guidePanelEl()) guidePanelEl().hidden = false;
  if (guideReopenBtn()) guideReopenBtn().hidden = true;
  guideCloseBtn()?.addEventListener("click", handleGuideClose);
  guideReopenBtn()?.addEventListener("click", handleGuideReopen);

  cameraPitch = 0;
  xrState.renderer.domElement.addEventListener("contextmenu", handleContextMenu);
  xrState.renderer.domElement.addEventListener("pointerdown", handleLookPointerDown);
  window.addEventListener("pointermove", handleLookPointerMove);
  window.addEventListener("pointerup", handleLookPointerUp);

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
    updateButterflies();
    updateStaticBobbers(elapsed);
    updateCollectibles();
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

  guideCloseBtn()?.removeEventListener("click", handleGuideClose);
  guideReopenBtn()?.removeEventListener("click", handleGuideReopen);

  xrState.renderer.domElement.removeEventListener("contextmenu", handleContextMenu);
  xrState.renderer.domElement.removeEventListener("pointerdown", handleLookPointerDown);
  window.removeEventListener("pointermove", handleLookPointerMove);
  window.removeEventListener("pointerup", handleLookPointerUp);
  lookActive = false;
  xrState.camera.rotation.x = 0; // shared camera object — don't leak a tilted view into other pages

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
  butterflies = null;
  staticBobbers = [];
  scoreboard = null;
  ringMarker = null;
  ringScore = 0;

  guideRoamer = null;
  marketCoins = [];
  marketQuestionPanel = null;
  marketFeedbackPanel = null;
  kitchenIngredients = [];
  kitchenZoneAtoms = [];
  kitchenLocked = false;
  kitchenFeedbackPanel = null;

  physics?.dispose();
  physics = null;

  audioCtx?.close().catch(() => {});
  audioCtx = null;

  xrState.renderer.shadowMap.enabled = false;
  sceneRef.fog = sceneRef.userData.baseFog ?? null;

  const baseEnv = sceneRef.getObjectByName("baseEnvironment");
  const demoCube = sceneRef.getObjectByName("demoCube");
  if (baseEnv) baseEnv.visible = true;
  if (demoCube) demoCube.visible = true;

  if (questLogPanel) {
    xrState.rig.remove(questLogPanel);
    disposeTree(questLogPanel);
    questLogPanel = null;
  }
  if (toastPanel) {
    clearTimeout(toastTimer);
    xrState.rig.remove(toastPanel);
    disposeTree(toastPanel);
    toastPanel = null;
  }
  playgroundRamp = null;
  playgroundRing = null;
  playgroundResetBtn = null;
  kitchenGroup = null;
  marketRoundsDone = 0;
  collectibles = [];
  gemsFound = 0;

  sceneRef.remove(worldGroup);
  disposeTree(worldGroup);
  worldGroup = null;
  sceneRef = null;
}
