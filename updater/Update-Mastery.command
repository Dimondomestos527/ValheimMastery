#!/bin/bash
set -euo pipefail
HERE="$(cd -- "$(dirname -- "$0")" && pwd)"
if ! command -v python3 >/dev/null 2>&1; then
  echo "Python 3.9 or newer is required. Install Python and run again." >&2
  exit 1
fi
python3 -c 'import sys; sys.exit(0 if sys.version_info >= (3,9) else 1)'
exec python3 "$HERE/mastery_update.py" --target "$HERE" "$@"
