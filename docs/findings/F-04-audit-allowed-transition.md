# F-04. Audit trail привілейованого переходу не фіксує дозволену дію

**Статус:** Fixed
**Актив:** дані аудиту (A5), привілейований перехід `Incident.Status`
**Компонент:** `IncidentEndpoints.TriageAsync`, `AuthorizationAudit`
**Пріоритет:** P2

## Передумови і межі огляду
Commit `e13ede9`, Development, reset; користувачі alice (Reporter) і morgan (Analyst); сценарії журналу з `scripts/lr06/lab06_log_scenarios.py`; журнал у `../lr06-logs/api.log`.

## Відтворення
1. Alice створює інцидент; Alice → `POST /api/incidents/{id}/triage` → 403; журнал містить `authorization.denied …`.
2. Morgan → той самий `triage` → 200, status `Triaged`.
3. Очікувано: запис і про дозволену дію. Фактично: у журналі лише `Auth event: login succeeded`; запису про сам перехід немає.

## Вплив і ймовірність
**Статус перевірки:** спостережено локально.
Оператор не може відновити, хто і коли змінив status: audit trail мав лише відмови. Impact Medium (відсутність сліду значущої дії), likelihood Medium → P2.

## Першопричина
`AuthorizationAudit` мав подію лише для deny; після успішного `SaveChangesAsync` подію не записували.

## Виправлення
Додано структуровану подію `authorization.allowed` (subject, action, resource, result=allowed, from, to, UTC-час, traceId) після збереження переходу. Секрети, cookie, token і тіло запиту не журналюються. Fixed commit `ebbc7e2`.

## Retest
Сценарій 2 на fixed commit: рядок `authorization.allowed subject=<morgan> action=incident.triage resource=incident:<id> result=allowed from=New to=Triaged occurredAtUtc=… traceId=…`. Автоматично: `Audit_AllowedPrivilegedTransition_IsRecorded_WithoutSecrets`, `A09_DeniedTriage_…`.

## Positive regression
Дозволений перехід і історія статусів працюють (`A07_Triage_AsAnalyst_…`); відмова Reporter не змінює status.

## Залишковий ризик
Журналюється лише privileged action (читання не журналюється, щоб не створювати шум). Строки зберігання й доступ до журналу не визначено.

## Доказ
Тести вище; grep по журналу: 0 збігів для пароля, cookie, X-CSRF-TOKEN, Authorization.
