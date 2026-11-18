const API_ORIGIN = "http://localhost:5080";

await fetch(`${API_ORIGIN}/health`, {
  headers: { "X-Lab-Cors-Probe": "stage-3" },
}).then(async (response) => ({
  status: response.status,
  body: await response.text(),
}));
