import * as THREE from "three";
import { XRControllerModelFactory } from "three/addons/webxr/XRControllerModelFactory.js";

/**
 * Creates the renderer/camera/scene plus a persistent VR room (floor, lighting,
 * demo cube) and a movable player rig with tracked controllers. The rig is what
 * locomotion.js translates, so the camera and controllers move together with it.
 */
export function createXRApp(canvas) {
  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x10131a);

  const rig = new THREE.Group();
  rig.name = "playerRig";
  rig.position.set(0, 0, 3);
  scene.add(rig);

  const camera = new THREE.PerspectiveCamera(70, window.innerWidth / window.innerHeight, 0.05, 100);
  camera.position.set(0, 1.6, 0);
  rig.add(camera);

  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(window.devicePixelRatio);
  renderer.setSize(window.innerWidth, window.innerHeight);
  renderer.xr.enabled = true;
  renderer.xr.setReferenceSpaceType("local-floor");

  renderer.xr.addEventListener("sessionstart", () => document.body.classList.add("xr-presenting"));
  renderer.xr.addEventListener("sessionend", () => document.body.classList.remove("xr-presenting"));

  const demoCube = setupEnvironment(scene);
  setupControllers(renderer, rig);

  window.addEventListener("resize", () => {
    camera.aspect = window.innerWidth / window.innerHeight;
    camera.updateProjectionMatrix();
    renderer.setSize(window.innerWidth, window.innerHeight);
  });

  return { scene, camera, renderer, rig, demoCube };
}

function setupEnvironment(scene) {
  const hemiLight = new THREE.HemisphereLight(0xffffff, 0x444466, 1.2);
  scene.add(hemiLight);

  const dirLight = new THREE.DirectionalLight(0xffffff, 1.5);
  dirLight.position.set(3, 5, 2);
  scene.add(dirLight);

  const floor = new THREE.Mesh(
    new THREE.PlaneGeometry(20, 20),
    new THREE.MeshStandardMaterial({ color: 0x1c2230 })
  );
  floor.rotation.x = -Math.PI / 2;
  floor.name = "floor";
  scene.add(floor);

  const grid = new THREE.GridHelper(20, 40, 0x3a4358, 0x252b39);
  grid.name = "grid";
  scene.add(grid);

  // Placeholder for real lesson content — swap for a GLTF loaded from
  // assets/models/ once a model with animations is ready.
  const demoCube = new THREE.Mesh(
    new THREE.BoxGeometry(0.6, 0.6, 0.6),
    new THREE.MeshStandardMaterial({ color: 0x4f8cff })
  );
  demoCube.position.set(0, 1, -1.5);
  demoCube.name = "demoCube";
  scene.add(demoCube);

  return demoCube;
}

export const RAY_COLOR_IDLE = 0x5b8cff;
export const RAY_COLOR_HOVER = 0x34d399;

function setupControllers(renderer, rig) {
  const modelFactory = new XRControllerModelFactory();
  const rayGeometry = new THREE.BufferGeometry().setFromPoints([
    new THREE.Vector3(0, 0, 0),
    new THREE.Vector3(0, 0, -1)
  ]);

  for (let i = 0; i < 2; i++) {
    const controller = renderer.xr.getController(i);

    const rayMaterial = new THREE.LineBasicMaterial({ color: RAY_COLOR_IDLE, transparent: true, opacity: 0.9 });
    const ray = new THREE.Line(rayGeometry, rayMaterial);
    ray.name = "ray";
    controller.add(ray);

    // Small dot that interaction.js slides along the ray to the hit point
    // (or a fixed default distance when nothing's hit) — without it, aiming
    // at small 3D UI is guesswork since there's no crosshair like on desktop.
    const reticle = new THREE.Mesh(
      new THREE.SphereGeometry(0.012, 12, 10),
      new THREE.MeshBasicMaterial({ color: RAY_COLOR_IDLE, transparent: true, opacity: 0.9 })
    );
    reticle.position.z = -1.5;
    controller.add(reticle);

    controller.userData.rayMaterial = rayMaterial;
    controller.userData.reticle = reticle;
    rig.add(controller);

    const grip = renderer.xr.getControllerGrip(i);
    grip.add(modelFactory.createControllerModel(grip));
    rig.add(grip);
  }
}
