import { initializeApp } from "firebase/app";
import {
  getAuth,
  GoogleAuthProvider,
  createUserWithEmailAndPassword,
  signInWithEmailAndPassword,
  signInWithPopup,
  sendPasswordResetEmail,
  signOut,
  updateProfile,
  updateEmail
} from "firebase/auth";
import { getFirestore } from "firebase/firestore";

const firebaseConfig = {
  apiKey: import.meta.env.VITE_FIREBASE_API_KEY,
  authDomain: import.meta.env.VITE_FIREBASE_AUTH_DOMAIN,
  projectId: import.meta.env.VITE_FIREBASE_PROJECT_ID,
  storageBucket: import.meta.env.VITE_FIREBASE_STORAGE_BUCKET,
  messagingSenderId: import.meta.env.VITE_FIREBASE_MESSAGING_SENDER_ID,
  appId: import.meta.env.VITE_FIREBASE_APP_ID
};

export const isFirebaseConfigured = Boolean(firebaseConfig.apiKey && firebaseConfig.projectId);

const app = isFirebaseConfigured ? initializeApp(firebaseConfig) : null;

export const auth = app ? getAuth(app) : null;
export const db = app ? getFirestore(app) : null;

const googleProvider = new GoogleAuthProvider();

const NOT_CONFIGURED_MESSAGE =
  "Firebase isn't configured yet. Add your project keys to .env (see .env.example).";

export async function registerWithEmail(name, email, password) {
  if (!auth) throw new Error(NOT_CONFIGURED_MESSAGE);
  const credential = await createUserWithEmailAndPassword(auth, email, password);
  if (name) await updateProfile(credential.user, { displayName: name });
  return credential.user;
}

export async function loginWithEmail(email, password) {
  if (!auth) throw new Error(NOT_CONFIGURED_MESSAGE);
  const credential = await signInWithEmailAndPassword(auth, email, password);
  return credential.user;
}

export async function loginWithGoogle() {
  if (!auth) throw new Error(NOT_CONFIGURED_MESSAGE);
  const credential = await signInWithPopup(auth, googleProvider);
  return credential.user;
}

export async function resetPassword(email) {
  if (!auth) throw new Error(NOT_CONFIGURED_MESSAGE);
  await sendPasswordResetEmail(auth, email);
}

export async function updateAuthProfile(updates) {
  if (!auth?.currentUser) return;
  await updateProfile(auth.currentUser, updates);
}

export async function changeEmail(newEmail) {
  if (!auth?.currentUser) throw new Error(NOT_CONFIGURED_MESSAGE);
  await updateEmail(auth.currentUser, newEmail);
}

export async function logout() {
  if (!auth) return;
  await signOut(auth);
}
