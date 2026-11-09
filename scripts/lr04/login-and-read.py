"""Local SecureLab login/read with an in-memory cookie jar; no secrets printed."""
import argparse, http.cookiejar, json, os, sys, urllib.error, urllib.request
from pathlib import Path
parser = argparse.ArgumentParser()
parser.add_argument('--user', choices=['alice', 'bob', 'morgan'], default='alice')
parser.add_argument('--incident', choices=['alice', 'bob'], default='alice')
parser.add_argument('--base', default='http://localhost:5080')
parser.add_argument('--check', action='store_true')
args = parser.parse_args()
password = os.environ.get('SeedUsers__Password', '')
payload = json.dumps({'userName': args.user, 'password': password}).encode()
if args.check:
    print(json.dumps({'password_present': bool(password),
                      'json_valid': isinstance(json.loads(payload), dict)}))
    raise SystemExit(0 if password else 2)
if not password:
    raise SystemExit('Set SeedUsers__Password in this terminal. No value printed.')
ids = json.loads((Path(__file__).resolve().parents[1]
                  / 'seed-identifiers.json').read_text())
jar = http.cookiejar.CookieJar()
opener = urllib.request.build_opener(urllib.request.HTTPCookieProcessor(jar))
def request(path, data=None):
    req = urllib.request.Request(args.base.rstrip('/') + path, data=data,
        headers={'Content-Type': 'application/json', 'Accept': 'application/json'})
    try:
        with opener.open(req, timeout=15) as response:
            return response.status, response.read()
    except urllib.error.HTTPError as error:
        return error.code, error.read()
    except urllib.error.URLError:
        raise SystemExit('API unavailable; check base URL and running API.')
status, body = request('/api/auth/login', payload)
print('login_status=' + str(status))
if status != 204:
    raise SystemExit('Login failed; check shared seed password/reset. No body printed.')
status, body = request('/api/incidents/' + ids['incidents'][args.incident])
print('read_status=' + str(status))
if body:
    dto = json.loads(body)
    if status == 200:
        print(json.dumps({k: dto.get(k) for k in ['id', 'title', 'ownerDisplayName']},
                         ensure_ascii=False))
    else:
        print(json.dumps({k: dto.get(k) for k in ['title', 'status', 'traceId']},
                         ensure_ascii=False))
