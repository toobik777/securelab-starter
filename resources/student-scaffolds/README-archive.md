# Студентські стартові стани SecureLab

Довідкова копія README з архіву `lab-05-scaffold.zip`, наданого поруч із PDF. Перед виконанням команд розпакуйте цей ZIP. Файли `manifest.json`, `install.py` та каталог `lab-05/` розташовані в корені розпакованого архіву, а не поряд із цією довідковою копією.

Цей README спільний для scaffold ЛР 02, 03 і 05; у цьому архіві є лише scaffold 05. Рядки 02 і 03 у таблиці нижче — довідка про попередні роботи, для ЛР 5 потрібен лише рядок 05. Версії й SHA-256 файлів задано в `manifest.json`, встановлення виконує `install.py`. Це додавання до вашого завершеного проєкту, а не готова наступна робота.

## Спільна процедура

1. Завершіть попередню ЛР, виконайте її регресію, збережіть власний тег із таблиці.
2. Створіть гілку наступної ЛР саме від цього тегу. Робоче дерево має бути чистим.
3. З каталогу отриманого scaffold виконайте dry run, потім встановлення (замініть PROJECT_DIRECTORY абсолютним шляхом, лапки збережіть):

```bash
python3 install.py 05 "PROJECT_DIRECTORY"
python3 install.py 05 "PROJECT_DIRECTORY" --write
```

Цей архів містить лише scaffold 05; інші номери в ньому недоступні. Installer не перемикає гілки,
не створює теги й не виконує reset. Він перевіряє походження від попереднього тегу,
checksums і всі конфлікти перед записом. Якщо власна зміна зачепила plumbing,
збережіть diff і порівняйте зазначений файл зі стартовим станом; порядок ручного узгодження наведено в методичці. Зміна лише CRLF/LF не є конфліктом. Власні зміни автоматично не перезаписуються. Installer
не застосовує diff/transition patches і не копіює завершені security fixes.

4. Перегляньте diff, запишіть release ID scaffold та власний base commit у звіті.
5. У корені власного проєкту виконайте:

```bash
dotnet tool restore
dotnet restore SecureLab.sln --locked-mode
dotnet build SecureLab.sln -c Release --no-restore
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet test SecureLab.sln -c Release
dotnet run --project src/SecureLab.Api
```

Остання команда займає термінал до Ctrl+C; залиште API працювати, а наступні команди виконуйте в іншому терміналі. API/client: `http://localhost:5080`. Перед міграцією ЛР 03 перевірте, що connection string вказує на власну локальну навчальну БД, і збережіть потрібні докази попередньої роботи. Продовження з тією самою навчальною БД дозволено; reset видаляє її дані. Для нового
Identity seed задайте `SeedUsers__Password` поза Git; пароль має відповідати
наданій Identity policy й використовуватися лише в цьому стенді. Він потрібний
також cookie mechanics test. Оновлені lock files для Identity постачаються; не змінюйте версії залежностей
заради встановлення. Reset дозволений лише для свідомо вибраної навчальної БД;
він не є обов’язковим способом установлення scaffold.

## Канонічні входи

| ЛР / release | Успадкований власний тег | Що додано | Що вже працює | Що виконує студент |
|---|---|---|---|---|
| 02 / `lab-02-start-v1` | `v0.1.0`, завершена ЛР 1 | search/create endpoints, DTO skeleton, додатковий seed, HTTP і test example | `GET /api/incidents/search?q=…`, `POST /api/incidents`, список/деталі й summary ЛР 1; навмисно уразливий SQL-пошук | відтворення SQLi, докази, root cause, параметризація, validation/business rules, sort allowlist, позитивна й security regression |
| 03 / `lab-03-start-v1` | `v0.2.0`, виправлена ЛР 2 зі встановленим scaffold 02 | Identity model/stores, migration/designer/snapshot, seed/reset, services/middleware, register/login skeleton, cookie helper, browser login, один auth test | register/login через framework, `/api/auth/session-check`; усі попередні student fixes зберігаються | завершення DTO boundary, `/api/me`, захист create, owner лише з principal, 401/403 semantics, security tests; logout від доброго рівня |
| 05 / `lab-05-start-v1` | `v0.4.0`, завершена ресурсна авторизація ЛР 4 | browser page/module і два origin probes з одного каталогу | читання/запис опису через успадковані protected API, навмисно небезпечне відображення stored text | source-to-sink карта, harmless PoC, причинне пояснення, safe rendering, inert marker regression, CORS experiment |

Старт ЛР 02 має щонайменше п’ять інцидентів Alice/Bob і легітимні апострофи.
Create поки використовує сталий Alice demo context: body не вибирає owner,
але verified principal ще не використовується. Це відкритий навчальний борг
ЛР 03. Пошуковий параметр канонічно називається `q`; студент додає `sortBy`.
API/EF scaffolding та початковий уразливий SQL не потрібно писати з нуля.

ЛР 03 постачає `ApplicationUser : IdentityUser<Guid>`, єдиний Identity store,
усі три domain foreign keys, збереження Guid, ролі Reporter/Analyst/Administrator
та Alice/Bob/Morgan/Admin. Міграція переносить існуючі user IDs і доменні записи;
повернення старої моделі потребує окремої БД попереднього етапу. Generated EF
файли, DI, порядок middleware й cookie test transport не є оцінюваною роботою.
`GET /api/me` без demo header і logout спочатку повертають `501`;
з `X-Demo-UserId` `/api/me` навмисно довіряє заявленому id навіть без cookie.
Це окремий досліджуваний дефект ЛР 03, який студент має усунути; create успадковує тимчасовий
контекст ЛР 02. Приклад auth test перевіряє лише mechanics на session-check,
а не A-01/A-02/A-05. Студент прибирає довіру до body/header identity, реалізує
verified-principal mapping, DTO separation і докази фактичного owner.

ЛР 05 відкривається на `/browser-lab.html`; авторизацію не обминають.
Обов’язкові успадковані API: `GET /api/incidents/{id}` і `POST /api/incidents`;
JSON create: `title`, `description`, `severity`, `occurredAtUtc`. На вході
Alice створює власний ресурс, Morgan/Analyst читає його, Bob отримує узгоджену
відмову. Інший власний контракт узгодьте до запуску — це інтеграція клієнта,
а не створення нового vulnerable API. Наявні auth форми з ЛР 03 зберігаються.
Probes: `python3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe`
і та сама команда з `4174` в іншому терміналі. Початково cross-origin policy
не надана: браузер блокує читання response. Студент налаштовує exact origin
`http://localhost:4173`, залишає `4174` контролем і перевіряє прості probes; credentials/preflight додаються для «Добре / Відмінно».
Усі згадки «наданий marker» або «той самий marker» у матеріалах ЛР 05 означають
точне значення з наступного розділу. Готового safe-rendering fix у пакеті немає.

## Канонічний harmless marker ЛР 05

Єдине джерело копіювання — однорядковий resources/lab05-marker.txt у цьому ZIP. Блок нижче лише ілюструє значення; з PDF його не копіюйте:

```html
<img src=x onerror="document.querySelector('#lab05-marker-target').setAttribute('data-lab05-marker','executed')">
```

Єдина очікувана дія під час уразливого render — елемент
`#lab05-marker-target`, уже наявний у `/browser-lab.html`, одержує атрибут
`data-lab05-marker="executed"`. Marker використовують лише на loopback-стенді
зі штучними даними. Заборонено змінювати його так, щоб він виконував мережевий
request, читав cookie, token, credential, browser storage чи інші секрети,
виконував exfiltration, змінював інші елементи або спричиняв руйнівну дію.

ЛР 04 та 06 продовжують власні `v0.3.0` і `v0.5.0` без окремого scaffold.
Усі ЛР 02–06 містять необхідні теоретичні пояснення у власних методичних рекомендаціях.

## Межа безпеки та перевірки

Scaffolds 02 і 05 містять навмисні навчальні уразливості. Використовуйте лише
loopback/дозволений ізольований стенд і штучні дані. Два додаткові Low seed-інциденти ЛР 02 змінюють очікувані counts агрегації
ЛР 1: для штатного seed Low=3, Medium=1, High=1. Оновіть лише ці похідні
очікування власного тесту після звірки seed; не послаблюйте assertions.
Початкові smoke tests
доводять доступність механізму; оцінювану security regression додає студент.
Виправлення має залишитися у власному cumulative project перед наступним тегом.
Повторне встановлення scaffold поверх виправлень заборонене installer-ом.


## SDK у власному накопичуваному проєкті

Установіть останню стабільну **.NET 10 SDK** з [офіційної сторінки Microsoft](https://dotnet.microsoft.com/download/dotnet/10.0), у блоці **Build apps — SDK**, для своєї ОС та архітектури. Runtime окремо не замінює SDK. Не обирайте Preview або RC. Поточний номер випуску змінюється, тому його тут не фіксуємо.

**Якщо ви вже почали з попереднім ZIP або тегом.** Збережіть свою роботу; відкрийте наявний `global.json` у корені власного проєкту й замініть лише об’єкт `sdk` наведеним нижче. Інші ключі, якщо вони є, збережіть. Так цей об’єкт виглядає в оновленому starter ЛР 1; `lab-05-scaffold.zip` файла `global.json` не містить і його не змінює:

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

`10.0.100` — нижня межа, а не вимога встановити саме цей старий випуск. `latestFeature` обирає найвищу встановлену стабільну SDK з major/minor `10.0`, зокрема зі смуг `1xx`, `3xx` або `4xx`; `allowPrerelease: false` виключає Preview/RC. SDK 9 чи 11 не замінює SDK 10 для цього проєкту. `net10.0`, версії NuGet, `packages.lock.json` і `.config/dotnet-tools.json` залишаються без змін. [Правила Microsoft для global.json](https://learn.microsoft.com/en-us/dotnet/core/tools/global-json) пояснюють вибір SDK окремо від target framework.

**Дві різні перевірки.** В окремому терміналі поза проєктом виконайте:

```bash
dotnet --list-sdks
```

Серед рядків має бути стабільна `10.0.… [шлях]` без `-preview` чи `-rc`. Це перелік SDK, доступних саме цьому виконуваному файлу `dotnet`, а не доказ вибору проєкту. Потім у корені проєкту з оновленим `global.json` виконайте:

```bash
dotnet --version
```

Очікуйте один номер `10.0.…` без суфікса попереднього випуску — найвищу сумісну встановлену SDK. Якщо бачите `A compatible .NET SDK was not found`, перевірте збережений файл, установлення SDK і шлях до `dotnet`. Рядок `global.json file:` у цьому повідомленні показує, який саме файл використав CLI. Якщо повідомлення ще містить `Requested SDK version: 10.0.302`, залишилася стара копія `global.json` або команда виконується не в потрібному каталозі. Перевірте поточний каталог і `global.json` у ньому та вище: CLI шукає цей файл також у батьківських каталогах. Не видаляйте його для обходу помилки.

Якщо виправили старий файл у Git-проєкті, перегляньте `git diff -- global.json`, переконайтеся, що diff містить лише політику SDK, і збережіть цю зміну окремим commit. Уже завершений тег попередньої ЛР не пересувайте. Перед installer робоче дерево має бути чисте:

```bash
git add global.json
git commit -m "Update .NET 10 SDK selection policy"
git status --short
```

Цей блок потрібен лише після фактичної зміни старого файла; для нового незміненого файла commit не потрібний. Після перерви знову перевірте `git status` і `dotnet --version` у тому самому корені; не розпаковуйте starter поверх власної роботи й не повторюйте installer заради SDK.

**Коли CLI працює, а IDE не бачить SDK.** Оновіть Visual Studio через Visual Studio Installer до стабільної версії, сумісної з обраною SDK, з workload **ASP.NET and web development**. Підтримка `net10.0` від 18.0 — мінімум для target; новіша смуга SDK може потребувати новішої IDE/MSBuild, тому звірте [таблицю Microsoft SDK і Visual Studio](https://learn.microsoft.com/en-us/dotnet/core/porting/versioning-sdk-msbuild-vs). Для Rider також установіть актуальну стабільну версію з підтримкою .NET 10. У Settings (на macOS — Rider → Settings) → **Build, Execution, Deployment → Toolset and Build** перевірте **.NET CLI executable path**. Це шлях до `dotnet`/`dotnet.exe`, а не до каталогу `sdk/10.0.…`; укажіть виконуваний файл тієї інсталяції, яка показує SDK 10 у `--list-sdks`. Знайти його можна командою `where.exe dotnet` у Windows або `command -v dotnet` у macOS/Linux. [Довідка JetBrains про Toolset and Build](https://www.jetbrains.com/help/rider/Settings_Toolset_and_Build.html) описує автоматичне визначення й ручний шлях.

Після встановлення SDK, зміни PATH або шляху Rider повністю закрийте й перезапустіть IDE та її термінал, відкрийте те саме `SecureLab.sln` і повторіть `dotnet --version` та build. Якщо термінал успішний, а Build IDE ні, збережіть її діагностику SDK/MSBuild: це різні механізми пошуку. До з’ясування використовуйте термінальний build/run із цієї методички. Для резервного VS Code оновіть C# Dev Kit і його залежності та також перезапустіть редактор.

Scaffold не постачає та не переписує `global.json` власного проєкту. Оновіть старий файл після створення гілки цієї ЛР, до пробного запуску installer; installer перевіряє чисте робоче дерево.
