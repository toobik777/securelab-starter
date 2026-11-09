await fetch("http://localhost:5080/api/incidents", {
  credentials: "include",
  headers: { Accept: "application/json" },
});
