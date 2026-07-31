// True when the page itself is running inside a headset's own browser
// (Meta Quest Browser, Pico Browser, etc.) rather than a desktop browser
// streaming to the headset via Link/Air Link/SteamVR — the "no VR runtime"
// error means something completely different for each audience, and most
// real users (anyone opening the deployed site directly on their headset)
// are in the first group, not the PC+Link group.
function isRunningInHeadsetBrowser() {
  return /OculusBrowser|Quest|PicoBrowser|VR Shell|Silk-Accelerated/i.test(navigator.userAgent);
}

/**
 * Requests an immersive-vr WebXR session directly via navigator.xr and hands
 * it to the Three.js renderer. Used by the "Attach" action on the vrSetup
 * page and the "Enter VR" button on the Learn page.
 */
export async function connectVRSession(renderer, { onConnected, onEnded, onWaiting } = {}) {
  if (!navigator.xr) {
    throw new Error("WebXR isn't available in this browser.");
  }

  // Double-tap guard: if a session is already live and actually presenting,
  // hand it back instead of asking the browser for a second one (which
  // throws InvalidStateError). Checking isPresenting (not just truthiness)
  // matters: three.js's WebXRManager records the session reference the
  // instant setSession() is called, before it even requests a reference
  // space — so a session that failed partway through a previous attempt
  // (e.g. the reference-space request was rejected) leaves getSession()
  // returning a dead, non-presenting session forever. Treating that as
  // "already connected" would silently no-op every future attempt until a
  // full page reload — exactly the "stops working after one failure" bug.
  const existing = renderer.xr.getSession();
  if (existing) {
    if (renderer.xr.isPresenting) {
      onConnected?.(existing);
      return existing;
    }
    try { await existing.end(); } catch { /* already dead — fine, we just need getSession() clear */ }
  }

  // requestSession() can sit pending with no error at all while the runtime
  // shows its own "allow this site to use VR" prompt *inside the headset* —
  // from the page's side that looks identical to a hang. Nudge the caller
  // after a few seconds so the UI can tell the user to look in the headset
  // instead of assuming the button did nothing.
  const waitTimer = onWaiting ? setTimeout(onWaiting, 4000) : null;

  // Request the session directly instead of awaiting isSessionSupported()
  // first — that extra await hop can burn through the browser's "recent
  // user gesture" window some devices require for permission prompts.
  let session;
  try {
    session = await navigator.xr.requestSession("immersive-vr", {
      optionalFeatures: ["local-floor", "bounded-floor", "hand-tracking", "layers"]
    });
  } catch (err) {
    clearTimeout(waitTimer);
    if (err.name === "NotSupportedError") {
      if (isRunningInHeadsetBrowser()) {
        throw new Error(
          "This headset browser reports no VR support for this page. Try: " +
          "(1) Make sure you're in the headset's own Browser app, not a link opened inside another app. " +
          "(2) Update the headset's system software (Settings → System → Software Update). " +
          "(3) Fully close and reopen the browser, then reload this page. " +
          "You can also open /xr-check.html for a detailed diagnostic."
        );
      }
      throw new Error(
        "No VR headset detected by this desktop browser. Two ways to fix this: " +
        "EASIEST — open this page directly in your headset's own browser instead (no PC needed at all). " +
        "OR, if you're deliberately streaming from this PC via Quest Link/Air Link/SteamVR, check: " +
        "(1) The Link app is running and the headset shows the Link home grid, not normal Quest home. " +
        "(2) The Link app's Settings → General has it set as the active OpenXR runtime. " +
        "(3) Restart the browser after changing the OpenXR runtime. " +
        "You can also open /xr-check.html for a detailed diagnostic."
      );
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
  clearTimeout(waitTimer);

  session.addEventListener("end", () => onEnded?.());

  try {
    // renderer.xr.setSession() already calls gl.makeXRCompatible() and
    // requests the reference space internally (this matches three.js's own
    // VRButton.js reference implementation, which never calls
    // makeXRCompatible manually) — doing it again here was redundant and
    // not part of the tested pattern.
    await renderer.xr.setSession(session);
  } catch (err) {
    // xrManager.js asks for "local-floor" up front, but that's only an
    // *optional* feature at session-request time — some runtimes grant the
    // session anyway and then reject the "local-floor" reference space
    // specifically here. "local" is mandatory for every immersive-vr
    // session per spec, so fall back to it instead of hard-failing.
    try {
      renderer.xr.setReferenceSpaceType("local");
      await renderer.xr.setSession(session);
      renderer.xr.setReferenceSpaceType("local-floor"); // restore for the next session attempt
      onConnected?.(session);
      return session;
    } catch (fallbackErr) {
      renderer.xr.setReferenceSpaceType("local-floor");
      session.end().catch(() => {});
      throw new Error(`Couldn't attach VR to the renderer (${err.name}): ${err.message}. Try reloading the page.`);
    }
  }

  onConnected?.(session);
  return session;
}
