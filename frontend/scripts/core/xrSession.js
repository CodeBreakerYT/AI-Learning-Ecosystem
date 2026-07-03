/**
 * Requests an immersive-vr WebXR session directly via navigator.xr and hands
 * it to the Three.js renderer. This is the "Attach" action on the vrSetup page.
 */
export async function connectVRSession(renderer, { onConnected, onEnded } = {}) {
  if (!navigator.xr) {
    throw new Error("WebXR isn't available in this browser.");
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
    throw err;
  }

  session.addEventListener("end", () => onEnded?.());
  await renderer.xr.setSession(session);
  onConnected?.(session);
  return session;
}
