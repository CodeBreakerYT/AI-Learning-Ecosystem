/**
 * Requests an immersive-vr WebXR session directly via navigator.xr and hands
 * it to the Three.js renderer. This is the "Attach" action on the vrSetup page.
 */
export async function connectVRSession(renderer, { onConnected, onEnded } = {}) {
  if (!navigator.xr) {
    throw new Error("WebXR isn't available in this browser.");
  }

  const supported = await navigator.xr.isSessionSupported("immersive-vr");
  if (!supported) {
    throw new Error("This device/browser doesn't support immersive VR.");
  }

  const session = await navigator.xr.requestSession("immersive-vr", {
    optionalFeatures: ["local-floor", "bounded-floor", "hand-tracking"]
  });

  session.addEventListener("end", () => onEnded?.());
  await renderer.xr.setSession(session);
  onConnected?.(session);
  return session;
}
