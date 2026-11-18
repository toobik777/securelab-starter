#!/usr/bin/env python3
"""Compose published scaffold into the student's completed previous lab; never apply a patch."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parent
API = 'src/SecureLab.Api/'

def replace_once(text, before, after, name):
    if text.count(before) != 1:
        raise ValueError(f'{name}: очікувався один integration anchor; узгодьте його без перезапису власного коду')
    return text.replace(before, after, 1)

def plan(lab, project):
    manifest = json.loads((ROOT / 'manifest.json').read_text())
    release = manifest['labs'][lab]
    marker = f'.scaffolds/lab-{lab}.json'
    receipt_path = project / marker
    if receipt_path.is_symlink() or not receipt_path.resolve().is_relative_to(project.resolve()):
        raise ValueError('Unsafe scaffold receipt path')
    if receipt_path.exists():
        raise ValueError('Scaffold already installed; do not replay over student remediation')
    writes = {}
    for relative, expected_hash in release['files'].items():
        data = (ROOT / f'lab-{lab}' / 'files' / relative).read_bytes()
        if hashlib.sha256(data).hexdigest() != expected_hash:
            raise ValueError(f'{relative}: scaffold checksum mismatch')
        target = project / relative
        if target.is_symlink() or not target.resolve().is_relative_to(project.resolve()):
            raise ValueError(f'{relative}: unsafe target')
        if target.exists():
            old_hash = hashlib.sha256(target.read_bytes().replace(b'\r\n', b'\n')).hexdigest()
            if old_hash != release.get('replace_baseline_sha256', {}).get(relative):
                raise ValueError(f'{relative}: файл уже існує або змінений; автоматичного overwrite немає')
        writes[relative] = data
    def edit(relative, transform):
        target = project / relative
        if target.is_symlink() or not target.resolve().is_relative_to(project.resolve()):
            raise ValueError(f'{relative}: unsafe integration target')
        text = writes.get(relative, target.read_bytes()).decode('utf-8-sig').replace('\r\n', '\n')
        writes[relative] = transform(text).encode()
    def sub(relative, before, after):
        edit(relative, lambda s: replace_once(s, before, after, relative))
    program = API + 'Program.cs'
    if lab in ('02', '03'):
        edit(program, lambda s: s if 'using SecureLab.Api.Scaffolding;' in s else 'using SecureLab.Api.Scaffolding;\n' + s)
    if lab == '02':
        sub(program, 'app.MapIncidentEndpoints();', 'app.MapIncidentEndpoints();\napp.MapLab02Endpoints();')
        edit(API+'Data/DatabaseBootstrap.cs', lambda s: s.replace('await DbSeeder.SeedAsync(dbContext);',
            'await DbSeeder.SeedAsync(dbContext);\n        await SecureLab.Api.Scaffolding.Lab02Seed.EnsureAsync(services);'))
    if lab == '03':
        sub(program, 'var app = builder.Build();', 'builder.AddLab03Identity();\n\nvar app = builder.Build();')
        sub(program, 'app.UseDefaultFiles();', 'app.UseAuthentication();\napp.UseAuthorization();\n\napp.UseDefaultFiles();')
        sub(program, 'app.MapIncidentEndpoints();', 'app.MapIncidentEndpoints();\napp.MapLab03Identity();')
        sub(API+'SecureLab.Api.csproj', '<PackageReference Include="Microsoft.EntityFrameworkCore"',
            '<PackageReference Include="Microsoft.AspNetCore.Identity.EntityFrameworkCore" Version="10.0.10" />\n    <PackageReference Include="Microsoft.EntityFrameworkCore"')
        for entity in ('Incident', 'IncidentComment', 'IncidentStatusHistory'):
            edit(API+f'Data/Entities/{entity}.cs', lambda s: s.replace('StudyUser', 'ApplicationUser'))
        edit(API+'Data/DatabaseBootstrap.cs', lambda s: s.replace(
            'await DbSeeder.SeedAsync(dbContext, userManager, roleManager, configuration);',
            'await DbSeeder.SeedAsync(dbContext, userManager, roleManager, configuration);\n        await SecureLab.Api.Scaffolding.Lab02Seed.EnsureAsync(services);'))
    # A marker prevents accidental double installation and records the inherited state separately from completion.
    marker = f'.scaffolds/lab-{lab}.json'
    if (project / marker).is_symlink() or not (project / marker).resolve().is_relative_to(project.resolve()):
        raise ValueError('Unsafe scaffold receipt path')
    if (project / marker).exists():
        raise ValueError('Scaffold already installed; do not replay over student remediation')
    writes[marker] = (json.dumps({'release': release['release'], 'inherits': release['inherits']}, indent=2)+'\n').encode()
    return writes

def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('lab', choices=sorted(json.loads((ROOT/'manifest.json').read_text())['labs']))
    parser.add_argument('project', type=Path)
    parser.add_argument('--write', action='store_true', help='Без цього прапорця лише план змін')
    args=parser.parse_args()
    project=args.project.resolve()
    manifest=json.loads((ROOT/'manifest.json').read_text())
    tag=manifest['labs'][args.lab]['inherits']
    try:
        # Require the actual previous student tag to be an ancestor, never switch branches or create tags.
        subprocess.run(['git','-C',str(project),'merge-base','--is-ancestor',tag,'HEAD'], check=True, capture_output=True)
        if subprocess.check_output(['git','-C',str(project),'status','--porcelain'], text=True).strip():
            raise ValueError('Спершу збережіть власні зміни; потрібне чисте робоче дерево')
        writes=plan(args.lab, project)
        marker=f'.scaffolds/lab-{args.lab}.json'
        receipt=json.loads(writes[marker])
        receipt['base_commit']=subprocess.check_output(
            ['git','-C',str(project),'rev-parse',tag+'^{commit}'],text=True).strip()
        receipt['entry_parent']=subprocess.check_output(
            ['git','-C',str(project),'rev-parse','HEAD'],text=True).strip()
        writes[marker]=(json.dumps(receipt,indent=2)+'\n').encode()
        for relative in writes:
            print(relative)
        if args.write:
            for relative,data in writes.items():
                target=project/relative;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
            print('Scaffold installed. Review diff, restore/build, verify; completion is student work.')
        else:
            print('Dry run only. Repeat with --write after checking the listed files.')
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        parser.exit(1, f'{error}\n')

if __name__=='__main__': main()
