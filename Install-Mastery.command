#!/bin/bash
set -euo pipefail
unset PYTHONHOME PYTHONPATH
export PYTHONNOUSERSITE=1
trap 'status=$?; if (( status != 0 )); then echo "Installer stopped. See the error above; existing files were preserved." >&2; fi; if [[ -t 0 ]]; then echo "Press Return to close."; read -r || true; fi' EXIT
action='install'
repository='Dimondomestos527/ValheimMastery'
work=$(mktemp -d "${TMPDIR:-/tmp}/ValheimMasteryInstaller.XXXXXX")
echo "Private installer runtime: $work"
curl --connect-timeout 15 --max-time 180 --fail --location --proto '=https' --tlsv1.2 --silent --show-error "https://api.github.com/repos/$repository/commits/Release" -o "$work/head.json"
commit=$(sed -n 's/^[[:space:]]*"sha": "\([a-f0-9]*\)",.*/\1/p' "$work/head.json" | head -n 1)
[[ "$commit" =~ ^[a-f0-9]{40}$ ]] || { echo 'Invalid release commit'; exit 1; }
base="https://raw.githubusercontent.com/$repository/$commit"
case "$(uname -m)" in
 arm64) name='aarch64-apple-darwin'; hash='d8975d7df4f08f7b1c7aafcdfacbddcec3d366415f2c1a72b2466b6850815933' ;;
 x86_64) name='x86_64-apple-darwin'; hash='8e9cb087305bfb8969f68a905f79f41469d4aa5220c1aa71ada7fc9953bdba0f' ;;
 *) echo 'Unsupported Mac architecture'; exit 1 ;;
esac
curl --connect-timeout 15 --max-time 180 --fail --location --proto '=https' --tlsv1.2 --silent --show-error "https://github.com/astral-sh/python-build-standalone/releases/download/20261003/cpython-3.13.16%2B20261003-$name-install_only.tar.gz" -o "$work/python.tar.gz"
actual=$(shasum -a 256 "$work/python.tar.gz" | cut -d ' ' -f 1)
[[ "$actual" == "$hash" ]] || { echo 'Python checksum mismatch'; exit 1; }
tar -xzf "$work/python.tar.gz" -C "$work"
python="$work/python/bin/python3"
curl --connect-timeout 15 --max-time 180 --fail --location --proto '=https' --tlsv1.2 --silent --show-error "$base/installer/bootstrap.json" -o "$work/bootstrap.json"
for item in mastery_installer.py steam_launch.py language_setting.py; do
 curl --connect-timeout 15 --max-time 180 --fail --location --proto '=https' --tlsv1.2 --silent --show-error "$base/installer/$item" -o "$work/$item"
 "$python" - "$work/bootstrap.json" "$work/$item" "$item" <<'PY'
import hashlib,json,sys
from pathlib import Path
manifest=json.loads(Path(sys.argv[1]).read_bytes())
if manifest.get('schema')!=2: raise SystemExit('Invalid bootstrap manifest')
expected=manifest['runtime_files'][sys.argv[3]]
if hashlib.sha256(Path(sys.argv[2]).read_bytes()).hexdigest()!=expected: raise SystemExit('Installer checksum mismatch')
PY
done
export VM_PYTHON_ARCHIVE="$work/python.tar.gz"
args=("$work/mastery_installer.py" "$action" --commit "$commit")
if (( $# > 0 )); then args+=(--target "$1"); fi
"$python" -X utf8 "${args[@]}"
echo 'Finished. Launch-Mastery enables updates before Valheim starts.'
