// ЛР 6: helper antiforgery для змінювальних запитів. Токен живе лише в пам'яті поточної сторінки
// (F5 або перехід створює нову пам'ять); cookie не копіюється в localStorage/sessionStorage.
let csrfToken = null;

async function ensureCsrfToken() {
  if (csrfToken) return csrfToken;
  const response = await fetch("/api/auth/csrf-token", { credentials: "same-origin" });
  if (!response.ok) throw new Error(`CSRF token: ${response.status}`);
  csrfToken = (await response.json()).token;
  return csrfToken;
}

// Після login/logout токен належить іншому principal: очистити й одержати новий при наступній mutation.
function clearCsrfToken() {
  csrfToken = null;
}

// Лише same-origin; header додається до кожної mutation. Після 4xx запит автоматично не повторюється.
async function csrfFetch(path, options = {}) {
  const url = new URL(path, location.origin);
  if (url.origin !== location.origin) throw new Error("Потрібна адреса цього застосунку.");
  const token = await ensureCsrfToken();
  return fetch(url, {
    ...options,
    credentials: "same-origin",
    headers: { ...(options.headers || {}), "X-CSRF-TOKEN": token },
  });
}
