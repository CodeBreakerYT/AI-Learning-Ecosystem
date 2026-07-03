import * as THREE from "three";

/**
 * Canvas-texture text helpers so the minigames have readable UI inside a
 * headset, where the HTML overlay isn't visible. Panels are rounded-rect
 * planes with word-wrapped multiline text; labels are lightweight transparent
 * planes for tagging orbs, atoms and buttons.
 */

const PX_PER_METER = 512;

function roundRect(ctx, x, y, w, h, r) {
  ctx.beginPath();
  ctx.moveTo(x + r, y);
  ctx.arcTo(x + w, y, x + w, y + h, r);
  ctx.arcTo(x + w, y + h, x, y + h, r);
  ctx.arcTo(x, y + h, x, y, r);
  ctx.arcTo(x, y, x + w, y, r);
  ctx.closePath();
}

/**
 * A rounded panel that renders multiline text. Returns a mesh with
 * `setText(lines)` — pass a string (auto-wrapped) or an array of
 * { text, size?, color?, bold? } line objects.
 */
export function createTextPanel({
  width = 1.2,
  height = 0.6,
  background = "rgba(16, 20, 30, 0.92)",
  border = "rgba(91, 140, 255, 0.8)",
  color = "#e8ecf6",
  fontSize = 44
} = {}) {
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(width * PX_PER_METER);
  canvas.height = Math.round(height * PX_PER_METER);
  const ctx = canvas.getContext("2d");

  const texture = new THREE.CanvasTexture(canvas);
  texture.anisotropy = 4;
  texture.colorSpace = THREE.SRGBColorSpace;

  const mesh = new THREE.Mesh(
    new THREE.PlaneGeometry(width, height),
    new THREE.MeshBasicMaterial({ map: texture, transparent: true })
  );

  function normalizeLines(input) {
    const raw = Array.isArray(input) ? input : String(input).split("\n");
    return raw.map((line) =>
      typeof line === "string" ? { text: line } : line
    );
  }

  mesh.userData.setText = (input) => {
    const lines = normalizeLines(input);
    ctx.clearRect(0, 0, canvas.width, canvas.height);

    roundRect(ctx, 4, 4, canvas.width - 8, canvas.height - 8, 28);
    ctx.fillStyle = background;
    ctx.fill();
    ctx.lineWidth = 5;
    ctx.strokeStyle = border;
    ctx.stroke();

    ctx.textAlign = "center";
    ctx.textBaseline = "middle";

    const heights = lines.map((l) => (l.size ?? fontSize) * 1.35);
    const totalHeight = heights.reduce((a, b) => a + b, 0);
    let y = canvas.height / 2 - totalHeight / 2;

    lines.forEach((line, i) => {
      const size = line.size ?? fontSize;
      ctx.font = `${line.bold ? "700" : "500"} ${size}px "Space Grotesk", "Inter", sans-serif`;
      ctx.fillStyle = line.color ?? color;
      ctx.fillText(line.text, canvas.width / 2, y + heights[i] / 2, canvas.width - 60);
      y += heights[i];
    });
    texture.needsUpdate = true;
  };

  mesh.userData.dispose = () => {
    texture.dispose();
    mesh.geometry.dispose();
    mesh.material.dispose();
  };

  return mesh;
}

/**
 * A borderless transparent label (single line) for tagging 3D objects,
 * e.g. the number on an answer orb or the symbol on an atom.
 */
export function createLabel(text, {
  width = 0.4,
  height = 0.2,
  color = "#ffffff",
  fontSize = 96,
  bold = true
} = {}) {
  const canvas = document.createElement("canvas");
  canvas.width = Math.round(width * PX_PER_METER);
  canvas.height = Math.round(height * PX_PER_METER);
  const ctx = canvas.getContext("2d");

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;

  const mesh = new THREE.Mesh(
    new THREE.PlaneGeometry(width, height),
    new THREE.MeshBasicMaterial({ map: texture, transparent: true, depthWrite: false })
  );

  mesh.userData.setText = (value) => {
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    ctx.font = `${bold ? "700" : "500"} ${fontSize}px "Space Grotesk", "Inter", sans-serif`;
    ctx.textAlign = "center";
    ctx.textBaseline = "middle";
    ctx.lineWidth = 10;
    ctx.strokeStyle = "rgba(0, 0, 0, 0.6)";
    ctx.strokeText(value, canvas.width / 2, canvas.height / 2, canvas.width - 20);
    ctx.fillStyle = color;
    ctx.fillText(value, canvas.width / 2, canvas.height / 2, canvas.width - 20);
    texture.needsUpdate = true;
  };
  mesh.userData.setText(text);

  mesh.userData.dispose = () => {
    texture.dispose();
    mesh.geometry.dispose();
    mesh.material.dispose();
  };

  return mesh;
}

/**
 * A clickable 3D button: rounded panel + hover handlers pre-wired for the
 * interaction manager (register with `interaction.add(btn, { onSelect })`
 * or use the returned helpers).
 */
export function createButton3D(text, {
  width = 0.42,
  height = 0.16,
  accent = "#5b8cff",
  fontSize = 52
} = {}) {
  const button = createTextPanel({
    width,
    height,
    background: "rgba(24, 32, 50, 0.95)",
    border: accent,
    fontSize
  });
  button.userData.setText([{ text, bold: true }]);
  button.userData.baseScale = 1;
  button.userData.onHoverStart = () => button.scale.setScalar(1.08);
  button.userData.onHoverEnd = () => button.scale.setScalar(1);
  button.userData.label = text;
  return button;
}

/** Recursively dispose any canvas-texture UI meshes under a group. */
export function disposeTree(root) {
  root.traverse((node) => {
    if (node.userData?.dispose) {
      node.userData.dispose();
    } else if (node.isMesh) {
      node.geometry?.dispose();
      if (Array.isArray(node.material)) node.material.forEach((m) => m.dispose());
      else node.material?.dispose();
    }
  });
}
