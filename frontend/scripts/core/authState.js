import { onAuthStateChanged } from "firebase/auth";
import { doc, setDoc, collection, addDoc, serverTimestamp } from "firebase/firestore";
import { auth, db } from "./firebase.js";

const STORAGE_KEY = "ale.auth.user";
const listeners = new Set();

let currentUser = readStoredUser();

function readStoredUser() {
  try {
    const raw = sessionStorage.getItem(STORAGE_KEY);
    return raw ? JSON.parse(raw) : null;
  } catch {
    return null;
  }
}

// Lets testers exercise the login -> VR setup flow without a live Firebase project.
export const DUMMY_CREDENTIALS = { username: "admin", password: "admin" };

const PROTECTED_ROUTES = ["learn", "vrSetup", "profile", "devices"];

export function isAuthenticated() {
  return currentUser !== null;
}

export function getCurrentUser() {
  return currentUser;
}

export function login(user) {
  currentUser = user;
  sessionStorage.setItem(STORAGE_KEY, JSON.stringify(user));
  listeners.forEach((fn) => fn(currentUser));
  recordLoginEvent(user);
}

export function logoutUser() {
  currentUser = null;
  sessionStorage.removeItem(STORAGE_KEY);
  listeners.forEach((fn) => fn(currentUser));
}

export function onAuthChange(fn) {
  listeners.add(fn);
  return () => listeners.delete(fn);
}

export function guardRoute(routeName) {
  if (PROTECTED_ROUTES.includes(routeName) && !isAuthenticated()) {
    return "login";
  }
  return null;
}

// Writes/updates the user's profile doc and appends a row to loginLogs so every
// sign-in (password, Google, or the offline dummy admin) is actually persisted.
async function recordLoginEvent(user) {
  if (!db) return;
  try {
    await setDoc(
      doc(db, "users", user.uid),
      { email: user.email ?? null, provider: user.provider, lastLogin: serverTimestamp() },
      { merge: true }
    );
    await addDoc(collection(db, "loginLogs"), {
      uid: user.uid,
      email: user.email ?? null,
      provider: user.provider,
      at: serverTimestamp()
    });
  } catch (err) {
    console.warn("Couldn't record login event in Firestore:", err.message);
  }
}

// Resolves once with Firebase's restored session (or null) so the router's
// first navigation reflects the real, persisted auth state instead of a
// blank sessionStorage read on a fresh tab.
export function waitForAuthReady() {
  return new Promise((resolve) => {
    if (!auth) {
      resolve(null);
      return;
    }
    const unsubscribe = onAuthStateChanged(auth, (firebaseUser) => {
      unsubscribe();
      if (firebaseUser) {
        login({ uid: firebaseUser.uid, email: firebaseUser.email, provider: "firebase" });
      }
      resolve(firebaseUser);
    });
  });
}

// Keeps authState in sync with Firebase for the rest of the session (e.g. the
// token refreshing, or the user signing out in another tab). The first event
// is skipped since waitForAuthReady() already handled the restored session.
export function watchAuthState() {
  if (!auth) return;
  let isFirstEvent = true;
  onAuthStateChanged(auth, (firebaseUser) => {
    if (isFirstEvent) {
      isFirstEvent = false;
      return;
    }
    if (firebaseUser) {
      login({ uid: firebaseUser.uid, email: firebaseUser.email, provider: "firebase" });
    } else if (currentUser?.provider !== "test") {
      logoutUser();
    }
  });
}
