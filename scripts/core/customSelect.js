/**
 * Lightweight custom dropdown to replace native <select> (whose option list
 * styling is locked to OS chrome in most browsers). Mirrors the bits of the
 * native select API callers need: a `.value` getter and a `change` event.
 */
export function createCustomSelect(root) {
  const trigger = root.querySelector(".vr-select-trigger");
  const triggerSwatch = trigger.querySelector(".vr-select-swatch");
  const label = root.querySelector(".vr-select-label");
  const optionsList = root.querySelector(".vr-select-options");
  const options = Array.from(root.querySelectorAll(".vr-select-option"));

  function close() {
    optionsList.hidden = true;
    root.classList.remove("is-open");
  }

  function toggle() {
    if (optionsList.hidden) {
      optionsList.hidden = false;
      root.classList.add("is-open");
    } else {
      close();
    }
  }

  function selectOption(option) {
    options.forEach((opt) => opt.classList.toggle("is-selected", opt === option));
    label.textContent = option.textContent.trim();
    const color = option.dataset.color;
    triggerSwatch.style.background = color;
    triggerSwatch.style.color = color;
    root.dataset.value = option.dataset.value;
    close();
    root.dispatchEvent(new CustomEvent("change", { detail: { value: option.dataset.value } }));
  }

  function handleOutsideClick(event) {
    if (!root.contains(event.target)) close();
  }

  function handleKeydown(event) {
    if (event.key === "Escape") close();
  }

  trigger.addEventListener("click", toggle);
  document.addEventListener("click", handleOutsideClick);
  document.addEventListener("keydown", handleKeydown);

  const optionHandlers = options.map((option) => {
    const onClick = () => selectOption(option);
    const onKeydown = (event) => {
      if (event.key === "Enter" || event.key === " ") {
        event.preventDefault();
        selectOption(option);
      }
    };
    option.addEventListener("click", onClick);
    option.addEventListener("keydown", onKeydown);
    return { option, onClick, onKeydown };
  });

  const initial = options.find((opt) => opt.classList.contains("is-selected")) ?? options[0];
  if (initial) {
    root.dataset.value = initial.dataset.value;
  }

  return {
    get value() {
      return root.dataset.value;
    },
    destroy() {
      trigger.removeEventListener("click", toggle);
      document.removeEventListener("click", handleOutsideClick);
      document.removeEventListener("keydown", handleKeydown);
      optionHandlers.forEach(({ option, onClick, onKeydown }) => {
        option.removeEventListener("click", onClick);
        option.removeEventListener("keydown", onKeydown);
      });
    }
  };
}
