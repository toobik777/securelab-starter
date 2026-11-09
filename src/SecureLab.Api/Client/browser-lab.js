let current;
const id = () => encodeURIComponent(document.querySelector("#incident-id").value.trim());
const status = document.querySelector("#status");
// Єдиний safe render helper для Incident.Description і IncidentComment.Text (звичайний текст, не HTML).
// Структуру елементів задає код (createElement), недовірене значення потрапляє лише в текстовий вузол,
// тому браузер не запускає HTML-парсер для даних із API. Без wrapperTag контейнер замінюється одним текстовим вузлом.
function renderPlainText(container, text, wrapperTag) {
  const node = document.createTextNode(text ?? "");
  if (!wrapperTag) {
    container.replaceChildren(node);
    return container;
  }
  const wrapper = document.createElement(wrapperTag);
  wrapper.append(node);
  container.append(wrapper);
  return wrapper;
}
async function load() {
  const response = await fetch(`/api/incidents/${id()}`);
  status.textContent = `GET: ${response.status}`;
  if (!response.ok) return;
  current = await response.json();
  document.querySelector("#title").textContent = current.title;
  renderPlainText(document.querySelector("#description"), current.description);
  document.querySelector("#comments").replaceChildren();
  for (const comment of current.comments ?? []) {
    renderPlainText(document.querySelector("#comments"), comment.text, "p");
  }
  document.querySelector("#edit").elements.description.value = current.description;
}
document.querySelector("#load").addEventListener("click", load);
document.querySelector("#edit").addEventListener("submit", async event => {
  event.preventDefault();
  const response = await fetch("/api/incidents", { method: "POST", headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ title: event.currentTarget.elements.title.value, description: event.currentTarget.elements.description.value,
      severity: "Low", occurredAtUtc: new Date().toISOString() }) });
  status.textContent = `POST: ${response.status}`;
  if (response.ok) {
    const created = await response.json();
    document.querySelector("#incident-id").value = created.id;
    await load();
  }
});
