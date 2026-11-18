"""ЛР 6, етап 3 п. 4: шість сценаріїв журналу (успішний/невдалий login, denied action, validation error,
privileged transition; unexpected error виконується окремим Production-запуском). Пароль — зі SeedUsers__Password."""
import http.cookiejar, json, os, sys, urllib.error, urllib.request, uuid

sys.stdout.reconfigure(encoding="utf-8")
BASE = "http://localhost:5080"
PWD = os.environ["SeedUsers__Password"]


def client():
    jar = http.cookiejar.CookieJar()
    op = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
    extra = {}

    def call(method, path, body=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(BASE + path, data=data, method=method,
                                     headers={"Content-Type": "application/json", **extra})
        try:
            with op.open(req, timeout=20) as r:
                raw = r.read()
                return r.status, (json.loads(raw) if raw else None)
        except urllib.error.HTTPError as e:
            raw = e.read()
            return e.code, (json.loads(raw) if raw else None)

    call.extra = extra
    return call


def with_token(call):
    s, b = call("GET", "/api/auth/csrf-token")
    assert s == 200
    call.extra["X-CSRF-TOKEN"] = b["token"]


anon, alice, morgan = client(), client(), client()
print("1. successful login:", anon("POST", "/api/auth/login", {"userName": "bob", "password": PWD})[0])
print("2. failed login:", client()("POST", "/api/auth/login", {"userName": "alice", "password": "NotARealPassword1!"})[0])
assert alice("POST", "/api/auth/login", {"userName": "alice", "password": PWD})[0] == 204
with_token(alice)
s, b = alice("POST", "/api/incidents", {"title": "Log scenario " + uuid.uuid4().hex[:6],
                                         "description": "Запис для сценаріїв журналу, довший за сорок символів.",
                                         "severity": "Low", "occurredAtUtc": "2026-11-05T09:00:00Z"})
iid = b["id"]
print("3. denied protected action (Reporter triage):", alice("POST", f"/api/incidents/{iid}/triage")[0])
print("4. validation error (status filter):", alice("GET", "/api/incidents?status=Unknown")[0])
assert morgan("POST", "/api/auth/login", {"userName": "morgan", "password": PWD})[0] == 204
with_token(morgan)
print("5. privileged transition (Analyst triage):", morgan("POST", f"/api/incidents/{iid}/triage")[0])
print("   (6. unexpected server error — окремий Production-запуск з недосяжною БД)")
