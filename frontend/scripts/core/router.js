const LOADING_LABELS = {
  world: "Building the world…"
};
const MIN_LOADER_MS = 260; // avoids a flash-of-loader on routes that resolve almost instantly
const loaderEl = () => document.getElementById("route-loader");
const loaderLabelEl = () => document.getElementById("route-loader-label");

// Waits for a real paint so the loader is actually visible before the route's
// (possibly blocking, synchronous) mount() work runs. Backed by a timeout
// fallback because rAF is throttled or fully paused by the browser when the
// tab is hidden/unfocused — without the fallback, navigating in a background
// tab would hang here forever and never reach mount() at all.
function nextFrame() {
  return new Promise((resolve) => {
    let settled = false;
    const done = () => { if (!settled) { settled = true; resolve(); } };
    requestAnimationFrame(() => requestAnimationFrame(done));
    setTimeout(done, 300);
  });
}

/**
 * Minimal hash-based router that swaps both the HTML UI overlay
 * and the active 3D page module mounted into the shared scene.
 * An optional `guard(routeName)` can redirect (e.g. auth-gated routes).
 */
export function createRouter(routes, { scene, guard, onRouteChange } = {}) {
  let activeRoute = null;
  let activePage = null;

  async function navigate(routeName) {
    const target = routeName && routes[routeName] ? routeName : "mainPage";
    const redirect = guard?.(target);
    if (redirect && redirect !== target) {
      window.location.hash = redirect;
      return navigate(redirect);
    }
    if (target === activeRoute) return;

    if (activePage?.unmount) activePage.unmount(scene);

    document.querySelectorAll(".ui-page").forEach((el) => {
      el.classList.toggle("is-active", el.id === `ui-${target}`);
    });
    document.querySelectorAll(".nav-btn").forEach((btn) => {
      btn.classList.toggle("active", btn.dataset.route === target);
    });

    const loader = loaderEl();
    const label = loaderLabelEl();
    if (label) label.textContent = LOADING_LABELS[target] ?? "Loading…";
    loader?.classList.add("is-active");
    const shownAt = performance.now();

    // Let the browser actually paint the loader before the route module's
    // (possibly heavy, synchronous) mount() work runs and blocks the thread.
    await nextFrame();

    activePage = await routes[target]();
    activePage.mount?.(scene);
    activeRoute = target;
    window.location.hash = target;
    onRouteChange?.(target);

    const elapsed = performance.now() - shownAt;
    if (elapsed < MIN_LOADER_MS) await new Promise((r) => setTimeout(r, MIN_LOADER_MS - elapsed));
    loader?.classList.remove("is-active");
  }

  document.querySelectorAll("[data-route]").forEach((btn) => {
    btn.addEventListener("click", () => navigate(btn.dataset.route));
  });

  window.addEventListener("hashchange", () => {
    navigate(window.location.hash.replace("#", ""));
  });

  navigate(window.location.hash.replace("#", "") || "mainPage");

  return { navigate };
}
