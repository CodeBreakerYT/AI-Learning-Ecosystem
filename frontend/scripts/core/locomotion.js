import * as THREE from "three";

const SPEED = 1.6; // meters per second
const direction = new THREE.Vector3();
const quaternion = new THREE.Quaternion();

/**
 * Reads the thumbstick axes from any connected XR controller and translates
 * the player rig (camera + controllers) in the direction it's pointing.
 */
export function moveRig(rig, camera, session, delta) {
  if (!session || !rig) return;

  for (const source of session.inputSources) {
    const gamepad = source.gamepad;
    if (!gamepad || gamepad.axes.length < 2) continue;

    const axes = gamepad.axes;
    const x = axes[2] ?? axes[0] ?? 0;
    const y = axes[3] ?? axes[1] ?? 0;
    if (Math.abs(x) < 0.12 && Math.abs(y) < 0.12) continue;

    camera.getWorldQuaternion(quaternion);
    direction.set(x, 0, y).applyQuaternion(quaternion);
    direction.y = 0;
    if (direction.lengthSq() === 0) continue;

    direction.normalize().multiplyScalar(SPEED * delta);
    rig.position.add(direction);
  }
}
