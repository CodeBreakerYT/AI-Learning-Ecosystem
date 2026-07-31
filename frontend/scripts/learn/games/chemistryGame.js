import * as THREE from "three";
import { createTextPanel, createLabel, createButton3D, disposeTree } from "../../core/textPanel.js";
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
// only tier that ever draws the 5-atom CH4 as a regular level) and tightens
// the drop-zone tolerance so placement itself gets less forgiving too.
const DIFFICULTY = {
  easy: { maxAtoms: 3, zoneRadius: 0.28 },
  medium: { maxAtoms: 4, zoneRadius: 0.22 },
  hard: { maxAtoms: 99, zoneRadius: 0.16 }
};

const MAX_LIVES = 3;
const BOSS_ZONE_SCALE = 0.75; // the mystery molecule's drop zone is tighter than a normal level's
const RESPAWN_DELAY = 500;
const BEST_SCORE_PREFIX = "ale.chemistryGame.best";
const GRAVITY = 9.81;
const THROW_BOOST = 1.5; // amplifies tracked hand speed so a natural toss carries to the zone
const MIN_THROW_SPEED = 0.4; // below this, a "release" just drops the atom rather than throwing it
const CAPTURE_HEIGHT_TOLERANCE = 0.12; // how far above the zone's own height still counts as "arrived"
const FLOOR_Y = 0.04; // local-space floor — an atom that reaches this without being captured is lost

function totalAtoms(recipe) {
  return Object.values(recipe.counts).reduce((sum, n) => sum + n, 0);
}

function recipePool(topic, difficulty) {
  const diff = DIFFICULTY[difficulty] ?? DIFFICULTY.easy;
  const all = RECIPES.filter((r) => r.topic === topic);
  const filtered = all.filter((r) => totalAtoms(r) <= diff.maxAtoms);
  return filtered.length ? filtered : all;
}

// The Boss Round's "mystery molecule" is the biggest recipe in the whole
// topic (unfiltered by difficulty) — the one regular levels at this
// difficulty never draw, built in a tighter drop zone.
function bossRecipe(topic) {
  const all = RECIPES.filter((r) => r.topic === topic);
  return all.reduce((biggest, r) => (totalAtoms(r) > totalAtoms(biggest) ? r : biggest), all[0]);
}

function loadBest(topic, difficulty) {
  try { return Number(sessionStorage.getItem(`${BEST_SCORE_PREFIX}.${topic}.${difficulty}`)) || 0; }
  catch { return 0; }
}
function saveBest(topic, difficulty, value) {
  try { sessionStorage.setItem(`${BEST_SCORE_PREFIX}.${topic}.${difficulty}`, String(value)); } catch { /* ignore */ }
}

/**
 * Chemistry — "Snap-Together Molecules". An alchemist's order list: brew
 * every molecule in the chosen topic's pool by grabbing atoms from the
 * pedestals and placing them in the glowing assembly zone, then a Boss
 * Round asks for the topic's biggest "mystery molecule" in a tighter drop
 * zone. Three lives — a missed placement or an overshot element costs one.
 */
export function createGame({ interaction, grab, config = {} }) {
  const group = new THREE.Group();
  group.name = "chemistryGame";

  const topic = config.topic ?? "diatomic";
  const difficulty = config.difficulty ?? "easy";
  const pool = recipePool(topic, difficulty);
  const boss = bossRecipe(topic);
  const baseZoneRadius = (DIFFICULTY[difficulty] ?? DIFFICULTY.easy).zoneRadius;
  const bestScore = loadBest(topic, difficulty);

  let levelIndex = 0; // index into `pool` — the current non-boss level
  let isBoss = false;
  let lives = MAX_LIVES;
  let solved = 0;
  let runOver = false;
  let locked = false;
  let popAnim = null; // { model, t } — elastic pop-in scale animation for a freshly completed molecule
  const timers = new Set();
  const zoneAtoms = []; // { symbol, mesh }
  const pedestalAtoms = new Map(); // symbol -> currently-offered grabbable mesh
  const flyingAtoms = new Set(); // atoms currently airborne (thrown, not yet captured/landed)
  const worldPos = new THREE.Vector3();
  let victoryButton = null;

  // --- Panels ---------------------------------------------------------------
  const targetPanel = createTextPanel({ width: 1.55, height: 0.56, fontSize: 40 });
  targetPanel.position.set(0, 2.0, -2.0);
  group.add(targetPanel);

  const benchPanel = createTextPanel({ width: 1.05, height: 0.42, fontSize: 26, border: "rgba(34, 211, 238, 0.8)" });
  benchPanel.position.set(1.35, 1.6, -1.9);
  benchPanel.rotation.y = -0.4;
  group.add(benchPanel);

  const feedbackPanel = createTextPanel({ width: 1.1, height: 0.34, fontSize: 28, border: "rgba(167, 139, 250, 0.8)" });
  feedbackPanel.position.set(-1.35, 1.6, -1.9);
  feedbackPanel.rotation.y = 0.4;
  group.add(feedbackPanel);

  // --- Assembly zone (where placed atoms bond) --------------------------------
  const zone = new THREE.Group();
  // Height/depth tuned so the zone sits inside the default forward camera
  // view (no built-in pitch control on desktop) as well as comfortable VR
  // reach — a chest-height object this close to the camera falls well
  // outside a 70°-FOV frustum if placed at true waist height.
  zone.position.set(0, 1.26, -0.65);
  group.add(zone);

  const zoneRing = new THREE.Mesh(
    new THREE.TorusGeometry(baseZoneRadius, 0.015, 12, 48),
    new THREE.MeshStandardMaterial({ color: 0x22d3ee, emissive: 0x22d3ee, emissiveIntensity: 0.4 })
  );
  zoneRing.rotation.x = Math.PI / 2;
  zone.add(zoneRing);

  const assembly = new THREE.Group(); // holds either loose zoneAtoms or the final bonded model
  zone.add(assembly);

  function zoneRadius() {
    return isBoss ? baseZoneRadius * BOSS_ZONE_SCALE : baseZoneRadius;
  }

  function flashZone(color) {
    zoneRing.material.color.setHex(color);
    zoneRing.material.emissiveIntensity = 1.2;
    later(400, () => {
      zoneRing.material.color.setHex(isBoss ? 0xf472b6 : 0x22d3ee);
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
    // Height tuned so the atom on top (local +0.32) sits inside the default
    // forward camera view (no built-in pitch control on desktop) as well as
    // comfortable VR reach — a chest-height grab point this close to the
    // camera falls well outside a 70°-FOV frustum if placed at true waist height.
    stand.position.set(-0.24 + i * 0.16, 1.05, -0.4);
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
        if (runOver) return;
        if (flyingAtoms.has(atom)) {
          // Caught mid-throw — stop its physics and let grabSystem's
          // followHand take over. A replacement was already scheduled when
          // it first left the pedestal, so don't schedule a second one.
          flyingAtoms.delete(atom);
        } else {
          pedestalAtoms.delete(symbol + stand.uuid);
          later(RESPAWN_DELAY, () => spawnPedestalAtom(symbol, stand));
        }
        atom.material.emissiveIntensity = 0.7;
        atom.getWorldPosition(worldPos);
        spawnBurst(group, {
          position: group.worldToLocal(worldPos.clone()),
          colors: [hexColor(el.color)], count: 8, speed: 0.5, size: 0.015, life: 0.3
        });
      },
      onRelease: (obj, releaseVelocity) => throwAtom(symbol, atom, releaseVelocity),
      onHoverStart: () => atom.scale.setScalar(1.25),
      onHoverEnd: () => atom.scale.setScalar(1)
    });
  }

  symbols.forEach((symbol, i) => spawnPedestalAtom(symbol, pedestals[i]));

  // --- Helpers ----------------------------------------------------------------
  const recipe = () => (isBoss ? boss : pool[levelIndex]);

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
    return id;
  }

  function currentCounts() {
    const counts = {};
    for (const a of zoneAtoms) counts[a.symbol] = (counts[a.symbol] ?? 0) + 1;
    return counts;
  }

  function heartsText() {
    return "♥".repeat(Math.max(lives, 0)) + "♡".repeat(MAX_LIVES - Math.max(lives, 0));
  }

  function refreshPanels() {
    const r = recipe();
    const need = Object.entries(r.counts).map(([s, n]) => `${n} × ${ELEMENTS[s].name}`).join("  +  ");
    const counts = currentCounts();
    const have = Object.entries(r.counts)
      .map(([s, n]) => `${s}: ${counts[s] ?? 0}/${n}`)
      .join("   ");
    const levelLabel = isBoss ? "MYSTERY MOLECULE" : `Order ${levelIndex + 1} / ${pool.length}`;
    targetPanel.userData.setText([
      { text: isBoss ? "Boss: brew it from the formula alone!" : `Build: ${r.formula} — ${r.name}`, bold: true, size: isBoss ? 32 : 40, color: isBoss ? "#f472b6" : "#e8ecf6" },
      { text: isBoss ? r.formula : need, size: 26, color: "#8fa3c8" },
      { text: levelLabel, size: 22, color: isBoss ? "#f472b6" : "#34d399" }
    ]);
    benchPanel.userData.setText([
      { text: "In the zone", size: 22, color: "#8fa3c8" },
      { text: have || "empty", bold: true, size: 28, color: "#22d3ee" },
      { text: `${heartsText()}   Score ${solved}   Best ${bestScore}`, size: 18, color: "#f87171" }
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

  function discardAtom(atom) {
    // grab.add() was called once when this atom was offered on its pedestal
    // — without removing it here too, every thrown atom leaves a stale,
    // disposed-but-still-registered entry in the shared grab system for the
    // rest of the session (it'd keep computing hover distance against it
    // every frame even though it's no longer in the scene at all).
    grab.remove(atom);
    flyingAtoms.delete(atom);
    atom.geometry.dispose();
    atom.material.dispose();
    atom.parent?.remove(atom);
  }

  function loseLife(lines) {
    lives -= 1;
    refreshPanels();
    if (lives <= 0) {
      setFeedback(lines);
      later(600, showDefeat);
    } else {
      setFeedback(lines);
    }
  }

  // Launches a released atom as a real gravity-affected projectile instead
  // of resolving it instantly — resolveAtomPlacement() only fires once its
  // flight path actually carries it into the zone (checked every frame in
  // update()), and a throw that never reaches the zone just falls and is lost.
  function throwAtom(symbol, atom, releaseVelocity) {
    if (locked || runOver) {
      discardAtom(atom);
      return;
    }
    atom.getWorldPosition(worldPos);
    const localPos = group.worldToLocal(worldPos.clone());
    group.add(atom); // re-parent from the pedestal stand into the game's shared physics space
    atom.position.copy(localPos);

    const speed = releaseVelocity.length();
    atom.userData.symbol = symbol;
    atom.userData.velocity = releaseVelocity.clone().multiplyScalar(speed < MIN_THROW_SPEED ? 1 : THROW_BOOST);
    flyingAtoms.add(atom);
  }

  function resolveAtomPlacement(symbol, atom) {
    const r = recipe();
    const wouldBe = (currentCounts()[symbol] ?? 0) + 1;
    if (wouldBe > (r.counts[symbol] ?? 0)) {
      flashZone(0xf87171);
      spawnBurst(zone, { position: zone.worldToLocal(atom.getWorldPosition(new THREE.Vector3())), colors: ["#f87171"], count: 10, speed: 0.8, size: 0.018, life: 0.35 });
      discardAtom(atom);
      loseLife([
        { text: `Too much ${ELEMENTS[symbol].name}!`, bold: true, size: 30, color: "#f87171" },
        { text: `${r.formula} only needs ${r.counts[symbol] ?? 0}`, size: 22, color: "#8fa3c8" }
      ]);
      return;
    }

    assembly.attach(atom); // attach() (not add()) preserves the atom's current world position across the reparent
    atom.material.emissiveIntensity = 0.2;
    zoneAtoms.push({ symbol, mesh: atom });
    layoutZoneAtoms();
    flashZone(isBoss ? 0xf472b6 : 0x34d399);
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

    const colors = [...new Set(Object.keys(r.counts))].map((s) => hexColor(ELEMENTS[s].color));
    spawnShockwave(zone, { position: new THREE.Vector3(0, 0, 0), color: isBoss ? "#f472b6" : "#22d3ee", radius: isBoss ? 0.95 : 0.7 });
    spawnBurst(zone, { position: new THREE.Vector3(0, 0.05, 0), colors, count: isBoss ? 60 : 40, speed: isBoss ? 2.6 : 2, life: 0.8 });

    if (isBoss) {
      setFeedback([
        { text: `You brewed ${r.formula}!`, bold: true, size: 34, color: "#34d399" },
        { text: r.fact, size: 22 }
      ]);
      later(700, () => { assembly.remove(model); disposeTree(model); showVictory(); });
      return;
    }

    setFeedback([
      { text: `You built ${r.formula}!`, bold: true, size: 34, color: "#34d399" },
      { text: r.fact, size: 22 }
    ]);
    refreshPanels();

    later(3000, () => {
      assembly.remove(model);
      disposeTree(model);
      locked = false;

      if (levelIndex >= pool.length - 1) {
        isBoss = true;
        zoneRing.geometry.dispose();
        zoneRing.geometry = new THREE.TorusGeometry(zoneRadius(), 0.015, 12, 48);
        setFeedback([{ text: "MYSTERY MOLECULE — tighter zone, no hints!", bold: true, size: 28, color: "#f472b6" }]);
      } else {
        levelIndex += 1;
        setFeedback([{ text: `Next order: ${recipe().formula}`, size: 28 }]);
      }
      refreshPanels();
    });
  }

  function showBanner(lines) {
    targetPanel.userData.setText(lines);
    setFeedback([{ text: "", size: 1 }]);
  }

  function showDefeat() {
    runOver = true;
    showBanner([
      { text: "Out of hearts", bold: true, size: 44, color: "#f87171" },
      { text: `You brewed ${solved} molecules — try again?`, size: 24, color: "#8fa3c8" }
    ]);
    showReplayButton("Retry ↻", 0xf87171);
  }

  function showVictory() {
    runOver = true;
    const stars = lives >= 3 ? "★★★" : lives === 2 ? "★★☆" : "★☆☆";
    const newBest = solved > bestScore;
    if (newBest) saveBest(topic, difficulty, solved);

    showBanner([
      { text: "QUEST COMPLETE! 🎉", bold: true, size: 40, color: "#34d399" },
      { text: stars, size: 36, color: "#fbbf24" },
      { text: `${solved} molecules${newBest ? "  — New best!" : `   Best ${Math.max(solved, bestScore)}`}`, size: 22, color: "#8fa3c8" }
    ]);
    showReplayButton("Play Again ▶", 0x34d399);
  }

  function showReplayButton(label, accent) {
    victoryButton = createButton3D(label, { width: 0.5, height: 0.17, accent: `#${accent.toString(16).padStart(6, "0")}`, fontSize: 42 });
    victoryButton.position.set(0, 1.35, -0.55);
    group.add(victoryButton);
    interaction.add(victoryButton, {
      onSelect: resetRun,
      onHoverStart: victoryButton.userData.onHoverStart,
      onHoverEnd: victoryButton.userData.onHoverEnd
    });
  }

  function clearReplayButton() {
    if (!victoryButton) return;
    interaction.remove(victoryButton);
    group.remove(victoryButton);
    disposeTree(victoryButton);
    victoryButton = null;
  }

  function resetRun() {
    clearReplayButton();
    levelIndex = 0;
    isBoss = false;
    lives = MAX_LIVES;
    solved = 0;
    runOver = false;
    locked = false;
    zoneRing.geometry.dispose();
    zoneRing.geometry = new THREE.TorusGeometry(zoneRadius(), 0.015, 12, 48);
    setFeedback([
      { text: "Grab atoms from the pedestals", size: 26 },
      { text: "and place them in the ring", size: 26, color: "#8fa3c8" }
    ]);
    refreshPanels();
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

      for (const atom of [...flyingAtoms]) {
        if (runOver) { discardAtom(atom); continue; }
        const ud = atom.userData;
        ud.velocity.y -= GRAVITY * delta;
        atom.position.addScaledVector(ud.velocity, delta);
        atom.rotation.x += delta * 3;
        atom.rotation.y += delta * 2;

        const dx = atom.position.x - zone.position.x;
        const dz = atom.position.z - zone.position.z;
        const horizDist = Math.hypot(dx, dz);
        const arrived = atom.position.y <= zone.position.y + CAPTURE_HEIGHT_TOLERANCE;

        if (!locked && horizDist <= zoneRadius() && arrived) {
          flyingAtoms.delete(atom);
          resolveAtomPlacement(ud.symbol, atom);
          continue;
        }
        if (atom.position.y <= FLOOR_Y) {
          flyingAtoms.delete(atom);
          discardAtom(atom);
          if (!locked) loseLife([{ text: "Missed the zone!", bold: true, size: 30, color: "#f87171" }]);
        }
      }

      if (popAnim) {
        popAnim.t += delta * 2.4;
        popAnim.model.scale.setScalar(Math.max(0.001, easeOutBack(Math.min(popAnim.t, 1))));
        if (popAnim.t >= 1) popAnim = null;
      }
    },
    dispose() {
      timers.forEach(clearTimeout);
      for (const [, atom] of pedestalAtoms) grab.remove(atom);
      for (const atom of flyingAtoms) grab.remove(atom);
      flyingAtoms.clear();
      clearReplayButton();
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "chemistry",
  title: "Snap-Together Molecules",
  tagline: "Build it atom by atom, with your hands",
  howTo: "An alchemist's order list: brew every molecule in the topic, then a Boss Round asks for the topic's biggest 'mystery molecule' in a tighter drop zone. Three hearts — a miss or an overshot element costs one.",
  topics: TOPICS
};
