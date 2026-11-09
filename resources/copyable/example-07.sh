git status --short
git add .scaffolds/lab-05.json demonstrations/cors-probe
git add src/SecureLab.Api/Client/browser-lab.html
git add src/SecureLab.Api/Client/browser-lab.js
git diff --staged --stat
git commit -m "lab5: install scaffold lab-05-start-v1 (vulnerable state)"
git rev-parse --short HEAD
