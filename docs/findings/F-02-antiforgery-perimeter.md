# F-02. Cookie-authenticated змінювальні маршрути без antiforgery (CSRF)

**Статус:** Fixed
**Актив:** право користувача змінювати власні інциденти та їхній статус; сеанс
**Компонент:** POST/PUT/DELETE `/api/incidents…`, POST `/api/auth/logout`
**Пріоритет:** P1

## Передумови і межі огляду
Commit `e269e1a` (до fix), Development, `http://localhost:5080`, користувач alice, reset перед пробою. Маршрути з таблиці периметра SC-01…SC-05 (`docs/security-review.md`). Поза межами: HTTPS, cross-site експлуатація зі справжньої сторонньої сторінки (відтворено на рівні HTTP-запитів із cookie без токена).

## Відтворення
1. Login alice → cookie-сеанс.
2. `POST /api/incidents` без токена → **201**; `PUT /api/incidents/{id}` без токена → **204**, `GET` показує змінений title; `DELETE` без токена → **204**, `GET` → 404.
3. Очікувано: відмова без валідного request token. Фактично: усі запити виконано лише за наявності cookie.

## Вплив і ймовірність
**Статус перевірки:** спостережено локально.
Браузер автоматично додає cookie, тому сторінка в тому самому браузері могла б створити, змінити чи видалити ресурс від імені користувача без його намірів. SameSite=Lax і CORS цього не закривають повністю (same-site запити, не-CORS запити). Impact High (цілісність), likelihood Medium → P1.

## Першопричина
Сервер вважав будь-який запит із чинною authentication cookie свідомою дією клієнта; перевірки request token не було ні на одному змінювальному маршруті.

## Виправлення
Стандартний ASP.NET Core antiforgery: `AddAntiforgery` (header `X-CSRF-TOKEN`, cookie `SecureLab.Antiforgery`, HttpOnly, SameSite=Strict, Secure поза Development); маршрут видачі `GET /api/auth/csrf-token` (лише автентифікованим); один спільний `AntiforgeryValidationFilter` (endpoint filter), що викликає `ValidateRequestAsync` і повертає один 400 Problem Details «Antiforgery token is missing or invalid.»; фільтр підключено до SC-01…SC-05; клієнтський helper `Client/csrf.js` (токен лише в пам'яті сторінки, очищується після login/logout, без автоповтору). Login/register поза периметром — окреме рішення (SC-06/07). Fixed commit `e13ede9` (`Security/Antiforgery.cs`, `IncidentEndpoints.cs`, `Lab02Endpoints.cs`, `Lab03Identity.cs`, `Client/*`).

## Retest
Ті самі запити без токена: **400** `application/problem+json`; повторне читання — ресурс не змінено; токен alice разом із cookie bob — 400; анонімний запит — 401 (authentication раніше за antiforgery). Автоматично: `Lab06SecurityTests.RegCsrf01_FullFlow_…`, `Perimeter_AllCookieAuthenticatedMutations_…`, `Perimeter_TokenOfAnotherUser_…`.

## Positive regression
Із валідною парою token/cookie: create 201, update 204, delete 204, logout 204 (`LegitimateFlow_WithValidToken_…`); у браузері login → create → logout працюють; усі старі тести ЛР 2–5 адаптовано (login із токеном) і проходять.

## Залишковий ризик
Токен доступний коду на власному origin (XSS); login/register не захищені antiforgery (прийнято, SC-06/07); поведінку простроченого токена не тестовано.

## Доказ
Тести вище; `py -3 scripts/lr06/lab06_probe.py csrf` (до: 201/204/204; після: 400, стан незмінний, з токеном 201/204/204). Cookie й токен не збережено.
