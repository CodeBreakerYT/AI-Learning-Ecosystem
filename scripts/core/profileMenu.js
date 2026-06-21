/**
 * Click-to-open dropdown for the nav profile avatar (View Profile / View
 * Devices / Logout). Closes on an option click, outside click, or Escape.
 */
export function initProfileMenu() {
  const root = document.getElementById("profile-menu");
  const trigger = document.getElementById("profile-trigger");
  const dropdown = document.getElementById("profile-dropdown");

  function close() {
    dropdown.hidden = true;
    root.classList.remove("is-open");
  }

  function toggle() {
    if (dropdown.hidden) {
      dropdown.hidden = false;
      root.classList.add("is-open");
    } else {
      close();
    }
  }

  trigger.addEventListener("click", toggle);
  dropdown.querySelectorAll("button").forEach((btn) => btn.addEventListener("click", close));
  document.addEventListener("click", (event) => {
    if (!root.contains(event.target)) close();
  });
  document.addEventListener("keydown", (event) => {
    if (event.key === "Escape") close();
  });
}
