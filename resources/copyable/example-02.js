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
