import * as THREE from "three";
import { xrState } from "../core/xrState.js";
import { connectVRSession } from "../core/xrSession.js";
import { createInteractionManager } from "../core/interaction.js";
import { createGrabSystem } from "../core/grabSystem.js";
import { createTextPanel, createButton3D, disposeTree } from "../core/textPanel.js";
import * as mathGame from "./games/mathGame.js";
import * as physicsGame from "./games/physicsGame.js";
import * as chemistryGame from "./games/chemistryGame.js";

/**
 * The Learn page: after login the student picks a subject — Maths, Physics or
 * Chemistry — and its minigame mounts into the shared VR room. Subjects can be
 * chosen from the HTML tabs (desktop/mobile) or from the floating 3D menu
 * (inside the headset, where the HTML overlay isn't visible). Both drive the
 * same game lifecycle.
 *
 * Menu navigation (subject cards, the back button) stays ray-based — point
 * and select — since those are far/occasional actions. The games themselves
 * use the grab system instead: their learning objects sit within arm's
 * reach and you pick them up and physically place/throw them, so all
 * content is attached to the player rig (not the static scene) and stays
 * reachable no matter where the player walks.
 */

const SUBJECTS = {
  maths: { module: mathGame, accent: "#5b8cff" },
  physics: { module: physicsGame, accent: "#22d3ee" },
  chemistry: { module: chemistryGame, accent: "#a78bfa" }
};

const tabButtons = () => document.querySelectorAll("[data-subject]");
const titleEl = () => document.getElementById("learn-title");
const descEl = () => document.getElementById("learn-desc");
const statusEl = () => document.getElementById("learn-status");
const enterVRBtn = () => document.getElementById("learn-enter-vr");

let interaction = null;
let grab = null;
let roomRef = null; // the player rig — game/menu content is attached here so it moves with the player
let sceneRef = null;
let menuGroup = null;
let backButton = null;
let activeGame = null;
let activeSubject = null;
let updateFn = null;

function setStatus(message, isError = false) {
  const el = statusEl();
  el.textContent = message;
  el.classList.toggle("is-error", isError);
}

function buildMenu() {
  const menu = new THREE.Group();
  menu.name = "learnMenu";

  const heading = createTextPanel({ width: 2.2, height: 0.42, fontSize: 48 });
  heading.position.set(0, 2.35, -1.2);
  heading.userData.setText([
    { text: "Choose a subject", bold: true, size: 56 },
    { text: "Point and pull the trigger (or click)", size: 28, color: "#8fa3c8" },
    { text: "Room looks off? Squeeze either grip to recenter", size: 22, color: "#5b8cff" }
  ]);
  menu.add(heading);

  Object.entries(SUBJECTS).forEach(([id, { module, accent }], i) => {
    const card = createTextPanel({ width: 0.95, height: 0.75, fontSize: 34, border: accent });
    card.position.set((i - 1) * 1.15, 1.55, -1.1);
    card.rotation.y = (1 - i) * 0.22;
    card.userData.setText([
      { text: id.charAt(0).toUpperCase() + id.slice(1), bold: true, size: 52, color: accent },
      { text: module.meta.title, size: 34 },
      { text: module.meta.tagline, size: 26, color: "#8fa3c8" }
    ]);
    menu.add(card);

    interaction.add(card, {
      onSelect: () => startGame(id),
      onHoverStart: () => card.scale.setScalar(1.06),
      onHoverEnd: () => card.scale.setScalar(1)
    });
  });

  return menu;
}

function syncTabs() {
  tabButtons().forEach((btn) => btn.classList.toggle("is-active", btn.dataset.subject === activeSubject));
}

function stopGame() {
  if (!activeGame) return;
  grab.releaseAll(); // guard against a mid-squeeze hold at the moment of switching
  roomRef.remove(activeGame.group);
  activeGame.dispose();
  activeGame = null;
  activeSubject = null;
  if (backButton) {
    interaction.remove(backButton);
    roomRef.remove(backButton);
    disposeTree(backButton);
    backButton = null;
  }
}

function showMenu() {
  stopGame();
  menuGroup.visible = true;
  syncTabs();
  const title = titleEl();
  if (title) {
    title.textContent = "Pick a subject to start its minigame";
    descEl().textContent = "Each game runs in the 3D room behind this panel — playable with the mouse here, or with your hands in VR.";
  }
}

function startGame(subjectId) {
  const subject = SUBJECTS[subjectId];
  if (!subject || subjectId === activeSubject) return;
  stopGame();

  menuGroup.visible = false;
  activeSubject = subjectId;
  activeGame = subject.module.createGame({ interaction, grab });
  roomRef.add(activeGame.group);

  backButton = createButton3D("◀ Menu", { width: 0.4, height: 0.15, accent: "#f472b6", fontSize: 44 });
  backButton.position.set(-0.55, 1.3, 0.25);
  backButton.rotation.y = 0.6;
  roomRef.add(backButton);
  interaction.add(backButton, {
    onSelect: showMenu,
    onHoverStart: backButton.userData.onHoverStart,
    onHoverEnd: backButton.userData.onHoverEnd
  });

  syncTabs();
  const title = titleEl();
  if (title) {
    title.textContent = subject.module.meta.title;
    descEl().textContent = subject.module.meta.howTo;
  }
}

async function handleEnterVR() {
  const btn = enterVRBtn();
  btn.disabled = true;
  setStatus("Starting VR session…");
  try {
    await connectVRSession(xrState.renderer, {
      onConnected: () => setStatus("In VR! Reach out and grab things — squeeze the grip to pick up, squeeze again to let go."),
      onEnded: () => {
        setStatus("VR session ended.");
        btn.disabled = false;
      }
    });
  } catch (err) {
    setStatus(err.message, true);
    btn.disabled = false;
  }
}

function handleTabClick(event) {
  startGame(event.currentTarget.dataset.subject);
}

export function mount(scene) {
  sceneRef = scene;
  roomRef = xrState.rig;

  const demoCube = scene.getObjectByName("demoCube");
  if (demoCube) demoCube.visible = false;

  interaction = createInteractionManager({ renderer: xrState.renderer, camera: xrState.camera });
  grab = createGrabSystem({ renderer: xrState.renderer, camera: xrState.camera });
  xrState.grabSystem = grab;

  menuGroup = buildMenu();
  roomRef.add(menuGroup);

  updateFn = (delta) => {
    interaction.update();
    grab.update(delta);
    activeGame?.update(delta);
  };
  xrState.updatables.add(updateFn);

  tabButtons().forEach((btn) => btn.addEventListener("click", handleTabClick));
  enterVRBtn().addEventListener("click", handleEnterVR);
  enterVRBtn().disabled = false;
  setStatus("");
  showMenu();
}

export function unmount() {
  tabButtons().forEach((btn) => btn.removeEventListener("click", handleTabClick));
  enterVRBtn().removeEventListener("click", handleEnterVR);

  xrState.updatables.delete(updateFn);
  updateFn = null;

  stopGame();
  roomRef.remove(menuGroup);
  disposeTree(menuGroup);
  menuGroup = null;

  xrState.grabSystem = null;
  grab.dispose();
  grab = null;

  interaction.dispose();
  interaction = null;

  const demoCube = sceneRef.getObjectByName("demoCube");
  if (demoCube) demoCube.visible = true;
  sceneRef = null;
  roomRef = null;
}
