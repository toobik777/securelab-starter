// REG-XSS-01 (автоматизований DOM-тест, рівень "Відмінно").
// Запуск: відкрийте http://localhost:5080/browser-lab.html у свіжій вкладці, увійдіть (auth-lab.html),
// вставте Guid запису з canonical marker у поле "Guid інциденту" і виконайте файл цілком у Console.
// Тест перевіряє ФАКТИЧНИЙ DOM, а не лише HTTP 200 чи відсутність marker (CSP не маскує sink).
// Побічний ефект: створює кілька нешкідливих тестових записів "dom-test" від імені поточного користувача.
await (async () => {
  const failures = [];
  const check = (condition, message) => { if (!condition) failures.push(message); };
  const marker = document.querySelector("#lab05-marker-target");
  const markerValue = () => marker.getAttribute("data-lab05-marker");
  const idField = document.querySelector("#incident-id");
  const originalId = idField.value.trim();
  const sleep = ms => new Promise(resolve => setTimeout(resolve, ms));
  const api = async id => {
    const response = await fetch(`/api/incidents/${encodeURIComponent(id)}`);
    return { status: response.status, body: response.ok ? await response.json() : null };
  };

  // 1. Fixture: запис, у якому description містить canonical marker.
  check(originalId.length > 0, "Enter the fixture Guid with marker description");
  const fixture = await api(originalId);
  check(fixture.status === 200, `Unexpected HTTP ${fixture.status} for fixture`);
  check(fixture.body && fixture.body.description.includes("data-lab05-marker"), "Wrong fixture (no marker)");
  check(markerValue() === null, "Reload the page before the test (marker attribute already set)");

  // 2. Description: текст без змін, жодного дочірнього елемента, marker не виконано.
  idField.value = originalId;
  await load();
  const box = document.querySelector("#description");
  check(box.textContent === fixture.body.description, "Description text changed");
  check(box.childElementCount === 0, "Description became HTML elements");
  await sleep(500);
  check(markerValue() === null, "Marker executed from description");

  // 3. Повторний render не дублює вміст.
  await load();
  await load();
  check(box.childNodes.length === 1, "Re-render duplicated description nodes");

  // 4. Comments: seed-інцидент Alice з marker у comments[].text.
  const commentIncident = "20000000-0000-0000-0000-000000000001";
  idField.value = commentIncident;
  await load();
  const commentData = (await api(commentIncident)).body;
  const commentBox = document.querySelector("#comments");
  check(commentData.comments.some(c => c.text.includes("data-lab05-marker")), "Seed comment with marker is missing");
  check(commentBox.children.length === commentData.comments.length, "Comments count differs from API");
  commentData.comments.forEach((comment, index) => {
    const node = commentBox.children[index];
    check(node && node.textContent === comment.text, `Comment ${index} text changed`);
    check(node && node.childElementCount === 0, `Comment ${index} became HTML elements`);
  });
  await load();
  check(commentBox.children.length === commentData.comments.length, "Re-render duplicated comments");
  await sleep(500);
  check(markerValue() === null, "Marker executed from comments");

  // 5. Позитивні рядки: апостроф/лапки, < >, Unicode, HTML-like текст, довгий текст.
  const positives = [
    `Користувач O'Brien написав "терміново"`,
    "5 < 7 і 9 > 3",
    "Київ — Ω",
    "<b>жирний</b> текст",
    "А".repeat(3900)
  ];
  const run = Date.now();
  for (const [index, text] of positives.entries()) {
    const created = await fetch("/api/incidents", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      // Унікальна назва: ЛР 2 забороняє дубль title серед активних інцидентів (409).
      body: JSON.stringify({ title: `dom-test-${run}-${index}`, description: text, severity: "Low", occurredAtUtc: new Date().toISOString() })
    });
    check(created.status === 201, `POST returned ${created.status} for "${text.slice(0, 20)}"`);
    if (!created.ok) continue;
    idField.value = (await created.json()).id;
    await load();
    check(box.textContent === text, `Positive text changed: "${text.slice(0, 20)}"`);
    check(box.childElementCount === 0, `Positive text became elements: "${text.slice(0, 20)}"`);
  }

  idField.value = originalId;
  const result = { passed: failures.length === 0, failures };
  console.log(result.passed ? "PASS: description and comments rendered as plain text" : `FAIL: ${failures.join(" | ")}`);
  return result;
})();
