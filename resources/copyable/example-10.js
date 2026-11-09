function renderLabels(container, items) {
  const list = document.createElement("ul");

  for (const item of items) {
    const listItem = document.createElement("li");
    listItem.textContent = item.label;
    list.append(listItem);
  }

  container.replaceChildren(list);
}
