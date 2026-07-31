import * as THREE from "three";
import { createTextPanel, disposeTree } from "../../core/textPanel.js";
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

/**
 * Physics — "Throw the Ball". Grab the ball from its stand and actually
 * throw it — the release velocity from your real hand/controller motion
 * becomes the launch velocity, gravity does the rest. After it lands, the
 * board shows the real angle and speed YOUR throw produced, plugged into
 * the range formula, so the physics is something you did, not a slider
 * you nudged.
 */
export function createGame({ grab, config = {} }) {
  const group = new THREE.Group();
  group.name = "physicsGame";

  const topicMeta = TOPICS.find((t) => t.id === config.topic) ?? TOPICS[0];
  const GRAVITY = topicMeta.g;
  const diff = DIFFICULTY[config.difficulty] ?? DIFFICULTY.easy;

  let attempts = 0;
  let hits = 0;
  let flying = false;
  const timers = new Set();
  const velocity = new THREE.Vector3();
  const worldPos = new THREE.Vector3();

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
  target.scale.setScalar(diff.ringScale);
  group.add(target);

  let targetDistance = 2;
  function placeTarget() {
    targetDistance = diff.minDist + Math.random() * (diff.maxDist - diff.minDist);
    target.position.set((Math.random() - 0.5) * 1.2, 0, stand.position.z - targetDistance);
  }

  // --- UI panels ------------------------------------------------------------
  const readout = createTextPanel({ width: 1.5, height: 0.56, fontSize: 36 });
  readout.position.set(0, 2.0, -2.0);
  group.add(readout);

  const lessonPanel = createTextPanel({ width: 1.2, height: 0.44, fontSize: 26, border: "rgba(167, 139, 250, 0.8)" });
  lessonPanel.position.set(-1.35, 1.55, -1.9);
  lessonPanel.rotation.y = 0.4;
  lessonPanel.userData.setText([
    { text: topicMeta.label, bold: true, size: 32, color: "#a78bfa" },
    { text: `g = ${GRAVITY.toFixed(2)} m/s²`, size: 30 },
    { text: "Range = v² · sin(2θ) / g", size: 26 },
    { text: "45° carries farthest!", size: 22, color: "#8fa3c8" }
  ]);
  group.add(lessonPanel);

  const feedbackPanel = createTextPanel({ width: 1.2, height: 0.38, fontSize: 30, border: "rgba(52, 211, 153, 0.8)" });
  feedbackPanel.position.set(1.35, 1.55, -1.9);
  feedbackPanel.rotation.y = -0.4;
  group.add(feedbackPanel);

  function updateReadout(throwInfo) {
    if (!throwInfo) {
      readout.userData.setText([
        { text: "Grab the ball and throw it!", bold: true, size: 40 },
        { text: `Target: ${targetDistance.toFixed(1)}m away   ·   Hits ${hits}/${attempts}`, size: 30, color: "#8fa3c8" }
      ]);
      return;
    }
    readout.userData.setText([
      { text: `Your throw: θ ${throwInfo.angle.toFixed(0)}°   v ${throwInfo.speed.toFixed(1)} m/s`, bold: true, size: 34 },
      { text: `Predicted range: ${throwInfo.predictedRange.toFixed(1)} m`, size: 32, color: "#22d3ee" },
      { text: `Hits ${hits}/${attempts}`, size: 26, color: "#8fa3c8" }
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

    const hitRadius = 0.5 * diff.tolerance;
    if (missBy <= hitRadius) {
      hits += 1;
      const bullseye = missBy <= 0.18 * diff.tolerance;
      const quality = bullseye ? "BULLSEYE! 🎯" : missBy <= 0.35 * diff.tolerance ? "Great throw!" : "Hit!";
      setFeedback([{ text: quality, bold: true, size: 36, color: "#34d399" }]);
      spawnShockwave(group, { position: landingSpot, color: "#34d399", radius: bullseye ? 0.9 : 0.6 });
      spawnBurst(group, {
        position: landingSpot,
        colors: bullseye ? ["#34d399", "#fbbf24", "#22d3ee"] : ["#34d399", "#8fa3c8"],
        count: bullseye ? 44 : 26,
        speed: bullseye ? 2.2 : 1.6,
        life: 0.7
      });
      later(1300, () => { placeTarget(); resetBall(); });
    } else {
      setFeedback([
        { text: `Missed by ${missBy.toFixed(1)} m`, size: 30, color: "#f87171" },
        { text: "Try a different angle or power", size: 24, color: "#8fa3c8" }
      ]);
      spawnBurst(group, { position: landingSpot, colors: ["#5b6478"], count: 10, speed: 0.7, size: 0.02, life: 0.4 });
      later(1300, resetBall);
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

  placeTarget();
  updateReadout(null);
  setFeedback([
    { text: "Land it on the rings!", size: 30 },
    { text: "Wind up and let go to throw", size: 24, color: "#8fa3c8" }
  ]);

  return {
    group,
    update(delta) {
      target.rotation.y += delta * 0.15; // idle motion so the target doesn't feel static

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
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "physics",
  title: "Throw the Ball",
  tagline: "Feel gravity in your own hands",
  howTo: "Grab the ball off its stand and physically throw it at the target rings on the floor — your real throw's angle and speed drive the same range formula real projectiles follow.",
  topics: TOPICS
};
