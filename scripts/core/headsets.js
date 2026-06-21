export const HEADSETS = [
  { value: "meta-quest", label: "Meta Quest 2 / 3 / Pro", color: "#5b8cff" },
  { value: "htc-vive", label: "HTC Vive", color: "#22d3ee" },
  { value: "valve-index", label: "Valve Index", color: "#a78bfa" },
  { value: "windows-mr", label: "Windows Mixed Reality", color: "#34d399" },
  { value: "other", label: "Other / Generic WebXR", color: "#f472b6" }
];

export function getHeadsetMeta(value) {
  return HEADSETS.find((h) => h.value === value) ?? null;
}

export function renderHeadsetOptionsHTML(selectedValue) {
  return HEADSETS.map(
    (h) => `
      <li class="vr-select-option${h.value === selectedValue ? " is-selected" : ""}" role="option" tabindex="0" data-value="${h.value}" data-color="${h.color}">
        <span class="vr-select-swatch" style="background:${h.color}; color:${h.color}"></span>${h.label}
      </li>`
  ).join("");
}

export function renderHeadsetSelectHTML(selectedValue) {
  const selected = getHeadsetMeta(selectedValue) ?? HEADSETS[0];
  return `
    <button type="button" class="vr-select-trigger">
      <span class="vr-select-swatch" style="background:${selected.color}; color:${selected.color}"></span>
      <span class="vr-select-label">${selected.label}</span>
      <span class="vr-select-chevron" aria-hidden="true">▾</span>
    </button>
    <ul class="vr-select-options" role="listbox" hidden>${renderHeadsetOptionsHTML(selected.value)}</ul>
  `;
}
