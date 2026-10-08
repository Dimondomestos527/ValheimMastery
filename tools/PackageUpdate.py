"""Package reviewed binaries; never builds current Dev source implicitly."""
import argparse, hashlib, json, re, zipfile
from pathlib import Path

def build(args):
    root = Path(__file__).resolve().parent.parent
    out = root / 'distribution'
    out.mkdir(exist_ok=True)
    if not re.fullmatch(r'[A-Za-z0-9_.-]+', args.version):
        raise ValueError('Invalid version')
    for variant, dll in [('Client', args.client), ('Server', args.server)]:
        payload = {'ValheimMastery.dll': dll.read_bytes()}
        if not payload['ValheimMastery.dll'].startswith(b'MZ'):
            raise ValueError('Not a DLL: ' + str(dll))
        if args.assets:
            for p in sorted(args.assets.rglob('*')):
                if p.is_file():
                    rel = p.relative_to(args.assets).as_posix()
                    if p.is_symlink() or p.suffix.lower() not in {'.png', '.jpg', '.jpeg', '.wav', '.ogg', '.json', '.bundle'}:
                        raise ValueError('Unexpected asset: ' + rel)
                    payload['assets/' + rel] = p.read_bytes()
        archive = variant.lower() + '-' + args.version + '.zip'
        with zipfile.ZipFile(out / archive, 'w', zipfile.ZIP_DEFLATED) as z:
            for name, data in payload.items(): z.writestr(name, data)
        manifest = {'schema': 1, 'product': 'ValheimMastery', 'variant': variant, 'version': args.version, 'source': args.source, 'validation': args.validation, 'archive': archive, 'archive_sha256': hashlib.sha256((out / archive).read_bytes()).hexdigest(), 'files': [{'path': name, 'size': len(data), 'sha256': hashlib.sha256(data).hexdigest()} for name, data in payload.items()]}
        (out / (variant.lower() + '.json')).write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
    print('Prepared Client and Server manifests/ZIPs; publish all together in one commit.')

if __name__ == '__main__':
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--client', type=Path, required=True)
    p.add_argument('--server', type=Path, required=True)
    p.add_argument('--version', required=True)
    p.add_argument('--source', required=True)
    p.add_argument('--validation', required=True)
    p.add_argument('--assets', type=Path)
    build(p.parse_args())
