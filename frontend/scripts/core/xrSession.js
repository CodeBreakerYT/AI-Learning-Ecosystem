/**
 * Requests an immersive-vr WebXR session directly via navigator.xr and hands
 * it to the Three.js renderer. Used by the "Attach" action on the vrSetup
 * page and the "Enter VR" button on the Learn page.
 */
export async function connectVRSession(renderer, { onConnected, onEnded } = {}) {
  if (!navigator.xr) {
    throw new Error("WebXR isn't available in this browser.");
  }

  // Double-tap guard: if a session is already live, hand it back instead of
  // asking the browser for a second one (which throws InvalidStateError).
  const existing = renderer.xr.getSession();
  if (existing) {
    onConnected?.(existing);
    return existing;
  }

  // Request the session directly instead of awaiting isSessionSupported()
  // first — that extra await hop can burn through the browser's "recent
  // user gesture" window some devices require for permission prompts.
  let session;
  try {
    session = await navigator.xr.requestSession("immersive-vr", {
      optionalFeatures: ["local-floor", "bounded-floor", "hand-tracking"]
    });
  } catch (err) {
    if (err.name === "NotSupportedError") {
      throw new Error("This device/browser doesn't support immersive VR.");
    }
    if (err.name === "SecurityError") {
      throw new Error("VR is blocked here — the site must be opened over https:// (or localhost).");
    }
    if (err.name === "InvalidStateError") {
      // The browser thinks a previous session is still pending/active
      // (e.g. after a cancelled permission prompt). Retry once with the
      // most minimal request possible.
      try {
        session = await navigator.xr.requestSession("immersive-vr");
      } catch (retryErr) {
        throw new Error(`VR session failed (${retryErr.name}): ${retryErr.message}. Try reloading the page.`);
      }
    } else {
      throw new Error(`VR session failed (${err.name}): ${err.message}`);
    }
  }

  session.addEventListener("end", () => onEnded?.());

  try {
    // Ensure the WebGL context is XR-compatible before Three.js builds its
    // XRWebGLLayer — on Quest Browser, skipping this is the classic source
    // of "An attempt was made to use an object that is not, or is no
    // longer, usable" (InvalidStateError).
    const gl = renderer.getContext();
    if (gl.makeXRCompatible) await gl.makeXRCompatible();
    await renderer.xr.setSession(session);
  } catch (err) {
    session.end().catch(() => {});
    throw new Error(`Couldn't attach VR to the renderer (${err.name}): ${err.message}. Try reloading the page.`);
  }

  onConnected?.(session);
  return session;
}
