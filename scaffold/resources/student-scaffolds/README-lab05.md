# Стартовий пакет лабораторної роботи 05

Ви продовжуєте власний завершений `v0.4.0`. У цьому комплекті лише `lab-05-scaffold.zip`; у його manifest немає інших номерів робіт. Release ID — `lab-05-start-v1`. Пакет додає browser-lab.html, browser-lab.js і probes для двох origin. Він не містить готового safe-rendering fix або налаштованої CORS policy; наявна авторизація ЛР 4 має працювати.

Розпакуйте scaffold **поза** Git-каталогом проєкту. Перейдіть до каталогу з `install.py`; PROJECT_DIRECTORY замініть абсолютним шляхом, зберігаючи подвійні лапки; останній аргумент є шляхом до власного проєкту з `global.json`.

```bash
python3 install.py 05 "PROJECT_DIRECTORY"
python3 install.py 05 "PROJECT_DIRECTORY" --write
```

У Windows використовуйте `py -3` замість `python3`; шлях із пробілами беріть у подвійні лапки. Перший запуск лише показує план. Після запису перегляньте `git diff` і квитанцію `.scaffolds/lab-05.json`; збережіть потрібні файли окремим commit. Перевірки build, reset і test виконайте за стартовим етапом практичної частини цієї методички.

Installer перевіряє чисте робоче дерево, походження від `v0.4.0`, SHA-256 виданих файлів, integration anchors і конфлікти до початку запису. Лише різниця CRLF/LF не є зміною змісту. Повторне встановлення поверх власних виправлень заборонене. Якщо змінено зміст baseline-файла, збережіть diff і порівняйте саме цей файл за діагностикою методички; не використовуйте `reset --hard` і не перезаписуйте весь проєкт.

Розширений довідник `README.md`, `manifest.json` і `install.py` містяться всередині ZIP. Пакет дає інфраструктуру навчального локального стенда; індивідуальні докази, виправлення й регресію виконуєте ви.

## Канонічний harmless marker ЛР 05

Копіюйте лише з однорядкового `resources/lab05-marker.txt` у виданому комплекті або розпакованому ZIP. Нижче — вигляд для читання: переноси у PDF не є частиною marker і цей блок не використовуйте для копіювання. Він лише встановлює атрибут на власній локальній сторінці.

```html
<img src=x onerror="document.querySelector('#lab05-marker-target').setAttribute('data-lab05-marker','executed')">
```

Очікувана дія у вразливому render: `#lab05-marker-target` має атрибут `data-lab05-marker="executed"`. Перед повтором оновіть сторінку, щоб прибрати попередній атрибут. Перевірку виконуйте лише на власному loopback-стенді зі штучними даними.


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
