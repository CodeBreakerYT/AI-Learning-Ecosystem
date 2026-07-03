const form = document.getElementById("contact-form");
const statusEl = document.getElementById("contact-status");

function setStatus(message, isError = false) {
  statusEl.textContent = message;
  statusEl.classList.toggle("is-error", isError);
}

function handleSubmit(event) {
  event.preventDefault();
  const name = document.getElementById("contact-name").value;
  console.log("Contact message submitted by:", name);
  setStatus("Message sent — we'll get back to you soon.");
  form.reset();
}

export function mount() {
  setStatus("");
  form.addEventListener("submit", handleSubmit);
}

export function unmount() {
  form.removeEventListener("submit", handleSubmit);
}
