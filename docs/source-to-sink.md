# Власна карта source-to-sink (ЛР 5)

Стан «до»: vulnerable commit `4fcf1a7` (scaffold lab-05-start-v1, `browser-lab.js` з `innerHTML`); тестові дані — `0f72d74`.
Стан «після»: fixed commit `814bef9` (safe DOM), кінцевий стан — тег `v0.5.0`.
Рівень виконання: **відмінний («Відмінно»)**. Спільний сценарій 2-A (Incident.Description, IncidentComment.Text, Morgan — Analyst).

Запис із marker (Alice): `8bf123f7-5246-469e-a020-84143789a39b`. Seed-інцидент із коментарем-marker: `20000000-0000-0000-0000-000000000001`.

## Потік 1. Incident.Description

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Client/browser-lab.html`, форма `#edit`, `textarea[name=description]` | текст, який вводить користувач (у PoC — canonical marker із `resources/lab05-marker.txt`) | контрольоване користувачем (`docs/evidence/04-marker-alice-before.png`) |
| Request | `Client/browser-lab.js`, обробник `submit` форми `#edit` → `fetch("/api/incidents", POST)` | JSON-поле `description` | контрольоване користувачем; у Network `POST /api/incidents` → 201, payload містить marker |
| Validation/mapping | `Scaffolding/Lab02Endpoints.cs`, `MapPost("/api/incidents")`, `CreateIncidentRequest.Description` | trim, довжина ≤ 4000, для High/Critical ≥ 40 символів; символи `<` `>` не відхиляються (Description — звичайний текст) | валідація не робить значення довіреним HTML |
| Storage | `Data/Entities/Incident.cs` → таблиця `incidents`, колонка `description` (`HasMaxLength(4000)`) | те саме Unicode-значення (довжина 223 для marker) | збережене значення користувача; `docs/evidence/09-db-api-before.png` |
| Response | `Presentation/Contracts/IncidentResponses.cs`, `IncidentDetailsResponse.Description`; `Application/Incidents/IncidentQueries.GetDetailsAsync`; `GET /api/incidents/{id:guid}` | JSON-поле `description` (лапки в JSON екрановані як `\"`) | недовірене для відображення браузером; у Network `GET` → 200 |
| Client transform | `Client/browser-lab.js`, `load()` | `current = await response.json()`, `current.description` без змін | залежить від реалізації sink |
| Sink (до) | `browser-lab.js`, `load()`: `document.querySelector("#description").innerHTML = current.description;` | HTML-контекст, браузер запускає HTML-парсер | **небезпечний**: з'являється `<img src="x" onerror=…>`, marker виконується |
| Sink (після) | `browser-lab.js`, `renderPlainText(document.querySelector("#description"), current.description)` → `container.replaceChildren(document.createTextNode(text))` | контекст текстового вузла | безпечний: один текстовий вузол, `childElementCount === 0` |

Класифікація: **stored XSS** (значення збережене в PostgreSQL і виконується під час пізнішого перегляду іншим користувачем — Morgan) з **DOM-based** місцем небезпечного перетворення (клієнтський `innerHTML`). Reflected XSS у роботі не відтворюється (пояснюється в звіті).

## Потік 2. IncidentComment.Text (рівень «Добре / Відмінно»)

| Крок | Файл та функція/DTO | Конкретне поле/значення | Доказ і довіра |
|---|---|---|---|
| Source | `Data/DbSeeder.cs`, `SeedAsync`, константа `Lab05Marker` | у продукті — будь-який автор коментаря; тут seed-коментар `30000000-0000-0000-0000-000000000005` (автор Morgan, `IsInternal=false`) | контрольоване користувачем у продукті |
| Request | endpoint створення коментаря в успадкованому API відсутній; тестовий запис додано відтворюваним seed (commit `0f72d74`) | — | — |
| Validation/mapping | `IncidentQueries.GetDetailsAsync`: `comment.Text` → `IncidentCommentResponse.Text` (фільтр `IsInternal`) | проєкція без очищення HTML | не стає довіреним |
| Storage | таблиця `incident_comments`, колонка `text` | marker | збережене значення користувача |
| Response | `IncidentDetailsResponse.Comments[]`, `IncidentCommentResponse.Text` | JSON `comments[].text` | недовірене |
| Client transform | `browser-lab.js`, цикл `for (const comment of current.comments ?? [])` | `comment.text` | залежить від sink |
| Sink (до) | `paragraph.innerHTML = comment.text;` | HTML-контекст | **небезпечний** (`docs/evidence/07-comments-before.png`) |
| Sink (після) | `renderPlainText(commentsContainer, comment.text, "p")` | контекст текстового вузла в `<p>` | безпечний (`docs/evidence/07-comments-after.png`) |

## Інвентаризація DOM sinks (власний код `Client/` і `demonstrations/`)

| Місце | Дані, що потрапляють у sink | Категорія даних | Контекст | Рішення |
|---|---|---|---|---|
| `browser-lab.js` `#description` (було `innerHTML`) | `incident.description` | збережене значення користувача | HTML parser | замінено на `renderPlainText` (text node) |
| `browser-lab.js` `#comments` (було `paragraph.innerHTML`) | `comment.text` | збережене значення користувача | HTML parser | замінено на `renderPlainText(…, "p")` |
| `browser-lab.js` `#title` | `current.title` | збережене значення користувача | text node (`textContent`) | безпечно, змін не потрібно |
| `browser-lab.js` `#status` | `GET: ${status}` | server-generated | text node | безпечно |
| `app.js` (список, деталі, summary) | title/description/comment/summary | збережене значення користувача | `createTextElement` → `textContent`, `createTextNode` | безпечно, змін не потрібно |
| `auth-lab.js` | відповіді `/api/me`, `/api/auth/*` | server-generated + введене | `textContent` | безпечно |
| `probe.js` (CORS-probe) | `${response.status}: ${await response.text()}` | server-generated | `textContent` | безпечно |
| `innerHTML`, `outerHTML`, `insertAdjacentHTML`, `document.write`, `eval`, `new Function`, inline `on*`-атрибути, динамічні `href`/`src` зі значень API | — | — | — | після fix у власному коді `Client/` відсутні (`grep`) |

Результат поглибленої перевірки comments і повторного render: `tests/browser/description-dom.js` перевіряє кожен `comments[].text`, кількість елементів після двох повторних `load()`, позитивні рядки; FAIL на vulnerable, PASS на fixed (`docs/evidence/08-dom-test-before.png`, `08-dom-test-after.png`, `22-final-regression.png`).
