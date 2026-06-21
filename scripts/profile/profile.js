import { changeEmail, updateAuthProfile } from "../core/firebase.js";
import { resizeImageToDataURL } from "../core/imageUtils.js";
import { getCurrentUser, login } from "../core/authState.js";
import { getProfile, loadProfile, saveProfile } from "../core/profileState.js";

const avatarEl = document.getElementById("profile-edit-avatar");
const photoInput = document.getElementById("profile-photo-input");
const nameInput = document.getElementById("profile-name-input");
const aboutInput = document.getElementById("profile-about-input");
const saveBtn = document.getElementById("profile-save-btn");
const statusEl = document.getElementById("profile-status");

const currentEmailEl = document.getElementById("profile-current-email");
const emailInput = document.getElementById("profile-email-input");
const emailBtn = document.getElementById("profile-email-btn");
const emailStatusEl = document.getElementById("profile-email-status");

let pendingPhotoURL = null;

function setStatus(el, message, isError = false) {
  el.textContent = message;
  el.classList.toggle("is-error", isError);
}

function renderAvatar(photoURL, fallbackText) {
  if (photoURL) {
    avatarEl.style.backgroundImage = `url(${photoURL})`;
    avatarEl.textContent = "";
  } else {
    avatarEl.style.backgroundImage = "";
    avatarEl.textContent = (fallbackText || "?").trim().charAt(0).toUpperCase();
  }
}

function fillForm(profile) {
  nameInput.value = profile?.displayName || "";
  aboutInput.value = profile?.about || "";
  currentEmailEl.textContent = profile?.email || getCurrentUser()?.email || "—";
  pendingPhotoURL = profile?.photoURL || null;
  renderAvatar(pendingPhotoURL, profile?.displayName || profile?.email);
}

async function handlePhotoChange(event) {
  const file = event.target.files?.[0];
  if (!file) return;
  try {
    pendingPhotoURL = await resizeImageToDataURL(file, 128);
    renderAvatar(pendingPhotoURL, nameInput.value);
    setStatus(statusEl, "Photo ready — click \"Save changes\" to keep it.");
  } catch (err) {
    setStatus(statusEl, err.message, true);
  }
}

async function handleSave() {
  const user = getCurrentUser();
  if (!user) return;

  const updates = {
    displayName: nameInput.value.trim(),
    about: aboutInput.value.trim(),
    photoURL: pendingPhotoURL
  };

  setStatus(statusEl, "Saving...");
  try {
    await saveProfile(user.uid, updates);
    if (user.provider !== "test") {
      await updateAuthProfile({ displayName: updates.displayName, photoURL: updates.photoURL || undefined });
    }
    setStatus(statusEl, "Profile saved.");
  } catch (err) {
    setStatus(statusEl, err.message, true);
  }
}

async function handleEmailUpdate() {
  const user = getCurrentUser();
  const newEmail = emailInput.value.trim();
  if (!user) return;
  if (user.provider === "test") {
    setStatus(emailStatusEl, "The test admin account has no real email to change.", true);
    return;
  }
  if (!newEmail.includes("@")) {
    setStatus(emailStatusEl, "Enter a valid email address.", true);
    return;
  }

  setStatus(emailStatusEl, "Updating email...");
  try {
    await changeEmail(newEmail);
    await saveProfile(user.uid, { email: newEmail });
    login({ ...user, email: newEmail });
    currentEmailEl.textContent = newEmail;
    emailInput.value = "";
    setStatus(emailStatusEl, "Email updated.");
  } catch (err) {
    setStatus(emailStatusEl, err.message, true);
  }
}

export async function mount() {
  setStatus(statusEl, "");
  setStatus(emailStatusEl, "");

  const user = getCurrentUser();
  const cached = getProfile();
  fillForm(cached);
  if (user) {
    const fresh = await loadProfile(user.uid, user);
    fillForm(fresh);
  }

  photoInput.addEventListener("change", handlePhotoChange);
  saveBtn.addEventListener("click", handleSave);
  emailBtn.addEventListener("click", handleEmailUpdate);
}

export function unmount() {
  photoInput.removeEventListener("change", handlePhotoChange);
  saveBtn.removeEventListener("click", handleSave);
  emailBtn.removeEventListener("click", handleEmailUpdate);
}
