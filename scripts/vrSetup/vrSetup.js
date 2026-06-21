import { serverTimestamp } from "firebase/firestore";
import { xrState } from "../core/xrState.js";
import { connectVRSession } from "../core/xrSession.js";
import { createCustomSelect } from "../core/customSelect.js";
import { getCurrentUser } from "../core/authState.js";
import { saveProfile } from "../core/profileState.js";

const attachBtn = document.getElementById("vr-attach-btn");
const statusEl = document.getElementById("vr-status");
let headsetSelect;

function setStatus(message, isError = false) {
  statusEl.textContent = message;
  statusEl.classList.toggle("is-error", isError);
}

async function saveHeadsetPreference(headset) {
  const user = getCurrentUser();
  if (!user) return;
  await saveProfile(user.uid, { headset, connectedAt: serverTimestamp() });
}

async function handleAttach() {
  attachBtn.disabled = true;
  setStatus("Connecting to your headset...");
  try {
    await connectVRSession(xrState.renderer, {
      onConnected: () => setStatus("Connected! Put on your headset and use the thumbstick to move around."),
      onEnded: () => {
        setStatus("VR session ended. Attach again whenever you're ready.");
        attachBtn.disabled = false;
      }
    });
    saveHeadsetPreference(headsetSelect.value);
  } catch (err) {
    setStatus(err.message, true);
    attachBtn.disabled = false;
  }
}

export function mount() {
  headsetSelect = createCustomSelect(document.getElementById("vr-headset-select"));
  setStatus("Choose your headset, then attach to enter VR.");
  attachBtn.disabled = false;
  attachBtn.addEventListener("click", handleAttach);
}

export function unmount() {
  attachBtn.removeEventListener("click", handleAttach);
  headsetSelect?.destroy();
}
