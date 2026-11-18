const demo = document.createElement("div");
renderLabels(demo, [{ label: "<b>демо</b>" }]);
console.log(demo.querySelector("li").textContent);
console.log(demo.querySelector("b"));
renderLabels(demo, []);
console.log(demo.querySelectorAll("li").length);
