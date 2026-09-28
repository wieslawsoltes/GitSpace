#!/usr/bin/env python3
"""Stage the actual Uno publish output; never substitute an HTML mockup."""
from pathlib import Path
import json
import os
import shutil
import sys

published, destination = map(Path, sys.argv[1:3])
candidates = list(published.rglob('index.html'))
if not candidates:
    raise SystemExit('The Uno publish output contains no index.html')
preferred = [p for p in candidates if p.parent.name == 'wwwroot']
index = preferred[0] if len(preferred) == 1 else candidates[0] if len(candidates) == 1 else None
if index is None:
    raise SystemExit('Ambiguous Uno publish roots: ' + repr(candidates))
if destination.exists():
    shutil.rmtree(destination)
shutil.copytree(index.parent, destination)
worker = Path('src/GitSpace.BrowserGit/dist/worker.js')
if not worker.exists():
    raise SystemExit('Build the browser Git worker before staging Pages')
(destination / 'git').mkdir(exist_ok=True)
shutil.copy2(worker, destination / 'git/worker.js')
(destination / '.nojekyll').touch()
(destination / 'build-info.json').write_text(json.dumps({
    'application': 'GitSpace', 'version': '0.1.0',
    'commit': os.environ.get('GITHUB_SHA', 'local'),
    'unoSdk': '6.7.30', 'renderer': 'Uno Skia',
}, indent=2) + '\n')
if not list(destination.rglob('*.wasm')):
    raise SystemExit('No WebAssembly binaries found: refusing to deploy a non-Uno shell')
print('Staged actual Uno application from', index.parent)
