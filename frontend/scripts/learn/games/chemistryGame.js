import * as THREE from "three";
import { createTextPanel, createLabel, createButton3D, disposeTree } from "../../core/textPanel.js";

const ELEMENTS = {
  H: { name: "Hydrogen", color: 0xf5f7fa, radius: 0.09 },
  O: { name: "Oxygen", color: 0xf87171, radius: 0.13 },
  C: { name: "Carbon", color: 0x4b5563, radius: 0.12 },
  N: { name: "Nitrogen", color: 0x5b8cff, radius: 0.12 }
};

// Each recipe: required atom counts, a display formula, a fun fact, and the
// 3D arrangement (center atom + satellite directions) used to assemble the
// molecule model on success.
const RECIPES = [
  {
    formula: "H₂O", name: "Water", counts: { H: 2, O: 1 },
    fact: "Bent shape (104.5°) — that's why water is polar!",
    center: "O",
    satellites: [{ el: "H", dir: [0.8, 0.55, 0] }, { el: "H", dir: [-0.8, 0.55, 0] }]
  },
  {
    formula: "O₂", name: "Oxygen gas", counts: { O: 2 },
    fact: "The air you breathe is 21% O₂ — a double-bonded pair.",
    center: "O",
    satellites: [{ el: "O", dir: [1, 0, 0] }]
  },
  {
    formula: "CO₂", name: "Carbon dioxide", counts: { C: 1, O: 2 },
    fact: "Linear molecule — you exhale it with every breath.",
    center: "C",
    satellites: [{ el: "O", dir: [1, 0, 0] }, { el: "O", dir: [-1, 0, 0] }]
  },
  {
    formula: "NH₃", name: "Ammonia", counts: { N: 1, H: 3 },
    fact: "Pyramid shape — the lone pair pushes the H atoms down.",
    center: "N",
    satellites: [
      { el: "H", dir: [0.9, -0.45, 0] },
      { el: "H", dir: [-0.45, -0.45, 0.78] },
      { el: "H", dir: [-0.45, -0.45, -0.78] }
    ]
  },
  {
    formula: "CH₄", name: "Methane", counts: { C: 1, H: 4 },
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

/**
 * Chemistry — "Molecule Builder". Dispensers offer H, O, C and N atoms; grab
 * the right mix for the target molecule and press REACT. A correct recipe
 * assembles a ball-and-stick model with its real geometry plus a fact;
 * a wrong mix tells you what's off.
 */
export function createGame({ interaction }) {
  const group = new THREE.Group();
  group.name = "chemistryGame";

  let recipeIndex = 0;
  let collected = { H: 0, O: 0, C: 0, N: 0 };
  let solved = 0;
  let locked = false;
  const timers = new Set();
  const interactives = [];

  // --- Panels ------------------------------------------------------------
  const targetPanel = createTextPanel({ width: 1.6, height: 0.6, fontSize: 44 });
  targetPanel.position.set(0, 2.2, -1.3);
  group.add(targetPanel);

  const benchPanel = createTextPanel({ width: 1.15, height: 0.42, fontSize: 36, border: "rgba(34, 211, 238, 0.8)" });
  benchPanel.position.set(1.7, 2.15, -1.05);
  benchPanel.rotation.y = -0.38;
  group.add(benchPanel);

  const feedbackPanel = createTextPanel({ width: 1.3, height: 0.42, fontSize: 34, border: "rgba(167, 139, 250, 0.8)" });
  feedbackPanel.position.set(-1.7, 2.15, -1.05);
  feedbackPanel.rotation.y = 0.38;
  group.add(feedbackPanel);

  // --- Atom dispensers -----------------------------------------------------
  const dispenserRoot = new THREE.Group();
  dispenserRoot.position.set(0, 0, 0.2);
  group.add(dispenserRoot);

  Object.entries(ELEMENTS).forEach(([symbol, el], i) => {
    const stand = new THREE.Group();
    stand.position.set(-1.2 + i * 0.8, 0, 0);

    const pedestal = new THREE.Mesh(
      new THREE.CylinderGeometry(0.16, 0.2, 0.75, 20),
      new THREE.MeshStandardMaterial({ color: 0x232b40, roughness: 0.7 })
    );
    pedestal.position.y = 0.375;
    stand.add(pedestal);

    const atom = new THREE.Mesh(
      new THREE.SphereGeometry(el.radius * 1.6, 28, 20),
      new THREE.MeshStandardMaterial({ color: el.color, emissive: el.color, emissiveIntensity: 0.15, roughness: 0.35 })
    );
    atom.position.y = 1.05;
    stand.add(atom);

    const label = createLabel(symbol, { width: 0.3, height: 0.18, fontSize: 110 });
    label.position.set(0, 1.38, 0);
    stand.add(label);

    const nameLabel = createLabel(el.name, { width: 0.55, height: 0.12, fontSize: 52, bold: false });
    nameLabel.position.set(0, 0.82, 0.18);
    stand.add(nameLabel);

    dispenserRoot.add(stand);
    stand.userData.atom = atom;
    interaction.add(stand, {
      onSelect: () => addAtom(symbol),
      onHoverStart: () => !locked && atom.scale.setScalar(1.2),
      onHoverEnd: () => atom.scale.setScalar(1)
    });
    interactives.push(stand);
  });

  // --- Reaction zone --------------------------------------------------------
  const reactionZone = new THREE.Group();
  reactionZone.position.set(0, 1.45, -1.1);
  group.add(reactionZone);

  const zoneRing = new THREE.Mesh(
    new THREE.TorusGeometry(0.5, 0.015, 12, 48),
    new THREE.MeshStandardMaterial({ color: 0x22d3ee, emissive: 0x22d3ee, emissiveIntensity: 0.4 })
  );
  reactionZone.add(zoneRing);

  const collectedAtoms = new THREE.Group();
  reactionZone.add(collectedAtoms);

  let moleculeModel = null;

  // --- Buttons ------------------------------------------------------------
  function addButton(text, x, accent, onSelect, width = 0.45) {
    const btn = createButton3D(text, { width, height: 0.16, accent, fontSize: 46 });
    btn.position.set(x, 1.02, 0.85);
    btn.rotation.x = -0.25;
    group.add(btn);
    interaction.add(btn, {
      onSelect,
      onHoverStart: btn.userData.onHoverStart,
      onHoverEnd: btn.userData.onHoverEnd
    });
    interactives.push(btn);
  }
  addButton("REACT ⚗", -0.35, "#34d399", react, 0.55);
  addButton("CLEAR", 0.35, "#f87171", () => { if (!locked) resetBench("Bench cleared."); });

  // --- Helpers -------------------------------------------------------------
  const recipe = () => RECIPES[recipeIndex % RECIPES.length];

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
  }

  function countsText() {
    const parts = Object.entries(collected).filter(([, n]) => n > 0).map(([s, n]) => `${s}×${n}`);
    return parts.length ? parts.join("   ") : "empty";
  }

  function refreshPanels() {
    const r = recipe();
    const need = Object.entries(r.counts).map(([s, n]) => `${n} × ${ELEMENTS[s].name} (${s})`).join("  +  ");
    targetPanel.userData.setText([
      { text: `Build:  ${r.formula} — ${r.name}`, bold: true, size: 52 },
      { text: need, size: 32, color: "#8fa3c8" },
      { text: `Molecules made: ${solved}`, size: 28, color: "#34d399" }
    ]);
    benchPanel.userData.setText([
      { text: "On the bench", size: 28, color: "#8fa3c8" },
      { text: countsText(), bold: true, size: 40, color: "#22d3ee" }
    ]);
  }

  function setFeedback(lines) {
    feedbackPanel.userData.setText(lines);
  }

  function layoutCollected() {
    collectedAtoms.clear();
    const all = Object.entries(collected).flatMap(([s, n]) => Array(n).fill(s));
    all.forEach((symbol, i) => {
      const el = ELEMENTS[symbol];
      const mesh = new THREE.Mesh(
        new THREE.SphereGeometry(el.radius, 20, 14),
        new THREE.MeshStandardMaterial({ color: el.color, roughness: 0.4 })
      );
      const angle = (i / Math.max(all.length, 1)) * Math.PI * 2;
      mesh.position.set(Math.cos(angle) * 0.3, Math.sin(angle) * 0.3, 0);
      collectedAtoms.add(mesh);
    });
  }

  function addAtom(symbol) {
    if (locked) return;
    const total = Object.values(collected).reduce((a, b) => a + b, 0);
    if (total >= 8) {
      setFeedback([{ text: "Bench is full — REACT or CLEAR!", size: 30, color: "#fbbf24" }]);
      return;
    }
    collected[symbol] += 1;
    layoutCollected();
    refreshPanels();
    setFeedback([{ text: `Added ${ELEMENTS[symbol].name}`, size: 32 }]);
  }

  function resetBench(message) {
    collected = { H: 0, O: 0, C: 0, N: 0 };
    collectedAtoms.clear();
    if (moleculeModel) {
      reactionZone.remove(moleculeModel);
      disposeTree(moleculeModel);
      moleculeModel = null;
    }
    refreshPanels();
    if (message) setFeedback([{ text: message, size: 32 }]);
  }

  function buildMoleculeModel(r) {
    const model = new THREE.Group();
    const centerEl = ELEMENTS[r.center];
    const center = new THREE.Mesh(
      new THREE.SphereGeometry(centerEl.radius, 28, 20),
      new THREE.MeshStandardMaterial({ color: centerEl.color, roughness: 0.35 })
    );
    model.add(center);

    const bondMaterial = new THREE.MeshStandardMaterial({ color: 0xaab4cc, roughness: 0.5 });
    const bondLength = 0.34;
    for (const sat of r.satellites) {
      const el = ELEMENTS[sat.el];
      const dir = new THREE.Vector3(...sat.dir).normalize();

      const atom = new THREE.Mesh(
        new THREE.SphereGeometry(el.radius, 28, 20),
        new THREE.MeshStandardMaterial({ color: el.color, roughness: 0.35 })
      );
      atom.position.copy(dir).multiplyScalar(bondLength);
      model.add(atom);

      const bond = new THREE.Mesh(new THREE.CylinderGeometry(0.02, 0.02, bondLength, 10), bondMaterial);
      bond.position.copy(dir).multiplyScalar(bondLength / 2);
      bond.quaternion.setFromUnitVectors(new THREE.Vector3(0, 1, 0), dir);
      model.add(bond);
    }
    return model;
  }

  function react() {
    if (locked) return;
    const r = recipe();
    const matches = Object.keys(ELEMENTS).every((s) => (r.counts[s] ?? 0) === collected[s]);

    if (!matches) {
      const missing = Object.entries(r.counts)
        .filter(([s, n]) => collected[s] < n)
        .map(([s, n]) => `${n - collected[s]} more ${s}`);
      const extra = Object.entries(collected)
        .filter(([s, n]) => n > (r.counts[s] ?? 0))
        .map(([s]) => `too much ${s}`);
      setFeedback([
        { text: "No reaction!", bold: true, size: 36, color: "#f87171" },
        { text: [...missing, ...extra].join(", ") || "Check the recipe", size: 28, color: "#8fa3c8" }
      ]);
      return;
    }

    locked = true;
    collectedAtoms.clear();
    moleculeModel = buildMoleculeModel(r);
    reactionZone.add(moleculeModel);
    solved += 1;
    setFeedback([
      { text: `You made ${r.formula}!`, bold: true, size: 40, color: "#34d399" },
      { text: r.fact, size: 26 }
    ]);
    refreshPanels();

    later(3200, () => {
      recipeIndex += 1;
      locked = false;
      resetBench(`Next up: ${recipe().formula}`);
    });
  }

  refreshPanels();
  setFeedback([
    { text: "Tap atoms to add them", size: 30 },
    { text: "then press REACT ⚗", size: 30, color: "#8fa3c8" }
  ]);

  let elapsed = 0;

  return {
    group,
    update(delta) {
      elapsed += delta;
      zoneRing.rotation.z += delta * 0.3;
      if (moleculeModel) moleculeModel.rotation.y += delta * 0.8;
      collectedAtoms.rotation.z = Math.sin(elapsed * 0.8) * 0.15;
    },
    dispose() {
      timers.forEach(clearTimeout);
      interactives.forEach((obj) => interaction.remove(obj));
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "chemistry",
  title: "Molecule Builder",
  tagline: "Mix atoms, make molecules",
  howTo: "The board shows a target molecule. Select atoms from the pedestals to add them to the reaction ring, then press REACT. Get the recipe right and watch the molecule assemble in 3D."
};
