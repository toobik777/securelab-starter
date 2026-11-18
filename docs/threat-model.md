# Модель загроз SecureLab, ЛР 6

Рівень огляду: **добрий («Добре»)** (накопичувальний). Предметний baseline: 2-A, ресурс `Incident`.

| Елемент | Що зафіксувати | Запис |
|---|---|---|
| Версія та середовище | review-start, профіль, seed/reset, SDK | review-start commit `92f94f5` (тег `v0.5.0`); локальний Development і production-like (`--no-launch-profile`, `ASPNETCORE_ENVIRONMENT=Production`); `--reset-database` лише в Development; .NET SDK 10.0.303 (`global.json`: нижня межа 10.0.100, `latestFeature`); лише навчальні облікові записи alice, bob, morgan (Analyst), admin |
| Компоненти в межах огляду | клієнт, API, БД, конфігурація, журнали | браузерний клієнт (`src/SecureLab.Api/Client`), ASP.NET Core API, PostgreSQL у Docker Compose (`infra/compose.yaml`), tracked-конфігурація, консольні журнали API. Redis не використовується |
| Активи | облікові дані, власні записи, привілейовані переходи, дані аудиту, секрети конфігурації | A1 пароль/hash користувача (Identity); A2 сеансова cookie; A3 право змінювати власні `Incident` (title, description, severity); A4 привілейований перехід `Incident.Status` (New → Triaged); A5 записи аудиту (`authorization.denied/allowed`, `Auth event`); A6 connection string і навчальні credentials БД; A7 дані інших користувачів (list, search, summary) |
| Суб'єкти | анонім, користувач, привілейована роль, розробник | anonymous visitor; Reporter (alice, bob); Analyst (morgan); Administrator (admin, без додаткових прав на Incident); локальний розробник |
| Entry points | маршрути, форми, конфігурація, restore, журнали | усі HTTP-маршрути (таблиця периметра в `docs/security-review.md`); форми `auth-lab.html`, `browser-lab.html`; `appsettings*.json` і змінні середовища; `dotnet restore` (NuGet); консольний журнал |
| Межі довіри | browser→API, API→PostgreSQL, repository→build, environment→configuration | TB1 browser→API; TB2 API→PostgreSQL; TB3 repository→build; TB4 environment→configuration |
| Наявні controls | validation, authn/authz, safe DOM, CORS, CSP, antiforgery | валідація DTO; Identity cookie (HttpOnly, SameSite=Lax); resource-based handler (ЛР 4); safe DOM `textContent` (ЛР 5); CSP, `nosniff`, `no-referrer`, CORS Development-only (ЛР 5); **antiforgery (ЛР 6)**; rate limiting login/register/search; audit events |
| Виключення | що не перевірялося й чому | зовнішній penetration test; реальний TLS/HSTS і reverse proxy (локальний HTTP-профіль); Git-історія та інші клони/CI (перевірено лише поточні tracked files); Redis/розподілений limiter; навантажувальні тести |

## Схема меж довіри

```text
browser client (недовірений ввід; cookie надсилається автоматично)
        │  TB1: HTTP на loopback (локальний профіль; TLS не тестувався)
        ▼
ASP.NET Core API  (headers/CSP, antiforgery, rate limit, authn/authz, validation, logging)
        │  TB2: connection string / EF Core (параметризовані запити)
        ▼
PostgreSQL (локальний контейнер, 127.0.0.1)

repository + NuGet ──TB3──► restore/build (locked lockfile, NuGet audit)
environment variables ──TB4──► API startup (Development vs Production-like)
```

## Очікувані властивості на межах

| Межа | Очікувана властивість | Наявна перевірка |
|---|---|---|
| TB1 browser→API | змінювальний запит із cookie без валідного antiforgery token відхиляється без зміни стану | `Lab06SecurityTests.RegCsrf01_*`, `Perimeter_*` |
| TB1 browser→API | текст користувача не виконується як код | REG-XSS-01 (`tests/browser/description-dom.js`), `IncidentEndpointTests.ClientScript_DoesNotUseDangerousInnerHtmlSink` |
| TB1 browser→API | cross-origin JS не читає відповідь (лише точний Development origin) | `RegressionPackTests.RegCors01_*` |
| TB1 browser→API | відповіді (у т. ч. 500) мають заголовки безпеки; API не кешується | `Lab06SecurityTests.Headers_*`, `Production_UnexpectedError_*` |
| TB1 browser→API | API не довіряє `OwnerUserId`/role із body; чужий ресурс недоступний | `AuthorizationTests.A03_*`, `A04_*`, `A08a_*` |
| TB1 browser→API | зловживання search обмежується per-user quota (429), без підміни authentication | `Lab06SecurityTests.SearchRateLimit_*` |
| TB2 API→PostgreSQL | параметр пошуку не змінює структуру SQL | `RegressionPackTests.RegSqli01_*` (S-02 ЛР 2) |
| TB3 repository→build | tracked files без реальних credentials; restore не змінює lockfile непомітно | `git grep` (розділ 9 review); `dotnet restore --locked-mode`; `dotnet package list --vulnerable` |
| TB4 environment→configuration | у Production не діє Development-конфігурація; reset заборонено; OpenAPI/CORS не реєструються | `Lab06SecurityTests.Production_*`, консольний доказ `Hosting environment: Production` |

## Ланцюжок asset → threat → control → verification (додатково до доброго)

| Актив | Загроза | Control | Перевірка |
|---|---|---|---|
| A3/A4: право Alice змінювати власні інциденти та їхній статус | сторонній сайт у тому самому браузері надсилає змінювальний запит з автоматичною cookie Alice (CSRF) | antiforgery-валідація на всіх cookie-authenticated mutations до будь-якої зміни стану | REG-CSRF-01: без токена 400 і незмінний ресурс; з валідною парою — очікувана зміна (reset, alice, власний інцидент, `dotnet test --filter Lab06SecurityTests`) |
| A7: дані інших користувачів | IDOR/BOLA; перебір через search/summary | resource-based handler; query scoping; per-user search limiter | `AuthorizationTests.A03/A04/A11`, `SearchRateLimit_*` |
| A5: записи аудиту | відсутність сліду для дозволеної privileged дії; секрети в журналі | `authorization.denied` та `authorization.allowed` без credentials | `A09_*`, `Audit_AllowedPrivilegedTransition_*`, grep-перевірка журналу (0 збігів) |
| A6: credentials БД | витік через tracked files, stack trace або журнал | study-only значення класифіковано; безпечна 500 з traceId | розділ 9 review; `Production_UnexpectedError_*` |
| A2: сеансова cookie | викрадення через XSS/ефект кешування; використання на чужому host | HttpOnly, CSP, safe DOM, `Cache-Control: no-store`, AllowedHosts | REG-XSS-01, `Headers_*`, `HostFiltering_*` |
