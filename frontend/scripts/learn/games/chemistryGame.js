import * as THREE from "three";
import { createTextPanel, createLabel, disposeTree } from "../../core/textPanel.js";
import { spawnBurst, spawnShockwave } from "../../core/effects.js";

function hexColor(n) {
  return `#${n.toString(16).padStart(6, "0")}`;
}

function easeOutBack(t) {
  const c1 = 1.70158, c3 = c1 + 1;
  return 1 + c3 * Math.pow(t - 1, 3) + c1 * Math.pow(t - 1, 2);
}

const ELEMENTS = {
  H: { name: "Hydrogen", color: 0xf5f7fa, radius: 0.05 },
  O: { name: "Oxygen", color: 0xf87171, radius: 0.075 },
  C: { name: "Carbon", color: 0x4b5563, radius: 0.07 },
  N: { name: "Nitrogen", color: 0x5b8cff, radius: 0.07 }
};

// Each recipe: required atom counts, a display formula, a fun fact, and the
// 3D arrangement (center atom + satellite directions) used to assemble the
// molecule model on success. `topic` groups it for the topic selector.
const RECIPES = [
  {
    formula: "H₂", name: "Hydrogen gas", counts: { H: 2 }, topic: "diatomic",
    fact: "The lightest, most abundant element in the universe.",
    center: "H",
    satellites: [{ el: "H", dir: [1, 0, 0] }]
  },
  {
    formula: "O₂", name: "Oxygen gas", counts: { O: 2 }, topic: "diatomic",
    fact: "The air you breathe is 21% O₂ — a double-bonded pair.",
    center: "O",
    satellites: [{ el: "O", dir: [1, 0, 0] }]
  },
  {
    formula: "H₂O", name: "Water", counts: { H: 2, O: 1 }, topic: "compounds",
    fact: "Bent shape (104.5°) — that's why water is polar!",
    center: "O",
    satellites: [{ el: "H", dir: [0.8, 0.55, 0] }, { el: "H", dir: [-0.8, 0.55, 0] }]
  },
  {
    formula: "CO₂", name: "Carbon dioxide", counts: { C: 1, O: 2 }, topic: "compounds",
    fact: "Linear molecule — you exhale it with every breath.",
    center: "C",
    satellites: [{ el: "O", dir: [1, 0, 0] }, { el: "O", dir: [-1, 0, 0] }]
  },
  {
    formula: "NH₃", name: "Ammonia", counts: { N: 1, H: 3 }, topic: "compounds",
    fact: "Pyramid shape — the lone pair pushes the H atoms down.",
    center: "N",
    satellites: [
      { el: "H", dir: [0.9, -0.45, 0] },
      { el: "H", dir: [-0.45, -0.45, 0.78] },
      { el: "H", dir: [-0.45, -0.45, -0.78] }
    ]
  },
  {
    formula: "CH₄", name: "Methane", counts: { C: 1, H: 4 }, topic: "compounds",
    fact: "A perfect tetrahedron — the main gas in natural gas.",
    center: "C",
    satellites: [
      { el: "H", dir: [1, 1, 1] },
      { el: "H", dir: [-1, -1, 1] },
      { el: "H", dir: [-1, 1, -1] },
      { el: "H", dir: [1, -1, -1] }
    ]
  }
];

const TOPICS = [
  { id: "diatomic", label: "Diatomic Molecules" },
  { id: "compounds", label: "Everyday Compounds" }
];

// Difficulty caps how many total atoms a recipe can need (so "hard" is the
// only tier that ever draws the 5-atom CH4) and tightens the drop-zone
// tolerance so placement itself gets less forgiving too.
const DIFFICULTY = {
  easy: { maxAtoms: 3, zoneRadius: 0.28 },
  medium: { maxAtoms: 4, zoneRadius: 0.22 },
  hard: { maxAtoms: 99, zoneRadius: 0.16 }
};

const RESPAWN_DELAY = 500;

function totalAtoms(recipe) {
  return Object.values(recipe.counts).reduce((sum, n) => sum + n, 0);
}

function recipePool(topic, difficulty) {
  const diff = DIFFICULTY[difficulty] ?? DIFFICULTY.easy;
  const all = RECIPES.filter((r) => r.topic === topic);
  const filtered = all.filter((r) => totalAtoms(r) <= diff.maxAtoms);
  return filtered.length ? filtered : all;
}

/**
 * Chemistry — "Snap-Together Molecules". Pedestals within arm's reach keep
 * offering H, O, C and N atoms; grab one and place it in the glowing
 * assembly zone. Atoms you drop there stay and get counted live; get the
 * exact mix the target molecule needs and the loose atoms snap into the
 * real bonded shape, with its real geometry and a fact about it.
 */
export function createGame({ grab, config = {} }) {
  const group = new THREE.Group();
  group.name = "chemistryGame";

  const topic = config.topic ?? "diatomic";
  const difficulty = config.difficulty ?? "easy";
  const pool = recipePool(topic, difficulty);
  const ZONE_RADIUS = (DIFFICULTY[difficulty] ?? DIFFICULTY.easy).zoneRadius;

  let recipeIndex = 0;
  let solved = 0;
  let locked = false;
  let popAnim = null; // { model, t } — elastic pop-in scale animation for a freshly completed molecule
  const timers = new Set();
  const zoneAtoms = []; // { symbol, mesh }
  const pedestalAtoms = new Map(); // symbol -> currently-offered grabbable mesh
  const worldPos = new THREE.Vector3();

  // --- Panels ---------------------------------------------------------------
  const targetPanel = createTextPanel({ width: 1.55, height: 0.56, fontSize: 40 });
  targetPanel.position.set(0, 2.0, -2.0);
  group.add(targetPanel);

  const benchPanel = createTextPanel({ width: 1.05, height: 0.34, fontSize: 30, border: "rgba(34, 211, 238, 0.8)" });
  benchPanel.position.set(1.35, 1.6, -1.9);
  benchPanel.rotation.y = -0.4;
  group.add(benchPanel);

  const feedbackPanel = createTextPanel({ width: 1.1, height: 0.34, fontSize: 28, border: "rgba(167, 139, 250, 0.8)" });
  feedbackPanel.position.set(-1.35, 1.6, -1.9);
  feedbackPanel.rotation.y = 0.4;
  group.add(feedbackPanel);

  // --- Assembly zone (where placed atoms bond) --------------------------------
  const zone = new THREE.Group();
  zone.position.set(0, 1.0, -0.55);
  group.add(zone);

  const zoneRing = new THREE.Mesh(
    new THREE.TorusGeometry(ZONE_RADIUS, 0.015, 12, 48),
    new THREE.MeshStandardMaterial({ color: 0x22d3ee, emissive: 0x22d3ee, emissiveIntensity: 0.4 })
  );
  zoneRing.rotation.x = Math.PI / 2;
  zone.add(zoneRing);

  const assembly = new THREE.Group(); // holds either loose zoneAtoms or the final bonded model
  zone.add(assembly);

  function flashZone(color) {
    zoneRing.material.color.setHex(color);
    zoneRing.material.emissiveIntensity = 1.2;
    later(400, () => {
      zoneRing.material.color.setHex(0x22d3ee);
      zoneRing.material.emissiveIntensity = 0.4;
    });
  }

  // --- Element pedestals (infinite-supply grab points) -----------------------
  const pedestalRoot = new THREE.Group();
  pedestalRoot.position.set(0, 0, -0.05);
  group.add(pedestalRoot);

  const symbols = Object.keys(ELEMENTS);
  const pedestals = symbols.map((symbol, i) => {
    const stand = new THREE.Group();
    stand.position.set(-0.45 + i * 0.3, 0.75, -0.3);
    pedestalRoot.add(stand);

    const post = new THREE.Mesh(
      new THREE.CylinderGeometry(0.035, 0.045, 0.3, 14),
      new THREE.MeshStandardMaterial({ color: 0x232b40, roughness: 0.7 })
    );
    post.position.y = 0.15;
    stand.add(post);

    const nameLabel = createLabel(symbol, { width: 0.2, height: 0.12, fontSize: 90 });
    nameLabel.position.set(0, -0.06, 0.05);
    stand.add(nameLabel);

    return stand;
  });

  function spawnPedestalAtom(symbol, stand) {
    const el = ELEMENTS[symbol];
    const atom = new THREE.Mesh(
      new THREE.SphereGeometry(el.radius * 1.5, 24, 18),
      new THREE.MeshStandardMaterial({ color: el.color, emissive: el.color, emissiveIntensity: 0.2, roughness: 0.35 })
    );
    atom.position.set(0, 0.32, 0);
    stand.add(atom);
    pedestalAtoms.set(symbol + stand.uuid, atom);

    grab.add(atom, {
      onGrab: () => {
        pedestalAtoms.delete(symbol + stand.uuid);
        atom.material.emissiveIntensity = 0.7;
        atom.getWorldPosition(worldPos);
        spawnBurst(group, {
          position: group.worldToLocal(worldPos.clone()),
          colors: [hexColor(el.color)], count: 8, speed: 0.5, size: 0.015, life: 0.3
        });
        later(RESPAWN_DELAY, () => spawnPedestalAtom(symbol, stand));
      },
      onRelease: () => handleAtomRelease(symbol, atom),
      onHoverStart: () => atom.scale.setScalar(1.25),
      onHoverEnd: () => atom.scale.setScalar(1)
    });
  }

  symbols.forEach((symbol, i) => spawnPedestalAtom(symbol, pedestals[i]));

  // --- Helpers ----------------------------------------------------------------
  const recipe = () => pool[recipeIndex % pool.length];

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
  }

  function currentCounts() {
    const counts = {};
    for (const a of zoneAtoms) counts[a.symbol] = (counts[a.symbol] ?? 0) + 1;
    return counts;
  }

  function refreshPanels() {
    const r = recipe();
    const need = Object.entries(r.counts).map(([s, n]) => `${n} × ${ELEMENTS[s].name}`).join("  +  ");
    const counts = currentCounts();
    const have = Object.entries(r.counts)
      .map(([s, n]) => `${s}: ${counts[s] ?? 0}/${n}`)
      .join("   ");
    targetPanel.userData.setText([
      { text: `Build: ${r.formula} — ${r.name}`, bold: true, size: 40 },
      { text: need, size: 26, color: "#8fa3c8" },
      { text: `Molecules made: ${solved}`, size: 24, color: "#34d399" }
    ]);
    benchPanel.userData.setText([
      { text: "In the zone", size: 24, color: "#8fa3c8" },
      { text: have || "empty", bold: true, size: 30, color: "#22d3ee" }
    ]);
  }

  function setFeedback(lines) {
    feedbackPanel.userData.setText(lines);
  }

  function layoutZoneAtoms() {
    zoneAtoms.forEach((a, i) => {
      const angle = (i / Math.max(zoneAtoms.length, 1)) * Math.PI * 2;
      const r = zoneAtoms.length > 1 ? 0.08 : 0;
      a.mesh.position.set(Math.cos(angle) * r, Math.sin(angle) * r * 0.5, 0);
    });
  }

  function handleAtomRelease(symbol, atom) {
    if (locked) {
      atom.parent?.remove(atom);
      atom.geometry.dispose();
      atom.material.dispose();
      return;
    }

    atom.getWorldPosition(worldPos);
    const zoneWorld = zone.getWorldPosition(new THREE.Vector3());
    const dist = worldPos.distanceTo(zoneWorld);

    if (dist > ZONE_RADIUS) {
      // Missed the zone — this atom is spent (the pedestal already has a
      // fresh one on the way), just remove it rather than track a rack slot.
      atom.geometry.dispose();
      atom.material.dispose();
      atom.parent?.remove(atom);
      return;
    }

    const r = recipe();
    const wouldBe = (currentCounts()[symbol] ?? 0) + 1;
    if (wouldBe > (r.counts[symbol] ?? 0)) {
      flashZone(0xf87171);
      setFeedback([
        { text: `Too much ${ELEMENTS[symbol].name}!`, bold: true, size: 30, color: "#f87171" },
        { text: `${r.formula} only needs ${r.counts[symbol] ?? 0}`, size: 24, color: "#8fa3c8" }
      ]);
      spawnBurst(zone, { position: zone.worldToLocal(worldPos.clone()), colors: ["#f87171"], count: 10, speed: 0.8, size: 0.018, life: 0.35 });
      atom.geometry.dispose();
      atom.material.dispose();
      atom.parent?.remove(atom);
      return;
    }

    assembly.add(atom);
    atom.material.emissiveIntensity = 0.2;
    zoneAtoms.push({ symbol, mesh: atom });
    layoutZoneAtoms();
    flashZone(0x34d399);
    spawnBurst(zone, { position: atom.position.clone(), colors: [hexColor(ELEMENTS[symbol].color)], count: 12, speed: 0.7, size: 0.018, life: 0.4 });
    refreshPanels();

    const matches = Object.keys(ELEMENTS).every((s) => (r.counts[s] ?? 0) === (currentCounts()[s] ?? 0));
    if (matches) {
      completeMolecule(r);
    } else {
      setFeedback([{ text: `Added ${ELEMENTS[symbol].name}`, size: 28 }]);
    }
  }

  function buildMoleculeModel(r) {
    const model = new THREE.Group();
    const centerEl = ELEMENTS[r.center];
    const center = new THREE.Mesh(
      new THREE.SphereGeometry(centerEl.radius, 24, 18),
      new THREE.MeshStandardMaterial({ color: centerEl.color, roughness: 0.35 })
    );
    model.add(center);

    const bondMaterial = new THREE.MeshStandardMaterial({ color: 0xaab4cc, roughness: 0.5 });
    const bondLength = 0.18;
    for (const sat of r.satellites) {
      const el = ELEMENTS[sat.el];
      const dir = new THREE.Vector3(...sat.dir).normalize();

      const atom = new THREE.Mesh(
        new THREE.SphereGeometry(el.radius, 24, 18),
        new THREE.MeshStandardMaterial({ color: el.color, roughness: 0.35 })
      );
      atom.position.copy(dir).multiplyScalar(bondLength);
      model.add(atom);

      const bond = new THREE.Mesh(new THREE.CylinderGeometry(0.012, 0.012, bondLength, 10), bondMaterial);
      bond.position.copy(dir).multiplyScalar(bondLength / 2);
      bond.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
      model.add(bond);
    }
    return model;
  }

  function completeMolecule(r) {
    locked = true;
    zoneAtoms.forEach((a) => { assembly.remove(a.mesh); a.mesh.geometry.dispose(); a.mesh.material.dispose(); });
    zoneAtoms.length = 0;

    const model = buildMoleculeModel(r);
    model.scale.setScalar(0.001);
    assembly.add(model);
    popAnim = { model, t: 0 };
    solved += 1;
    setFeedback([
      { text: `You built ${r.formula}!`, bold: true, size: 34, color: "#34d399" },
      { text: r.fact, size: 22 }
    ]);
    refreshPanels();

    const colors = [...new Set(Object.keys(r.counts))].map((s) => hexColor(ELEMENTS[s].color));
    spawnShockwave(zone, { position: new THREE.Vector3(0, 0, 0), color: "#22d3ee", radius: 0.7 });
    spawnBurst(zone, { position: new THREE.Vector3(0, 0.05, 0), colors, count: 40, speed: 2, life: 0.8 });

    later(3200, () => {
      assembly.remove(model);
      disposeTree(model);
      recipeIndex += 1;
      locked = false;
      refreshPanels();
      setFeedback([{ text: `Next up: ${recipe().formula}`, size: 28 }]);
    });
  }

  refreshPanels();
  setFeedback([
    { text: "Grab atoms from the pedestals", size: 26 },
    { text: "and place them in the ring", size: 26, color: "#8fa3c8" }
  ]);

  let elapsed = 0;

  return {
    group,
    update(delta) {
      elapsed += delta;
      zoneRing.rotation.z += delta * 0.4;
      if (locked) assembly.rotation.y += delta * 0.8;
      else assembly.rotation.y = 0;

      if (popAnim) {
        popAnim.t += delta * 2.4;
        popAnim.model.scale.setScalar(Math.max(0.001, easeOutBack(Math.min(popAnim.t, 1))));
        if (popAnim.t >= 1) popAnim = null;
      }
    },
    dispose() {
      timers.forEach(clearTimeout);
      for (const [, atom] of pedestalAtoms) grab.remove(atom);
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "chemistry",
  title: "Snap-Together Molecules",
  tagline: "Build it atom by atom, with your hands",
  howTo: "The board shows a target molecule. Grab atoms from the pedestals and place them into the glowing ring — get the exact mix and watch it snap into the real bonded shape.",
  topics: TOPICS
};
