# Звіт до лабораторної роботи № TODO

## 1. Ідентифікація стану

- Варіант: 2-A «Трекер інцидентів».
- Гілка: lab/1-system.
- Фінальний тег: v0.1.0.
- Commit hash: `TODO`.

## 2. Змінений маршрут

 → handler loadSeveritySummary у Client/app.js
  → GET /api/incidents/severity-summary?status=...
  → Presentation/Endpoints/IncidentEndpoints.cs: GetSeveritySummaryAsync (allowlist-перевірка status)
  → Application/Incidents/IncidentQueries.cs: GetSeveritySummaryAsync
  → Data/SecureLabDbContext.cs → таблиця incidents
  → IncidentSeveritySummaryResponse у Presentation/Contracts/
  → JSON
  → textContent у Client/app.js

## 3. Виконані зміни

- Реалізовано GET /api/incidents/severity-summary (замінено baseline 501 на 200).
- Додано query parameter status з allowlist-перевіркою (400 для некоректного значення).
- Окремий response DTO IncidentSeveritySummaryResponse без зайвих полів entity.
- Клієнт: кнопка, стани завантаження/порожнього результату/безпечної помилки.
- Структуроване журналювання кількості груп і фільтра.

## 4. Перевірка

| ID | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|
| T-01 | PostgreSQL healthy, API запущено | GET /health | 200 | 200 | вивід docker compose ps + .http response |
| T-02 | відновлений seed | GET /api/incidents?status=Triaged | 200 | 200 | .http response |
| T-04 | відновлений seed | GET /api/incidents/99999999-... | 404 Problem Details | 404 | .http response |
| T-06 | реалізовано severity-summary | GET /api/incidents/severity-summary | 200, Low/Medium/High по 1, Critical 0 | [{"severity":"Low","count":1},{"severity":"Medium","count":1},{"severity":"High","count":1},{"severity":"Critical","count":0}] | .http response |
| T-06b | реалізовано severity-summary | GET .../severity-summary?status=Resolved | 200, усі 0 | [{"severity":"Low","count":0},{"severity":"Medium","count":0},{"severity":"High","count":0},{"severity":"Critical","count":0}] | .http response |
| T-06c | реалізовано severity-summary | GET .../severity-summary?status=NotAStatus | 400 Validation Problem | 400 | .http response |
| T-07 | API і клієнт запущено | натиснути "Показати підсумок" | UI показує результат безпечно | У блоці «Підсумок за severity» відобразилися значення High: 1, Medium: 1, Low: 1, Critical: 0. Дані безпечно виведені через textContent/DOM API. | скріншот UI + Network |

## 5. Security-сценарій

Не застосовується — ЛР 1 не містить навмисної вразливості.

## 6. Висновок

У ході виконання лабораторної роботи було опановано процес наскрізного трасування HTTP-запитів через усі шари архітектури вебдодатка — від клієнтської частини у браузері до бази даних PostgreSQL. Реалізовано нову функцію агрегації даних із застосуванням окремого DTO (IncidentSeveritySummaryResponse) та суворою серверною валідацією вхідних параметрів за допомогою allowlist-фільтрації. Отримані навички дозволяють безпечно розширювати REST API та інтегрувати отримані дані в інтерфейс користувача з дотриманням принципів безпеки DOM.


6721a196ce6bedbc9da2f0b43607da8c90c6036b