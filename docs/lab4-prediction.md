# Лаба 4: матриця Read і прогноз (до запуску)

## Матриця 4×2 (A = дозволено)

| Користувач | P_A (власник Alice) | P_B (власник Bob) | Обґрунтування |
|---|---|---|---|
| Alice (Reporter) | A | deny | власник P_A |
| Bob (Reporter) | deny | A | власник P_B |
| Morgan (Analyst) | A | A | роль Analyst |
| Admin | deny | deny | немає окремого права, не власник і не Analyst |

**Allowed-id sets:** Alice = {P_A}; Bob = {P_B}; Morgan = {P_A, P_B}; Admin = ∅.

## Прогноз до запуску

| Користувач | Handler (bool) | Owner-only query (HTTP) |
|---|---|---|
| Alice | P_A true, P_B false | P_A 200, P_B 404 |
| Bob | P_A false, P_B true | P_A 404, P_B 200 |
| Morgan | P_A true, P_B true | 404 і 404 (не власник, роль у query не враховано) |
| Admin | false, false | 404, 404 |