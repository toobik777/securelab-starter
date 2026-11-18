API_URL="http://localhost:5080/health"
ALLOWED_ORIGIN="http://localhost:4173"
DISALLOWED_ORIGIN="http://localhost:4174"

curl -sS -D - -o /dev/null \
  -H "Origin: ${ALLOWED_ORIGIN}" "${API_URL}"
curl -sS -D - -o /dev/null \
  -H "Origin: ${DISALLOWED_ORIGIN}" "${API_URL}"
