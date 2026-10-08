#!/usr/bin/env python3
"""Run-on-demand updater. Python 3.9+, standard library only."""
import argparse
import csv
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import subprocess
import sys
import tempfile
import time
import urllib.request
import uuid
import zipfile

REPOSITORY = 'Dimondomestos527/ValheimMastery'
BRANCH = 'Dev'
LIMIT = 64 * 1024 * 1024

def digest(data):
    return hashlib.sha256(data).hexdigest()

def download(url):
    request = urllib.request.Request(url, headers={'User-Agent': 'ValheimMastery-Updater/1'})
    with urllib.request.urlopen(request, timeout=60) as response:
        data = response.read(LIMIT + 1)
    if len(data) > LIMIT:
        raise ValueError('Download exceeds 64 MiB')
    return data

def json_bytes(data):
    return json.dumps(data, indent=2).encode('utf-8')

def running():
    if sys.platform == 'win32':
        output = subprocess.check_output(['tasklist', '/FO', 'CSV', '/NH'])
        names = [row[0].lower() for row in csv.reader(output.decode(errors='replace').splitlines()) if row]
    else:
        output = subprocess.check_output(['ps', '-axo', 'comm'], text=True)
        names = [Path(line.strip()).name.lower() for line in output.splitlines()]
    return any(name in {'valheim', 'valheim.exe', 'valheim_server', 'valheim_server.exe'} for name in names)

def relative(name):
    if not isinstance(name, str) or '\\' in name or ':' in name:
        raise ValueError('Invalid managed path')
    p = PurePosixPath(name)
    if p.is_absolute() or '..' in p.parts or str(p) != name:
        raise ValueError('Unsafe managed path: ' + name)
    if name != 'ValheimMastery.dll' and not (name.startswith('assets/') and p.suffix.lower() in {'.png', '.jpg', '.jpeg', '.wav', '.ogg', '.json', '.bundle'}):
        raise ValueError('Outside Mastery DLL/assets scope: ' + name)
    return p

def confined(root, name):
    relative(name)
    target = root / name
    for p in (target, *target.parents):
        if p == root.parent:
            break
        if p.is_symlink():
            raise ValueError('Symlink/reparse destination rejected')
        if sys.platform == 'win32' and p.exists() and getattr(p.stat(), 'st_file_attributes', 0) & 0x400:
            raise ValueError('Windows reparse destination rejected')
    target.resolve().relative_to(root.resolve())
    return target

def validate(manifest, variant):
    if manifest.get('schema') != 1 or manifest.get('product') != 'ValheimMastery' or manifest.get('variant') != variant:
        raise ValueError('Wrong manifest/product/variant')
    files = manifest.get('files', [])
    if not files or len(files) > 256:
        raise ValueError('Invalid file count')
    names = set()
    for row in files:
        relative(row['path'])
        key = row['path'].casefold()
        if key in names or not re.fullmatch('[0-9a-fA-F]{64}', row['sha256']) or not isinstance(row['size'], int) or not 0 < row['size'] <= LIMIT:
            raise ValueError('Invalid/duplicate file record')
        names.add(key)
    if 'valheimmastery.dll' not in names:
        raise ValueError('DLL missing')
    return files

def install(root, manifest, archive):
    root = Path(root).absolute()
    if not root.is_dir() or root.is_symlink():
        raise ValueError('Target must be an existing ordinary directory')
    files = validate(manifest, manifest.get('variant'))
    for row in files:
        confined(root, row['path'])
    control = root / '.mastery-updates'
    if control.is_symlink() or (sys.platform == 'win32' and control.exists() and getattr(control.stat(), 'st_file_attributes', 0) & 0x400):
        raise ValueError('Unsafe updater directory')
    control.mkdir(exist_ok=True)
    lock = control / 'lock'
    lock.mkdir()  # Existing lock blocks concurrent update; no stale-lock guessing.
    try:
        state_path = control / 'installed.json'
        if state_path.is_symlink():
            raise ValueError('Unsafe updater state')
        old_bytes = state_path.read_bytes() if state_path.exists() else None
        old = json.loads(old_bytes) if old_bytes is not None else None
        old_files = validate(old, manifest['variant']) if old is not None else []
        spellings = {}
        for row in files + old_files:
            key = row['path'].casefold()
            if key in spellings and spellings[key] != row['path']:
                raise ValueError('Case-only managed path rename rejected')
            spellings[key] = row['path']
        names = sorted({row['path'] for row in files + old_files})
        for name in names:
            confined(root, name)
        if archive is None or len(archive) > LIMIT:
            raise ValueError('Archive missing/too large')
        with tempfile.TemporaryDirectory(prefix='stage-', dir=control) as tmp:
            stage = Path(tmp)
            with zipfile.ZipFile(io.BytesIO(archive)) as z:
                entries = z.infolist()
                expected = {row['path']: row for row in files}
                if len(entries) != len(expected) or {i.filename for i in entries} != set(expected):
                    raise ValueError('ZIP differs from file manifest')
                if sum(i.file_size for i in entries) > LIMIT:
                    raise ValueError('Expanded archive too large')
                for entry in entries:
                    row = expected[entry.filename]
                    if entry.file_size != row['size'] or (entry.external_attr >> 16) & 0o170000 == 0o120000:
                        raise ValueError('ZIP size/symlink mismatch')
                    content = z.read(entry)
                    if digest(content) != row['sha256'].lower():
                        raise ValueError('File hash mismatch')
                    dst = stage / entry.filename
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    dst.write_bytes(content)
            if running():
                raise RuntimeError('Close Valheim and its server normally before updating')
            backup_root = control / 'backups'
            if backup_root.is_symlink() or (sys.platform == 'win32' and backup_root.exists() and getattr(backup_root.stat(), 'st_file_attributes', 0) & 0x400):
                raise ValueError('Unsafe backup directory')
            backup = backup_root / (time.strftime('%Y%m%d-%H%M%S') + '-' + uuid.uuid4().hex[:8])
            backup.mkdir(parents=True)
            existed = set()
            for name in names:
                source = confined(root, name)
                if source.exists():
                    if not source.is_file():
                        raise ValueError('Managed target is not a file')
                    dst = backup / name
                    dst.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(source, dst)
                    existed.add(name)
            (backup / 'rollback.json').write_bytes(json_bytes({'files': names, 'existed': sorted(existed)}))
            if old_bytes is not None:
                (backup / 'installed.json').write_bytes(old_bytes)
            if running():
                raise RuntimeError('Game started during staging; update cancelled')
            try:
                for row in files:
                    target = confined(root, row['path'])
                    target.parent.mkdir(parents=True, exist_ok=True)
                    os.replace(stage / row['path'], target)
                for name in set(names) - set(expected):
                    target = confined(root, name)
                    if target.exists():
                        target.unlink()
                for row in files:
                    if digest(confined(root, row['path']).read_bytes()) != row['sha256'].lower():
                        raise RuntimeError('Installed hash mismatch')
                pending = control / ('state-' + uuid.uuid4().hex + '.json')
                pending.write_bytes(json_bytes(manifest))
                os.replace(pending, state_path)
            except BaseException:
                for name in names:
                    target = confined(root, name)
                    if name in existed:
                        target.parent.mkdir(parents=True, exist_ok=True)
                        shutil.copy2(backup / name, target)
                    elif target.exists():
                        target.unlink()
                if old_bytes is not None:
                    state_path.write_bytes(old_bytes)
                elif state_path.exists():
                    state_path.unlink()
                raise
            print('Updated Valheim Mastery', manifest['version'], manifest['variant'])
            print('Backup:', backup)
    finally:
        lock.rmdir()

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--variant', choices=['Client', 'Server'], default='Client')
    parser.add_argument('--target', type=Path, default=Path(__file__).resolve().parent)
    args = parser.parse_args()
    if running():
        raise RuntimeError('Close Valheim and its server normally before updating')
    head = json.loads(download('https://api.github.com/repos/' + REPOSITORY + '/commits/' + BRANCH))['sha']
    if not re.fullmatch('[0-9a-f]{40}', head):
        raise ValueError('Invalid commit SHA')
    base = 'https://raw.githubusercontent.com/' + REPOSITORY + '/' + head + '/distribution/'
    manifest = json.loads(download(base + args.variant.lower() + '.json'))
    validate(manifest, args.variant)
    if not re.fullmatch(r'[A-Za-z0-9_.-]+\.zip', manifest['archive']):
        raise ValueError('Invalid archive path')
    archive = download(base + manifest['archive'])
    if digest(archive) != manifest['archive_sha256'].lower():
        raise ValueError('Archive hash mismatch')
    manifest['commit'] = head
    install(args.target, manifest, archive)

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print('Update failed:', error, file=sys.stderr)
        sys.exit(1)
