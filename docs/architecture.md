# Карта архітектури

Це початкова карта. Під час ЛР 1 доповніть її власним трасуванням запиту,
конкретними файлами та спостереженнями з DevTools і журналу PostgreSQL.

## Компоненти

| Компонент | Розташування | Відповідальність |
|---|---|---|
| Browser client | `src/SecureLab.Api/Client/` | Надсилає HTTP-запити, безпечно показує відповідь через DOM API |
| Presentation | `Presentation/` | Описує endpoints, читає зовнішні параметри, формує HTTP-відповідь |
| Application | `Application/` | Виконує сценарій отримання списку або деталей інциденту |
| Data | `Data/` | Відображає C#-сутності на PostgreSQL через EF Core/Npgsql |
| PostgreSQL | `infra/compose.yaml` | Зберігає навчальні дані у локальному контейнері |

## Підготовлений наскрізний маршрут

кнопка summary у Client/index.html
  → handler loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary?status=...
  → Presentation/Endpoints/IncidentEndpoints.cs: GetSeveritySummaryAsync (allowlist-перевірка status)
  → Application/Incidents/IncidentQueries.cs: GetSeveritySummaryAsync
  → Data/SecureLabDbContext.cs → таблиця incidents
  → IncidentSeveritySummaryResponse у Presentation/Contracts/
  → JSON
  → textContent у Client/app.js

## Межі довіри

Доповніть таблицю щонайменше трьома конкретними спостереженнями.

| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
|---|---|---|
| Користувач → Browser client | Користувач контролює введення | TODO |
| Browser client → API | Клієнт і HTTP-запит можна змінити поза UI | TODO |
| PostgreSQL → API → DOM | У БД може зберігатися раніше введений недовірений текст | DTO та безпечний DOM sink; доповнити |
| Межа | Чому даним ще не можна довіряти | Де перевіряємо або обмежуємо |
| :--- | :--- | :--- |
| **Користувач → Browser client** | Користувач може ввести будь-який текст, включно зі шкідливим вмістом (наприклад, `<script>`). Дані з форми надсилаються у `fetch` без попередньої "очистки" на клієнті. | На клієнті дані обробляються як сирі строки без виконання. |
| **Browser client → API** | Клієнт і HTTP-запит можна підробити або змінити поза UI (наприклад, викликавши `fetch` напряму з консолі DevTools, як у кроці 6.2). | На боці API: обмеження маршруту `/{id:guid}` (GUID constraint), валідація вхідних даних та обробка `null` з поверненням коду `404 Not Found`. |
| **PostgreSQL → API → DOM** | У БД зберігається раніше введений недовірений текст (наприклад, `<script>` у полі `description`). | **API:** `IncidentDetailsResponse` фільтрує чутливі дані (відсутні `OwnerUserId`, `email`).<br>**DOM:** використання `textContent` / `createTextNode` замість `innerHTML` при рендерингу (крок 6.4). |

## Конфігураційні входи

- `global.json` — версія .NET SDK;
- `src/SecureLab.Api/appsettings*.json` — режим міграцій і локальний connection string;
- `infra/compose.yaml` — версія PostgreSQL, порт і локальні навчальні облікові дані;
- змінна середовища `ConnectionStrings__SecureLab` — безпечний спосіб перевизначити connection string поза репозиторієм.
