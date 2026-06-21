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

    activePage = await routes[target]();
    activePage.mount?.(scene);
    activeRoute = target;
    window.location.hash = target;
    onRouteChange?.(target);
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
