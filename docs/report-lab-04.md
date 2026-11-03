# Звіт ЛР04

Автор: Немировський Артем Юрійович, група КБ-41. Дисципліна «Прикладні технології програмування в інформаційній безпеці». Викладач: Бабенко Ю.М.
Повний звіт зі скріншотами: `docs/Звіт_ЛР04_Немировський.docx` (подається окремо в Moodle; у git не додається).

## Контекст і межа виконання
- Рівень: **відмінний («Відмінно»)**; обґрунтування: виконано достатній і добрий рівні (матриця, PoC/retest read і mutation, list, update, delete, triage, child, internal comment, audit, A-01…A-10, A-12) та відмінний (відокремлений resource-based handler, A-11 для search і severity-summary з окремим finding, обґрунтування видимості/least privilege/залишкового ризику).
- Власний baseline 2-A, base tag v0.3.0, hash `bacc655`.
- Головна action: `GET /api/incidents/{id}` (details) і всі mutation-маршрути; політика: **404** для ресурсу, який principal не може читати (збігається з неіснуючим id), **403** для видимого, але забороненого, **409** для недопустимого поточного status.
- Виправлення стану ЛР 3 після тега не потрібні: тег v0.3.0 не пересувався.

## Матриця і server-side рішення
- Матриця: `docs/access-matrix.md`.
- Рішення для одного ресурсу: `IncidentAuthorizationHandler` (`src/SecureLab.Api/Application/Incidents/IncidentAuthorization.cs`) і єдина функція `DecideAsync` в `IncidentEndpoints.cs`.
- Query scoping: `IncidentQueries.GetListAsync/GetSeveritySummaryAsync` і search у `Lab02Endpoints.cs` (умова `canSeeAll || OwnerUserId == principal` у самому запиті).
- DTO: `UpdateIncidentRequest` містить лише `title`, `description`, `severity`; owner, status і role визначає сервер.

## Вразливий стан і PoC
- Vulnerable commit: **`072c63c`**. Сценарій: `tests/http/lab-04-access.http`, `scripts/lr04/lab04_poc.py`.
- До: Alice → деталі Bob 200 з даними Bob; PUT чужого 204 (заголовок змінено); DELETE чужого 204; Reporter triage 200; PUT власного з `status: Closed` і `ownerUserId: Bob` змінив status і власника; чужий і internal comment 200; анонімні details, пошук і зведення 200.
- Першопричина: пошук лише за id; відсутні порівняння principal з owner, ресурсне рішення, перевірка ролі для privileged action; update DTO містив protected поля; child завантажувався за власним id.

## Виправлення та retest
- Fixed commit: **`4472036`** (тег `v0.4.0`).
- Після: Alice → власні деталі 200; Alice → Bob 404 «Інцидент не знайдено», без даних Bob; PUT/DELETE чужого 404, стан Bob без змін; triage Reporter 403 (власний) / 404 (чужий), status без змін; overposting – змінюється лише дозволений content, owner і status не змінюються; Morgan: details 200 з internal comment, triage New → Triaged 200, повтор 409; чужий і internal comment для Reporter 404; список Alice – лише власні; search O'Brien у Alice 0 результатів; анонім 401.
- Відповіді для чужого й неіснуючого id збігаються за status, Content-Type і тілом без `traceId` (A-10) для read, update, delete, triage і child.

## Доказова таблиця

| ID | Рівень | Сценарій | Передумови | Дія | Очікувано | Фактично | Доказ |
|---|---|---|---|---|---|---|---|
| A-01 | добрий | Анонімний доступ | немає authentication context | details, list, summary, search, comment, triage | `401`, не HTML redirect | 401 для всіх; `Location` відсутній | `A01_Anonymous_ProtectedRoutes_Return401_NotHtmlRedirect` |
| A-02 | достатній, обов'язковий | Власний ресурс | Alice має власний seed resource | details; update власного | allow | details 200 (id …0001); update 204, змінено лише title/severity | `A02_*` |
| A-03 | достатній, обов'язковий | Чужий ресурс | Alice, інцидент Bob | details з id Bob | `404`, без даних Bob | 404; у тілі немає тексту, імені, id Bob | `A03_Alice_ReadsBobIncident_Returns404_AndNoBobData` |
| A-03R | достатній | Аналітичний read | Morgan (`Analyst`), ресурс Alice | `GET /api/incidents/{id}` | allow для аналізу | 200; коментарі 01 і 03 (internal); Alice бачить лише 01 | `A03R_*` |
| A-04 | добрий | Чужий ресурс, mutation | Alice, ресурс Bob | PUT, DELETE | deny, state без змін | 404; title/description/severity/UpdatedAt без змін; запис існує | `A04_*` |
| A-05 | добрий | List | Alice | `GET /api/incidents` | лише дозволена множина | інциденти Alice (1, 3, 5); Bob відсутній; Analyst бачить усі | `A05_*` |
| A-06 | добрий | Вертикальне підвищення | Reporter | triage | deny, status без змін | 403 (власний), 404 (чужий); status `New` | `A06_*` |
| A-07 | добрий | Дозволена привілейована дія | Morgan, status New | triage | allow | 200, `Triaged`; history від Morgan; повтор 409; PUT/DELETE чужого 403 | `A07_*` |
| A-08a | добрий | Overposting (контракт ігнорує невідоме поле) | Alice, DTO без protected полів | PUT з forged status/ownerUserId/role/id | content може змінитися, protected state – ні | 204; title змінено; owner Alice; status New; triage Alice 403 | `A08a_*` |
| A-08b | добрий, альтернатива | — | — | — | — | не виконувалось: обрано A-08a | — |
| A-09 | добрий | Audit denied privileged action | Reporter, deny triage | знайти `authorization.denied` у виводі API | безпечна подія без секретів | рядок із subject, action, resource, result=denied, UTC, traceId; traceId збігається з відповіддю | `A09_*`, `lab04-evidence/audit-evidence.txt` |
| A-10 | добрий (політика 404) | Нерозрізнюваність 404 | Alice; чужий і неіснуючий id | два однакові запити | однакові status, Content-Type, тіло | збігаються (read, update, delete, triage, child) | `A10_*` |
| A-11 | відмінний | search і severity-summary | Alice | `search?q=O'Brien`, `severity-summary` | лише дозволена множина | 0 результатів (до: 1); summary Low 4, Medium 1, High 0 (до: 5/1/1); анонім 401 | `A11_*` |
| A-12 | добрий | Child і internal comment | Alice; comment-2, comment-3 | `GET /api/incident-comments/{id}` | deny за політикою | 404 для обох; comment-3 200 для Morgan | `A12_*` |

## Основний finding і залишковий ризик
Контекст: після ЛР 3 є автентифікація, але немає авторизації ресурсу. Гіпотеза: заміна id у запиті дає доступ до чужого ресурсу. Стан до: commit `072c63c`. Першопричина: пошук лише за id. Fix: handler + query scoping + звужений DTO (commit `4472036`). Retest: ті самі запити 404/403 замість 200; 56 тестів проходять; ті самі нові тести падають (19 з 23) на `072c63c`.
Залишковий ризик: CSRF для cookie mutation до ЛР 6 (навчальний борг); роль у cookie оновлюється лише після повторного login; аудит лише для privileged action.

## Browser flow
Login на `auth-lab.html` → головна сторінка → власні деталі Alice → probe з id Bob (`loadIncidentDetails`): «Помилка: Інцидент не знайдено». Клієнт відображає результат через `textContent`/`createTextElement`; список Alice – лише власні; Morgan бачить усі інциденти й internal comment. Знімки – у `Звіт_ЛР04_Немировський.docx`.

## Добрий рівень: роль, transition, child, audit
Triage – лише `Analyst` і лише `New → Triaged`, next status встановлює сервер; Reporter отримує 403/404 і audit; Morgan до comment Bob/internal до Alice: Reporter 404, Analyst – після рішення для parent. Audit містить subject, action, resource, result, UTC, trace; trace збігається з `traceId` у відповіді.

## Відмінний рівень
Відокремлений resource-based handler `IncidentAuthorizationHandler`; A-11 для search і severity-summary (finding з доказом до/після); обґрунтування видимості, least privilege і залишкового ризику – у `docs/access-matrix.md` і в docx-звіті. Batch/export — не застосовується; concurrent transitions — не застосовується (відтворюваний сценарій від викладача не надавався).

## Перевірки, відтворення і подання
- ОС: Windows 11; .NET SDK 10.0.303; PostgreSQL у Docker Compose.
- Команди: `dotnet build SecureLab.sln -c Release`; `docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait`; `dotnet run --project src/SecureLab.Api -- --reset-database`; `dotnet test tests/SecureLab.Api.Tests -c Release`; потім `dotnet run --project src/SecureLab.Api --launch-profile http`.
- Результат: Build succeeded; 56 з 56 тестів пройдено (після reset).
- Тег `v0.4.0` = fixed commit `4472036`; vulnerable commit `072c63c`; гілка `lab/4-authorization`.
- Не виконувалось: A-08b (обрано A-08a); batch/export; concurrent transitions (немає сценарію).
