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
 * Chemistry — then a topic and difficulty (Easy/Medium/Hard) for it, and that
 * minigame mounts into the shared VR room. Every control (subject tabs, the
 * topic/difficulty pickers, the back buttons) exists in two parallel forms:
 * HTML overlay (desktop/mobile) and floating 3D panels (inside the headset,
 * where the HTML overlay is hidden) — both write to the same pending
 * selection state so either input path stays in sync with the other.
 *
 * Menu/setup navigation stays ray-based — point and select — since those are
 * occasional, at-a-distance actions. The games themselves use the grab
 * system instead: their learning objects sit within arm's reach and you
 * pick them up and physically place/throw them, so all content is attached
 * to the player rig (not the static scene) and stays reachable no matter
 * where the player walks.
 */

const SUBJECTS = {
  maths: { module: mathGame, accent: "#5b8cff" },
  physics: { module: physicsGame, accent: "#22d3ee" },
  chemistry: { module: chemistryGame, accent: "#a78bfa" }
};

const DIFFICULTIES = [
  { id: "easy", label: "Easy", color: "#34d399" },
  { id: "medium", label: "Medium", color: "#fbbf24" },
  { id: "hard", label: "Hard", color: "#f87171" }
];

const tabButtons = () => document.querySelectorAll("[data-subject]");
const titleEl = () => document.getElementById("learn-title");
const descEl = () => document.getElementById("learn-desc");
const statusEl = () => document.getElementById("learn-status");
const enterVRBtn = () => document.getElementById("learn-enter-vr");
const setupPanelEl = () => document.getElementById("learn-setup");
const topicOptionsEl = () => document.getElementById("learn-topic-options");
const difficultyOptionsEl = () => document.getElementById("learn-difficulty-options");
const startBtnEl = () => document.getElementById("learn-start-btn");

let interaction = null;
let grab = null;
let roomRef = null; // the player rig — game/menu content is attached here so it moves with the player
let sceneRef = null;
let menuGroup = null;
let setupGroup = null; // 3D topic/difficulty picker for the pending subject
let backButton = null; // in-game "back to subjects" button
let activeGame = null;
let activeSubject = null;
let updateFn = null;

// Pending selection while the player is on the setup screen (not playing yet).
let pendingSubject = null;
let pendingTopic = null;
let pendingDifficulty = "easy";
let htmlTopicButtons = [];
let htmlDifficultyButtons = [];
let vrTopicCards = []; // { id, mesh }
let vrDifficultyCards = [];

function setStatus(message, isError = false) {
  const el = statusEl();
  el.textContent = message;
  el.classList.toggle("is-error", isError);
}

function buildMenu() {
  const menu = new THREE.Group();
  menu.name = "learnMenu";

  const heading = createTextPanel({ width: 2.2, height: 0.42, fontSize: 48 });
  heading.position.set(0, 2.4, -2.0);
  heading.userData.setText([
    { text: "Choose a subject", bold: true, size: 56 },
    { text: "Point and pull the trigger (or click)", size: 28, color: "#8fa3c8" },
    { text: "Room looks off? Squeeze either grip to recenter", size: 22, color: "#5b8cff" }
  ]);
  menu.add(heading);

  Object.entries(SUBJECTS).forEach(([id, { module, accent }], i) => {
    const card = createTextPanel({ width: 0.95, height: 0.75, fontSize: 34, border: accent });
    card.position.set((i - 1) * 1.3, 1.6, -1.9);
    card.rotation.y = (1 - i) * 0.22;
    card.userData.setText([
      { text: id.charAt(0).toUpperCase() + id.slice(1), bold: true, size: 52, color: accent },
      { text: module.meta.title, size: 34 },
      { text: module.meta.tagline, size: 26, color: "#8fa3c8" }
    ]);
    menu.add(card);

    interaction.add(card, {
      onSelect: () => showSetup(id),
      onHoverStart: () => card.scale.setScalar(1.06),
      onHoverEnd: () => card.scale.setScalar(1)
    });
  });

  return menu;
}

function clearSetupMenu3D() {
  if (!setupGroup) return;
  [...vrTopicCards.map((t) => t.mesh), ...vrDifficultyCards.map((d) => d.mesh)].forEach((mesh) => interaction.remove(mesh));
  roomRef.remove(setupGroup);
  disposeTree(setupGroup);
  setupGroup = null;
  vrTopicCards = [];
  vrDifficultyCards = [];
}

function buildSetupMenu3D(subjectId) {
  const subject = SUBJECTS[subjectId];
  const group = new THREE.Group();
  group.name = "learnSetupMenu";

  const heading = createTextPanel({ width: 2.0, height: 0.36, fontSize: 40, border: subject.accent });
  heading.position.set(0, 2.3, -1.7);
  heading.userData.setText([
    { text: subject.module.meta.title, bold: true, size: 44, color: subject.accent },
    { text: "Pick a topic and difficulty", size: 24, color: "#8fa3c8" }
  ]);
  group.add(heading);

  const topics = subject.module.meta.topics;
  topics.forEach((t, i) => {
    const card = createTextPanel({ width: 0.85, height: 0.32, fontSize: 26, border: subject.accent });
    card.position.set((i - (topics.length - 1) / 2) * 0.95, 1.78, -1.6);
    card.userData.setText([{ text: t.label, bold: true, size: 28 }]);
    group.add(card);
    vrTopicCards.push({ id: t.id, mesh: card });
    interaction.add(card, {
      onSelect: () => selectTopic(t.id),
      onHoverStart: () => card.scale.setScalar(1.04),
      onHoverEnd: () => refreshSetupHighlight()
    });
  });

  DIFFICULTIES.forEach((d, i) => {
    const card = createTextPanel({ width: 0.55, height: 0.26, fontSize: 24, border: d.color });
    card.position.set((i - 1) * 0.65, 1.35, -1.55);
    card.userData.setText([{ text: d.label, bold: true, size: 26, color: d.color }]);
    group.add(card);
    vrDifficultyCards.push({ id: d.id, mesh: card });
    interaction.add(card, {
      onSelect: () => selectDifficulty(d.id),
      onHoverStart: () => card.scale.setScalar(1.1),
      onHoverEnd: () => refreshSetupHighlight()
    });
  });

  const startButton = createButton3D("Start ▶", { width: 0.5, height: 0.17, accent: "#34d399", fontSize: 44 });
  startButton.position.set(0.55, 0.95, -1.4);
  group.add(startButton);
  interaction.add(startButton, {
    onSelect: confirmStart,
    onHoverStart: startButton.userData.onHoverStart,
    onHoverEnd: startButton.userData.onHoverEnd
  });

  const backButton3D = createButton3D("◀ Subjects", { width: 0.5, height: 0.15, accent: "#f472b6", fontSize: 36 });
  backButton3D.position.set(-0.55, 0.95, -1.4);
  group.add(backButton3D);
  interaction.add(backButton3D, {
    onSelect: showMenu,
    onHoverStart: backButton3D.userData.onHoverStart,
    onHoverEnd: backButton3D.userData.onHoverEnd
  });

  roomRef.add(group);
  setupGroup = group;
}

function renderSetupHTML(subjectId) {
  const subject = SUBJECTS[subjectId];

  const topicRow = topicOptionsEl();
  topicRow.innerHTML = "";
  htmlTopicButtons = subject.module.meta.topics.map((t) => {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "learn-chip";
    btn.dataset.topic = t.id;
    btn.textContent = t.label;
    btn.addEventListener("click", () => selectTopic(t.id));
    topicRow.appendChild(btn);
    return btn;
  });

  const diffRow = difficultyOptionsEl();
  diffRow.innerHTML = "";
  htmlDifficultyButtons = DIFFICULTIES.map((d) => {
    const btn = document.createElement("button");
    btn.type = "button";
    btn.className = "learn-chip";
    btn.dataset.difficulty = d.id;
    btn.textContent = d.label;
    btn.addEventListener("click", () => selectDifficulty(d.id));
    diffRow.appendChild(btn);
    return btn;
  });

  setupPanelEl().hidden = false;
}

function selectTopic(id) {
  pendingTopic = id;
  refreshSetupHighlight();
}

function selectDifficulty(id) {
  pendingDifficulty = id;
  refreshSetupHighlight();
}

function refreshSetupHighlight() {
  htmlTopicButtons.forEach((b) => b.classList.toggle("is-active", b.dataset.topic === pendingTopic));
  htmlDifficultyButtons.forEach((b) => b.classList.toggle("is-active", b.dataset.difficulty === pendingDifficulty));
  vrTopicCards.forEach(({ id, mesh }) => mesh.scale.setScalar(id === pendingTopic ? 1.08 : 1));
  vrDifficultyCards.forEach(({ id, mesh }) => mesh.scale.setScalar(id === pendingDifficulty ? 1.15 : 1));
}

function syncTabs() {
  const highlighted = activeSubject ?? pendingSubject;
  tabButtons().forEach((btn) => btn.classList.toggle("is-active", btn.dataset.subject === highlighted));
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

function hideSetup() {
  clearSetupMenu3D();
  setupPanelEl().hidden = true;
  pendingSubject = null;
  htmlTopicButtons = [];
  htmlDifficultyButtons = [];
}

function showMenu() {
  stopGame();
  hideSetup();
  menuGroup.visible = true;
  syncTabs();
  const title = titleEl();
  if (title) {
    title.textContent = "Pick a subject to start its minigame";
    descEl().textContent = "Each game runs in the 3D room behind this panel — playable with the mouse here, or with your hands in VR.";
  }
}

function showSetup(subjectId) {
  const subject = SUBJECTS[subjectId];
  if (!subject) return;
  stopGame();
  clearSetupMenu3D();

  pendingSubject = subjectId;
  pendingTopic = subject.module.meta.topics[0].id;
  pendingDifficulty = "easy";

  menuGroup.visible = false;
  buildSetupMenu3D(subjectId);
  renderSetupHTML(subjectId);
  refreshSetupHighlight();

  syncTabs();
  const title = titleEl();
  if (title) {
    title.textContent = subject.module.meta.title;
    descEl().textContent = `${subject.module.meta.tagline} — choose a topic and difficulty, then press Start.`;
  }
}

function confirmStart() {
  if (!pendingSubject) return;
  startGame(pendingSubject, { topic: pendingTopic, difficulty: pendingDifficulty });
}

function startGame(subjectId, config) {
  const subject = SUBJECTS[subjectId];
  if (!subject) return;
  hideSetup();
  stopGame();

  menuGroup.visible = false;
  activeSubject = subjectId;
  activeGame = subject.module.createGame({ interaction, grab, config });
  roomRef.add(activeGame.group);

  backButton = createButton3D("◀ Menu", { width: 0.4, height: 0.15, accent: "#f472b6", fontSize: 44 });
  backButton.position.set(-1.0, 1.3, -0.5);
  backButton.rotation.y = 0.5;
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
      onWaiting: () => setStatus("Still connecting — put on your headset and look for a prompt there to allow VR."),
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
  showSetup(event.currentTarget.dataset.subject);
}

function handleStartClick() {
  confirmStart();
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
  startBtnEl().addEventListener("click", handleStartClick);
  enterVRBtn().addEventListener("click", handleEnterVR);
  enterVRBtn().disabled = false;
  setStatus("");
  showMenu();
}

export function unmount() {
  tabButtons().forEach((btn) => btn.removeEventListener("click", handleTabClick));
  startBtnEl().removeEventListener("click", handleStartClick);
  enterVRBtn().removeEventListener("click", handleEnterVR);

  xrState.updatables.delete(updateFn);
  updateFn = null;

  stopGame();
  hideSetup();
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
