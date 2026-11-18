# F-06. Недопустиме sortBy повертає 500 замість 400

**Статус:** Fixed
**Актив:** контракт помилок API, придатність моніторингу
**Компонент:** `Scaffolding/Lab02Endpoints.cs`, `GET /api/incidents/search`
**Пріоритет:** P3
*(Знайдено повторною перевіркою контрольованих даних ЛР 2 у regression pack; не є готовою перевіркою методики.)*

## Передумови і межі огляду
Commit `ebbc7e2`/`92f94f5`, Development, alice після login; `GET /api/incidents/search?q=a&sortBy=title;DROP TABLE incidents`.

## Відтворення
1. Login alice.
2. Запит із недопустимим `sortBy`.
3. Очікувано: 400 (allowlist ЛР 2). Фактично: **500** (`Expected: BadRequest, Actual: InternalServerError` у `RegSqli01_*` до fix).

## Вплив і ймовірність
**Статус перевірки:** спостережено локально.
Захист від SQL injection (allowlist) спрацьовував, але очікувана помилка валідації перетворювалась на необроблений виняток: клієнт бачить помилку сервера, моніторинг отримує хибні 5xx, у журналі з'являється stack trace від валідного input. Impact Low, likelihood High (будь-який автентифікований користувач) → P3.

## Першопричина
Handler кидав `BadHttpRequestException` у коді endpoint (після прив'язки параметрів); `UseExceptionHandler()` перехоплює такий виняток і повертає 500, а не код із винятку.

## Виправлення
Перевірка `sortBy` до побудови запиту; для недопустимого значення — `Results.ValidationProblem` (400, поле `sortBy`). Fixed commit `d678320`.

## Retest
Той самий запит → 400 `application/problem+json` із `errors.sortBy` (`RegSqli01_SearchInjectionPayloads_…`).

## Positive regression
Допустимі `sortBy` (`createdAtUtc`, `severity`, `status`) і пошук за `q` працюють; payload `' OR '1'='1`, `%`, `_` не змінюють структуру SQL.

## Залишковий ризик
Інші вирази, що кидають винятки в коді endpoint, не досліджувалися систематично.

## Доказ
Тест `RegSqli01_*`, команда `dotnet test --filter RegressionPackTests`.
