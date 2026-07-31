import * as THREE from "three";
import { createTextPanel, createButton3D, disposeTree } from "../../core/textPanel.js";
import { spawnBurst, spawnShockwave, createTrail } from "../../core/effects.js";

const THROW_BOOST = 1.7; // amplifies tracked hand speed so a natural toss covers game-scale distance
const MIN_THROW_SPEED = 0.5; // below this, a "release" just drops the ball rather than counting as a throw

// Each "topic" is really a different gravity — same throw, different planet,
// so players can feel g change the range formula's result directly.
const TOPICS = [
  { id: "earth", label: "Earth Gravity", g: 9.81 },
  { id: "moon", label: "Moon Gravity", g: 1.62 },
  { id: "mars", label: "Mars Gravity", g: 3.71 }
];

// Difficulty scales how far/small the target is and how forgiving a "hit" is.
const DIFFICULTY = {
  easy: { minDist: 1.0, maxDist: 2.0, ringScale: 1.35, tolerance: 1.35 },
  medium: { minDist: 1.6, maxDist: 3.2, ringScale: 1.0, tolerance: 1.0 },
  hard: { minDist: 2.4, maxDist: 4.4, ringScale: 0.7, tolerance: 0.75 }
};

// The quest: five levels of the chosen gravity, target shrinking and moving
// farther each round, then a Boss target that drifts side to side and takes
// two hits to bring down.
const LEVEL_COUNT = 5;
const LEVEL_SCALE = [1.2, 1.0, 0.85, 0.72, 0.6]; // multiplies diff.ringScale per level
const LEVEL_DIST_MULT = [0.75, 0.85, 1.0, 1.1, 1.25]; // multiplies the distance range per level
const MAX_LIVES = 3;
const BOSS_SCALE = 0.5;
const BOSS_HITS_REQUIRED = 2;
const BOSS_DRIFT_SPEED = 0.6;
const BOSS_DRIFT_RANGE = 0.9;
const BEST_SCORE_PREFIX = "ale.physicsGame.best";

function loadBest(topic, difficulty) {
  try { return Number(sessionStorage.getItem(`${BEST_SCORE_PREFIX}.${topic}.${difficulty}`)) || 0; }
  catch { return 0; }
}
function saveBest(topic, difficulty, value) {
  try { sessionStorage.setItem(`${BEST_SCORE_PREFIX}.${topic}.${difficulty}`, String(value)); } catch { /* ignore */ }
}

/**
 * Physics — "Throw the Ball". Grab the ball from its stand and actually
 * throw it — the release velocity from your real hand/controller motion
 * becomes the launch velocity, gravity does the rest. Five levels shrink
 * and push the target farther each round, then a drifting Boss target
 * takes two hits to beat. Three lives — a miss costs one.
 */
export function createGame({ interaction, grab, config = {} }) {
  const group = new THREE.Group();
  group.name = "physicsGame";

  const topicMeta = TOPICS.find((t) => t.id === config.topic) ?? TOPICS[0];
  const GRAVITY = topicMeta.g;
  const topic = config.topic ?? topicMeta.id;
  const difficulty = config.difficulty ?? "easy";
  const diff = DIFFICULTY[difficulty] ?? DIFFICULTY.easy;
  const bestScore = loadBest(topic, difficulty);

  let level = 1;
  let lives = MAX_LIVES;
  let hits = 0;
  let attempts = 0;
  let isBoss = false;
  let bossHits = 0;
  let runOver = false;
  let flying = false;
  const timers = new Set();
  const velocity = new THREE.Vector3();
  const worldPos = new THREE.Vector3();
  let victoryButton = null;
  let elapsed = 0;

  // --- Ball stand (grab point) ---------------------------------------------
  const stand = new THREE.Group();
  stand.position.set(0, 0.95, -0.35);
  group.add(stand);

  const pedestal = new THREE.Mesh(
    new THREE.CylinderGeometry(0.05, 0.07, 0.14, 16),
    new THREE.MeshStandardMaterial({ color: 0x232b40, roughness: 0.7 })
  );
  pedestal.position.y = -0.07;
  stand.add(pedestal);

  const ball = new THREE.Mesh(
    new THREE.SphereGeometry(0.06, 24, 18),
    new THREE.MeshStandardMaterial({ color: 0xfbbf24, emissive: 0xfbbf24, emissiveIntensity: 0.3 })
  );
  stand.add(ball);
  let ballHome = true; // sitting on the stand, as opposed to flying loose in `scene`

  const trail = createTrail(group, { color: 0xfbbf24 });

  grab.add(ball, {
    onGrab: () => {
      if (runOver) return;
      ball.material.emissiveIntensity = 0.8;
      ball.getWorldPosition(worldPos);
      const localPos = group.worldToLocal(worldPos.clone());
      spawnBurst(group, { position: localPos, colors: ["#fbbf24"], count: 8, speed: 0.6, life: 0.3, size: 0.02 });
    },
    onRelease: (obj, releaseVelocity) => throwBall(releaseVelocity),
    onHoverStart: () => { if (ballHome) ball.scale.setScalar(1.25); },
    onHoverEnd: () => ball.scale.setScalar(1)
  });

  // --- Target ring on the ground ---------------------------------------------
  const target = new THREE.Group();
  const rings = [
    { r: 0.42, color: 0xf87171 },
    { r: 0.28, color: 0xfbbf24 },
    { r: 0.14, color: 0x34d399 }
  ].map(({ r, color }) => {
    const ring = new THREE.Mesh(
      new THREE.CircleGeometry(r, 40),
      new THREE.MeshStandardMaterial({ color, roughness: 0.8, side: THREE.DoubleSide })
    );
    ring.rotation.x = -Math.PI / 2;
    target.add(ring);
    return ring;
  });
  rings.forEach((ring, i) => { ring.position.y = 0.004 + i * 0.002; });
  group.add(target);

  let targetDistance = 2;
  let bossBaseX = 0;

  function currentRingScale() {
    return isBoss ? diff.ringScale * BOSS_SCALE : diff.ringScale * LEVEL_SCALE[level - 1];
  }

  const NORMAL_RING_COLORS = [0xf87171, 0xfbbf24, 0x34d399];
  const BOSS_RING_COLORS = [0xf472b6, 0xffffff, 0xf472b6];

  function placeTarget() {
    target.scale.setScalar(currentRingScale());
    const distMult = isBoss ? LEVEL_DIST_MULT[LEVEL_DIST_MULT.length - 1] : LEVEL_DIST_MULT[level - 1];
    targetDistance = (diff.minDist + Math.random() * (diff.maxDist - diff.minDist)) * distMult;
    bossBaseX = (Math.random() - 0.5) * 1.2;
    target.position.set(bossBaseX, 0, stand.position.z - targetDistance);
    const palette = isBoss ? BOSS_RING_COLORS : NORMAL_RING_COLORS;
    rings.forEach((ring, i) => ring.material.color.setHex(palette[i]));
  }

  // --- UI panels ------------------------------------------------------------
  const readout = createTextPanel({ width: 1.5, height: 0.56, fontSize: 36 });
  readout.position.set(0, 2.0, -2.0);
  group.add(readout);

  const hudPanel = createTextPanel({ width: 0.95, height: 0.42, fontSize: 26, border: "rgba(167, 139, 250, 0.8)" });
  hudPanel.position.set(-1.35, 1.55, -1.9);
  hudPanel.rotation.y = 0.4;
  group.add(hudPanel);

  const feedbackPanel = createTextPanel({ width: 1.2, height: 0.38, fontSize: 30, border: "rgba(52, 211, 153, 0.8)" });
  feedbackPanel.position.set(1.35, 1.55, -1.9);
  feedbackPanel.rotation.y = -0.4;
  group.add(feedbackPanel);

  function heartsText() {
    return "♥".repeat(Math.max(lives, 0)) + "♡".repeat(MAX_LIVES - Math.max(lives, 0));
  }

  function updateHud() {
    const levelLabel = isBoss ? `BOSS · ${bossHits}/${BOSS_HITS_REQUIRED} hits` : `Level ${level} / ${LEVEL_COUNT}`;
    hudPanel.userData.setText([
      { text: topicMeta.label, bold: true, size: 28, color: isBoss ? "#f472b6" : "#a78bfa" },
      { text: `g = ${GRAVITY.toFixed(2)} m/s²`, size: 22 },
      { text: levelLabel, size: 24, color: isBoss ? "#f472b6" : "#8fa3c8" },
      { text: heartsText(), size: 26, color: "#f87171" }
    ]);
  }

  function updateReadout(throwInfo) {
    if (!throwInfo) {
      readout.userData.setText([
        { text: "Grab the ball and throw it!", bold: true, size: 38 },
        { text: `Target: ${targetDistance.toFixed(1)}m away   ·   Hits ${hits}   Best ${bestScore}`, size: 26, color: "#8fa3c8" }
      ]);
      return;
    }
    readout.userData.setText([
      { text: `Your throw: θ ${throwInfo.angle.toFixed(0)}°   v ${throwInfo.speed.toFixed(1)} m/s`, bold: true, size: 34 },
      { text: `Predicted range: ${throwInfo.predictedRange.toFixed(1)} m`, size: 32, color: "#22d3ee" },
      { text: `Hits ${hits}   Best ${bestScore}`, size: 24, color: "#8fa3c8" }
    ]);
  }

  function setFeedback(lines) {
    feedbackPanel.userData.setText(lines);
  }

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
  }

  function throwBall(releaseVelocity) {
    if (runOver) return;
    const speed = releaseVelocity.length();

    if (speed < MIN_THROW_SPEED) {
      // Too gentle to call a throw — just let it drop from wherever it was released.
      ball.getWorldPosition(worldPos);
      group.worldToLocal(worldPos);
      launch(worldPos, releaseVelocity.clone());
      return;
    }

    ball.getWorldPosition(worldPos);
    group.worldToLocal(worldPos);
    launch(worldPos, releaseVelocity.clone().multiplyScalar(THROW_BOOST));
  }

  function launch(startLocalPos, startVelocity) {
    attempts += 1;
    ballHome = false;
    // Re-parent from the stand to the game group so its position/physics
    // are expressed in the same frame as the target for the rest of the flight.
    group.add(ball);
    ball.position.copy(startLocalPos);
    velocity.copy(startVelocity);
    flying = true;
    trail.reset();

    const horizontalSpeed = Math.hypot(startVelocity.x, startVelocity.z);
    const angle = THREE.MathUtils.radToDeg(Math.atan2(startVelocity.y, horizontalSpeed));
    const speed = startVelocity.length();
    const predictedRange = angle > 0 ? (speed * speed * Math.sin(THREE.MathUtils.degToRad(angle * 2))) / GRAVITY : 0;
    updateReadout({ angle, speed, predictedRange });
    setFeedback([{ text: "Ball away…", size: 32 }]);
  }

  function loseLife(missBy) {
    lives -= 1;
    updateHud();
    setFeedback([
      { text: `Missed by ${missBy.toFixed(1)} m`, size: 30, color: "#f87171" },
      { text: lives > 0 ? "Try a different angle or power" : "Out of hearts!", size: 24, color: "#8fa3c8" }
    ]);
    if (lives <= 0) {
      later(600, showDefeat);
    } else {
      later(1300, resetBall);
    }
  }

  function land() {
    flying = false;
    trail.reset();
    // Quick squash-and-recover on impact instead of just stopping dead.
    ball.scale.set(1.5, 0.5, 1.5);
    later(120, () => ball.scale.setScalar(1));

    const dx = ball.position.x - target.position.x;
    const dz = ball.position.z - target.position.z;
    const missBy = Math.sqrt(dx * dx + dz * dz);
    const landingSpot = ball.position.clone();
    landingSpot.y = 0.006;

    const scaleFactor = isBoss ? BOSS_SCALE : LEVEL_SCALE[level - 1];
    const hitRadius = 0.5 * diff.tolerance * scaleFactor;
    if (missBy <= hitRadius) {
      hits += 1;
      const bullseye = missBy <= 0.18 * diff.tolerance * scaleFactor;
      const quality = bullseye ? "BULLSEYE! 🎯" : missBy <= 0.35 * diff.tolerance * scaleFactor ? "Great throw!" : "Hit!";
      spawnShockwave(group, { position: landingSpot, color: "#34d399", radius: bullseye ? 0.9 : 0.6 });
      spawnBurst(group, {
        position: landingSpot,
        colors: bullseye ? ["#34d399", "#fbbf24", "#22d3ee"] : ["#34d399", "#8fa3c8"],
        count: bullseye ? 44 : 26,
        speed: bullseye ? 2.2 : 1.6,
        life: 0.7
      });

      if (isBoss) {
        bossHits += 1;
        updateHud();
        if (bossHits >= BOSS_HITS_REQUIRED) {
          setFeedback([{ text: `${quality} Boss down!`, bold: true, size: 34, color: "#34d399" }]);
          later(700, showVictory);
          return;
        }
        setFeedback([{ text: `${quality} One more hit!`, bold: true, size: 32, color: "#f472b6" }]);
        later(1000, () => { placeTarget(); resetBall(); });
      } else if (level >= LEVEL_COUNT) {
        isBoss = true;
        bossHits = 0;
        setFeedback([{ text: "BOSS TARGET — it's on the move!", bold: true, size: 30, color: "#f472b6" }]);
        updateHud();
        later(1000, () => { placeTarget(); resetBall(); });
      } else {
        level += 1;
        setFeedback([{ text: `${quality} Level ${level} of ${LEVEL_COUNT}!`, bold: true, size: 32, color: "#34d399" }]);
        updateHud();
        later(1000, () => { placeTarget(); resetBall(); });
      }
    } else {
      spawnBurst(group, { position: landingSpot, colors: ["#5b6478"], count: 10, speed: 0.7, size: 0.02, life: 0.4 });
      loseLife(missBy);
    }
  }

  function resetBall() {
    ball.visible = false;
    later(150, () => {
      ballHome = true;
      ball.position.set(0, 0, 0); // local to `stand`
      ball.scale.setScalar(1);
      ball.material.emissiveIntensity = 0.3;
      ball.visible = true;
      // Re-parent back onto the stand (launch() moved it into `group` world space).
      stand.add(ball);
      updateReadout(null);
    });
  }

  function showBanner(lines) {
    readout.userData.setText(lines);
  }

  function showDefeat() {
    runOver = true;
    target.visible = false;
    ball.visible = false;
    showBanner([
      { text: "Out of hearts", bold: true, size: 44, color: "#f87171" },
      { text: `You landed ${hits} hits — try again?`, size: 26, color: "#8fa3c8" }
    ]);
    setFeedback([{ text: "", size: 1 }]);
    showReplayButton("Retry ↻", 0xf87171);
  }

  function showVictory() {
    runOver = true;
    target.visible = false;
    ball.visible = false;
    const stars = lives >= 3 ? "★★★" : lives === 2 ? "★★☆" : "★☆☆";
    const newBest = hits > bestScore;
    if (newBest) saveBest(topic, difficulty, hits);

    showBanner([
      { text: "QUEST COMPLETE! 🎉", bold: true, size: 40, color: "#34d399" },
      { text: stars, size: 36, color: "#fbbf24" },
      { text: `${hits} hits${newBest ? "  — New best!" : `   Best ${Math.max(hits, bestScore)}`}`, size: 22, color: "#8fa3c8" }
    ]);
    setFeedback([{ text: "", size: 1 }]);
    spawnShockwave(group, { position: new THREE.Vector3(0, 0.05, -1), color: "#fbbf24", radius: 1.2 });
    showReplayButton("Play Again ▶", 0x34d399);
  }

  function showReplayButton(label, accent) {
    victoryButton = createButton3D(label, { width: 0.5, height: 0.17, accent: `#${accent.toString(16).padStart(6, "0")}`, fontSize: 42 });
    victoryButton.position.set(0, 1.1, -0.6);
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
    level = 1;
    lives = MAX_LIVES;
    hits = 0;
    attempts = 0;
    isBoss = false;
    bossHits = 0;
    runOver = false;
    target.visible = true;
    updateHud();
    placeTarget();
    resetBall();
    setFeedback([
      { text: "Land it on the rings!", size: 30 },
      { text: "Wind up and let go to throw", size: 24, color: "#8fa3c8" }
    ]);
  }

  placeTarget();
  updateHud();
  updateReadout(null);
  setFeedback([
    { text: "Land it on the rings!", size: 30 },
    { text: "Wind up and let go to throw", size: 24, color: "#8fa3c8" }
  ]);

  return {
    group,
    update(delta) {
      elapsed += delta;
      target.rotation.y += delta * 0.15; // idle motion so the target doesn't feel static
      if (isBoss && !runOver) {
        target.position.x = bossBaseX + Math.sin(elapsed * BOSS_DRIFT_SPEED) * BOSS_DRIFT_RANGE;
      }

      if (!flying) return;

      // The ball flies in `group` local space (group never moves relative to
      // the rig, so this is equivalent to world space for physics purposes,
      // and keeps target/ball comparisons in the same coordinate frame).
      velocity.y -= GRAVITY * delta;
      ball.position.addScaledVector(velocity, delta);
      trail.sample(ball.position);

      if (ball.position.y <= 0.06 && velocity.y < 0) {
        ball.position.y = 0.06;
        land();
      }
    },
    dispose() {
      timers.forEach(clearTimeout);
      trail.dispose();
      grab.remove(ball);
      clearReplayButton();
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "physics",
  title: "Throw the Ball",
  tagline: "Feel gravity in your own hands",
  howTo: "Five levels shrink the target and push it farther each round, then a drifting Boss target takes two hits to beat. Three hearts — a miss costs one. Your throw's real angle and speed drive the same range formula real projectiles follow.",
  topics: TOPICS
};
