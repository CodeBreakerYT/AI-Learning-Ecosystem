import { xrState } from "../core/xrState.js";
import { getCurrentUser } from "../core/authState.js";
import { getProfile, loadProfile, saveProfile } from "../core/profileState.js";
import { createCustomSelect } from "../core/customSelect.js";
import { getHeadsetMeta, renderHeadsetSelectHTML } from "../core/headsets.js";

const listEl = document.getElementById("device-list");
const previewIcon = document.getElementById("headset-preview-icon");
const previewLabel = document.getElementById("headset-preview-label");
const selectContainer = document.getElementById("device-headset-select");
const headsetStatusEl = document.getElementById("device-headset-status");

let intervalId = null;
let headsetSelect = null;

// Frame delta is the one thing the browser actually exposes — there's no
// WebXR API for headset signal strength, so render smoothness is used as an
// honest stand-in for "connection quality."
function classifySession(frameDelta) {
  if (frameDelta <= 0.0135) return { status: "green", label: "Strong" };
  if (frameDelta <= 0.022) return { status: "yellow", label: "Medium" };
  return { status: "red", label: "Weak" };
}

function renderRow({ label, sub, status }) {
  const li = document.createElement("li");
  li.className = "device-row";
  li.innerHTML = `
    <span class="device-dot is-${status}"></span>
    <span class="device-info">
      <span class="device-label">${label}</span>
      <span class="device-sub">${sub}</span>
    </span>
  `;
  return li;
}

function updatePreview(headsetValue) {
  const meta = getHeadsetMeta(headsetValue);
  if (!meta) {
    previewLabel.textContent = "No headset selected";
    previewIcon.style.color = "#6b7287";
    return;
  }
  previewLabel.textContent = meta.label;
  previewIcon.style.color = meta.color;
}

function refreshDeviceList() {
  const headsetLabel = getHeadsetMeta(getProfile()?.headset)?.label || "Headset";
  const session = xrState.renderer?.xr.getSession();
  const rows = [];

  if (!session) {
    rows.push({ label: headsetLabel, sub: "Not connected — attach in VR Setup", status: "red" });
  } else {
    const { status, label } = classifySession(xrState.frameDelta);
    rows.push({ label: headsetLabel, sub: `Active session — ${label} connection`, status });

    const controllers = Array.from(session.inputSources).filter((source) => source.gamepad);
    if (controllers.length === 0) {
      rows.push({ label: "Controllers", sub: "None detected", status: "yellow" });
    } else {
      controllers.forEach((source, i) => {
        rows.push({
          label: `Controller (${source.handedness !== "none" ? source.handedness : i + 1})`,
          sub: "Connected",
          status: "green"
        });
      });
    }
  }

  listEl.replaceChildren(...rows.map(renderRow));
}

async function handleHeadsetChange(event) {
  const user = getCurrentUser();
  const headset = event.detail.value;
  updatePreview(headset);
  refreshDeviceList();
  if (!user) return;

  headsetStatusEl.textContent = "Saving...";
  headsetStatusEl.classList.remove("is-error");
  try {
    await saveProfile(user.uid, { headset });
    headsetStatusEl.textContent = "Headset updated.";
  } catch (err) {
    headsetStatusEl.textContent = err.message;
    headsetStatusEl.classList.add("is-error");
  }
}

export async function mount() {
  const user = getCurrentUser();
  if (user && !getProfile()) {
    await loadProfile(user.uid, user);
  }

  const headsetValue = getProfile()?.headset || "meta-quest";
  selectContainer.innerHTML = renderHeadsetSelectHTML(headsetValue);
  headsetSelect = createCustomSelect(selectContainer);
  selectContainer.addEventListener("change", handleHeadsetChange);

  updatePreview(getProfile()?.headset);
  refreshDeviceList();
  intervalId = setInterval(refreshDeviceList, 1000);
}

export function unmount() {
  clearInterval(intervalId);
  intervalId = null;
  selectContainer.removeEventListener("change", handleHeadsetChange);
  headsetSelect?.destroy();
  headsetSelect = null;
}
