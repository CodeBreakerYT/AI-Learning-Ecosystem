import { doc, getDoc, setDoc, serverTimestamp } from "firebase/firestore";
import { db } from "./firebase.js";

let currentProfile = null;
const listeners = new Set();

function notify() {
  listeners.forEach((fn) => fn(currentProfile));
}

export function getProfile() {
  return currentProfile;
}

export function onProfileChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export async function loadProfile(uid, fallback = {}) {
  if (!db || !uid) {
    currentProfile = { displayName: fallback.email || "Explorer", photoURL: null, about: "", email: fallback.email || "" };
    notify();
    return currentProfile;
  }

  try {
    const snap = await getDoc(doc(db, "users", uid));
    const data = snap.exists() ? snap.data() : {};
    currentProfile = {
      displayName: data.displayName || fallback.email || "Explorer",
      photoURL: data.photoURL || null,
      about: data.about || "",
      email: data.email || fallback.email || "",
      headset: data.headset || null
    };
  } catch (err) {
    console.warn("Couldn't load profile from Firestore:", err.message);
    currentProfile = { displayName: fallback.email || "Explorer", photoURL: null, about: "", email: fallback.email || "" };
  }
  notify();
  return currentProfile;
}

export async function saveProfile(uid, updates) {
  currentProfile = { ...currentProfile, ...updates };
  notify();
  if (!db || !uid) return;
  try {
    await setDoc(doc(db, "users", uid), { ...updates, updatedAt: serverTimestamp() }, { merge: true });
  } catch (err) {
    console.warn("Couldn't save profile to Firestore:", err.message);
  }
}

export function clearProfile() {
  currentProfile = null;
  notify();
}
