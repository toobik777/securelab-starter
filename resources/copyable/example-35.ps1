curl.exe -sS -D - -o NUL `
  -H "Origin: http://localhost:4173" "http://localhost:5080/health"
curl.exe -sS -D - -o NUL `
  -H "Origin: http://localhost:4174" "http://localhost:5080/health"
