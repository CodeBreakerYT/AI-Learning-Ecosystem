import * as CANNON from "cannon-es";

/**
 * Thin bridge between a CANNON.World and the THREE meshes it drives — the
 * World page's crates/barrels/ramp get real gravity, collision and
 * restitution instead of hand-rolled velocity math. Fixed-timestep stepping
 * (rather than stepping by the raw render delta) keeps the simulation
 * stable if a frame hitches.
 */
export function createPhysicsWorld() {
  const world = new CANNON.World({ gravity: new CANNON.Vec3(0, -9.82, 0) });
  world.allowSleep = true;

  const defaultMaterial = new CANNON.Material("default");
  world.defaultContactMaterial = new CANNON.ContactMaterial(defaultMaterial, defaultMaterial, {
    friction: 0.4,
    restitution: 0.3
  });

  const bodies = new Map(); // mesh -> { body, sync }
  const FIXED_STEP = 1 / 60;
  const MAX_SUBSTEPS = 5;

  function addGroundPlane(y = 0) {
    const body = new CANNON.Body({ type: CANNON.Body.STATIC, material: defaultMaterial });
    body.addShape(new CANNON.Plane());
    body.quaternion.setFromEuler(-Math.PI / 2, 0, 0);
    body.position.set(0, y, 0);
    world.addBody(body);
    return body;
  }

  function addBody(mesh, body, { sync = true } = {}) {
    body.material = body.material ?? defaultMaterial;
    world.addBody(body);
    bodies.set(mesh, { body, sync });
    return body;
  }

  function setSync(mesh, sync) {
    const entry = bodies.get(mesh);
    if (entry) entry.sync = sync;
  }

  function removeBody(mesh) {
    const entry = bodies.get(mesh);
    if (!entry) return;
    world.removeBody(entry.body);
    bodies.delete(mesh);
  }

  function step(delta) {
    world.step(FIXED_STEP, delta, MAX_SUBSTEPS);
    for (const [mesh, { body, sync }] of bodies) {
      if (!sync) continue;
      mesh.position.copy(body.position);
      mesh.quaternion.copy(body.quaternion);
    }
  }

  function dispose() {
    for (const [, { body }] of bodies) world.removeBody(body);
    bodies.clear();
  }

  return { world, defaultMaterial, addGroundPlane, addBody, setSync, removeBody, step, dispose };
}
