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
  // Google, so land the now-authenticated user on the Learn page. Firebase
  // sometimes resolves the redirect via waitForAuthReady()'s own listener
  // first, in which case getRedirectResult() here returns null even though
  // the sign-in did complete — so also check auth state directly instead of
  // only trusting this call's return value, otherwise the user comes back
  // from Google fully signed in but stuck on the Login screen.
  try {
    const redirectedUser = await consumeGoogleRedirectResult();
    if (redirectedUser) {
      login({ uid: redirectedUser.uid, email: redirectedUser.email, provider: "google" });
    }
    const strandedOnLogin =
      getCurrentUser() && (!window.location.hash || window.location.hash === "#login");
    if (strandedOnLogin) {
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
      world: () => import("../world/world.js"),
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

// --- Jump ---------------------------------------------------------------
// A simple parabolic hop: Space on desktop, the A/X button in VR. Tracked
// as a relative offset (jumpVelocity/jumpOffset) added on top of whatever
// rig.position.y already is, rather than an absolute height — the VR floor
// calibration above and the manual recenter both set rig.position.y
// directly, and a jump mid-flight shouldn't fight or get clobbered by that.
const JUMP_SPEED = 3.2;
const JUMP_GRAVITY = 9.81;
let jumpVelocity = 0;
let jumpOffset = 0; // cumulative height added by the current jump, always >= 0

function requestJump() {
  if (jumpOffset > 0 || jumpVelocity !== 0) return; // already mid-jump
  jumpVelocity = JUMP_SPEED;
}

window.addEventListener("keydown", (event) => {
  if (event.code !== "Space" || event.repeat) return;
  const tag = document.activeElement?.tagName;
  if (tag === "INPUT" || tag === "TEXTAREA") return; // don't hijack typing
  requestJump();
});

// VR controllers don't expose button events the way squeeze/select do — poll
// each frame and edge-detect the A/X button (index 4) per input source.
const jumpButtonWasPressed = new Set();
function pollVRJumpButton(session) {
  if (!session) { jumpButtonWasPressed.clear(); return; }
  for (const source of session.inputSources) {
    const button = source.gamepad?.buttons?.[4];
    if (!button) continue;
    if (button.pressed && !jumpButtonWasPressed.has(source)) {
      jumpButtonWasPressed.add(source);
      requestJump();
    } else if (!button.pressed) {
      jumpButtonWasPressed.delete(source);
    }
  }
}

renderer.setAnimationLoop(() => {
  const delta = clock.getDelta();
  xrState.frameDelta = delta;
  demoCube.rotation.y += delta * 0.4;
  xrState.updatables.forEach((fn) => fn(delta));
  const session = renderer.xr.getSession();
  moveRig(rig, camera, session, delta);
  pollVRJumpButton(session);

  if (jumpVelocity !== 0 || jumpOffset > 0) {
    jumpVelocity -= JUMP_GRAVITY * delta;
    const step = jumpVelocity * delta;
    jumpOffset += step;
    if (jumpOffset <= 0) {
      // Landed — remove exactly the remaining offset so rig.position.y ends
      // up back at whatever height it was before the jump started.
      rig.position.y -= jumpOffset;
      jumpOffset = 0;
      jumpVelocity = 0;
    } else {
      rig.position.y += step;
    }
  }

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
