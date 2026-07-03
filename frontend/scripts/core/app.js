import * as THREE from "three";
import { createXRApp } from "./xrManager.js";
import { createRouter } from "./router.js";
import { moveRig } from "./locomotion.js";
import { xrState } from "./xrState.js";
import { initProfileMenu } from "./profileMenu.js";
import {
  guardRoute,
  getCurrentUser,
  onAuthChange,
  logoutUser,
  waitForAuthReady,
  watchAuthState
} from "./authState.js";
import { logout as firebaseLogout } from "./firebase.js";
import { getProfile, loadProfile, clearProfile, onProfileChange } from "./profileState.js";

const canvas = document.getElementById("scene-canvas");
const { scene, camera, renderer, rig, demoCube } = createXRApp(canvas);
Object.assign(xrState, { scene, camera, renderer, rig });

async function start() {
  // Wait for Firebase to restore any existing session before the first route
  // guard check runs, so a deep link to #vrSetup on a fresh tab doesn't bounce
  // an already-signed-in user back to Login.
  await waitForAuthReady();
  watchAuthState();

  createRouter(
    {
      mainPage: () => import("../mainPage/mainPage.js"),
      login: () => import("../login/login.js"),
      contact: () => import("../contact/contact.js"),
      learn: () => import("../learn/learn.js"),
      vrSetup: () => import("../vrSetup/vrSetup.js"),
      profile: () => import("../profile/profile.js"),
      devices: () => import("../devices/devices.js")
    },
    { scene, guard: guardRoute }
  );
}
start();

const nav = document.getElementById("ui-nav");
const logoutBtn = document.getElementById("nav-logout");
const profileAvatar = document.getElementById("profile-avatar");
const profileName = document.getElementById("profile-name");

initProfileMenu();

function syncProfileUI(profile) {
  if (!profile) {
    profileName.textContent = "Account";
    profileAvatar.textContent = "?";
    profileAvatar.style.backgroundImage = "";
    return;
  }
  const fallback = profile.displayName || profile.email || "Explorer";
  profileName.textContent = fallback;
  if (profile.photoURL) {
    profileAvatar.style.backgroundImage = `url(${profile.photoURL})`;
    profileAvatar.textContent = "";
  } else {
    profileAvatar.style.backgroundImage = "";
    profileAvatar.textContent = fallback.trim().charAt(0).toUpperCase();
  }
}
onProfileChange(syncProfileUI);
syncProfileUI(getProfile());

function syncNav(user) {
  nav.classList.toggle("is-authed", Boolean(user));
  if (user) {
    loadProfile(user.uid, user);
  } else {
    clearProfile();
  }
}
syncNav(getCurrentUser());
onAuthChange(syncNav);

logoutBtn.addEventListener("click", () => {
  firebaseLogout().catch(() => {});
  logoutUser();
  window.location.hash = "mainPage";
});

const clock = new THREE.Clock();

renderer.setAnimationLoop(() => {
  const delta = clock.getDelta();
  xrState.frameDelta = delta;
  demoCube.rotation.y += delta * 0.4;
  xrState.updatables.forEach((fn) => fn(delta));
  moveRig(rig, camera, renderer.xr.getSession(), delta);
  renderer.render(scene, camera);
});
