import * as THREE from "three";

/**
 * Unified pointer interaction for the minigames: VR controller rays fire
 * onSelect via the XR "selectstart" event, and on desktop the mouse does the
 * same via pointerdown. Hover (onHoverStart/onHoverEnd) is driven every frame
 * from both controllers and the last known mouse position, so orbs/buttons can
 * highlight before you commit to a click or trigger pull.
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

  function pick() {
    const roots = [...handlers.keys()].filter((o) => o.visible !== false);
    if (roots.length === 0) return null;
    const hits = raycaster.intersectObjects(roots, true);
    for (const hit of hits) {
      const root = findRoot(hit.object);
      if (root) return root;
    }
    return null;
  }

  function raycastFromController(controller) {
    tempMatrix.identity().extractRotation(controller.matrixWorld);
    raycaster.ray.origin.setFromMatrixPosition(controller.matrixWorld);
    raycaster.ray.direction.set(0, 0, -1).applyMatrix4(tempMatrix);
    return pick();
  }

  function raycastFromMouse() {
    raycaster.setFromCamera(mouseNDC, camera);
    return pick();
  }

  function trigger(root) {
    if (!root) return;
    handlers.get(root)?.onSelect?.(root);
  }

  const onControllerSelect = (event) => trigger(raycastFromController(event.target));

  function onPointerDown(event) {
    if (renderer.xr.isPresenting) return;
    updateMouseNDC(event);
    trigger(raycastFromMouse());
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
          const root = raycastFromController(controller);
          if (root) nowHovered.add(root);
        }
      } else if (mouseInside) {
        const root = raycastFromMouse();
        if (root) nowHovered.add(root);
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
      controllers.forEach((c) => c.removeEventListener("selectstart", onControllerSelect));
      renderer.domElement.removeEventListener("pointerdown", onPointerDown);
      renderer.domElement.removeEventListener("pointermove", onPointerMove);
      renderer.domElement.removeEventListener("pointerleave", onPointerLeave);
    }
  };
}
