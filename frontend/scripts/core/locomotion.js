import * as THREE from "three";

const SPEED = 1.6; // meters per second
const SNAP_TURN_ANGLE = THREE.MathUtils.degToRad(30);
const SNAP_TURN_THRESHOLD = 0.7;
const direction = new THREE.Vector3();
const quaternion = new THREE.Quaternion();
let turnReady = true; // edge-trigger guard so a held stick doesn't spin continuously

/**
 * Reads the left controller's thumbstick to translate the player rig, and
 * the right controller's thumbstick to snap-turn it. Movement and turning
 * are split by handedness deliberately — summing every connected gamepad's
 * axes together (the previous behavior) meant pushing both thumbsticks at
 * once added their vectors, silently doubling speed or drifting sideways
 * depending on what each hand happened to be doing. Snap turn also gives
 * headset users the same "look around without physically rotating" ability
 * the desktop mouse-look (World's right-click drag) already provides.
 */
export function moveRig(rig, camera, session, delta) {
  if (!session || !rig) return;

  let rightStickDeflected = false;

  for (const source of session.inputSources) {
    const gamepad = source.gamepad;
    if (!gamepad || gamepad.axes.length < 2) continue;

    const axes = gamepad.axes;
    const x = axes[2] ?? axes[0] ?? 0;
    const y = axes[3] ?? axes[1] ?? 0;

    if (source.handedness === "right") {
      if (Math.abs(x) > SNAP_TURN_THRESHOLD) {
        rightStickDeflected = true;
        if (turnReady) {
          rig.rotation.y -= Math.sign(x) * SNAP_TURN_ANGLE;
          turnReady = false;
        }
      }
      continue; // right stick turns; it never contributes to movement
    }

    if (Math.abs(x) < 0.12 && Math.abs(y) < 0.12) continue;

    camera.getWorldQuaternion(quaternion);
    direction.set(x, 0, y).applyQuaternion(quaternion);
    direction.y = 0;
    if (direction.lengthSq() === 0) continue;

    direction.normalize().multiplyScalar(SPEED * delta);
    rig.position.add(direction);
  }

  if (!rightStickDeflected) turnReady = true;
}
