# Картка варіанта 2-A

## Предметна область

Навчальний трекер кібербезпекових інцидентів. Reporter створює повідомлення,
Analyst класифікує його та змінює стан, Administrator виконує лише окремо
визначені адміністративні операції.

## Дані

- основний owned resource — `Incident`;
- пов'язані сутності — `IncidentComment`, `IncidentStatusHistory`;
- власник — `Incident.OwnerUserId`;
- текстові поля — `Title`, `Description`, `IncidentComment.Text`;
- workflow — `New → Triaged → InProgress → Resolved → Closed`;
- навчальні користувачі — Alice, Bob, Morgan (Analyst), Admin;
- відтворювані UUID Alice/Bob та їхніх інцидентів визначені у `DbSeeder`.

## Профіль A «Пошук»

| ЛР | Точка розвитку |
|---:|---|
| 1 | Підсумок за severity; готовий фільтр за status є близьким прикладом |
| 2 | Пошук у title/description, параметризація значень і allowlist сортування |
| 3 | Сервер призначає власника інциденту з principal |
| 4 | Горизонтальний IDOR читання/зміни чужого інциденту; вертикальна зміна severity/status |
| 5 | Stored XSS у description/comment; безпечний list/details render |
| 6 | Rate limit створення/пошуку, аудит залежностей і мінімізація журналів |

Навмисно вразливі стани ЛР 2 та ЛР 5 постачають відповідні scaffold ZIP поверх власного завершеного попереднього тегу. ЛР 4 продовжує власний v0.3.0; контрольовану перевірку виконуйте за її методичкою. Не створюйте нові вразливості для ЛР 5: browser-lab.js уже має два навчальні sinks. Власні попередні виправлення зберігаються.

Канонічний create JSON: title,description,severity,occurredAtUtc. Severity — рядок Low/Medium/High/Critical; OccurredAtUtc — nullable DateTimeOffset на вході, обов’язковість перевіряє validation, перед збереженням UTC. Id/OwnerUserId серверні.
