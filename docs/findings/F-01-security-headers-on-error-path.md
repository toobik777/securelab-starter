# F-01. На error path (500) відсутні заголовки безпеки

**Статус:** Fixed
**Актив:** браузерні захисти відповіді (CSP, nosniff, referrer), сеансова cookie
**Компонент:** `Program.cs`, middleware заголовків і `UseExceptionHandler()`
**Пріоритет:** P2

## Передумови і межі огляду
Review-start `92f94f5` (v0.5.0); production-like запуск без launch profile (`ASPNETCORE_ENVIRONMENT=Production`, `ConnectionStrings__SecureLab` на недосяжний порт 54399); публічний `POST /api/auth/login` з вигаданим паролем. Поза межами: TLS, інші 5xx-сценарії.

## Відтворення
1. Production-запуск із недосяжною БД (етап 1, п. 5); консоль: `Hosting environment: Production`.
2. `POST /api/auth/login` `{"userName":"alice","password":"NotARealPassword1!"}`.
3. Очікувано: 500 Problem Details **із** заголовками безпеки. Фактично: 500 `application/problem+json`, `traceId` є, але `x-content-type-options`, `referrer-policy`, `content-security-policy` — відсутні (лише `cache-control: no-cache,no-store`).

## Вплив і ймовірність
**Статус перевірки:** спостережено локально.
На відповіді помилки браузерні захисти не діяли: MIME sniffing, витік referrer, відсутність CSP. Помилкова відповідь не містить HTML/скриптів, тому прямої експлуатації не виявлено; але захист зникає саме під час збою. Ймовірність Medium (достатньо будь-якого необробленого винятку), impact Medium → P2.

## Першопричина
Заголовки виставлялись у `app.Use(...)` ПІСЛЯ `UseExceptionHandler()`. `ExceptionHandlerMiddleware` очищає заголовки відповіді перед записом 500, тому значення, виставлені всередині конвеєра, губилися.

## Виправлення
Заголовки перенесено в `Response.OnStarting` у найзовнішньому middleware до `UseExceptionHandler()`; callback спрацьовує під час фактичного початку відповіді, після очищення. Fixed commit `e13ede9` (`Program.cs`). Паралельного catch-all middleware не додано.

## Retest
Той самий сценарій на fixed commit: 500 `application/problem+json`, `x-content-type-options: nosniff`, `referrer-policy: no-referrer`, CSP (frame-ancestors 'none'), `x-frame-options: DENY`, `cache-control: no-store`; тіло без stack trace; `traceId` дорівнює `TraceId` у записі `ExceptionHandlerMiddleware`. Автоматично: `Lab06SecurityTests.Production_UnexpectedError_IsSafeProblemDetails_WithSecurityHeadersAndTraceId`.

## Positive regression
Заголовки на 200/400/404 і статичній сторінці збережено (`Headers_OnStatic_Success_Validation_NotFound_AndUnknownApiPath`); клієнт працює із CSP; очікувані 4xx не перетворено на 500.

## Залишковий ризик
Відповідь HostFiltering (400 для невідомого Host) формується до цього middleware й заголовків не має. Інші транспортні питання (TLS/HSTS) не тестувалися.

## Доказ
Тест `Production_UnexpectedError_*`; `py -3 scripts/lr06/lab06_probe.py crash` (до/після); без cookie, token і password.
