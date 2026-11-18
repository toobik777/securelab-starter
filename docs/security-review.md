# Security review SecureLab, ЛР 6

Рівень: **добрий («Добре»)** (накопичувальний: достатній + добрий). Окремі елементи вищого рівня (повний автоматизований REG-CSRF-01, шість сценаріїв журналу, advanced variant) до оцінюваного рівня не заявляються: advanced variant не виконувався.

## 1. Executive summary

Перевірено власний fixed state ЛР 5 (`v0.5.0`, review-start `92f94f5`) і далі гілку `lab/6-hardening-review` на локальному стенді (ASP.NET Core API + клієнт + PostgreSQL, Development і production-like профілі, лише навчальні облікові записи). Межі та виключення — розділ 2 і `docs/threat-model.md`. Зафіксовано й виправлено **6 findings** (P1: 1, P2: 2, P3: 3): відсутність antiforgery на cookie-authenticated mutations (F-02), відсутність заголовків безпеки на error path 500 (F-01), неповний audit trail привілейованого переходу (F-04), кешування й SPA-fallback для API (F-03), `AllowedHosts: "*"` (F-05) та 500 замість 400 для недопустимого `sortBy` (F-06, знайдено регресією ЛР 2). Final regression `dotnet test`: **69 з 69**; автоматизований повний REG-CSRF-01 пройшов. Найважливіші залишкові ризики: CSRF-токен доступний коду, що виконається на власному origin (XSS), HTTPS/HSTS не тестувалися на локальному HTTP-профілі.

## 2. Scope та exclusions

У межах: браузерний клієнт (`Client/*`), API (`src/SecureLab.Api`), PostgreSQL (Docker Compose), tracked-конфігурація, консольні журнали, залежності NuGet. Поза межами (причина): реальний TLS/HSTS і reverse proxy (немає production host); Git-історія, stash, інші клони й зовнішній CI (огляд лише поточних tracked files); зовнішній pentest; Redis (не використовується, див. розділ 10); навантажувальне тестування.

## 3. System overview і trust boundaries

Схема і таблиця «межа → властивість → перевірка» — `docs/threat-model.md`. Cookie-профіль фіксований: ASP.NET Core Identity, cookie `.AspNetCore.Identity.Application` (HttpOnly, SameSite=Lax, Secure поза Development). Роль Analyst виконує лише `New → Triaged`; Administrator не має прав на Incident понад baseline.

## 4. Methodology та test environment

- review-start commit: `92f94f5` (тег `v0.5.0`); SDK-policy commit `e269e1a`; код — `e13ede9`, `ebbc7e2`, `d678320`; прийнятий merge і фінальний тег `v1.0.1` — hash додається в PDF-експорт (`git rev-list -n 1 v1.0.1`). Назва `v1.0.0`, яку вказує методика, вже зайнята лише-легким тегом на commit `bcf8a16` (ЛР 1, уже опублікований на GitHub); існуючий тег не переміщували й не перезаписували, тому фінальний тег ЛР 6 має назву `v1.0.1`.
- Середовище: Windows 11; .NET SDK 10.0.303; PostgreSQL у Docker (`127.0.0.1:54329`); Development — `launchSettings.json` (`http://localhost:5080`); production-like — `ASPNETCORE_ENVIRONMENT=Production`, `--no-launch-profile`, `ConnectionStrings__SecureLab` поза Git, `Logging__Console__IncludeScopes=true`.
- Підхід: спершу межі й активи (threat model), далі перевірка контролів на фактичних відповідях (`scripts/lr06/lab06_probe.py`), тести через HTTP-межу (xUnit + `WebApplicationFactory`), перегляд журналів і залежностей. Reset (`--reset-database`) лише в Development перед експериментами.
- Клієнти спостережень: Python-helper (cookie в пам'яті процесу), `curl.exe`, cookie-aware test host (`https://localhost` base address). Test host не доводить реальний TLS чи HSTS.

## 5. Findings summary

| ID | Finding | Актив | Impact | Likelihood | Priority | Status | Retest / регресія |
|---|---|---|---|---|---|---|---|
| F-02 | Cookie-authenticated POST/PUT/DELETE без antiforgery (CSRF) | A3, A4 | High | Medium | P1 | Fixed | REG-CSRF-01, `Perimeter_*` |
| F-01 | На 500 (error path) відсутні CSP, `nosniff`, `Referrer-Policy` | A2, A6 | Medium | Medium | P2 | Fixed | `Production_UnexpectedError_*`, `Headers_*` |
| F-04 | Audit trail привілейованого переходу не фіксує дозволену дію | A5 | Medium | Medium | P2 | Fixed | `Audit_AllowedPrivilegedTransition_*`, `A09_*` |
| F-03 | API-відповіді без `Cache-Control`; невідомий `/api/*` → 200 text/html | A2, A7 | Low | Medium | P3 | Fixed | `Headers_*` (unknown API path) |
| F-06 | Недопустимий `sortBy` → 500 замість 400 | якість контракту, моніторинг | Low | High | P3 | Fixed | `RegSqli01_*` |
| F-05 | `AllowedHosts: "*"` (Host-фільтрацію вимкнено) | A2 | Low | Low | P3 | Fixed | `HostFiltering_*` |

**Порядок виправлення.** F-02 першою: єдина знахідка, що дозволяла змінювати та видаляти чужі від імені користувача ресурси без його відома (impact High, достатньо відкритої сторінки в тому ж браузері). F-01 і F-04 — P2: перша послаблює захист у момент відмови (саме коли клієнт отримує нестандартну відповідь), друга позбавляє оператора сліду значущої дії; обидві потребують певних умов (збій; privileged роль). F-03, F-05, F-06 — P3: низький impact, обмежені передумови; F-06 виправлено разом із розширенням regression pack, бо її виявила повторна перевірка контрактів ЛР 2. Тимчасово прийнятих ризиків немає; відповідальний за follow-up residual-ризиків — автор роботи (розділ 10).

## 6. Detailed findings

Картки: `docs/findings/F-01-security-headers-on-error-path.md`, `F-02-antiforgery-perimeter.md`, `F-03-api-cache-and-fallback.md`, `F-04-audit-allowed-transition.md`, `F-05-allowed-hosts.md`, `F-06-invalid-sortby-500.md`. Кожна містить відтворення, вплив, першопричину, fix, retest, positive regression і залишковий ризик. Третя знахідка поза прямим checklist (F-03, F-06) знайдена власним аналізом класів відповідей і regression pack.

### Периметр змінювальних маршрутів (етап 2, п. 3)

Джерела зіставлено: `git grep -n -E "\.Map(Post|Put|Patch|Delete)\(" -- src`, `git grep -n -E "method: *.(POST|PUT|PATCH|DELETE)" -- src/SecureLab.Api/Client`, OpenAPI (`/openapi/v1.json`), код ЛР 2–5.

| ID | Method + route | Джерело | Яка зміна стану | Cookie auth | Browser-accessible | Antiforgery | Regression |
|---|---|---|---|---|---|---|---|
| SC-01 | POST `/api/incidents` | ЛР 2/3; клієнт: `auth-lab.js`, `browser-lab.js` | створення Incident | так | так | explicit: `RequireAntiforgery()` (`AntiforgeryValidationFilter`) | `Perimeter_*`, `RegCsrf01_*` |
| SC-02 | PUT `/api/incidents/{id}` | ЛР 4 (API-only: клієнт не викликає) | зміна content | так | так (довільний HTTP-клієнт у браузері) | explicit | REG-CSRF-01 |
| SC-03 | DELETE `/api/incidents/{id}` | ЛР 4 (API-only) | видалення | так | так | explicit | `Perimeter_*` |
| SC-04 | POST `/api/incidents/{id}/triage` | ЛР 4 (API-only) | New → Triaged | так | так | explicit | `Perimeter_*` |
| SC-05 | POST `/api/auth/logout` | ЛР 3; клієнт: `auth-lab.js` | завершення сеансу | так | так | explicit; після logout токен очищається | `Perimeter_*`, `LegitimateFlow_*` |
| SC-06 | POST `/api/auth/login` | ЛР 3; клієнт | створення сеансу | **ні** | так | absent — рішення в scope: login-CSRF ризик прийнято (низький; mitigations: rate limit 5/60 с за адресою, однакова відповідь 401, SameSite=Lax); токен до login потребував би окремого анонімного bootstrap | `A07_*`, `Login_RateLimit_*` |
| SC-07 | POST `/api/auth/register` | ЛР 3; клієнт | створення акаунта | **ні** | так | absent — рішення аналогічне SC-06 (account-creation spam обмежує rate limit 10/60 с) | `T01_*`, `Register_RateLimit_*` |

Розбіжностей, що вимагали виправлення документації, немає: маршрути SC-02…SC-04 присутні лише на сервері (клієнт їх не викликає), CSRF оцінюється за HTTP endpoint, а не за кнопкою UI. Safe-метод endpoint-ів, що змінюють стан, не знайдено. Токен видає `GET /api/auth/csrf-token` (лише автентифікованим; header `X-CSRF-TOKEN`).

Ручна перевірка клієнта (всі змінювальні дії UI із валідною парою token/cookie): login → create incident (`auth-lab.html`) — 201; створення з `browser-lab.html` — 201; logout — 204 і повторний `/api/me` — 401; після нового login токен одержується знову. Усі працюють.

## 7. Regression results

Команда фінального прогону: `dotnet run --project src/SecureLab.Api -- --reset-database`, потім `dotnet test SecureLab.sln -c Release` (SeedUsers__Password задано поза Git). Результат: **Passed 69, Failed 0**.

| Рівень | ID сценарію | Property | Негативний сценарій | Позитивна перевірка | Evidence / command | Result |
|---|---|---|---|---|---|---|
| достатній | REG-SQLI-01 (S-02 ЛР 2) | query не змінює структуру SQL | payload `' OR '1'='1`, `; DROP TABLE`, `%`, `_`, `\`; `sortBy` з SQL | звичайний пошук і сортування | `dotnet test --filter RegSqli01` | pass |
| достатній | A-01…A-07 (ЛР 3) | identity lifecycle | анонім/невалідний login → 401; однакова відмова | login/logout, `/api/me` | `--filter AuthSecurityTests` | pass |
| достатній | A-03, A-04, A-10, A-12 (ЛР 4) | server-side ownership/role | чужий id → 404, стан без змін | owner/Analyst дозволені | `--filter AuthorizationTests` | pass |
| достатній | REG-XSS-01 (ЛР 5) | text не виконується як code | marker `<img onerror>` | normal complex text | `tests/browser/description-dom.js` (DevTools Console), `--filter ClientScript_DoesNotUseDangerousInnerHtmlSink` | pass (xUnit); DOM-процедура — докази ЛР 5 |
| достатній | **REG-CSRF-01** | token pair required | без токена і з невалідним токеном → 400, стан не змінився | валідна пара → очікувана зміна | `--filter RegCsrf01` | pass |
| добрий | T-02 (ЛР 2) → `Validation_*` | server validation | недопустиме DTO (з валідним токеном) → 400 + поле `errors.Severity` | допустиме input зберігається | `--filter RegressionPackTests` | pass |
| добрий | REG-CORS-01 (ЛР 5) | exact browser origin | origin 4174 і `https://evil.example` без `Access-Control-Allow-Origin` | origin 4173 дозволено з credentials | `--filter RegCors01` | pass |
| добрий | rate limiting login (ЛР 3) | abuse flow limited | 4-й запит за вікно → 429 | відновлення після вікна | `--filter Login_RateLimit` | pass |
| добрий | rate limiting search (ЛР 6) | per-user quota | 4-й запит → 429; інший partition не блокується | відновлення після вікна | `--filter SearchRateLimit` | pass |
| відмінний | REG-CSRF-01 (автоматизований повний) | login → відмова без токена → незмінний стан → токен → дозволена зміна → змінений стан | див. вище | див. вище | `--filter RegCsrf01_FullFlow` | pass |

Повторне виконання original-сценарію після fix і positive regression для кожної finding — у картках. Ручні/DevTools-докази ЛР 5 (REG-XSS-01, REG-CORS-01 у браузері) — у `docs/browser-security-evidence.md`.

## 8. Hardening controls

| Control | Ризик | Верифікація | Межа |
|---|---|---|---|
| Antiforgery (header `X-CSRF-TOKEN` + cookie `SecureLab.Antiforgery`, HttpOnly, SameSite=Strict, Secure поза Development) | CSRF на cookie-authenticated mutations | REG-CSRF-01, `Perimeter_*` | не захищає від коду на власному origin (XSS); login/register поза периметром (SC-06/07) |
| CSP (enforcement) + `frame-ancestors 'none'` + `X-Frame-Options: DENY` | наслідки injection, clickjacking | `Headers_*` (без `unsafe-inline`/`*`), клієнт працює (browser flow) | не замінює safe DOM; `/scalar` у Development має виняток |
| `X-Content-Type-Options: nosniff`, `Referrer-Policy: no-referrer` на всіх класах відповідей, включно з 500 | MIME sniffing; витік referrer | `Headers_*`, `Production_UnexpectedError_*` | не замінює правильний Content-Type; відповідь HostFiltering (400) формується до middleware |
| `Cache-Control: no-store` для `/api/*` | кешування даних користувача | `Headers_*` | не охоплює статичні файли (публічні) |
| CORS: лише Development, точний origin 4173, без wildcard; у Production не реєструється | читання cross-origin відповіді | REG-CORS-01, `Production_Profile_*` | не замінює authN/authZ/CSRF |
| Auth cookie: HttpOnly, SameSite=Lax, Secure поза Development | частина cookie-ризиків | `Cookie_*` (ЛР 3) | Secure на локальному HTTP не доводить TLS |
| Rate limiting: login (за адресою), register, **search (за user id)** | зловживання, brute force | `Login_RateLimit_*`, `SearchRateLimit_*` | in-memory, не спільний між інстансами; rate limit не замінює authN/authZ |
| AllowedHosts `localhost;127.0.0.1` | Host-header атаки | `HostFiltering_*` | для deployment потрібен власний перелік |
| Безпечна 500: Problem Details без деталей + `traceId` | витік внутрішніх деталей | `Production_UnexpectedError_*`, консольний доказ | помилки БД лишаються в логах |
| Audit: `authorization.denied`, `authorization.allowed`, `Auth event` | немає сліду значущих дій | `A09_*`, `Audit_Allowed*` | audit лише для privileged action; read-доступ не журналюється |

Development vs production-like (змістові відмінності): `MapOpenApi()`/Scalar лише в Development (у Production `/openapi/v1.json` → 404 Problem Details, `/scalar/v1` → SPA-fallback HTML); CORS policy лише в Development; `--reset-database` у Production відхиляється (`InvalidOperationException`); connection string поза Git (`ConnectionStrings__SecureLab`), `Include Error Detail=false`; cookie Secure (`SecurePolicy.Always`) у Production; `Database:ApplyMigrationsOnStartup/AllowReset=false`.

### Заголовки на п'яти класах відповідей (етап 2, п. 1)

Development (клас 1–4) і production-like (клас 5); клієнт: Python-helper з сеансом alice (cookie в пам'яті).

| Клас | Profile | Method + route | Status | Заголовки (після виправлень) | Ризик | Межа |
|---|---|---|---|---|---|---|
| статична сторінка | Dev, anonymous | GET `/` | 200 text/html | nosniff; no-referrer; CSP; X-Frame-Options DENY | MIME sniffing; clickjacking; referrer leakage | не замінює правильний Content-Type |
| успішний API | Dev, alice | GET `/api/incidents/{aliceId}` | 200 JSON | ті самі + `Cache-Control: no-store` | кешування персональних даних | — |
| validation 4xx | Dev, alice | GET `/api/incidents?status=Unknown` | 400 problem+json | ті самі + no-store | — | — |
| 404 | Dev, alice | GET `/api/incidents/99999999-…` | 404 problem+json | ті самі + no-store | — | — |
| контрольована 500 | Production, anonymous login, БД 54399 недосяжна | POST `/api/auth/login` | 500 problem+json | **до fix: лише `cache-control: no-cache,no-store` (від antiforgery-відповіді відсутній), nosniff/referrer/CSP відсутні; після fix: усі заголовки є** | витік діагностики не відбувається, але браузерні захисти зникали саме на error path | `traceId` лише корелює, деталей не розкриває |

Додатково: невідомий API-шлях до fix → 200 text/html (SPA fallback), після → 404 problem+json.

## 9. Dependency and secret audit

**Залежності.** SDK 10.0.303; `dotnet restore --locked-mode` без помилок; `dotnet package list --vulnerable --include-transitive` (2026-11-18, код завершення 0): для `SecureLab.Api` і `SecureLab.Api.Tests` джерело `api.nuget.org` не повідомило про відомі advisories для версій із `packages.lock.json`. Direct: Microsoft.AspNetCore.Identity.EntityFrameworkCore 10.0.10, Microsoft.AspNetCore.OpenApi 10.0.10, Microsoft.EntityFrameworkCore(.Design/.Relational) 10.0.10, Microsoft.OpenApi 2.7.5, Npgsql.EntityFrameworkCore.PostgreSQL 10.0.3, Scalar.AspNetCore 2.16.16. Нових пакетів ЛР 6 не додавала (antiforgery і rate limiting — вбудовані). Межа висновку: лише відомі на цю дату advisories цього джерела; досяжність коду та невідомі вразливості не оцінювалися.

| Package | Version | Direct / transitive | Advisory / severity | Чи використовується relevant functionality | Доступне оновлення | Рішення і причина |
|---|---|---|---|---|---|---|
| (немає знайдених advisories) | — | — | немає | — | не оцінювалось | рішення «без змін»; придушення `NuGetAuditSuppress`/`WarningsNotAsErrors` не застосовувались; повторити audit перед кожним release |

**Секрети й конфігурація (tracked state).** Команди: `git ls-files '*appsettings*.json' '*.env*' '*compose*.yml' '*compose*.yaml' '*.config'`; `git check-ignore -v infra/.env src/SecureLab.Api/appsettings.Local.json`; `git grep -n -I -i -E 'password|passwd|secret|api.?key|token|connectionstring' -- .` (межа: усі поточні tracked files; Git-історія, stash, інші клони й зовнішній CI не перевірялися). Matches: 318 у 33 файлах, переважна більшість — назви полів, DTO, тести й документація.

| Path | Безпечний контекст | Рішення | Причина й межа |
|---|---|---|---|
| `infra/compose.yaml` (POSTGRES_PASSWORD), `src/SecureLab.Api/appsettings.Development.json`, `Data/SecureLabDesignTimeDbContextFactory.cs` | відкрите навчальне значення БД, порт прив'язано до 127.0.0.1; README забороняє повторне використання | accepted, study-only | не поширюється на інші БД і середовища; значення не видаляли, бо узгоджені три входи конфігурації |
| `infra/.env.example` | лише `POSTGRES_PORT`, коментар «це не секрети» | OK | `.env` ігнорується (`.gitignore:15`); не доводить відсутність секретів в untracked файлах |
| `tests/.../Lab06SecurityTests.cs`, `Lab04*`/`lab04_poc.py` | навмисно недосяжний тестовий connection string `unreachable-study-value`, назви змінних | не секрет | локальний test-only; недосяжний порт 54399 |
| `Scaffolding/Lab03Identity.cs`, `*Tests.cs`, `scripts/*`, `Client/*.js` | назви полів `password`, `token`, зміна середовища `SeedUsers__Password` | не секрет | значення задаються поза Git |
| `packages.lock.json`, `*.csproj` | ідентифікатори пакетів (`...UserSecrets`) | не секрет | назви залежностей |
| `README.md`, `docs/*` | згадки в документації | не секрет | README не просить копіювати реальний пароль |
| `src/SecureLab.Api/appsettings.json` → `AllowedHosts` | було `*` | finding F-05 | виправлено на `localhost;127.0.0.1` |

Реальних credentials не виявлено. Для реального витоку одного видалення з поточного файла недостатньо: значення могло залишитися в історії, clone, cache, log або fork; потрібні rotation/відкликання (історію не переписують без інструкції викладача).

**Логи (шість сценаріїв, відмінний рівень).** Sink: консоль API → `../lr06-logs/api.log` (поза Git), 408 рядків; сценарії: successful login (`Auth event: login succeeded`), failed login (`login failed`, без імені), denied protected action (`authorization.denied`), validation error (400), privileged transition (`authorization.allowed`), unexpected server error (Production, БД недосяжна: `ExceptionHandlerMiddleware` з тим самим TraceId). Перевірені поля: пароль seed, `NotARealPassword1!`, `Cookie:`/`Set-Cookie`, `X-CSRF-TOKEN`, `Authorization`/`Bearer`, connection-string значення. Результат: **0 збігів** для кожного рядка. Межа: ці сценарії і цей sink. Користувацьке ім'я при невдалому login не журналюється.

## 10. Residual risks

1. **CSRF-токен і XSS.** Код, що виконається на власному origin, може одержати токен так само, як helper; захист від цього — safe DOM і CSP (ЛР 5), не antiforgery.
2. **Login/register поза antiforgery** (SC-06/07): login-CSRF і account-creation spam прийнято за низького ризику; мітигація — rate limit і однакові відповіді.
3. **HTTPS/HSTS не тестувалися.** Локальний профіль — HTTP; `UseHttpsRedirection()`/`UseHsts()` у застосунку немає; deployment, TLS termination і HSTS на production host не перевірялися (HSTS на loopback — не дефект).
4. **Rate limiter in-memory** — лічильники не спільні між інстансами; для кількох інстансів потрібне спільне сховище. Partition за IP може об'єднувати користувачів за NAT.
5. **Роль у cookie** оновлюється лише після повторного login; відкликання скопійованого квитка до події на сервері неможливе (ЛР 3).
6. **Host filtering** відхиляє запит до middleware заголовків: відповідь 400 не має заголовків безпеки (приймається).
7. **Advanced variant не виконувався** — він потрібен лише для рівня «Відмінно», який не заявляється.

## 11. Conclusion

Систему розглянуто як цілісну: контролі ЛР 2–5 повторно перевірено, знайдено й усунено шість власних дефектів, головний з яких — відсутність CSRF-захисту cookie-периметру. Кожну finding підтверджено тестом із негативним і позитивним результатом; фінальний прогін на прийнятому merge commit — 69 з 69. Залишкові ризики названо чесно, а advanced variant, потрібний лише для рівня «Відмінно», не виконувався й не заявляється.

## 12. Evidence appendix

| ID | Що доводить | Де лежить / як повторити |
|---|---|---|
| E-01 | відтворюваний старт | `git rev-parse v0.5.0` = `92f94f5…`; `docs/threat-model.md` |
| E-02 | CSRF до fix (мутації без токена: 201/204/204) і після (400, стан незмінний, з токеном 201/204/204) | `py -3 scripts/lr06/lab06_probe.py csrf` (до: токена не було); `RegCsrf01_*` |
| E-03 | заголовки на п'яти класах | `py -3 scripts/lr06/lab06_probe.py headers`; `curl.exe -s -o NUL -D - http://localhost:5080/` |
| E-04 | безпечна 500 + traceId + журнал | `scripts` + production-запуск з БД 54399; середній 32-hex сегмент `traceId` = `TraceId` у записі `ExceptionHandlerMiddleware` |
| E-05 | production-профіль | консоль `Hosting environment: Production`; `/openapi/v1.json` → 404; `--reset-database` → InvalidOperationException |
| E-06 | dependency audit, secret grep, log grep | команди розділу 9 |
| E-07 | final regression | `dotnet test SecureLab.sln -c Release` → Passed 69 |
