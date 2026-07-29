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
  login,
  logoutUser,
  waitForAuthReady,
  watchAuthState
} from "./authState.js";
import { logout as firebaseLogout, consumeGoogleRedirectResult } from "./firebase.js";
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

  // Picks up a sign-in that fell back to signInWithRedirect() (e.g. the
  // Google popup was blocked) — the page has just reloaded coming back from
  // Google, so land the now-authenticated user on the Learn page.
  try {
    const redirectedUser = await consumeGoogleRedirectResult();
    if (redirectedUser) {
      login({ uid: redirectedUser.uid, email: redirectedUser.email, provider: "google" });
      window.location.hash = "learn";
    }
  } catch (err) {
    console.warn("Google redirect sign-in failed:", err.message);
  }

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

// Different OpenXR runtimes (Meta's own vs. SteamVR acting as a bridge for
// Quest Link) can report wildly different floor calibration for the
// "local-floor" reference space if the runtime's room/floor setup was never
// run — the whole scene then renders at head height near the ceiling or
// underfoot, out of controller reach. Rather than depend on every runtime
// being calibrated, sample the real headset height a few frames into each
// VR session and, if it's clearly broken (not a plausible human height),
// shift the rig to bring the room back to a normal eye level. Squeezing
// either controller re-runs this on demand (forcing the correction even if
// the reading looked "plausible"), as a manual escape hatch.
const EXPECTED_EYE_HEIGHT = 1.6;
const PLAUSIBLE_HEIGHT_RANGE = [1.0, 2.3];
const worldPos = new THREE.Vector3();
let pendingCalibration = null; // { forceFull: boolean } | null
let calibrationFramesLeft = 0;

function requestVRRecenter({ forceFull = false, frameDelay = 0 } = {}) {
  pendingCalibration = { forceFull };
  calibrationFramesLeft = frameDelay;
}

renderer.xr.addEventListener("sessionstart", () => {
  // Let a few real XR frames render before sampling head height, so the
  // very first reading reflects the actual headset pose, not the pre-XR default.
  requestVRRecenter({ forceFull: false, frameDelay: 5 });
});
renderer.xr.addEventListener("sessionend", () => {
  rig.position.y = 0;
  pendingCalibration = null;
});

for (let i = 0; i < 2; i++) {
  renderer.xr.getController(i).addEventListener("squeezestart", (event) => {
    // Squeezing near a grabbable object grabs/releases it (learn.js owns
    // this while mounted); an empty-handed squeeze anywhere else recenters
    // the room instead — the same gesture does double duty contextually.
    const handled = xrState.grabSystem?.trySqueeze(i, event.data);
    if (!handled) requestVRRecenter({ forceFull: true });
  });
}

renderer.setAnimationLoop(() => {
  const delta = clock.getDelta();
  xrState.frameDelta = delta;
  demoCube.rotation.y += delta * 0.4;
  xrState.updatables.forEach((fn) => fn(delta));
  moveRig(rig, camera, renderer.xr.getSession(), delta);

  if (pendingCalibration) {
    if (calibrationFramesLeft > 0) {
      calibrationFramesLeft -= 1;
    } else {
      camera.getWorldPosition(worldPos);
      const [min, max] = PLAUSIBLE_HEIGHT_RANGE;
      const outOfRange = worldPos.y < min || worldPos.y > max;
      if (pendingCalibration.forceFull || outOfRange) {
        rig.position.y += EXPECTED_EYE_HEIGHT - worldPos.y;
      }
      pendingCalibration = null;
    }
  }

  renderer.render(scene, camera);
});
