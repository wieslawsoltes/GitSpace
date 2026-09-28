#!/usr/bin/env python3
from pathlib import Path
import sys
import tarfile
import zipfile

root = Path(sys.argv[1])
expected = ['Core', 'Diff', 'Git', 'Hosting.GitHub', 'Rendering.Skia', 'Controls.Uno', 'Workbench.Uno']
for name in expected:
    files = list(root.glob('GitSpace.' + name + '.*.nupkg'))
    if len(files) != 1:
        raise SystemExit(f'Expected one package for {name}, found {files}')
    with zipfile.ZipFile(files[0]) as archive:
        entries = archive.namelist()
        if not any(p.endswith('/GitSpace.' + name + '.dll') for p in entries):
            raise SystemExit('Missing reusable assembly: ' + name)
        if not any(p.lower().endswith('readme.md') for p in entries):
            raise SystemExit('Missing packaged README: ' + name)
        manifest = archive.read(next(p for p in entries if p.endswith('.nuspec'))).decode()
        if 'MIT' not in manifest:
            raise SystemExit('Missing license metadata: ' + name)
    print('Verified', files[0].name)
workers = list(root.glob('gitspace-browser-git-*.tgz'))
if len(workers) != 1:
    raise SystemExit('Expected the standalone npm package')
with tarfile.open(workers[0]) as archive:
    names = archive.getnames()
    for entry in ['package/backend.mjs', 'package/dist/worker.js', 'package/README.md']:
        if entry not in names:
            raise SystemExit('Missing npm package payload: ' + entry)
print('Verified', workers[0].name)
