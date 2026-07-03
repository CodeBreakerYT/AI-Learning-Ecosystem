import * as THREE from "three";
import { createTextPanel, createButton3D, disposeTree } from "../../core/textPanel.js";

const GRAVITY = 9.81;

/**
 * Physics — "Projectile Lab". A cannon fires a ball with real projectile
 * motion (constant gravity, no drag). Tune launch angle and power with 3D
 * buttons, then launch and try to land the ball on the target ring. The
 * readout shows the predicted range from R = v²·sin(2θ)/g so players connect
 * the formula with what they see.
 */
export function createGame({ interaction }) {
  const group = new THREE.Group();
  group.name = "physicsGame";

  let angleDeg = 45;
  let power = 8; // launch speed, m/s
  let attempts = 0;
  let hits = 0;
  const timers = new Set();

  // --- Cannon -------------------------------------------------------------
  const cannon = new THREE.Group();
  cannon.position.set(0, 0, 0.4);
  group.add(cannon);

  const base = new THREE.Mesh(
    new THREE.CylinderGeometry(0.28, 0.34, 0.22, 24),
    new THREE.MeshStandardMaterial({ color: 0x2a3350, roughness: 0.6 })
  );
  base.position.y = 0.11;
  cannon.add(base);

  const pivot = new THREE.Group();
  pivot.position.y = 0.26;
  cannon.add(pivot);

  const barrel = new THREE.Mesh(
    new THREE.CylinderGeometry(0.07, 0.09, 0.7, 20),
    new THREE.MeshStandardMaterial({ color: 0x5b8cff, roughness: 0.3, metalness: 0.4 })
  );
  barrel.position.y = 0.35; // pivot at barrel base
  pivot.add(barrel);

  function aimBarrel() {
    // 0° = flat toward -z, 90° = straight up
    pivot.rotation.x = -(Math.PI / 2) + THREE.MathUtils.degToRad(angleDeg);
  }

  // --- Target ring ----------------------------------------------------------
  const target = new THREE.Group();
  const rings = [
    { r: 0.72, color: 0xf87171 },
    { r: 0.48, color: 0xfbbf24 },
    { r: 0.24, color: 0x34d399 }
  ].map(({ r, color }) => {
    const ring = new THREE.Mesh(
      new THREE.CircleGeometry(r, 40),
      new THREE.MeshStandardMaterial({ color, roughness: 0.8, side: THREE.DoubleSide })
    );
    ring.rotation.x = -Math.PI / 2;
    target.add(ring);
    return ring;
  });
  rings.forEach((ring, i) => { ring.position.y = 0.005 + i * 0.003; });
  group.add(target);

  let targetDistance = 5;
  function placeTarget() {
    targetDistance = 3 + Math.random() * 5.5; // 3–8.5 m downrange
    target.position.set((Math.random() - 0.5) * 1.6, 0, cannon.position.z - targetDistance);
  }

  // --- Ball -----------------------------------------------------------------
  const ball = new THREE.Mesh(
    new THREE.SphereGeometry(0.09, 24, 18),
    new THREE.MeshStandardMaterial({ color: 0xfbbf24, emissive: 0xfbbf24, emissiveIntensity: 0.3 })
  );
  ball.visible = false;
  group.add(ball);
  const velocity = new THREE.Vector3();
  let flying = false;

  const trail = [];
  const trailMaterial = new THREE.MeshBasicMaterial({ color: 0xfbbf24, transparent: true, opacity: 0.45 });
  const trailGeometry = new THREE.SphereGeometry(0.03, 8, 6);

  function clearTrail() {
    trail.forEach((dot) => group.remove(dot));
    trail.length = 0;
  }

  // --- UI panels --------------------------------------------------------------
  const readout = createTextPanel({ width: 1.5, height: 0.62, fontSize: 40 });
  readout.position.set(0, 2.15, -0.6);
  group.add(readout);

  const lessonPanel = createTextPanel({ width: 1.35, height: 0.5, fontSize: 30, border: "rgba(167, 139, 250, 0.8)" });
  lessonPanel.position.set(-1.85, 2.1, -0.35);
  lessonPanel.rotation.y = 0.4;
  lessonPanel.userData.setText([
    { text: "Projectile motion", bold: true, size: 36, color: "#a78bfa" },
    { text: "Range = v² · sin(2θ) / g", size: 34 },
    { text: "45° gives the longest throw!", size: 28, color: "#8fa3c8" }
  ]);
  group.add(lessonPanel);

  const feedbackPanel = createTextPanel({ width: 1.35, height: 0.42, fontSize: 34, border: "rgba(52, 211, 153, 0.8)" });
  feedbackPanel.position.set(1.85, 2.1, -0.35);
  feedbackPanel.rotation.y = -0.4;
  group.add(feedbackPanel);

  function predictedRange() {
    return (power * power * Math.sin(2 * THREE.MathUtils.degToRad(angleDeg))) / GRAVITY;
  }

  function updateReadout() {
    readout.userData.setText([
      { text: `Angle θ  ${angleDeg}°     Power v  ${power.toFixed(1)} m/s`, bold: true, size: 42 },
      { text: `Predicted range: ${predictedRange().toFixed(1)} m`, size: 36, color: "#22d3ee" },
      { text: `Target: ${targetDistance.toFixed(1)} m away   ·   Hits ${hits}/${attempts}`, size: 32, color: "#8fa3c8" }
    ]);
  }

  function setFeedback(lines) {
    feedbackPanel.userData.setText(lines);
  }

  // --- Buttons ------------------------------------------------------------------
  const buttons = [];
  function addButton(text, x, accent, onSelect, width = 0.34) {
    const btn = createButton3D(text, { width, height: 0.15, accent, fontSize: 48 });
    btn.position.set(x, 1.05, 0.9);
    btn.rotation.x = -0.25;
    group.add(btn);
    interaction.add(btn, {
      onSelect,
      onHoverStart: btn.userData.onHoverStart,
      onHoverEnd: btn.userData.onHoverEnd
    });
    buttons.push(btn);
    return btn;
  }

  const clampAngle = (v) => THREE.MathUtils.clamp(v, 10, 80);
  const clampPower = (v) => THREE.MathUtils.clamp(v, 4, 14);

  addButton("θ −", -1.05, "#5b8cff", () => { angleDeg = clampAngle(angleDeg - 5); aimBarrel(); updateReadout(); });
  addButton("θ +", -0.65, "#5b8cff", () => { angleDeg = clampAngle(angleDeg + 5); aimBarrel(); updateReadout(); });
  addButton("v −", -0.2, "#22d3ee", () => { power = clampPower(power - 0.5); updateReadout(); });
  addButton("v +", 0.2, "#22d3ee", () => { power = clampPower(power + 0.5); updateReadout(); });
  addButton("LAUNCH", 0.85, "#34d399", fire, 0.55);

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
  }

  function fire() {
    if (flying) return;
    attempts += 1;
    clearTrail();
    const rad = THREE.MathUtils.degToRad(angleDeg);
    ball.position.set(cannon.position.x, 0.26 + Math.sin(rad) * 0.7, cannon.position.z - Math.cos(rad) * 0.7);
    velocity.set(0, power * Math.sin(rad), -power * Math.cos(rad));
    ball.visible = true;
    flying = true;
    setFeedback([{ text: "Ball away…", size: 34 }]);
    updateReadout();
  }

  function land() {
    flying = false;
    const dx = ball.position.x - target.position.x;
    const dz = ball.position.z - target.position.z;
    const missBy = Math.sqrt(dx * dx + dz * dz);
    const flew = Math.abs(ball.position.z - cannon.position.z);

    if (missBy <= 0.75) {
      hits += 1;
      const quality = missBy <= 0.25 ? "BULLSEYE! 🎯" : missBy <= 0.5 ? "Great shot!" : "Hit!";
      setFeedback([
        { text: quality, bold: true, size: 40, color: "#34d399" },
        { text: `It flew ${flew.toFixed(1)} m`, size: 30 }
      ]);
      later(1400, () => { placeTarget(); updateReadout(); });
    } else {
      const short = flew < targetDistance;
      setFeedback([
        { text: `Missed by ${missBy.toFixed(1)} m — ${short ? "too short" : "too far"}`, size: 32, color: "#f87171" },
        { text: short ? "More power, or angle closer to 45°" : "Less power, or steeper angle", size: 28, color: "#8fa3c8" }
      ]);
    }
    later(1200, () => { ball.visible = false; clearTrail(); });
    updateReadout();
  }

  let trailClock = 0;

  aimBarrel();
  placeTarget();
  updateReadout();
  setFeedback([
    { text: "Land the ball on the rings!", size: 32 },
    { text: "Tune θ and v, then LAUNCH", size: 28, color: "#8fa3c8" }
  ]);

  return {
    group,
    update(delta) {
      if (!flying) return;
      velocity.y -= GRAVITY * delta;
      ball.position.addScaledVector(velocity, delta);

      trailClock += delta;
      if (trailClock > 0.05 && trail.length < 80) {
        trailClock = 0;
        const dot = new THREE.Mesh(trailGeometry, trailMaterial);
        dot.position.copy(ball.position);
        group.add(dot);
        trail.push(dot);
      }

      if (ball.position.y <= 0.09 && velocity.y < 0) {
        ball.position.y = 0.09;
        land();
      }
    },
    dispose() {
      timers.forEach(clearTimeout);
      buttons.forEach((btn) => interaction.remove(btn));
      clearTrail();
      trailGeometry.dispose();
      trailMaterial.dispose();
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "physics",
  title: "Projectile Lab",
  tagline: "Aim, launch, learn gravity",
  howTo: "Set the cannon's angle (θ) and power (v) with the buttons, then LAUNCH. Land the ball on the target ring — the board shows the real range formula so you can predict your shot."
};
