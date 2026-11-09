// Браузерний клієнт ЛР 03. Cookie автентифікації HttpOnly: JavaScript її не читає і не зберігає.
// Паролі й профіль не записуються у localStorage/sessionStorage, DOM dataset чи Console.
const result = document.querySelector("#result");
const stateBadge = document.querySelector("#state");
const profile = document.querySelector("#profile");
const created = document.querySelector("#created");

// Запити лише на свій origin; cookie надсилається явно.
function apiFetch(path, options = {}) {
  const url = new URL(path, location.origin);
  if (url.origin !== location.origin) throw new Error("Потрібна адреса цього застосунку.");
  return fetch(url, { ...options, credentials: "same-origin" });
}

function postJson(path, body) {
  return apiFetch(path, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });
}

function showState(state) {
  const labels = { "signed-in": "Сеанс активний", anonymous: "Не ввійшли", unknown: "Стан невідомий" };
  stateBadge.dataset.state = state;
  stateBadge.textContent = labels[state];
}

function clearUserView() {
  profile.textContent = "—";
  created.textContent = "—";
}

async function loadProfile() {
  const response = await apiFetch("/api/me");
  if (response.status === 401) {
    clearUserView();
    showState("anonymous");
    return;
  }
  const isJson = (response.headers.get("Content-Type") || "").includes("json");
  if (!response.ok || !isJson) {
    profile.textContent = `Помилка профілю: ${response.status}`;
    showState("unknown");
    return;
  }
  const me = await response.json();
  // Лише дозволені поля DTO, через textContent.
  profile.textContent = `userName: ${me.userName}\nuserId: ${me.userId}\nroles: ${(me.roles || []).join(", ")}`;
  showState("signed-in");
}

async function refreshSessionState() {
  try {
    const response = await apiFetch("/api/auth/session-check");
    if (response.status === 204) {
      await loadProfile();
    } else if (response.status === 401) {
      clearUserView();
      showState("anonymous");
    } else {
      showState("unknown");
    }
  } catch {
    showState("unknown");
  }
}

document.querySelector("#register").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const response = await postJson("/api/auth/register", Object.fromEntries(new FormData(form)));
  form.elements.password.value = "";
  // Реєстрація не виконує автоматичний вхід: профіль з'являється лише після login.
  result.textContent = response.status === 201
    ? "Register: 201. Тепер увійдіть."
    : `Register: ${response.status}`;
  if (response.status === 201) form.reset();
});

document.querySelector("#login").addEventListener("submit", async event => {
  event.preventDefault();
  const form = event.currentTarget;
  const response = await postJson("/api/auth/login", Object.fromEntries(new FormData(form)));
  form.elements.password.value = "";
  result.textContent = `Login: ${response.status}`;
  await refreshSessionState();
});

document.querySelector("#me").addEventListener("click", async () => {
  await loadProfile();
  result.textContent = `Profile: ${stateBadge.textContent}`;
});

document.querySelector("#incident").addEventListener("submit", async event => {
  event.preventDefault();
  const data = Object.fromEntries(new FormData(event.currentTarget));
  // OwnerUserId, Role, Status, Id клієнт не передає: ними керує сервер.
  const body = {
    title: data.title,
    description: data.description,
    severity: data.severity,
    occurredAtUtc: new Date(data.occurredAt).toISOString()
  };
  const response = await postJson("/api/incidents", body);
  if (response.status === 201) {
    const incident = await response.json();
    created.textContent = `201 Created\nid: ${incident.id}\ntitle: ${incident.title}`;
  } else {
    created.textContent = `Create: ${response.status}`;
  }
  result.textContent = `Create: ${response.status}`;
});

document.querySelector("#logout").addEventListener("click", async () => {
  const response = await postJson("/api/auth/logout", {});
  clearUserView();
  result.textContent = `Logout: ${response.status}`;
  // Очищення UI не замінює sign-out: повторний запит до /api/me має дати 401.
  const check = await apiFetch("/api/me");
  result.textContent += `; повторний /api/me: ${check.status}`;
  await refreshSessionState();
});

refreshSessionState();
