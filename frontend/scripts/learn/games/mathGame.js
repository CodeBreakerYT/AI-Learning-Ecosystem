import * as THREE from "three";
import { createTextPanel, createLabel, createButton3D, disposeTree } from "../../core/textPanel.js";
import { spawnBurst, spawnShockwave } from "../../core/effects.js";

const BLOCK_COLORS = [0x5b8cff, 0x22d3ee, 0xa78bfa, 0xf472b6];
const BASKET_RADIUS = 0.22;
const RETURN_SPEED = 6; // how fast a missed block eases back to its rack slot
const GRAVITY = 9.81;
const THROW_BOOST = 1.6; // amplifies tracked hand speed so a natural toss carries to the basket
const MIN_THROW_SPEED = 0.4; // below this, a "release" just drops the block rather than throwing it
const FLOOR_Y = 0.07; // ~half the block's size — local-space resting height once it misses everything

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

// The quest: five rounds of the chosen topic, each needing a few more
// correct answers than the last, then a mixed-operation, timed Boss Round.
const LEVEL_TARGETS = [3, 4, 5, 6, 7];
const MAX_LIVES = 3;
const BOSS_TARGET = 5;
const BOSS_TIME_LIMIT = 7; // seconds to answer each boss question
const BEST_SCORE_PREFIX = "ale.mathGame.best";

function randomInt(min, max) {
  return min + Math.floor(Math.random() * (max - min + 1));
}

function generateQuestion(topic, difficulty, forceMixed = false) {
  const ranges = DIFFICULTY[difficulty] ?? DIFFICULTY.easy;
  const resolvedTopic = (topic === "mixed" || forceMixed)
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

function loadBest(topic, difficulty) {
  try { return Number(sessionStorage.getItem(`${BEST_SCORE_PREFIX}.${topic}.${difficulty}`)) || 0; }
  catch { return 0; }
}
function saveBest(topic, difficulty, value) {
  try { sessionStorage.setItem(`${BEST_SCORE_PREFIX}.${topic}.${difficulty}`, String(value)); } catch { /* ignore */ }
}

/**
 * Maths — "Block Toss". A traveling merchant needs blocks delivered to the
 * right basket: five rounds of the chosen topic (each needing a few more
 * correct deliveries than the last), then a timed, mixed-operation Boss
 * Round. Lose all three lives on a wrong delivery and the run ends —
 * clear the boss round for a star rating and a shot at your best score.
 */
export function createGame({ interaction, grab, config = {} }) {
  const group = new THREE.Group();
  group.name = "mathGame";

  const topic = config.topic ?? "addition";
  const difficulty = config.difficulty ?? "easy";
  const bestScore = loadBest(topic, difficulty);

  let level = 1;
  let levelProgress = 0;
  let lives = MAX_LIVES;
  let score = 0;
  let streak = 0;
  let isBoss = false;
  let runOver = false;
  let locked = false;
  let elapsed = 0;
  let bossTimeLeft = 0;
  let bossTimerActive = false;
  const timers = new Set();
  const localPos = new THREE.Vector3();
  let victoryButton = null;

  const questionPanel = createTextPanel({ width: 1.5, height: 0.5, fontSize: 54 });
  questionPanel.position.set(0, 2.0, -2.0);
  group.add(questionPanel);

  const hudPanel = createTextPanel({ width: 0.85, height: 0.36, fontSize: 28, border: "rgba(52, 211, 153, 0.8)" });
  hudPanel.position.set(1.35, 1.6, -1.9);
  hudPanel.rotation.y = -0.4;
  group.add(hudPanel);

  const feedbackPanel = createTextPanel({ width: 1.1, height: 0.28, fontSize: 32, border: "rgba(167, 139, 250, 0.8)" });
  feedbackPanel.position.set(-1.35, 1.6, -1.9);
  feedbackPanel.rotation.y = 0.4;
  group.add(feedbackPanel);

  // --- Basket (the goal you toss blocks into) --------------------------------
  const basket = new THREE.Group();
  // Height/depth tuned so the basket sits inside the default forward camera
  // view (no built-in pitch control on desktop) as well as comfortable VR
  // reach — a chest-height object this close to the camera falls well
  // outside a 70°-FOV frustum if placed at true waist height.
  basket.position.set(0, 1.26, -0.65);
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
      basketRing.material.color.setHex(isBoss ? 0xf472b6 : 0x34d399);
      basketRing.material.emissiveIntensity = 0.5;
    });
  }

  // --- Block rack (grabbable answer options) ---------------------------------
  const blocks = [];
  for (let i = 0; i < 4; i++) {
    const block = new THREE.Group();
    const homeLocal = new THREE.Vector3(-0.18 + i * 0.12, 1.34, -0.45);
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

    block.userData = {
      cube, label, labelBack, home: homeLocal, value: 0,
      returning: false, resolved: false, held: false, flying: false,
      velocity: new THREE.Vector3()
    };
    group.add(block);
    blocks.push(block);

    grab.add(block, {
      onGrab: () => {
        if (runOver) return;
        block.userData.held = true;
        block.userData.returning = false;
        block.userData.flying = false;
        cube.material.emissiveIntensity = 0.6;
        spawnBurst(group, {
          position: block.position.clone(),
          colors: [`#${BLOCK_COLORS[i].toString(16).padStart(6, "0")}`],
          count: 8, speed: 0.5, size: 0.015, life: 0.3
        });
      },
      onRelease: (obj, releaseVelocity) => {
        block.userData.held = false;
        throwBlock(block, releaseVelocity);
      },
      onHoverStart: () => { if (!block.userData.returning && !block.userData.flying) cube.scale.setScalar(1.15); },
      onHoverEnd: () => cube.scale.setScalar(1)
    });
  }

  function setBlockValue(block, value) {
    block.userData.value = value;
    block.userData.label.userData.setText(String(value));
    block.userData.labelBack.userData.setText(String(value));
  }

  function heartsText() {
    return "♥".repeat(Math.max(lives, 0)) + "♡".repeat(MAX_LIVES - Math.max(lives, 0));
  }

  function updateHud() {
    const levelLabel = isBoss ? "BOSS ROUND" : `Level ${level} / ${LEVEL_TARGETS.length}`;
    hudPanel.userData.setText([
      { text: levelLabel, bold: true, size: isBoss ? 26 : 30, color: isBoss ? "#f472b6" : "#34d399" },
      { text: heartsText(), size: 30, color: "#f87171" },
      { text: `Score ${score}   Best ${bestScore}`, size: 20, color: "#8fa3c8" }
    ]);
  }

  function setFeedback(text, color = "#e8ecf6") {
    feedbackPanel.userData.setText([{ text, color, size: 32 }]);
  }

  let correctAnswer = 0;
  let currentEquationText = "";
  let currentTopicLabel = "";

  function nextQuestion() {
    const { a, b, op, answer, topicLabel, distractorRange } = generateQuestion(topic, difficulty, isBoss);
    correctAnswer = answer;
    currentEquationText = `${a} ${op} ${b} = ?`;
    currentTopicLabel = topicLabel;

    questionPanel.userData.setText([
      { text: isBoss ? `${topicLabel} · ${bossTimeLeft}s` : topicLabel, size: 26, color: isBoss ? "#f472b6" : "#8fa3c8" },
      { text: currentEquationText, bold: true, size: 68 }
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
      block.userData.flying = false;
      block.userData.velocity.set(0, 0, 0);
      block.position.copy(block.userData.home);
      block.userData.cube.material.emissiveIntensity = 0.2;
      block.visible = true;
    });
    locked = false;

    if (isBoss) startBossTimer();
  }

  function later(ms, fn) {
    const id = setTimeout(() => { timers.delete(id); fn(); }, ms);
    timers.add(id);
    return id;
  }

  function cancelTimer(id) {
    clearTimeout(id);
    timers.delete(id);
  }

  let bossTimerId = null;
  function startBossTimer() {
    bossTimerActive = true;
    bossTimeLeft = BOSS_TIME_LIMIT;
    tickBossTimer();
  }
  function tickBossTimer() {
    if (bossTimerId) cancelTimer(bossTimerId);
    if (!bossTimerActive || locked || runOver) return;
    bossTimerId = later(1000, () => {
      bossTimeLeft -= 1;
      if (bossTimeLeft <= 0) {
        bossTimerActive = false;
        loseLife("Too slow!");
      } else {
        // Re-stamp just the timer text without regenerating the question.
        questionPanel.userData.setText([
          { text: `${currentTopicLabel} · ${bossTimeLeft}s`, size: 26, color: "#f472b6" },
          { text: currentEquationText, bold: true, size: 68 }
        ]);
        tickBossTimer();
      }
    });
  }

  function loseLife(reason) {
    if (runOver) return;
    // Stop the boss countdown immediately — otherwise a wrong drop right
    // before the timer's own next tick could double-penalize the player.
    bossTimerActive = false;
    if (bossTimerId) cancelTimer(bossTimerId);
    lives -= 1;
    streak = 0;
    updateHud();
    flashBasket(0xf87171);
    spawnBurst(group, {
      position: basket.position.clone().add(new THREE.Vector3(0, 0.08, 0)),
      colors: ["#f87171"], count: 14, speed: 1.0, size: 0.02, life: 0.4
    });
    if (lives <= 0) {
      setFeedback(reason, "#f87171");
      later(400, showDefeat);
    } else {
      setFeedback(`${reason} ${heartsText()}`, "#f87171");
      later(500, nextQuestion);
    }
  }

  function throwBlock(block, releaseVelocity) {
    if (locked || block.userData.resolved || runOver) {
      block.userData.returning = true;
      return;
    }
    const speed = releaseVelocity.length();
    block.userData.flying = true;
    block.userData.velocity.copy(releaseVelocity).multiplyScalar(speed < MIN_THROW_SPEED ? 1 : THROW_BOOST);
  }

  // Called every frame a block is flying, once it's either crossed into the
  // basket (a hit — right or wrong) or hit the floor (a clean miss, no
  // penalty, it just eases back to the rack).
  function resolveBasketEntry(block) {
    block.userData.flying = false;
    block.userData.resolved = true;
    block.userData.cube.material.emissiveIntensity = 1.2;

    if (block.userData.value === correctAnswer) {
      locked = true;
      bossTimerActive = false;
      score += 1;
      streak += 1;
      levelProgress += 1;
      flashBasket(0x34d399);

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

      const target = isBoss ? BOSS_TARGET : LEVEL_TARGETS[level - 1];
      if (levelProgress >= target) {
        if (isBoss) {
          later(700, showVictory);
        } else if (level >= LEVEL_TARGETS.length) {
          isBoss = true;
          levelProgress = 0;
          setFeedback("BOSS ROUND — mixed ops, beat the clock!", "#f472b6");
          updateHud();
          later(1000, nextQuestion);
        } else {
          level += 1;
          levelProgress = 0;
          setFeedback(`Level up! Round ${level} of ${LEVEL_TARGETS.length}`, "#34d399");
          updateHud();
          later(900, nextQuestion);
        }
      } else {
        setFeedback(streak >= 3 ? `Nice! ${streak} in a row 🔥` : "Correct!", "#34d399");
        updateHud();
        later(700, nextQuestion);
      }
    } else {
      block.userData.resolved = false;
      later(400, () => { block.userData.returning = true; });
      loseLife(`${block.userData.value} isn't it —`);
    }
  }

  function showBanner(lines) {
    questionPanel.userData.setText(lines);
    feedbackPanel.userData.setText([{ text: "", size: 1 }]);
  }

  function showDefeat() {
    runOver = true;
    bossTimerActive = false;
    blocks.forEach((b) => { b.visible = false; });
    showBanner([
      { text: "Out of hearts", bold: true, size: 48, color: "#f87171" },
      { text: `You delivered ${score} correct — try again?`, size: 26, color: "#8fa3c8" }
    ]);
    showReplayButton("Retry ↻", 0xf87171);
  }

  function showVictory() {
    runOver = true;
    bossTimerActive = false;
    blocks.forEach((b) => { b.visible = false; });
    const stars = lives >= 3 ? "★★★" : lives === 2 ? "★★☆" : "★☆☆";
    const newBest = score > bestScore;
    if (newBest) saveBest(topic, difficulty, score);

    showBanner([
      { text: "QUEST COMPLETE! 🎉", bold: true, size: 44, color: "#34d399" },
      { text: stars, size: 40, color: "#fbbf24" },
      { text: `Score ${score}${newBest ? "  — New best!" : `   Best ${Math.max(score, bestScore)}`}`, size: 24, color: "#8fa3c8" }
    ]);
    spawnShockwave(group, { position: basket.position.clone(), color: "#fbbf24", radius: 1.1 });
    spawnBurst(group, {
      position: basket.position.clone().add(new THREE.Vector3(0, 0.2, 0)),
      colors: BLOCK_COLORS.map((c) => `#${c.toString(16).padStart(6, "0")}`),
      count: 60, speed: 2.4, life: 0.9
    });
    showReplayButton("Play Again ▶", 0x34d399);
  }

  function showReplayButton(label, accent) {
    victoryButton = createButton3D(label, { width: 0.5, height: 0.17, accent: `#${accent.toString(16).padStart(6, "0")}`, fontSize: 42 });
    victoryButton.position.set(0, 1.3, -0.55);
    group.add(victoryButton);
    interaction.add(victoryButton, {
      onSelect: resetRun,
      onHoverStart: victoryButton.userData.onHoverStart,
      onHoverEnd: victoryButton.userData.onHoverEnd
    });
  }

  function clearReplayButton() {
    if (!victoryButton) return;
    interaction.remove(victoryButton);
    group.remove(victoryButton);
    disposeTree(victoryButton);
    victoryButton = null;
  }

  function resetRun() {
    clearReplayButton();
    level = 1;
    levelProgress = 0;
    lives = MAX_LIVES;
    score = 0;
    streak = 0;
    isBoss = false;
    runOver = false;
    setFeedback("Grab a block, drop it in the basket!");
    updateHud();
    nextQuestion();
  }

  setFeedback("Deliver the right block to the merchant's basket!");
  updateHud();
  nextQuestion();

  return {
    group,
    update(delta) {
      elapsed += delta;
      basketRing.rotation.z += delta * 0.5;

      for (const block of blocks) {
        const ud = block.userData;
        if (ud.held) {
          continue; // grabSystem drives position while held
        }

        if (ud.flying) {
          if (runOver) continue; // let the run end without fighting a still-airborne block
          ud.velocity.y -= GRAVITY * delta;
          block.position.addScaledVector(ud.velocity, delta);
          block.rotation.x += delta * 4;
          block.rotation.z += delta * 3;

          if (!locked) {
            const dx = block.position.x - basket.position.x;
            const dz = block.position.z - basket.position.z;
            const horizDist = Math.hypot(dx, dz);
            const crossingBasket = block.position.y <= basket.position.y && ud.velocity.y < 0;
            if (crossingBasket && horizDist <= BASKET_RADIUS) {
              resolveBasketEntry(block);
              continue;
            }
          }
          if (block.position.y <= FLOOR_Y && ud.velocity.y < 0) {
            // Missed the basket entirely — no penalty, just ease back to the rack.
            block.position.y = FLOOR_Y;
            ud.flying = false;
            if (!ud.resolved) ud.returning = true;
          }
        } else if (ud.returning) {
          localPos.copy(ud.home);
          block.position.lerp(localPos, Math.min(1, RETURN_SPEED * delta));
          if (block.position.distanceTo(ud.home) < 0.01) {
            block.position.copy(ud.home);
            ud.returning = false;
          }
        } else if (block.visible && !ud.resolved) {
          block.rotation.y += delta * 0.6;
          block.position.y = ud.home.y + Math.sin(elapsed * 1.6 + ud.home.x * 5) * 0.015;
          ud.cube.material.emissiveIntensity = 0.2 + Math.sin(elapsed * 3 + ud.home.x * 5) * 0.1;
        }
      }
    },
    dispose() {
      timers.forEach(clearTimeout);
      blocks.forEach((block) => grab.remove(block));
      clearReplayButton();
      disposeTree(group);
    }
  };
}

export const meta = {
  id: "maths",
  title: "Block Toss",
  tagline: "Grab it, toss it, solve it",
  howTo: "A merchant needs blocks delivered! Clear 5 rounds of the chosen topic, then survive a timed Boss Round mixing every operation. Three hearts — a wrong delivery costs one.",
  topics: TOPICS
};
