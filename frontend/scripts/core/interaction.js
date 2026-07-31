import * as THREE from "three";
import { RAY_COLOR_IDLE, RAY_COLOR_HOVER } from "./xrManager.js";

const DEFAULT_RETICLE_DISTANCE = 1.5;

/**
 * Unified pointer interaction for the minigames: VR controller rays fire
 * onSelect via the XR "selectstart" event, and on desktop the mouse does the
 * same via pointerdown. Hover (onHoverStart/onHoverEnd) is driven every frame
 * from both controllers and the last known mouse position, so orbs/buttons can
 * highlight before you commit to a click or trigger pull.
 *
 * Each controller also gets live aim feedback: the ray turns green and its
 * reticle dot snaps to the hit point when something interactive is under it,
 * and a short haptic pulse confirms a successful select — without this, VR
 * users have no way to tell whether they're actually pointing at a target.
 *
 * Objects are registered with `add(object3D, { onSelect, onHoverStart, onHoverEnd })`.
 * Raycasts test the object's whole subtree, so labels attached to a button
 * still count as hits on the button.
 */
export function createInteractionManager({ renderer, camera }) {
  const raycaster = new THREE.Raycaster();
  raycaster.far = 30;

  const handlers = new Map(); // root Object3D -> handler set
  const hovered = new Set();
  const mouseNDC = new THREE.Vector2();
  let mouseInside = false;

  const controllers = [renderer.xr.getController(0), renderer.xr.getController(1)];
  const tempMatrix = new THREE.Matrix4();

  function findRoot(object) {
    let node = object;
    while (node) {
      if (handlers.has(node)) return node;
      node = node.parent;
    }
    return null;
  }

  // Returns { root, distance } for the closest interactive hit, or null.
  function pick() {
    const roots = [...handlers.keys()].filter((o) => o.visible !== false);
    if (roots.length === 0) return null;
    const hits = raycaster.intersectObjects(roots, true);
    for (const hit of hits) {
      const root = findRoot(hit.object);
      if (root) return { root, distance: hit.distance };
    }
    return null;
  }

  function aimController(controller) {
    tempMatrix.identity().extractRotation(controller.matrixWorld);
    raycaster.ray.origin.setFromMatrixPosition(controller.matrixWorld);
    raycaster.ray.direction.set(0, 0, -1).applyMatrix4(tempMatrix);
    return pick();
  }

  function aimMouse() {
    raycaster.setFromCamera(mouseNDC, camera);
    return pick();
  }

  function pulseHaptics(inputSource, intensity = 0.5, duration = 40) {
    const actuator = inputSource?.gamepad?.hapticActuators?.[0];
    actuator?.pulse?.(intensity, duration).catch?.(() => {});
  }

  function trigger(hit, inputSource) {
    if (!hit) return;
    handlers.get(hit.root)?.onSelect?.(hit.root);
    pulseHaptics(inputSource);
  }

  // Three.js re-dispatches the WebXR select events on the controller with
  // the originating XRInputSource as event.data — that's how we reach the
  // gamepad for haptics without threading the session through this module.
  const onControllerSelect = (event) => trigger(aimController(event.target), event.data);

  function onPointerDown(event) {
    // Left-click only — right-click is reserved for camera-look (World's
    // mouse-look), and without this check a right-click landing on a
    // button/NPC would also fire select the same as a real click.
    if (renderer.xr.isPresenting || event.button !== 0) return;
    updateMouseNDC(event);
    trigger(aimMouse());
  }

  function updateMouseNDC(event) {
    const rect = renderer.domElement.getBoundingClientRect();
    mouseNDC.x = ((event.clientX - rect.left) / rect.width) * 2 - 1;
    mouseNDC.y = -((event.clientY - rect.top) / rect.height) * 2 + 1;
    mouseInside = true;
  }

  const onPointerMove = (event) => updateMouseNDC(event);
  const onPointerLeave = () => { mouseInside = false; };

  controllers.forEach((c) => c.addEventListener("selectstart", onControllerSelect));
  renderer.domElement.addEventListener("pointerdown", onPointerDown);
  renderer.domElement.addEventListener("pointermove", onPointerMove);
  renderer.domElement.addEventListener("pointerleave", onPointerLeave);

  return {
    add(object, objectHandlers) {
      handlers.set(object, objectHandlers);
    },
    remove(object) {
      handlers.delete(object);
      if (hovered.has(object)) {
        hovered.delete(object);
      }
    },
    update() {
      const nowHovered = new Set();

      if (renderer.xr.isPresenting) {
        for (const controller of controllers) {
          const hit = aimController(controller);
          if (hit) nowHovered.add(hit.root);

          const { rayMaterial, reticle } = controller.userData;
          if (rayMaterial && reticle) {
            rayMaterial.color.setHex(hit ? RAY_COLOR_HOVER : RAY_COLOR_IDLE);
            reticle.material.color.setHex(hit ? RAY_COLOR_HOVER : RAY_COLOR_IDLE);
            reticle.position.z = -(hit?.distance ?? DEFAULT_RETICLE_DISTANCE);
          }
        }
      } else if (mouseInside) {
        const hit = aimMouse();
        if (hit) nowHovered.add(hit.root);
      }

      for (const root of nowHovered) {
        if (!hovered.has(root)) handlers.get(root)?.onHoverStart?.(root);
      }
      for (const root of hovered) {
        if (!nowHovered.has(root)) handlers.get(root)?.onHoverEnd?.(root);
      }
      hovered.clear();
      nowHovered.forEach((root) => hovered.add(root));
    },
    dispose() {
      for (const root of hovered) handlers.get(root)?.onHoverEnd?.(root);
      hovered.clear();
      handlers.clear();
      controllers.forEach((c) => {
        c.removeEventListener("selectstart", onControllerSelect);
        const { rayMaterial, reticle } = c.userData;
        if (rayMaterial) rayMaterial.color.setHex(RAY_COLOR_IDLE);
        if (reticle) {
          reticle.material.color.setHex(RAY_COLOR_IDLE);
          reticle.position.z = -DEFAULT_RETICLE_DISTANCE;
        }
      });
      renderer.domElement.removeEventListener("pointerdown", onPointerDown);
      renderer.domElement.removeEventListener("pointermove", onPointerMove);
      renderer.domElement.removeEventListener("pointerleave", onPointerLeave);
    }
  };
}
