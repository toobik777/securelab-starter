# Докази безпеки браузера (DEL-01) — ЛР 5

Рівень: **відмінний («Відмінно»)** (накопичувальний: включає «Задовільно» і «Добре»). Студент: Немировський Артем Юрійович, КБ-41. Спільний сценарій 2-A.
Усі докази зібрано лише на локальному стенді (loopback, штучні дані). Паролі, cookie Value і token у файлах немає.
Основні докази — **знімки справжнього DevTools Chrome 156** у `docs/evidence/devtools/` (фронтенд DevTools, приєднаний до сторінки; кроки виконано в Console самого DevTools, сторінкою керував Playwright). Додаткові рисунки в `docs/evidence/` — знімки сторінок і панелі Network/Elements/Console, побудовані зі зафіксованих даних реального запуску (Playwright/CDP) без зміни значень; сирі значення — `xss-data.json`, `cors-data.json`, `csp-data.json`, `final-data.json`.

### Знімки справжнього DevTools (`docs/evidence/devtools/`)

| Файл | Стан / commit | Що показує |
|---|---|---|
| `v-alice-console.png`, `v-alice-network.png`, `v-alice-elements.png` | vulnerable `0f72d74` | Console: `null`, потім `'executed'`; Network: `POST incidents 201`, `GET {id} 200`, запит `x`; Elements: `<img src="x" onerror=…>` у `#description` і `data-lab05-marker="executed"` |
| `v-morgan-console.png`, `v-morgan-elements.png` | vulnerable | сеанс Morgan: `null` до GET, `'executed'` після; `<img>` у DOM |
| `v-comments-console.png`, `v-comments-elements.png` | vulnerable | другий sink: `<img>` у `<p>` коментаря |
| `v-domtest-console.png` | vulnerable | `description-dom.js` у Console: `FAIL: …` (`passed: false`, 8 порушень) |
| `f-alice-*.png`, `f-morgan-*.png`, `f-comments-*.png` | fixed (final) | той самий запис: `null`; в Elements текстовий вузол `"<img src=x onerror=…>"`, без елемента `<img>` |
| `f-domtest-console.png` | fixed + CSP | `PASS: description and comments rendered as plain text` (`passed: true`) |
| `cv-4174-*.png` | без CORS policy `0f72d74` | probe 4174: `blocked by CORS policy`, `CORS error`, `OPTIONS 405` |
| `cf-4173-*.png` | policy `Lab05BrowserProbe` | probe 4173: GET 200, `GET + Preflight`, `OPTIONS 204`, `Access-Control-Allow-Origin/-Methods/-Headers`, `Access-Control-Allow-Credentials` |
| `cf-4174-*.png` | policy | probe 4174: `CORS error`, preflight без `Access-Control-*` |
| `ro-csp-headers.png`, `ro-csp-console.png` | CSP report-only `87adbca` | `Content-Security-Policy-Report-Only`; порушення лише залоговано |
| `f-csp-headers.png`, `f-csp-console.png` | CSP enforcement | `Content-Security-Policy`, `nosniff`, `no-referrer`; `The action has been blocked.` |

## Стенд і Git-стан

ОС: Windows 11 Home 10.0.26200; браузер Google Chrome 156.0.8078.81 (керування Playwright); .NET SDK 10.0.303; PostgreSQL `postgres:18.4-alpine` (Docker). API і штатний клієнт — `http://localhost:5080`; probes — `http://localhost:4173` (дозволений) і `http://localhost:4174` (недозволений контроль).
Receipt `.scaffolds/lab-05.json`: release `lab-05-start-v1`, base_commit = entry_parent = `447203604b600741a20826e78b534804bad03aca` (тег `v0.4.0`, `4472036`).

| Стан | Commit | Зміст |
|---|---|---|
| вхід | `4472036` (v0.4.0) | fixed state ЛР 4 |
| **vulnerable** | `4fcf1a7` | scaffold lab-05-start-v1 (браузерна сторінка з `innerHTML`) |
| ресурси | `981f3e4` | каталог `resources/` (шаблони, фрагменти, рисунки методички) |
| тестові дані | `0f72d74` | seed-коментар із marker (`DbSeeder.cs`) |
| XSS fix | `814bef9` | safe DOM `renderPlainText` + `tests/browser/description-dom.js` |
| CORS | `7187210` | Development-only policy `Lab05BrowserProbe` |
| CSP report-only | `87adbca` | `Content-Security-Policy-Report-Only` + `auth-lab.css` |
| CSP enforcement | `3540524` | `Content-Security-Policy` |
| **fixed / v0.5.0** | тег `v0.5.0` (`git rev-parse v0.5.0`) | код `3540524` + docs, справжні знімки DevTools, звіт; кінцевий стан Етапу 3 (CORS лише Development, 4173) |

Передавання: гілка `lab/5-browser-xss`, тег `v0.5.0` (вказує на фінальний commit гілки з кодом, документами, доказами і звітом), bundle `lab05.bundle` (див. звіт DEL-03). Локальна `main` (`bcf8a16`, тег `v1.0.0`) не є предком гілки ЛР 5 (у `main` увійшла лише ЛР 1 через pull request), тому fast-forward неможливий; fixed state перенесено до `main` merge-commit без конфліктів (`git merge --no-ff`), без `reset --hard` і без `push --force` для гілок.

Механізм сеансу (Application → Storage → Cookies → `http://localhost:5080`, без Value): назва `.AspNetCore.Identity.Application`, Domain `localhost`, Path `/`, HttpOnly так, SameSite Lax, Secure ні (`docs/evidence/00-login-session.png`). HttpOnly лише обмежує читання cookie з JavaScript — не усуває виконання ін'єктованого коду.

## Гіпотеза й REG-XSS-01

Карта Description — `docs/source-to-sink.md`.
Гіпотеза: значення з поля `textarea[name=description]` (форма `#edit` у `browser-lab.html`) зберігається в PostgreSQL (`incidents.description`), повертається через JSON-поле `description` (`GET /api/incidents/{id}`) і передається у sink `#description.innerHTML` як HTML-контекст; тому за умови, що рядок схожий на тег із атрибутом-обробником, браузер інтерпретує дані не як звичайний текст.

Передумови REG-XSS-01: commit (vulnerable `4fcf1a7`/`0f72d74` або fixed `814bef9`+), `dotnet run --project src/SecureLab.Api -- --reset-database` (змінна `SeedUsers__Password` поза Git), `dotnet run --project src/SecureLab.Api`, облікові записи alice / morgan (Analyst) / bob через `/auth-lab.html`, origin `http://localhost:5080`.
Вхід: рівно один рядок із `resources/lab05-marker.txt` (без зовнішніх hosts, без секретів).
Кроки: (1) Alice: `/browser-lab.html`, у консолі `document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker")` → `null`; (2) ввести назву, вставити marker в «Опис нового інциденту», «Створити й прочитати» (`POST` 201 → `GET` 200); (3) повторити перевірку; (4) скопіювати Guid; (5) Morgan у окремому контексті (private window): login, «Прочитати» цього Guid, перевірка; (6) Bob: той самий Guid (контроль); (7) повторити для `IncidentComment.Text`: seed-інцидент `20000000-0000-0000-0000-000000000001`; (8) автоматично: вставити `tests/browser/description-dom.js` у Console після введення Guid (PASS/FAIL).
Очікування: після fix marker не виконується, в Elements один текстовий вузол, `childElementCount = 0`, нормальний текст працює. Негативний контроль: на vulnerable commit marker виконує лише `data-lab05-marker="executed"` на `#lab05-marker-target`.

| Спостереження | До fix (`0f72d74`) | Після fix (`814bef9`) | Посилання на доказ |
|---|---|---|---|
| Значення в БД/API | `description` = marker, довжина 223 | те саме значення, збіг із `resources/lab05-marker.txt` | `09-db-api-before.png`, `09-db-api-after.png` |
| DOM основного Description | `<div id="description"><img src="x" onerror="…"></div>`, 1 дочірній елемент | один текстовий вузол (`&lt;img …&gt;`), 0 елементів | `04-marker-alice-before/after.png` |
| Дія marker | `data-lab05-marker="executed"` (Alice і Morgan) | атрибута немає (`null`) | `04-…`, `05-marker-morgan-before/after.png` |
| Апостроф/лапки, 5 < 7, Unicode, HTML-like рядок | текст збережено без змін; `<b>жирний</b> текст` стає елементом `<b>` (1 дочірній) | усі п'ять рядків — текст, 0 елементів; `<script>`-seed показано цілком | `02-normal-text-before/after.png`, `03-seed-script-before/after.png` |
| Довжина (3900 символів; межа 4000 за ЛР 2) | відображається, клієнтських помилок немає | так само | `02-normal-text-*.png` |
| Вікно Morgan: GET і перевірка marker; контроль Bob | `GET: 200`, `'executed'`; Bob `GET: 404`, `null` | Morgan `GET: 200`, `null`; Bob `GET: 404`, `null` | `05-…`, `06-bob-control-before/after.png` |
| IncidentComment.Text | у `#comments` елемент `<img>`, marker `'executed'` | текст у `<p>`, marker `null` | `07-comments-before/after.png` |
| DOM-тест `tests/browser/description-dom.js` | **FAIL** (8 причин: текст змінено, дані стали елементами, marker виконано для description і comments, `<b>`) | **PASS** | `08-dom-test-before/after.png` |

Finding. Актив: browser session і дані, доступні від імені користувача (Morgan, Analyst). Передумови: локальний тестовий акаунт, контроль над текстовим полем. Першопричина: недовірений збережений рядок (`description`, `comments[].text`) потрапляє у HTML-sink `innerHTML`. Вплив на стенді: marker виконав лише безпечну локальну DOM-дію. Виправлення: safe DOM (`createTextNode` / `replaceChildren` через єдиний `renderPlainText`), без фільтра слова `script`, без sanitizer (formatted HTML не дозволено в спільному scope). Retest: той самий marker, той самий запис — не виконується. Залишковий ризик: інші поля й sinks у майбутньому коді, сторонні скрипти (у дослідженому flow їх немає; `/scalar` у Development — окремий сторонній UI), контексти URL/атрибута.

Контроль «CSP маскує sink»: `docs/evidence/21-csp-masks-sink.png` — стара vulnerable JS під enforcement CSP: marker `null` (inline handler заблоковано), але `<img>` у DOM лишається; висновок про виправлення робиться лише за DOM.

## CORS і REG-CORS-01

Прогноз (Етап 3, крок 1) і фактичний результат:

| Сценарій | Чи доходить request до API? | Чи може JavaScript прочитати response? | Чому / факт |
|---|---|---|---|
| same-origin 5080 → API | так, `GET /health` 200 | так | CORS не потрібен (`10-cors-before-5080.png`) |
| simple GET 4173 → API (до policy) | так (200 у Network) | ні | `TypeError: Failed to fetch`, `No 'Access-Control-Allow-Origin'` (`11-cors-before-4173.png`) |
| simple GET 4174 → API | так | ні | без allowlist (`12-cors-before-4174.png`) |
| curl/.http → API | так | не застосовується | це не browser JavaScript (`15-curl-direct.png`) |
| preflighted 4173 → API | спочатку `OPTIONS` (405 до policy; 204 після) | до policy ні; після — так | `OPTIONS` + `Access-Control-Request-Headers: x-lab-cors-probe` |

Команди запуску probes (з кореня проєкту):

```
py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe
py -3 -m http.server 4174 --bind localhost --directory demonstrations/cors-probe
```

Policy `Lab05BrowserProbe` — **навчальний Development-стенд**: реєструється і застосовується лише в `Development` (`Program.cs`), один exact origin `http://localhost:4173`, методи `GET`, заголовок `X-Lab-Cors-Probe`, `AllowCredentials()` (для рівня «Добре / Відмінно»); `http://localhost:4174` — негативний контроль; `AllowAnyOrigin()` і відбиття Origin не використано; у Production policy відсутня. Штатна топологія — same-origin `http://localhost:5080`.

| Клієнт / простий запит | До policy | Fixed state | Status, ACAO, Console / доказ |
|---|---|---|---|
| Same-origin 5080 | 200, читається | 200, читається | `10-cors-before-5080.png` |
| Allowed 4173 | `CORS error` (немає ACAO) | 200, читається; `ACAO: http://localhost:4173` | `13-cors-after-4173.png`; з cookie: `ACAC: true` (`16-cookie-4173.png`) |
| Disallowed 4174 | `CORS error` | `CORS error`; actual GET виконано (HTTP 200 на боці API), відповідь не надано JS | `14-cors-after-4174.png`, `16-cookie-4174.png` |
| Direct HTTP / curl | 200 | 200; `ACAO` лише для Origin 4173 | `15-curl-direct.png`, `24-reg-cors-final.png` |

REG-CORS-01. Browser evidence: probes 4173/4174, API 5080, Network містить запити до абсолютної адреси з `Origin` (рисунки 11–14, 16, 18, 19). HTTP evidence (fixed state `3540524`): `curl.exe -sS -D - -o NUL -H "Origin: http://localhost:4173" http://localhost:5080/health` → `HTTP/1.1 200`, `Access-Control-Allow-Origin: http://localhost:4173`, `Access-Control-Allow-Credentials: true`, `nosniff`, `no-referrer`; для Origin 4174 → `HTTP/1.1 200`, жодного `Access-Control-*` (`24-reg-cors-final.png`).
Перевірка порядку (`UseRouting` → `UseCors` до `UseAuthentication`/`UseAuthorization`): запит без cookie з 4173 до `/api/incidents` повертає `401`, який JavaScript може прочитати, а в response є `ACAO` (`13-cors-after-4173.png`, `19-cookie-401-private.png`).
Різниця browser/direct HTTP: same-origin policy — правило браузера для веб-контенту; curl читає відповідь, бо не застосовує SOP, але проходить authentication/authorization API (без cookie — 401). Недозволений origin отримує відповідь API-стороною, браузер лише не надає її коду сторінки; CORS не є authorization і не замінює CSRF-захист.

## Додатково для «Добре» та «Відмінно»

Другий flow IncidentComment.Text: seed-коментар `30000000-0000-0000-0000-000000000005` (Morgan, публічний, marker) на інциденті `20000000-…0001`; до fix у `#comments` — елемент `<img>` і `'executed'`; після — `<p>` із текстовим вузлом і `null` (`07-comments-before/after.png`). Єдиний safe helper: `renderPlainText(container, text, wrapperTag)` у `browser-lab.js`; виклики — для `#description` і для кожного коментаря.

Preflight з 4173: `OPTIONS` 204 → `GET` 200, `Access-Control-Allow-Origin/-Methods/-Headers` (`13-cors-after-4173.png`); з 4174: `OPTIONS` 204 без `Access-Control-*`, actual GET не надіслано (`14-cors-after-4174.png`).
Cookie-дослід: 4173 з Alice — `200`, request із Cookie (значення приховано), response `ACAO` + `ACAC: true` (`16-cookie-4173.png`); 4174 — запит виконано, відповідь не надано (`16-cookie-4174.png`); без `AllowCredentials()` — `The value of the 'Access-Control-Allow-Credentials' header … is '' which must be 'true'` (`18-cookie-negative.png`); приватне вікно без входу — `401` із `ACAO` (`19-cookie-401-private.png`). Кінцевий `AllowCredentials()` лише в Development policy для єдиного origin 4173.

| Директива CSP | Фактичні ресурси застосунку | Обране джерело / причина |
|---|---|---|
| default-src | неявні типи ресурсів | `'self'` |
| script-src | `/app.js`, `/auth-lab.js`, `/browser-lab.js` (власні файли, inline-скриптів немає) | `'self'` |
| style-src | `/styles.css`, `/auth-lab.css` (inline `<style>` auth-lab.html винесено) | `'self'`, без `'unsafe-inline'` |
| connect-src | same-origin `fetch` до API | `'self'` |
| img-src | зображень немає; marker `<img src=x>` — same-origin | `'self'` |
| frame-ancestors | вбудовування не потрібне | `'none'` (немає fallback до default-src) |
| додатково | `object-src 'none'`, `base-uri 'self'`, `form-action 'self'` | закривають плагіни, `<base>`, зовнішні форми |

Місце зміни: наявна спільна middleware заголовків у `Program.cs` (одна middleware, один CSP-header); виняток `/scalar` лише в Development (сторонній UI-пакет; межа і причина записані як залишковий ризик). Enforcement: документи `/`, `/auth-lab.html`, `/browser-lab.html` — `200`, рівно один CSP-header, flow працює без CSP-порушень (`20-csp-enforce.png`); кероване порушення `new Image().src = "http://localhost:4173/x.png"` у report-only лише логується (`…The policy is report-only…`), у enforcement блокується (`…The action has been blocked.`, у Network `blocked:csp`).

| Header / control | Фактичне значення | Ризик і межа захисту / policy rationale |
|---|---|---|
| X-Content-Type-Options | `nosniff` (baseline) | зменшує MIME sniffing; не замінює правильний `Content-Type` і валідацію |
| Referrer-Policy | `no-referrer` (baseline) | клієнт не використовує зовнішніх переходів і не передає secrets у URL, тому повна відмова від Referer прийнятна; не замінює заборону secrets у URL |
| frame-ancestors | `'none'` у CSP | embedding/clickjacking; не замінює authorization чи виправлення XSS |

Review XSS / CORS / CSRF / authorization — у DEL-03 (звіт).

## Додатково для «Відмінно»

DOM-тест: `tests/browser/description-dom.js`, запуск у Console справжнього Chrome 156 на `/browser-lab.html` після входу й введення Guid marker-запису. На `0f72d74` (vulnerable): **FAIL** (`08-dom-test-before.png`); на `814bef9` (fixed): **PASS** (`08-dom-test-after.png`); на `3540524` (fixed + enforcement CSP): **PASS** (`22-final-regression.png`). Тест перевіряє description, кожен `comments[].text`, повторний render без дублювання та п'ять позитивних рядків; FAIL на unsafe sink незалежно від CSP.
CSP report-only → enforcement: `20-csp-report.png` (`Content-Security-Policy-Report-Only`, flow без порушень, кероване порушення лише залоговано), `20-csp-enforce.png` (`Content-Security-Policy`, порушення заблоковано).
safe-url.js: `tests/browser/safe-url.js` виводить сім рядків: `https://example.com/a`, `http://localhost:5080/incidents/1`, тричі `null`, `http://localhost:5080/incidents/1` (href створеного посилання) і `null` (атрибута `onclick` немає) (`22-final-regression.png`).
Різні правила виведення і ризик third-party scripts — у DEL-03 (звіт). Автоматичні тести проєкту: `dotnet test … --configuration Release` на чистій БД — 56 з 56 пройдено.
