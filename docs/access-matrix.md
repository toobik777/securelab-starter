# Матриця доступу ЛР 4 (baseline 2-A, рівень «Відмінно»)

Суб'єкт береться лише з verified principal (`ClaimTypes.NameIdentifier`, `ClaimTypes.Role`), які сервер формує під час login.
Рішення приймається для комбінації **суб'єкт + дія + ресурс + контекст**; роль сама по собі не є дозволом.

## Політика відмов (обґрунтована)

| Стан запиту | Відповідь | Обґрунтування |
|---|---|---|
| Немає verified principal | `401` | authentication challenge без HTML redirect (профіль cookie ЛР 3) |
| Ресурс не існує **або** principal не має права його **читати** | `404`, один helper `IncidentNotFound()` | існування чужого інциденту не розкривається; відповіді збігаються за status, `Content-Type` і тілом (крім `traceId`) |
| Principal може читати ресурс, але не може виконати дію | `403` | ресурс уже видимий (owner, Analyst), тож явна відмова корисна клієнту й не розкриває нового |
| Дозволений перехід, але поточний status не дозволяє | `409` | рішення про право ухвалено; конфлікт стану |

Правило однакове для details, update, delete, triage і child (`/api/incident-comments/{id}`).

## Матриця

| Дія | Рівень | Анонім | Reporter | Analyst | Administrator | Власник має значення? | Стан реалізації |
|---|---|---|---|---|---|---|---|
| Перегляд списку `GET /api/incidents` | добрий | deny, 401 | own: лише власні інциденти | all: усі (для аналізу) | own: лише власні (окремих прав немає) | Так для Reporter/Administrator | `IncidentQueries.GetListAsync` (query scoping), тест A-05 |
| Пошук `GET /api/incidents/search` | відмінний (A-11) | deny, 401 | own: лише власні (до сортування й `Take(50)`) | all | own | Так для Reporter/Administrator | `Lab02Endpoints` + scoping у запиті, тест A-11 |
| Зведення `GET /api/incidents/severity-summary` | відмінний (A-11) | deny, 401 | own: лічильники лише за власними (до групування) | all | own | Так для Reporter/Administrator | `IncidentQueries.GetSeveritySummaryAsync`, тест A-11 |
| Перегляд деталей `GET /api/incidents/{id}` | достатній (entry-контракт ЛР 5) | deny, 401 | owner — allow; non-owner Reporter — deny, `404` | allow для аналізу | non-owner — deny, `404` (прав «на все» немає) | Так | `IncidentOperations.Read` у handler, тести A-01, A-02, A-03, A-03R, A-10 |
| Створення інциденту | успадкований baseline ЛР 3 | deny, 401 | allow for self; owner = principal | allow for self | allow for self | Сервер призначає власника | `Lab02Endpoints` (owner з claim) |
| Update content `PUT /api/incidents/{id}` | добрий | deny, 401 | owner — allow (лише `title`, `description`, `severity`); non-owner — deny, `404` | deny, `403` (читає, але content не змінює) | non-owner — deny, `404` | Так | `IncidentOperations.Update`, тести A-02-update, A-04, A-08a |
| Delete `DELETE /api/incidents/{id}` | добрий | deny, 401 | owner — allow **лише для status New**; owner не-New — deny, `403`; non-owner — deny, `404` | deny, `403` | non-owner — deny, `404` | Так | `IncidentOperations.Delete`, тест A-04 |
| Зміна status / triage `POST /api/incidents/{id}/triage` | добрий | deny, 401 | deny: owner — `403`, не owner — `404` (+ audit) | allow **лише New → Triaged**; інший поточний status — `409` | deny: `404` (+ audit) | Ні для дозволу; видимість ресурсу перевіряється окремо | `IncidentOperations.Triage`, тести A-06, A-07, A-09 |
| Перегляд internal comment (details та `GET /api/incident-comments/{id}`) | добрий | deny, 401 | deny: internal не потрапляє у відповідь; direct id → `404` | allow після авторизації parent incident | deny, `404` | Ні для internal rule; доступ до parent перевіряється окремо | `IncidentOperations.ReadInternalComments`, тест A-12 |
| Export / audit-read | відмінний, якщо flow існує | — | — | — | — | — | не застосовується: такого flow у проєкті немає |

Batch endpoint у проєкті відсутній (не застосовується). Concurrent transitions: викладач відтворюваного сценарію не надавав (не застосовується).

## Умови словами

- **owner**: `Incident.OwnerUserId` дорівнює Guid із claim `NameIdentifier`.
- **Analyst**: роль `Analyst` у server-issued role claim; читає всі інциденти, виконує лише `New → Triaged`, не редагує й не видаляє чужий content.
- **Administrator**: за відсутності явного права в картці baseline 2-A усі додаткові дії — deny. Роль не дає автоматичного обходу ownership.
- **Delete (conditional для owner)**: лише поки `Status == New`.
- **Triage (conditional для Analyst)**: лише коли поточний `Status == New`; наступний status (`Triaged`) призначає сервер, клієнт не передає status.
- Поля `OwnerUserId`, `Status`, `Role` ніколи не приймаються з request DTO звичайного update.

## Child resource

`IncidentComment` доступний лише через рішення для parent `Incident`: прямий id коментаря чужого incident дає `404`, internal comment для Reporter також `404`.

## Залишковий ризик

CSRF для cookie-автентифікованих mutating endpoint-ів не закрито до ЛР 6 (SameSite=Lax — лише часткове пом'якшення, документований навчальний борг ЛР 3).
Cookie містить роль, видану під час login: після зміни ролі в БД потрібен повторний login.
