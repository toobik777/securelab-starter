"""ЛР 6: спостереження заголовків і поведінки API. Cookie лише в пам'яті; пароль зі SeedUsers__Password не друкується.
Запуск: py -3 scripts/lr06/lab06_probe.py headers [--base http://localhost:5080] [--with-login]
        py -3 scripts/lr06/lab06_probe.py crash [--base ...]   (лише для Production-запуску з недосяжною БД)
        py -3 scripts/lr06/lab06_probe.py csrf  [--base ...]   (мутації з cookie без/із request token)"""
import argparse, http.cookiejar, json, os, sys, urllib.error, urllib.request, uuid

sys.stdout.reconfigure(encoding="utf-8")
ap = argparse.ArgumentParser()
ap.add_argument("mode", choices=["headers", "crash", "csrf"])
ap.add_argument("--base", default="http://localhost:5080")
ap.add_argument("--token-route", default="/api/auth/csrf-token")
ap.add_argument("--token-header", default="X-CSRF-TOKEN")
args = ap.parse_args()
PWD = os.environ.get("SeedUsers__Password", "")
SHOW = ["content-type", "x-content-type-options", "referrer-policy", "content-security-policy", "cache-control", "server"]


class Client:
    def __init__(self):
        self.jar = http.cookiejar.CookieJar()
        self.op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        self.extra = {}

    def call(self, method, path, body=None, headers=None):
        data = json.dumps(body).encode() if body is not None else None
        h = {"Content-Type": "application/json", "Accept": "application/json", **self.extra, **(headers or {})}
        req = urllib.request.Request(args.base.rstrip("/") + path, data=data, method=method, headers=h)
        try:
            with self.op.open(req, timeout=30) as r:
                raw = r.read()
                return r.status, dict((k.lower(), v) for k, v in r.getheaders()), raw
        except urllib.error.HTTPError as e:
            return e.code, dict((k.lower(), v) for k, v in e.headers.items()), e.read()

    def login(self, user):
        s, _, _ = self.call("POST", "/api/auth/login", {"userName": user, "password": PWD})
        assert s == 204, f"login {user} -> {s}"


def short(v, n=60):
    return v if len(v) <= n else v[:n] + "…"


def show(label, method, path, s, h, raw=b""):
    print(f"[{label}] {method} {path} -> {s}")
    for k in SHOW:
        print(f"    {k}: {short(h[k]) if k in h else '(відсутній)'}")
    body = raw[:60].decode("utf-8", "replace").replace("\n", " ") if raw else ""
    print(f"    body: {body!r}")


if args.mode == "headers":
    alice = Client(); alice.login("alice")
    anon = Client()
    for label, cl, m, p in [
        ("1 статична сторінка", anon, "GET", "/"),
        ("2 успішний API", alice, "GET", "/api/incidents/20000000-0000-0000-0000-000000000001"),
        ("3 validation 4xx", alice, "GET", "/api/incidents?status=Unknown"),
        ("4 404", alice, "GET", "/api/incidents/99999999-9999-9999-9999-999999999999"),
        ("додатково: /api/me", alice, "GET", "/api/me"),
        ("додатково: невідомий API-шлях", alice, "GET", "/api/nonexistent-route"),
    ]:
        s, h, raw = cl.call(m, p)
        show(label, m, p, s, h, raw)
elif args.mode == "crash":
    c = Client()
    s, h, raw = c.call("POST", "/api/auth/login", {"userName": "alice", "password": "NotARealPassword1!"})
    show("5 контрольована 500", "POST", "/api/auth/login", s, h, raw)
    try:
        trace = json.loads(raw).get("traceId")
    except ValueError:
        trace = None
    print("traceId у відповіді:", trace)
else:
    alice = Client(); alice.login("alice")
    inc = {"title": "CSRF " + uuid.uuid4().hex[:6], "description": "Запис для перевірки antiforgery, довший за сорок символів.",
           "severity": "Low", "occurredAtUtc": "2026-11-05T09:00:00Z"}
    s, h, raw = alice.call("POST", "/api/incidents", inc)
    print(f"POST /api/incidents без токена -> {s}")
    s, h, raw = alice.call("GET", args.token_route)
    has_token = s == 200 and "json" in h.get("content-type", "") and bool(json.loads(raw).get("token"))
    print(f"GET {args.token_route} -> {s}; token_present={has_token}")
    if has_token:
        alice.extra[args.token_header] = json.loads(raw)["token"]
        s, h, raw = alice.call("POST", "/api/incidents", inc)
        iid = json.loads(raw)["id"]
        print(f"POST /api/incidents з токеном -> {s}")
        saved = alice.extra.pop(args.token_header)
        s, _, rb = alice.call("PUT", f"/api/incidents/{iid}", {"title": "змінено без токена"})
        print(f"PUT без токена -> {s} ({json.loads(rb).get('title') if rb else ''})")
        s, _, rb = alice.call("GET", f"/api/incidents/{iid}")
        print(f"GET після відмови -> title={json.loads(rb)['title']!r} (стан не змінився)")
        alice.extra[args.token_header] = saved
        s, _, _ = alice.call("PUT", f"/api/incidents/{iid}", {"title": "змінено з токеном"})
        print(f"PUT з токеном -> {s}")
        s, _, rb = alice.call("GET", f"/api/incidents/{iid}")
        print(f"GET після дозволеної зміни -> title={json.loads(rb)['title']!r}")
        s, _, _ = alice.call("DELETE", f"/api/incidents/{iid}")
        print(f"DELETE з токеном -> {s}")
        alice.extra.pop(args.token_header)
        s, _, _ = alice.call("POST", "/api/auth/logout")
        print(f"logout без токена -> {s}")
        alice.extra[args.token_header] = saved
        s, _, _ = alice.call("POST", "/api/auth/logout")
        print(f"logout з токеном -> {s}")
