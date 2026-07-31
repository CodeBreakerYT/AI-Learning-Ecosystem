import * as THREE from "three";
import { createTextPanel, createLabel, disposeTree } from "../../core/textPanel.js";
import { spawnBurst, spawnShockwave } from "../../core/effects.js";

const BLOCK_COLORS = [0x5b8cff, 0x22d3ee, 0xa78bfa, 0xf472b6];
const BASKET_RADIUS = 0.22;
const RETURN_SPEED = 6; // how fast a dropped-outside block eases back to its rack slot

const TOPICS = [
  { id: "addition", label: "Addition", symbol: "+" },
  { id: "subtraction", label: "Subtraction", symbol: "−" },
  { id: "multiplication", label: "Multiplication", symbol: "×" },
  { id: "mixed", label: "Mixed Review", symbol: "±×" }
];

// Number ranges and distractor spread per difficulty — hard both uses
// bigger operands AND crowds the wrong answers closer to the real one, so
// scanning-for-the-biggest-block stops working as a strategy.
const DIFFICULTY = {
  easy: { add: [1, 9], sub: [5, 15], mul: [1, 5], distractor: [1, 3] },
  medium: { add: [8, 40], sub: [15, 50], mul: [3, 9], distractor: [2, 6] },
  hard: { add: [25, 99], sub: [40, 120], mul: [6, 12], distractor: [3, 9] }
};

function randomInt(min, max) {
  return min + Math.floor(Math.random() * (max - min + 1));
}

function generateQuestion(topic, difficulty) {
  const ranges = DIFFICULTY[difficulty] ?? DIFFICULTY.easy;
  const resolvedTopic = topic === "mixed"
    ? ["addition", "subtraction", "multiplication"][randomInt(0, 2)]
    : topic;

  let a, b, op, answer;
  if (resolvedTopic === "subtraction") {
    const [lo, hi] = ranges.sub;
    a = randomInt(lo, hi);
    b = randomInt(1, a);
    op = "−";
    answer = a - b;
  } else if (resolvedTopic === "multiplication") {
    const [lo, hi] = ranges.mul;
    a = randomInt(lo, hi);
    b = randomInt(lo, hi);
    op = "×";
    answer = a * b;
  } else {
    const [lo, hi] = ranges.add;
    a = randomInt(lo, hi);
    b = randomInt(lo, hi);
    op = "+";
    answer = a + b;
  }

  const topicMeta = TOPICS.find((t) => t.id === resolvedTopic);
  return { a, b, op, answer, topicLabel: topicMeta?.label ?? "Maths", distractorRange: ranges.distractor };
}

/**
 * Maths — "Block Toss". A number block sits within arm's reach for each
 * possible answer; grab one and physically drop it into the glowing basket
 * to submit it. Miss the basket and the block eases itself back to the rack
 * so you can try again. Difficulty ramps from addition to subtraction to
 * multiplication as your score grows.
 */
export function createGame({ grab, config = {} }) {
  const group = new THREE.Group();
  group.name = "mathGame";

  const topic = config.topic ?? "addition";
  const difficulty = config.difficulty ?? "easy";

  let score = 0;
  let streak = 0;
  let locked = false;
  let elapsed = 0;
  const timers = new Set();
  const localPos = new THREE.Vector3();

  const questionPanel = createTextPanel({ width: 1.5, height: 0.5, fontSize: 54 });
  questionPanel.position.set(0, 2.0, -2.0);
  group.add(questionPanel);

  const scorePanel = createTextPanel({ width: 0.75, height: 0.3, fontSize: 34, border: "rgba(52, 211, 153, 0.8)" });
  scorePanel.position.set(1.35, 1.6, -1.9);
  scorePanel.rotation.y = -0.4;
  group.add(scorePanel);

  const feedbackPanel = createTextPanel({ width: 1.1, height: 0.28, fontSize: 32, border: "rgba(167, 139, 250, 0.8)" });
  feedbackPanel.position.set(-1.35, 1.6, -1.9);
  feedbackPanel.rotation.y = 0.4;
  group.add(feedbackPanel);

  // --- Basket (the goal you toss blocks into) --------------------------------
  const basket = new THREE.Group();
  basket.position.set(0, 0.95, -0.55);
  group.add(basket);

  const basketRing = new THREE.Mesh(
    new THREE.TorusGeometry(BASKET_RADIUS, 0.02, 12, 40),
    new THREE.MeshStandardMaterial({ color: 0x34d399, emissive: 0x34d399, emissiveIntensity: 0.5 })
  );
  basketRing.rotation.x = Math.PI / 2;
  basket.add(basketRing);

  const basketWell = new THREE.Mesh(
    new THREE.CircleGeometry(BASKET_RADIUS * 0.9, 32),
    new THREE.MeshBasicMaterial({ color: 0x152030, transparent: true, opacity: 0.6, side: THREE.DoubleSide })
  );
  basketWell.rotation.x = -Math.PI / 2;
  basketWell.position.y = -0.01;
  basket.add(basketWell);

  const basketLabel = createTextPanel({ width: 0.55, height: 0.16, fontSize: 26, border: "rgba(52, 211, 153, 0.6)" });
  basketLabel.position.set(0, 0.24, 0);
  basketLabel.userData.setText([{ text: "Toss it here!", size: 24, color: "#34d399" }]);
  basket.add(basketLabel);

  function flashBasket(color) {
    basketRing.material.color.setHex(color);
    basketRing.material.emissiveIntensity = 1.4;
    later(400, () => {
      basketRing.material.color.setHex(0x34d399);
      basketRing.material.emissiveIntensity = 0.5;
    });
  }

  // --- Block rack (grabbable answer options) ---------------------------------
  const blocks = [];
  for (let i = 0; i < 4; i++) {
    const block = new THREE.Group();
    const homeLocal = new THREE.Vector3(-0.33 + i * 0.22, 1.08, -0.32);
    block.position.copy(homeLocal);

    const cube = new THREE.Mesh(
      new THREE.BoxGeometry(0.13, 0.13, 0.13),
      new THREE.MeshStandardMaterial({
        color: BLOCK_COLORS[i],
        emissive: BLOCK_COLORS[i],
        emissiveIntensity: 0.2,
        roughness: 0.4
      })
    );
    block.add(cube);

    const label = createLabel("0", { width: 0.28, height: 0.28, fontSize: 130 });
    label.position.z = 0.075;
    block.add(label);
    const labelBack = createLabel("0", { width: 0.28, height: 0.28, fontSize: 130 });
    labelBack.position.z = -0.075;
    labelBack.rotation.y = Math.PI;
    block.add(labelBack);

    block.userData = { cube, label, labelBack, home: homeLocal, value: 0, returning: false, resolved: false, held: false };
    group.add(block);
    blocks.push(block);

    grab.add(block, {
      onGrab: () => {
        block.userData.held = true;
        block.userData.returning = false;
        cube.material.emissiveIntensity = 0.6;
        spawnBurst(group, {
          position: block.position.clone(),
          colors: [`#${BLOCK_COLORS[i].toString(16).padStart(6, "0")}`],
          count: 8, speed: 0.5, size: 0.015, life: 0.3
        });
      },
      onRelease: () => {
        block.userData.held = false;
        handleRelease(block);
      },
      onHoverStart: () => { if (!block.userData.returning) cube.scale.setScalar(1.15); },
      onHoverEnd: () => cube.scale.setScalar(1)
    });
  }

  function setBlockValue(block, value) {
    block.userData.value = value;
    block.userData.label.userData.setText(String(value));
    block.userData.labelBack.userData.setText(String(value));
  }

  function updateScore() {
    scorePanel.userData.setText([
      { text: `Score ${score}`, bold: true, size: 38, color: "#34d399" },
      { text: `Streak ${streak}`, size: 26 }
    ]);
  }

  function setFeedback(text, color = "#e8ecf6") {
    feedbackPanel.userData.setText([{ text, color, size: 32 }]);
  }

  let correctAnswer = 0;

  function nextQuestion() {
    const { a, b, op, answer, topicLabel, distractorRange } = generateQuestion(topic, difficulty);
    correctAnswer = answer;

    questionPanel.userData.setText([
      { text: topicLabel, size: 26, color: "#8fa3c8" },
      { text: `${a} ${op} ${b} = ?`, bold: true, size: 68 }
    ]);

    const [distLo, distHi] = distractorRange;
    const values = new Set([answer]);
    while (values.size < 4) {
      const offset = randomInt(distLo, distHi) * (Math.random() < 0.5 ? -1 : 1);
      const candidate = answer + offset;
      if (candidate >= 0) values.add(candidate);
    }
    const shuffled = [...values].sort(() => Math.random() - 0.5);
    blocks.forEach((block, i) => {
      setBlockValue(block, shuffled[i]);
      block.userData.resolved = false;
      block.userData.returning = false;
      block.position.copy(block.userData.home);
      block.userData.cube.material.emissiveIntensity = 0.2;
      block.visible = true;
    });
    locked = false;
  }

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
  }

  function handleRelease(block) {
    if (locked || block.userData.resolved) return;
    const worldPos = block.getWorldPosition(new THREE.Vector3());
    const basketWorld = basket.getWorldPosition(new THREE.Vector3());
    const dist = worldPos.distanceTo(basketWorld);

    if (dist > BASKET_RADIUS) {
      block.userData.returning = true;
      return;
    }

    block.userData.resolved = true;
    block.userData.cube.material.emissiveIntensity = 1.2;

    if (block.userData.value === correctAnswer) {
      locked = true;
      score += 1;
      streak += 1;
      flashBasket(0x34d399);
      setFeedback(streak >= 3 ? `Nice! ${streak} in a row 🔥` : "Correct!", "#34d399");
      updateScore();

      const onFire = streak >= 3;
      spawnShockwave(group, { position: basket.position.clone(), color: "#34d399", radius: onFire ? 0.75 : 0.55 });
      spawnBurst(group, {
        position: basket.position.clone().add(new THREE.Vector3(0, 0.08, 0)),
        colors: onFire
          ? BLOCK_COLORS.map((c) => `#${c.toString(16).padStart(6, "0")}`)
          : ["#34d399", "#8fa3c8"],
        count: onFire ? 42 : 24,
        speed: onFire ? 2.1 : 1.5,
        life: 0.7
      });

      later(900, nextQuestion);
    } else {
      streak = 0;
      flashBasket(0xf87171);
      setFeedback(`${block.userData.value} isn't it — grab another!`, "#f87171");
      updateScore();
      spawnBurst(group, {
        position: basket.position.clone().add(new THREE.Vector3(0, 0.08, 0)),
        colors: ["#f87171"], count: 12, speed: 0.9, size: 0.02, life: 0.4
      });
      block.userData.resolved = false;
      later(500, () => { block.userData.returning = true; });
    }
  }

  setFeedback("Grab a block, drop it in the basket!");
  updateScore();
  nextQuestion();

  return {
    group,
    update(delta) {
      elapsed += delta;
      basketRing.rotation.z += delta * 0.5;

      for (const block of blocks) {
        if (block.userData.returning) {
          localPos.copy(block.userData.home);
          block.position.lerp(localPos, Math.min(1, RETURN_SPEED * delta));
          if (block.position.distanceTo(block.userData.home) < 0.01) {
            block.position.copy(block.userData.home);
            block.userData.returning = false;
          }
        } else if (block.visible && !block.userData.resolved && !block.userData.held) {
          block.rotation.y += delta * 0.6;
          block.position.y = block.userData.home.y + Math.sin(elapsed * 1.6 + block.userData.home.x * 5) * 0.015;
          block.userData.cube.material.emissiveIntensity = 0.2 + Math.sin(elapsed * 3 + block.userData.home.x * 5) * 0.1;
        }
      }
    },
    dispose() {
      timers.forEach(clearTimeout);
      blocks.forEach((block) => grab.remove(block));
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "maths",
  title: "Block Toss",
  tagline: "Grab it, toss it, solve it",
  howTo: "Read the equation, then grab the block with the right answer and drop it into the glowing basket. Miss and it eases back so you can try again.",
  topics: TOPICS
};
