import * as THREE from "three";
import { createTextPanel, createLabel, disposeTree } from "../../core/textPanel.js";

/**
 * Maths — "Equation Orbs". A question panel shows a basic arithmetic problem
 * and four numbered orbs float in front of the player; point (controller ray
 * in VR, mouse on desktop) and select the right answer. Difficulty ramps from
 * addition to subtraction to multiplication as the score grows.
 */
export function createGame({ interaction }) {
  const group = new THREE.Group();
  group.name = "mathGame";

  let score = 0;
  let streak = 0;
  let locked = false; // ignore input while the "correct!" animation plays
  let elapsed = 0;
  const timers = new Set();

  const questionPanel = createTextPanel({ width: 1.7, height: 0.55, fontSize: 60 });
  questionPanel.position.set(0, 2.15, -1.4);
  group.add(questionPanel);

  const scorePanel = createTextPanel({ width: 0.9, height: 0.34, fontSize: 40, border: "rgba(52, 211, 153, 0.8)" });
  scorePanel.position.set(1.6, 2.15, -1.2);
  scorePanel.rotation.y = -0.35;
  group.add(scorePanel);

  const feedbackPanel = createTextPanel({ width: 1.3, height: 0.3, fontSize: 38, border: "rgba(167, 139, 250, 0.8)" });
  feedbackPanel.position.set(-1.65, 2.15, -1.2);
  feedbackPanel.rotation.y = 0.35;
  group.add(feedbackPanel);

  const ORB_COLORS = [0x5b8cff, 0x22d3ee, 0xa78bfa, 0xf472b6];
  const orbs = [];
  for (let i = 0; i < 4; i++) {
    const orb = new THREE.Group();
    const sphere = new THREE.Mesh(
      new THREE.SphereGeometry(0.22, 32, 24),
      new THREE.MeshStandardMaterial({
        color: ORB_COLORS[i],
        emissive: ORB_COLORS[i],
        emissiveIntensity: 0.25,
        roughness: 0.35
      })
    );
    orb.add(sphere);
    const label = createLabel("0", { width: 0.5, height: 0.25 });
    label.position.z = 0.24;
    orb.add(label);

    orb.position.set(-1.05 + i * 0.7, 1.35, -0.9);
    orb.userData = { sphere, label, baseY: orb.position.y, phase: i * 1.7, value: 0 };
    group.add(orb);
    orbs.push(orb);

    interaction.add(orb, {
      onSelect: () => handlePick(orb),
      onHoverStart: () => !locked && orb.scale.setScalar(1.18),
      onHoverEnd: () => orb.scale.setScalar(1)
    });
  }

  function updateScore() {
    scorePanel.userData.setText([
      { text: `Score  ${score}`, bold: true, size: 46, color: "#34d399" },
      { text: `Streak ${streak}`, size: 34 }
    ]);
  }

  function setFeedback(text, color = "#e8ecf6") {
    feedbackPanel.userData.setText([{ text, color, size: 38 }]);
  }

  function randomInt(min, max) {
    return min + Math.floor(Math.random() * (max - min + 1));
  }

  let correctAnswer = 0;

  function nextQuestion() {
    let a, b, op, answer, topic;
    if (score < 3) {
      [a, b, op, topic] = [randomInt(1, 9), randomInt(1, 9), "+", "Addition"];
      answer = a + b;
    } else if (score < 6) {
      a = randomInt(5, 18); b = randomInt(1, a); op = "−"; topic = "Subtraction";
      answer = a - b;
    } else {
      [a, b, op, topic] = [randomInt(2, 9), randomInt(2, 9), "×", "Multiplication"];
      answer = a * b;
    }
    correctAnswer = answer;

    questionPanel.userData.setText([
      { text: topic, size: 30, color: "#8fa3c8" },
      { text: `${a} ${op} ${b} = ?`, bold: true, size: 76 }
    ]);

    // Correct answer plus three distinct nearby distractors.
    const values = new Set([answer]);
    while (values.size < 4) {
      const offset = randomInt(1, 4) * (Math.random() < 0.5 ? -1 : 1);
      const candidate = answer + offset;
      if (candidate >= 0) values.add(candidate);
    }
    const shuffled = [...values].sort(() => Math.random() - 0.5);
    orbs.forEach((orb, i) => {
      orb.userData.value = shuffled[i];
      orb.userData.label.userData.setText(String(shuffled[i]));
      orb.userData.sphere.material.emissiveIntensity = 0.25;
      orb.visible = true;
      orb.scale.setScalar(1);
    });
    locked = false;
  }

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
  }

  function handlePick(orb) {
    if (locked || !orb.visible) return;
    if (orb.userData.value === correctAnswer) {
      locked = true;
      score += 1;
      streak += 1;
      orb.userData.sphere.material.emissiveIntensity = 1.2;
      setFeedback(streak >= 3 ? `Correct! ${streak} in a row 🔥` : "Correct!", "#34d399");
      updateScore();
      later(250, () => { orb.visible = false; });
      later(900, nextQuestion);
    } else {
      streak = 0;
      orb.visible = false;
      setFeedback(`${orb.userData.value} isn't it — try again!`, "#f87171");
      updateScore();
    }
  }

  setFeedback("Pick the right answer!");
  updateScore();
  nextQuestion();

  return {
    group,
    update(delta) {
      elapsed += delta;
      for (const orb of orbs) {
        orb.position.y = orb.userData.baseY + Math.sin(elapsed * 1.4 + orb.userData.phase) * 0.06;
        orb.rotation.y += delta * 0.4;
      }
    },
    dispose() {
      timers.forEach(clearTimeout);
      orbs.forEach((orb) => interaction.remove(orb));
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "maths",
  title: "Equation Orbs",
  tagline: "Solve it, then zap the right orb",
  howTo: "Read the equation on the board, then select the orb with the correct answer — trigger in VR, click on desktop. Three levels: add, subtract, multiply."
};
