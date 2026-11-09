# Заповнений зразок карти назви

Потік: seed-інцидент Alice → GET деталей → заголовок штатного клієнта `/`. Це безпечний паралельний приклад структури DEL-01, а не розв’язання Description на `/browser-lab.html`. У власному cumulative коді сигнатура GetDetailsAsync може містити principal/роль із ЛР 4; зіставте її з фактичним файлом.

Передумови: API на 5080, локальна БД, вхід Alice через `/auth-lab.html`; інцидент `20000000-0000-0000-0000-000000000001`. На `/` виберіть його в списку; у Network відкрийте GET з цим id. Очікування: 200, JSON, поле title має `Підозрілий лист із вкладенням`; у Elements заголовок h3 містить один текстовий вузол. Якщо 401 — повторіть вхід у тому самому вікні; 404 — перевірте Guid/seed/доступ.

| Крок | Точне місце | Значення / доказ | Довіра й перетворення |
|---|---|---|---|
| Source | Data/DbSeeder.cs, SeedAsync, aliceIncident.Title | Підозрілий лист із вкладенням | у прикладі synthetic seed; у продукті назву вводить користувач |
| Request | GET /api/incidents/20000000-0000-0000-0000-000000000001 | body відсутнє; Guid лише обирає запис | це читання seed, нового title у request немає |
| Storage | Incident.Title у PostgreSQL | те саме значення | збереження не робить майбутні назви довіреним HTML |
| Server | Application/Incidents/IncidentQueries.cs, GetDetailsAsync | Incident.Title → IncidentDetailsResponse.Title | projection без HTML-очищення; авторизація перевіряє право, не DOM |
| JSON | Presentation/Contracts/IncidentResponses.cs, IncidentDetailsResponse; response JSON title | те саме Unicode-значення | JSON escape не є захистом HTML sink |
| Client | Client/app.js, loadIncidentDetails → renderIncidentDetails | incident.title → createTextElement("h3", incident.title) | дані не конкатенуються у markup |
| Sink | app.js, createTextElement | element.textContent = text | контекст текстового вузла: h3 створює код, назву додає textContent |

У рядку інвентаризації зазначте: app.js → createTextElement → Incident.Title із response → текстовий вузол → безпечно, бо title не передається HTML parser. Висновок стосується цього sink; він не доводить безпечності інших клієнтів/полів. Для своєї карти додайте конкретні form/request DTO/storage/response/function/DOM element із фактичного потоку Description, його небезпечну інтерпретацію й після цього власне виправлення.
