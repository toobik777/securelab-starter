# F-03. API-відповіді без Cache-Control; невідомий /api/* повертає SPA-сторінку

**Статус:** Fixed
**Актив:** персональні дані в API-відповідях; контракт помилок API
**Компонент:** `Program.cs` (middleware заголовків, `MapFallbackToFile`)
**Пріоритет:** P3
*(Знайдено власним аналізом класів відповідей; не є готовою перевіркою методики.)*

## Передумови і межі огляду
Commit `92f94f5`/`e269e1a`, Development, alice після login; GET `/api/me`, GET details, GET `/api/nonexistent-route`. Поза межами: поведінка проміжних кешів.

## Відтворення
1. `GET /api/me`, `GET /api/incidents/{id}` → 200 JSON; заголовок `Cache-Control` відсутній.
2. `GET /api/nonexistent-route` → **200 `text/html`** (сторінка клієнта через `MapFallbackToFile("index.html")`) замість 404.

## Вплив і ймовірність
**Статус перевірки:** оцінено за кодом і спостереженням заголовків.
Дані автентифікованого користувача без політики кешування можуть потрапити в кеш браузера/проксі; API-клієнт, що звернувся до неіснуючого маршруту, отримує «успішний» HTML, а помилка маскується. Impact Low, likelihood Medium → P3.

## Першопричина
Відсутня політика `Cache-Control` для `/api/*`; fallback на `index.html` реєструвався для всіх шляхів без розширення, включно з `/api/…`.

## Виправлення
`Cache-Control: no-store` для шляхів `/api` у `OnStarting`; `MapFallback("/api/{**path}")` повертає 404 Problem Details і стоїть перед `MapFallbackToFile`. Fixed commit `e13ede9` (`Program.cs`).

## Retest
`GET /api/me` → `Cache-Control: no-store`; `GET /api/nonexistent-route` → 404 `application/problem+json` із заголовками безпеки (`Headers_OnStatic_Success_Validation_NotFound_AndUnknownApiPath`).

## Positive regression
Статична сторінка `/` і клієнтські маршрути без розширення віддаються як і раніше; `/api` відповіді без змін за змістом.

## Залишковий ризик
`no-store` не застосовано до публічних статичних файлів (навмисно). Невідомі не-API шляхи й далі віддають SPA-сторінку.

## Доказ
Тест вище; `py -3 scripts/lr06/lab06_probe.py headers` (до: `cache-control: (відсутній)`, 200 text/html; після: `no-store`, 404).
