import * as THREE from "three";

const GRAB_RADIUS = 0.2; // meters — how close a hand must be to grab something
const HOVER_RADIUS = GRAB_RADIUS * 1.4;
const VELOCITY_SMOOTHING = 0.6;

/**
 * Direct-manipulation "hands" for the minigames: get close to a grabbable
 * object and squeeze the grip to pick it up, it follows your hand while
 * held, and squeezing again lets go — carrying real release velocity so a
 * dropped/thrown object can react physically. This is the "touch it and do
 * it yourself" interaction VR is uniquely good at, used instead of pointing
 * a ray at something far away.
 *
 * On desktop, the mouse acts as a single virtual hand: clicking near a
 * grabbable object picks it up, dragging carries it along a plane facing
 * the camera at the grab depth, and releasing the mouse lets go.
 */
export function createGrabSystem({ renderer, camera }) {
  const grabbables = new Map(); // object3D -> { onGrab, onRelease, onHoverStart, onHoverEnd }
  const controllers = [renderer.xr.getController(0), renderer.xr.getController(1)];
  const grips = [renderer.xr.getControllerGrip(0), renderer.xr.getControllerGrip(1)];

  const hands = controllers.map((controller, i) => ({
    controller,
    grip: grips[i],
    held: null,
    position: new THREE.Vector3(),
    prevPosition: new THREE.Vector3(),
    velocity: new THREE.Vector3(),
    hasPosition: false
  }));

  const mouseHand = {
    held: null,
    position: new THREE.Vector3(),
    prevPosition: new THREE.Vector3(),
    velocity: new THREE.Vector3()
  };
  const raycaster = new THREE.Raycaster();
  const mouseNDC = new THREE.Vector2();
  const dragPlane = new THREE.Plane();
  const planeHit = new THREE.Vector3();
  let mouseDown = false;

  const hovered = new Set();
  const tmp = new THREE.Vector3();
  const tmp2 = new THREE.Vector3();

  function isHeldByAnyone(object) {
    if (mouseHand.held === object) return true;
    return hands.some((h) => h.held === object);
  }

  function findNearestFree(position) {
    let closest = null;
    let closestDist = GRAB_RADIUS;
    for (const [object] of grabbables) {
      if (object.visible === false || isHeldByAnyone(object)) continue;
      const dist = object.getWorldPosition(tmp).distanceTo(position);
      if (dist < closestDist) {
        closest = object;
        closestDist = dist;
      }
    }
    return closest;
  }

  function pulseHaptics(inputSource, intensity = 0.5, duration = 35) {
    const actuator = inputSource?.gamepad?.hapticActuators?.[0];
    actuator?.pulse?.(intensity, duration).catch?.(() => {});
  }

  function grab(hand, object, inputSource) {
    hand.held = object;
    grabbables.get(object)?.onGrab?.(object);
    pulseHaptics(inputSource, 0.6, 30);
  }

  function release(hand, inputSource) {
    const object = hand.held;
    if (!object) return;
    hand.held = null;
    grabbables.get(object)?.onRelease?.(object, hand.velocity);
    pulseHaptics(inputSource, 0.3, 20);
  }

  // Called by app.js's squeeze handler with the controller index and the
  // XRInputSource that fired it. Returns true if a grab/release happened,
  // so the caller can fall back to its own squeeze behavior (room recenter)
  // when the player's hand is empty and nothing was nearby to grab.
  function trySqueeze(controllerIndex, inputSource) {
    const hand = hands[controllerIndex];
    if (!hand) return false;
    hand.grip.getWorldPosition(hand.position);
    hand.hasPosition = true;
    if (hand.held) {
      release(hand, inputSource);
      return true;
    }
    const target = findNearestFree(hand.position);
    if (target) {
      grab(hand, target, inputSource);
      return true;
    }
    return false;
  }

  function updateMouseNDC(event) {
    const rect = renderer.domElement.getBoundingClientRect();
    mouseNDC.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    mouseNDC.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
  }

  function onPointerDown(event) {
    if (renderer.xr.isPresenting) return;
    updateMouseNDC(event);
    raycaster.setFromCamera(mouseNDC, camera);
    const roots = [...grabbables.keys()].filter((o) => o.visible !== false && !isHeldByAnyone(o));
    const hits = raycaster.intersectObjects(roots, true);
    if (hits.length === 0) return;
    let root = hits[0].object;
    while (root && !grabbables.has(root)) root = root.parent;
    if (!root) return;

    mouseDown = true;
    camera.getWorldDirection(tmp);
    dragPlane.setFromNormalAndCoplanarPoint(tmp, hits[0].point);
    root.getWorldPosition(mouseHand.position);
    mouseHand.prevPosition.copy(mouseHand.position);
    grab(mouseHand, root);
  }

  function onPointerMove(event) {
    updateMouseNDC(event);
    if (!mouseDown || !mouseHand.held) return;
    raycaster.setFromCamera(mouseNDC, camera);
    if (raycaster.ray.intersectPlane(dragPlane, planeHit)) {
      mouseHand.position.copy(planeHit);
    }
  }

  function onPointerUp() {
    mouseDown = false;
    if (mouseHand.held) release(mouseHand);
  }

  renderer.domElement.addEventListener("pointerdown", onPointerDown);
  renderer.domElement.addEventListener("pointermove", onPointerMove);
  window.addEventListener("pointerup", onPointerUp);

  function followHand(hand, delta) {
    if (!hand.held) return;
    const parent = hand.held.parent;
    if (!parent) return;
    tmp.copy(hand.position);
    parent.worldToLocal(tmp);
    hand.held.position.copy(tmp);
  }

  return {
    add(object, handlers) {
      grabbables.set(object, handlers);
    },
    remove(object) {
      grabbables.delete(object);
      hands.forEach((h) => { if (h.held === object) h.held = null; });
      if (mouseHand.held === object) mouseHand.held = null;
      hovered.delete(object);
    },
    // Clears any hand's held reference without calling onRelease — used
    // before a game is disposed so a mid-squeeze hold at the moment of
    // switching games can't leave a hand pointing at an object whose
    // geometry/material are about to be disposed out from under it.
    releaseAll() {
      hands.forEach((h) => { h.held = null; });
      mouseHand.held = null;
    },
    trySqueeze,
    update(delta) {
      const dt = Math.max(delta, 0.001);

      for (const hand of hands) {
        hand.prevPosition.copy(hand.position);
        hand.grip.getWorldPosition(hand.position);
        hand.hasPosition = true;
        tmp2.copy(hand.position).sub(hand.prevPosition).divideScalar(dt);
        hand.velocity.lerp(tmp2, VELOCITY_SMOOTHING);
        followHand(hand, delta);
      }

      if (mouseHand.held) {
        tmp2.copy(mouseHand.position).sub(mouseHand.prevPosition).divideScalar(dt);
        mouseHand.velocity.lerp(tmp2, VELOCITY_SMOOTHING);
        mouseHand.prevPosition.copy(mouseHand.position);
        followHand(mouseHand, delta);
      }

      const nowHovered = new Set();
      const activeHandPositions = hands.filter((h) => h.hasPosition).map((h) => h.position);
      if (mouseDown) activeHandPositions.push(mouseHand.position);

      for (const [object] of grabbables) {
        if (isHeldByAnyone(object) || object.visible === false) continue;
        const p = object.getWorldPosition(tmp);
        const near = activeHandPositions.some((hp) => p.distanceTo(hp) < HOVER_RADIUS);
        if (near) nowHovered.add(object);
      }
      for (const o of nowHovered) if (!hovered.has(o)) grabbables.get(o)?.onHoverStart?.(o);
      for (const o of hovered) if (!nowHovered.has(o)) grabbables.get(o)?.onHoverEnd?.(o);
      hovered.clear();
      nowHovered.forEach((o) => hovered.add(o));
    },
    dispose() {
      for (const o of hovered) grabbables.get(o)?.onHoverEnd?.(o);
      hovered.clear();
      grabbables.clear();
      renderer.domElement.removeEventListener("pointerdown", onPointerDown);
      renderer.domElement.removeEventListener("pointermove", onPointerMove);
      window.removeEventListener("pointerup", onPointerUp);
    }
  };
}
