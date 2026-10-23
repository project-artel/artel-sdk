#!/usr/bin/env bash
#
# Assemble the throwaway Unity project used to run the package's tests.
#
# The repository root is not a Unity project and the only Unity project in the
# tree (samples/WordVenture) does not declare kr.artel.sdk as a testable, so the
# Test Runner cannot discover Packages/kr.artel.sdk/Tests anywhere. This script
# materialises a minimal project that does:
#
#   <dest>/ProjectSettings/ProjectVersion.txt   pinned editor version
#   <dest>/Packages/manifest.json               deps + "testables": ["kr.artel.sdk"]
#   <dest>/Packages/kr.artel.sdk/               the package, embedded (not a file: ref)
#   <dest>/Assets/                              empty, required by Unity
#
# Everything except the package copy comes from .github/unity-test-project/, so
# CI and a local run use the exact same project definition.
#
# An existing <dest>/Library is left untouched so a restored CI cache survives.
#
# Usage: .github/scripts/setup-unity-test-project.sh <dest> [0|1|2] [absent|present]

set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
template="${repo_root}/.github/unity-test-project"
package="${repo_root}/Packages/kr.artel.sdk"

dest="${1:-}"
if [ -z "${dest}" ]; then
  echo "usage: $0 <dest>" >&2
  exit 64
fi

mkdir -p "${dest}"
dest="$(cd "${dest}" && pwd)"

cp -R "${template}/." "${dest}/"
mkdir -p "${dest}/Assets"

# 각 job이 서로 다른 입력 backend로 컴파일되게 설정한다.
input_handling="${2:-0}"
case "$input_handling" in 0|1|2) ;; *) echo "input handling must be 0, 1, or 2" >&2; exit 64 ;; esac
input_package="${3:-absent}"
case "$input_package" in absent|present) ;; *) echo "input package must be absent or present" >&2; exit 64 ;; esac
if [ "$input_package" = absent ] && [ "$input_handling" != 0 ]; then
  echo "New/Both input handling requires the Input System package" >&2
  exit 64
fi
python3 - "${dest}/Packages/manifest.json" "$input_package" <<'PY'
import json, sys
from pathlib import Path
path = Path(sys.argv[1])
manifest = json.loads(path.read_text())
if sys.argv[2] == "present":
    manifest["dependencies"]["com.unity.inputsystem"] = "1.7.0"
else:
    manifest["dependencies"].pop("com.unity.inputsystem", None)
path.write_text(json.dumps(manifest, indent=2) + "\n")
PY
cat > "${dest}/ProjectSettings/ProjectSettings.asset" <<EOF
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!129 &1
PlayerSettings:
  activeInputHandler: ${input_handling}
EOF

rm -rf "${dest}/Packages/kr.artel.sdk"
cp -R "${package}" "${dest}/Packages/kr.artel.sdk"

echo "Unity test project ready at ${dest}"
echo "  editor:  $(sed -n 's/^m_EditorVersion: //p' "${dest}/ProjectSettings/ProjectVersion.txt")"
echo "  library: $([ -d "${dest}/Library" ] && echo present || echo absent)"
