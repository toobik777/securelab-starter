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
