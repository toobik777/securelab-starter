# Готові ресурси WEB ЛР 5

Тут є повні копії всіх C#/JS/Bash/PowerShell blocks основних розділів, а не лише уривки. Покажчик [commands.md](commands.md) вказує крок, готовий файл і код; застосовуйте очікування/діагностику цього кроку. `.sh` файли можна читати як текст або запускати явно Bash тільки після заміни шляхів і перевірки передумов; не запускайте всі файли поспіль. Console JS вставляйте цілком у вказаній локальній сторінці; C# — фрагменти в наявний метод/Program, не окремі програми.

comment-seed.cs містить const та initializer для AddRange; csp-headers.cs показує зміну наявної headers middleware. description-dom.js запускається Console на browser-lab після входу/Guid/reload; очікування PASS лише після власного safe fix, FAIL на unsafe. safe-url.js — нейтральна проба протоколу й href; файл виводить сім рядків: перші 2 URL дозволені, 3 відхилені; далі `href` створеного посилання і `null` — атрибута `onclick` немає.

[Заповнена карта Title](../examples/title-source-to-sink.md) і [каркас карти](../templates/source-to-sink.md) допомагають створити свій DEL-01. [Каркас доказів](../templates/browser-security-evidence.md) скопіюйте в docs/browser-security-evidence.md, [каркас звіту](../templates/report.md) використайте для DEL-03. Маркер копіюйте тільки з resources/lab05-marker.txt. Готового graded safe-rendering рішення тут немає.

Рівні накопичувальні: критерії й точний обсяг — на початку практичної частини. comment-seed.cs — «Добре / Відмінно»; csp-headers.cs — зразок для «Добре / Відмінно» (кінцевий enforcement), докази обох режимів — «Відмінно»; description-dom.js і safe-url.js — «Відмінно». Загальні команди й регресії потрібні всім.

START-HERE.md у ZIP пояснює вхід v0.4.0, що інсталюється, і чому resources не треба копіювати у Client.
