import { loginWithEmail, loginWithGoogle, registerWithEmail, resetPassword } from "../core/firebase.js";
import { login, DUMMY_CREDENTIALS } from "../core/authState.js";

const toggleButtons = document.querySelectorAll(".auth-toggle-btn");
const panels = {
  login: document.getElementById("login-form"),
  register: document.getElementById("register-form")
};

const loginForm = panels.login;
const googleBtn = document.getElementById("login-google");
const forgotBtn = document.getElementById("login-forgot");
const loginStatusEl = document.getElementById("login-status");

const registerForm = panels.register;
const registerGoogleBtn = document.getElementById("register-google");
const registerStatusEl = document.getElementById("register-status");

function setStatus(el, message, isError = false) {
  el.textContent = message;
  el.classList.toggle("is-error", isError);
}

function switchTab(tab) {
  toggleButtons.forEach((btn) => btn.classList.toggle("is-active", btn.dataset.authTab === tab));
  Object.entries(panels).forEach(([name, panel]) => panel.classList.toggle("is-active", name === tab));
}

function handleToggleClick(event) {
  switchTab(event.currentTarget.dataset.authTab);
}

async function handleLoginSubmit(event) {
  event.preventDefault();
  const identifier = document.getElementById("login-email").value.trim();
  const password = document.getElementById("login-password").value;

  if (identifier === DUMMY_CREDENTIALS.username && password === DUMMY_CREDENTIALS.password) {
    login({ uid: "dummy-admin", email: "admin", provider: "test" });
    window.location.hash = "learn";
    return;
  }

  setStatus(loginStatusEl, "Signing in...");
  try {
    const user = await loginWithEmail(identifier, password);
    login({ uid: user.uid, email: user.email, provider: "password" });
    window.location.hash = "learn";
  } catch (err) {
    setStatus(loginStatusEl, err.message, true);
  }
}

async function handleGoogleLogin() {
  setStatus(loginStatusEl, "Opening Google sign-in...");
  try {
    const user = await loginWithGoogle();
    login({ uid: user.uid, email: user.email, provider: "google" });
    window.location.hash = "learn";
  } catch (err) {
    setStatus(loginStatusEl, err.message, true);
  }
}

async function handleForgot() {
  const email = document.getElementById("login-email").value.trim();
  if (!email.includes("@")) {
    setStatus(loginStatusEl, "Enter your email above first, then click \"Forgot password?\".", true);
    return;
  }

  setStatus(loginStatusEl, "Sending password reset email...");
  try {
    await resetPassword(email);
    setStatus(loginStatusEl, `Password reset email sent to ${email}. Check your inbox.`);
  } catch (err) {
    setStatus(loginStatusEl, err.message, true);
  }
}

async function handleRegisterSubmit(event) {
  event.preventDefault();
  const name = document.getElementById("register-name").value.trim();
  const email = document.getElementById("register-email").value.trim();
  const password = document.getElementById("register-password").value;
  const confirm = document.getElementById("register-confirm").value;

  if (password !== confirm) {
    setStatus(registerStatusEl, "Passwords don't match.", true);
    return;
  }

  setStatus(registerStatusEl, "Creating your account...");
  try {
    const user = await registerWithEmail(name, email, password);
    login({ uid: user.uid, email: user.email, provider: "password" });
    window.location.hash = "learn";
  } catch (err) {
    setStatus(registerStatusEl, err.message, true);
  }
}

async function handleGoogleRegister() {
  setStatus(registerStatusEl, "Opening Google sign-up...");
  try {
    const user = await loginWithGoogle();
    login({ uid: user.uid, email: user.email, provider: "google" });
    window.location.hash = "learn";
  } catch (err) {
    setStatus(registerStatusEl, err.message, true);
  }
}

export function mount() {
  switchTab("login");
  setStatus(loginStatusEl, "");
  setStatus(registerStatusEl, "");

  toggleButtons.forEach((btn) => btn.addEventListener("click", handleToggleClick));
  loginForm.addEventListener("submit", handleLoginSubmit);
  googleBtn.addEventListener("click", handleGoogleLogin);
  forgotBtn.addEventListener("click", handleForgot);
  registerForm.addEventListener("submit", handleRegisterSubmit);
  registerGoogleBtn.addEventListener("click", handleGoogleRegister);
}

export function unmount() {
  toggleButtons.forEach((btn) => btn.removeEventListener("click", handleToggleClick));
  loginForm.removeEventListener("submit", handleLoginSubmit);
  googleBtn.removeEventListener("click", handleGoogleLogin);
  forgotBtn.removeEventListener("click", handleForgot);
  registerForm.removeEventListener("submit", handleRegisterSubmit);
  registerGoogleBtn.removeEventListener("click", handleGoogleRegister);
}
