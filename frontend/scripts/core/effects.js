import * as THREE from "three";
import { xrState } from "./xrState.js";

/**
 * Fire-and-forget VFX for the minigames — particle bursts, expanding
 * shockwave rings, and fading motion trails. Each spawn function adds its
 * own geometry to `parent`, self-registers an update tick on
 * `xrState.updatables` (which always runs, regardless of which route is
 * mounted), and removes/disposes itself when its lifetime ends — callers
 * don't need to track or clean these up.
 */

const tmpColor = new THREE.Color();

/** A short burst of colored particles (confetti / sparkle / impact puff). */
export function spawnBurst(parent, {
  position = new THREE.Vector3(),
  colors = ["#34d399"],
  count = 24,
  speed = 1.4,
  spread = 1,
  size = 0.03,
  life = 0.6,
  gravity = 1.1
} = {}) {
  const positions = new Float32Array(count * 3);
  const velocities = new Float32Array(count * 3);
  const colorAttr = new Float32Array(count * 3);

  for (let i = 0; i < count; i++) {
    positions[i * 3] = position.x;
    positions[i * 3 + 1] = position.y;
    positions[i * 3 + 2] = position.z;

    const dir = new THREE.Vector3(Math.random() - 0.5, Math.random() * 0.9 + 0.15, Math.random() - 0.5)
      .normalize()
      .multiplyScalar(speed * (0.4 + Math.random() * 0.6) * spread);
    velocities[i * 3] = dir.x;
    velocities[i * 3 + 1] = dir.y;
    velocities[i * 3 + 2] = dir.z;

    tmpColor.set(colors[Math.floor(Math.random() * colors.length)]);
    colorAttr[i * 3] = tmpColor.r;
    colorAttr[i * 3 + 1] = tmpColor.g;
    colorAttr[i * 3 + 2] = tmpColor.b;
  }

  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  geometry.setAttribute("color", new THREE.BufferAttribute(colorAttr, 3));

  const material = new THREE.PointsMaterial({
    size,
    vertexColors: true,
    transparent: true,
    opacity: 1,
    depthWrite: false,
    blending: THREE.AdditiveBlending
  });

  const points = new THREE.Points(geometry, material);
  points.frustumCulled = false;
  parent.add(points);

  const posAttr = geometry.attributes.position;
  let age = 0;

  function tick(delta) {
    age += delta;
    const t = Math.min(age / life, 1);
    for (let i = 0; i < count; i++) {
      velocities[i * 3 + 1] -= gravity * delta;
      posAttr.array[i * 3] += velocities[i * 3] * delta;
      posAttr.array[i * 3 + 1] += velocities[i * 3 + 1] * delta;
      posAttr.array[i * 3 + 2] += velocities[i * 3 + 2] * delta;
    }
    posAttr.needsUpdate = true;
    material.opacity = 1 - t;
    if (t >= 1) {
      xrState.updatables.delete(tick);
      parent.remove(points);
      geometry.dispose();
      material.dispose();
    }
  }
  xrState.updatables.add(tick);
  return points;
}

/** An expanding, fading ring — a "shockwave" pulse for hits/successes. */
export function spawnShockwave(parent, {
  position = new THREE.Vector3(),
  color = "#34d399",
  radius = 0.5,
  life = 0.5,
  opacity = 0.9
} = {}) {
  const geometry = new THREE.RingGeometry(0.85, 1, 40);
  const material = new THREE.MeshBasicMaterial({
    color,
    transparent: true,
    opacity,
    side: THREE.DoubleSide,
    depthWrite: false,
    blending: THREE.AdditiveBlending
  });
  const mesh = new THREE.Mesh(geometry, material);
  mesh.position.copy(position);
  mesh.rotation.x = -Math.PI / 2;
  mesh.scale.setScalar(0.01);
  parent.add(mesh);

  let age = 0;
  function tick(delta) {
    age += delta;
    const t = Math.min(age / life, 1);
    mesh.scale.setScalar(0.01 + t * radius);
    material.opacity = opacity * (1 - t);
    if (t >= 1) {
      xrState.updatables.delete(tick);
      parent.remove(mesh);
      geometry.dispose();
      material.dispose();
    }
  }
  xrState.updatables.add(tick);
  return mesh;
}

/**
 * A fixed-length fading line trail. Call `sample(pos)` once per frame with
 * the tracked object's current (parent-local) position while it's moving,
 * and `reset()` to clear it (e.g. when the object is put back at rest).
 */
export function createTrail(parent, { color = 0xfbbf24, maxPoints = 14, opacity = 0.55 } = {}) {
  const positions = new Float32Array(maxPoints * 3);
  const geometry = new THREE.BufferGeometry();
  geometry.setAttribute("position", new THREE.BufferAttribute(positions, 3));
  geometry.setDrawRange(0, 0);

  const material = new THREE.LineBasicMaterial({
    color,
    transparent: true,
    opacity,
    depthWrite: false,
    blending: THREE.AdditiveBlending
  });
  const line = new THREE.Line(geometry, material);
  line.frustumCulled = false;
  line.visible = false;
  parent.add(line);

  let filled = 0;

  return {
    line,
    reset() {
      filled = 0;
      geometry.setDrawRange(0, 0);
      line.visible = false;
    },
    sample(pos) {
      line.visible = true;
      if (filled < maxPoints) {
        positions[filled * 3] = pos.x;
        positions[filled * 3 + 1] = pos.y;
        positions[filled * 3 + 2] = pos.z;
        filled++;
      } else {
        positions.copyWithin(0, 3);
        positions[(maxPoints - 1) * 3] = pos.x;
        positions[(maxPoints - 1) * 3 + 1] = pos.y;
        positions[(maxPoints - 1) * 3 + 2] = pos.z;
      }
      geometry.attributes.position.needsUpdate = true;
      geometry.setDrawRange(0, filled);
    },
    dispose() {
      parent.remove(line);
      geometry.dispose();
      material.dispose();
    }
  };
}
