# F-05. AllowedHosts: "*" вимикає фільтрацію заголовка Host

**Статус:** Fixed
**Актив:** сеанс і сторінки застосунку
**Компонент:** `src/SecureLab.Api/appsettings.json`
**Пріоритет:** P3
*(Baseline-значення методика прямо називає «не прийнятним висновком для реального deployment без окремого рішення».)*

## Передумови і межі огляду
Commit `ebbc7e2^`, Development і production-like, `GET /health` з довільним заголовком `Host`.

## Відтворення
`curl.exe -H "Host: evil.example" http://localhost:5080/health` → 200 (до fix).

## Вплив і ймовірність
**Статус перевірки:** спостережено локально.
Застосунок обслуговує запити з довільним Host: можливі Host-header атаки (підміна посилань, cache poisoning) у реальному deployment. Для локального стенда наслідок обмежений: impact Low, likelihood Low → P3.

## Першопричина
`AllowedHosts: "*"` у tracked `appsettings.json`.

## Виправлення
`"AllowedHosts": "localhost;127.0.0.1"`. Fixed commit `ebbc7e2`.

## Retest
`Host: evil.example` → 400; `Host: localhost:5080` → 200 (`HostFiltering_UnknownHostHeader_IsRejected_ButLocalhostWorks`; Production-запуск: 400 `text/html`).

## Positive regression
Звичайний локальний сценарій (`localhost`, `127.0.0.1`) і всі тести працюють.

## Залишковий ризик
Перелік треба замінити на реальні імена хостів при deployment; відповідь 400 HostFiltering не має заголовків безпеки.

## Доказ
Тест вище; консольний вивід curl у `docs/security-review.md` (розділ 8).
