# Команди і приклади WEB ЛР5

Рівні накопичувальні; точний маршрут — на початку практичної частини. Приклади 12–13, 21, 25–26 і 31–32: «Добре / Відмінно» (CSP: кінцевий enforcement; обидва режими — «Відмінно»). Приклади 14 і 33: «Відмінно». Інші номери — теорія або спільні кроки; відкривайте їх за маршрутом, не запускайте все поспіль.

Застосовуйте лише у відповідному кроці методички. Bash і PowerShell — альтернативи. Замініть PROJECT_DIRECTORY/BUNDLE_CHECK_DIRECTORY власним абсолютним шляхом у лапках, VULNERABLE_COMMIT/FIXED_COMMIT — власними hashes. C# — фрагменти наявних файлів, JS — Console зазначеної локальної сторінки; це не готовий safe rendering.


## Приклад 1: Promise і await (теорія)


```js
function later(value, ms) {
  return new Promise((resolve) => setTimeout(() => resolve(value), ms));
}

async function main() {
  console.log("1: before");
  const promise = later("done", 100);
  console.log("2:", promise instanceof Promise);
  const value = await promise;
  console.log("3:", value);
}

main();
console.log("4: main returned");
```


## Приклад 2: Проба /health у Console (теорія)


```javascript
async function inspectHealth() {
    try {
        const response = await fetch("/health", { cache: "no-store" });
        const body = await response.text();
        console.log("status", response.status);
        console.log("ok", response.ok);
        console.log("body", body);
        if (!response.ok) {
            console.log("HTTP response received, but it is not successful.");
        }
    } catch (error) {
        console.log("Response unavailable; inspect Network and Console.");
    }
}
inspectHealth();
```


## Приклад 3: Проба в Console: той самий рядок як HTML і як текст


```js
{
  const value = `<img src=x onerror="this.parentNode.dataset.demo='ran'">`;
  const unsafeBox = document.createElement("div");
  const safeBox = document.createElement("div");
  document.body.append(unsafeBox, safeBox);

  unsafeBox.innerHTML = value;    // рядок розбирає HTML parser
  safeBox.textContent = value;    // рядок стає одним текстовим вузлом

  setTimeout(() => {
    console.log("innerHTML:", unsafeBox.dataset.demo, unsafeBox.children.length);
    console.log("textContent:", safeBox.dataset.demo, safeBox.children.length);
    unsafeBox.remove();
    safeBox.remove();
  }, 1000);
}
```


## Приклад 4: 1. Підготуйте відтворюваний локальний стан


```bash
git status --short
git switch -c lab/5-browser-xss v0.4.0
```


## Приклад 5: 1. Підготуйте відтворюваний локальний стан


```bash
python3 install.py 05 "PROJECT_DIRECTORY"
```


## Приклад 6: 1. Підготуйте відтворюваний локальний стан


```powershell
py -3 install.py 05 "PROJECT_DIRECTORY"
```


## Приклад 7: 1. Підготуйте відтворюваний локальний стан


```bash
git status --short
git add .scaffolds/lab-05.json demonstrations/cors-probe
git add src/SecureLab.Api/Client/browser-lab.html
git add src/SecureLab.Api/Client/browser-lab.js
git diff --staged --stat
git commit -m "lab5: install scaffold lab-05-start-v1 (vulnerable state)"
git rev-parse --short HEAD
```


## Приклад 8: 1. Підготуйте відтворюваний локальний стан


```bash
docker compose --env-file infra/.env.example -f infra/compose.yaml up -d --wait
dotnet run --project src/SecureLab.Api -- --reset-database
dotnet test tests/SecureLab.Api.Tests/SecureLab.Api.Tests.csproj --configuration Release
dotnet run --project src/SecureLab.Api
```


## Приклад 9: Команда перевірки marker у Console (Етап 2, крок 2)


```js
document.querySelector("#lab05-marker-target").getAttribute("data-lab05-marker")
```


## Приклад 10: 4. Замініть dangerous sink safe DOM API


```js
function renderLabels(container, items) {
  const list = document.createElement("ul");

  for (const item of items) {
    const listItem = document.createElement("li");
    listItem.textContent = item.label;
    list.append(listItem);
  }

  container.replaceChildren(list);
}
```


## Приклад 11: 4. Замініть dangerous sink safe DOM API


```js
const demo = document.createElement("div");
renderLabels(demo, [{ label: "<b>демо</b>" }]);
console.log(demo.querySelector("li").textContent);
console.log(demo.querySelector("b"));
renderLabels(demo, []);
console.log(demo.querySelectorAll("li").length);
```


## Приклад 12: Seed коментаря константа marker


```csharp
const string Lab05Marker =
    "<img src=x onerror=\""
    + "document.querySelector('#lab05-marker-target')"
    + ".setAttribute('data-lab05-marker','executed')\">";
```


## Приклад 13: Seed коментаря initializer AddRange


```csharp
new IncidentComment
{
    Id = Guid.Parse("30000000-0000-0000-0000-000000000005"),
    Incident = aliceIncident,
    Author = morgan,
    Text = Lab05Marker,
    IsInternal = false,
    CreatedAtUtc = new DateTimeOffset(
        2026, 8, 1, 9, 35, 0, TimeSpan.Zero)
}
```


## Приклад 14: Контекст URL — safeHttpUrl (Етап 2, крок 4; «Відмінно»)


```js
function safeHttpUrl(value, base) {
  try {
    const url = new URL(value, base);
    return url.protocol === "https:" || url.protocol === "http:" ? url.href : null;
  } catch {
    return null;
  }
}

const base = "http://localhost:5080";
console.log(safeHttpUrl("https://example.com/a", base));
console.log(safeHttpUrl("/incidents/1", base));
console.log(safeHttpUrl("javascript:alert(1)", base));
console.log(safeHttpUrl(" JaVaScRiPt:alert(1)", base));
console.log(safeHttpUrl("http://[bad", base));
```


## Приклад 15: 2. Підніміть реальну другу origin


```bash
dotnet run --project src/SecureLab.Api
```


## Приклад 16: 2. Підніміть реальну другу origin


```bash
python3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe
```


## Приклад 17: 2. Підніміть реальну другу origin


```powershell
py -3 -m http.server 4173 --bind localhost --directory demonstrations/cors-probe
```


## Приклад 18: 2. Підніміть реальну другу origin


```bash
python3 -m http.server 4174 --bind localhost --directory demonstrations/cors-probe
```


## Приклад 19: 2. Підніміть реальну другу origin


```powershell
py -3 -m http.server 4174 --bind localhost --directory demonstrations/cors-probe
```


## Приклад 20: 2. Підніміть реальну другу origin


```js
const API_ORIGIN = "http://localhost:5080";

await fetch(`${API_ORIGIN}/health`)
  .then(async (response) => ({
    status: response.status,
    body: await response.text(),
  }));
```


## Приклад 21: 2. Підніміть реальну другу origin


```js
const API_ORIGIN = "http://localhost:5080";

await fetch(`${API_ORIGIN}/health`, {
  headers: { "X-Lab-Cors-Probe": "stage-3" },
}).then(async (response) => ({
  status: response.status,
  body: await response.text(),
}));
```


## Приклад 22: 3. Налаштуйте мінімальну навчальну CORS policy


```csharp
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Lab05BrowserProbe", policy =>
        {
            policy.WithOrigins("http://localhost:4173")
                  .WithMethods("GET")
                  .WithHeaders("X-Lab-Cors-Probe");
        });
    });
}
```


## Приклад 23: 3. Налаштуйте мінімальну навчальну CORS policy


```csharp
app.UseRouting();
if (app.Environment.IsDevelopment())
{
    app.UseCors("Lab05BrowserProbe");
}
app.UseAuthentication();
app.UseAuthorization();
app.UseDefaultFiles();
app.UseStaticFiles();
// Далі — наявні Map...
```


## Приклад 24: 3. Налаштуйте мінімальну навчальну CORS policy


```js
await fetch("http://localhost:5080/api/incidents")
  .then(response => response.status);
```


## Приклад 25: 5. Перевірте credentials і межі policy


```js
await fetch("http://localhost:5080/api/incidents", {
  credentials: "include",
  headers: { Accept: "application/json" },
});
```


## Приклад 26: 5. Перевірте credentials і межі policy


```csharp
policy.WithOrigins("http://localhost:4173")
      .WithMethods("GET")
      .WithHeaders("X-Lab-Cors-Probe")
      .AllowCredentials();
```


## Приклад 27: 6. Порівняйте browser і direct HTTP


```bash
curl -i http://localhost:5080/health
```


## Приклад 28: 6. Порівняйте browser і direct HTTP


```powershell
curl.exe -i http://localhost:5080/health
```


## Приклад 29: 6. Порівняйте browser і direct HTTP


```bash
curl -i -H "Origin: http://localhost:4174" \
  "http://localhost:5080/health"
```


## Приклад 30: 6. Порівняйте browser і direct HTTP


```powershell
curl.exe -i -H "Origin: http://localhost:4174" `
  "http://localhost:5080/health"
```


## Приклад 31: 1. Складіть мінімальну CSP із фактичних ресурсів


```csharp
var csp = "default-src 'self'; frame-ancestors 'none'";

app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";

    var isDevelopmentApiReference =
        app.Environment.IsDevelopment()
        && context.Request.Path.StartsWithSegments("/scalar");

    if (!isDevelopmentApiReference)
    {
        context.Response.Headers[
            "Content-Security-Policy-Report-Only"] = csp;
    }

    await next(context);
});
```


## Приклад 32: 1. Складіть мінімальну CSP із фактичних ресурсів


```js
new Image().src = "http://localhost:4173/x.png";
```


## Приклад 33: Відтворюваний тест реального DOM без додаткового framework


```js
await (async () => {
  const check = (condition, message) => {
    if (!condition) throw new Error(message);
  };
  const id = document.querySelector("#incident-id").value.trim();
  check(id.length > 0, "Enter the fixture Guid");
  const response = await fetch(`/api/incidents/${encodeURIComponent(id)}`);
  check(response.status === 200, `Unexpected HTTP ${response.status}`);
  const data = await response.json();
  check(data.description.includes("data-lab05-marker"), "Wrong fixture");
  check(document.querySelector("#lab05-marker-target")
    .getAttribute("data-lab05-marker") === null, "Reload before test");
  await load();
  const box = document.querySelector("#description");
  check(box.textContent === data.description, "Text changed");
  check(box.childElementCount === 0, "Data became HTML elements");
  await new Promise(resolve => setTimeout(resolve, 500));
  check(document.querySelector("#lab05-marker-target")
    .getAttribute("data-lab05-marker") === null, "Marker executed");
  console.log("PASS: persisted description rendered as plain text");
})();
```


## Приклад 34: REG-CORS-01 — HTTP evidence (Bash)


```bash
API_URL="http://localhost:5080/health"
ALLOWED_ORIGIN="http://localhost:4173"
DISALLOWED_ORIGIN="http://localhost:4174"

curl -sS -D - -o /dev/null \
  -H "Origin: ${ALLOWED_ORIGIN}" "${API_URL}"
curl -sS -D - -o /dev/null \
  -H "Origin: ${DISALLOWED_ORIGIN}" "${API_URL}"
```


## Приклад 35: REG-CORS-01 — HTTP evidence (PowerShell)


```powershell
curl.exe -sS -D - -o NUL `
  -H "Origin: http://localhost:4173" "http://localhost:5080/health"
curl.exe -sS -D - -o NUL `
  -H "Origin: http://localhost:4174" "http://localhost:5080/health"
```


## Приклад 36: 5. Збережіть канонічний стан у Git


```bash
git status
git log --oneline --decorate -n 10
git diff VULNERABLE_COMMIT..FIXED_COMMIT
git tag -a v0.5.0 -m "Lab 5 browser security verified"
git show v0.5.0 --stat
```


## Приклад 37: 5. Збережіть канонічний стан у Git


```bash
git switch main
git merge --ff-only lab/5-browser-xss
git show --no-patch v0.5.0
```


## Приклад 38: 5. Збережіть канонічний стан у Git


```bash
git push -u origin lab/5-browser-xss
git push origin v0.5.0
git push origin main
```


## Приклад 39: 5. Збережіть канонічний стан у Git


```bash
git bundle create lab05.bundle main lab/5-browser-xss v0.5.0
git bundle verify lab05.bundle
git bundle list-heads lab05.bundle
```


## Приклад 40: 5. Збережіть канонічний стан у Git


```bash
git clone lab05.bundle "BUNDLE_CHECK_DIRECTORY"
git -C "BUNDLE_CHECK_DIRECTORY" show --no-patch v0.5.0
```


## Політика SDK Етап 1 крок 1.2

Готовий файл: resources/copyable/sdk-policy.json. Це об’єкт для звірки наявного `global.json`; інші ключі збережіть. Scaffold його не встановлює.

```json
{
  "sdk": {
    "version": "10.0.100",
    "rollForward": "latestFeature",
    "allowPrerelease": false
  }
}
```

Готовий файл: resources/copyable/sdk-policy-commit.sh. Виконуйте лише після фактичної зміни старої політики SDK, перегляду `git diff -- global.json` та перевірок `dotnet --list-sdks` поза проєктом і `dotnet --version` у корені. Commit не пересуває попередній тег.

```bash
git add global.json
git commit -m "Update .NET 10 SDK selection policy"
git status --short
```
