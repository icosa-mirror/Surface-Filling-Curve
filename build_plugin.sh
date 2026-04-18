#!/usr/bin/env bash
# Builds the native library and installs it into the UPM package.
set -euo pipefail

REPO_ROOT="$(cd "$(dirname "$0")" && pwd)"
BUILD_DIR="$REPO_ROOT/build_plugin"
PKG_DIR="$REPO_ROOT/unity/com.iota97.surface-filling-curve"

cmake -B "$BUILD_DIR" -S "$REPO_ROOT" -DCMAKE_BUILD_TYPE=Release
cmake --build "$BUILD_DIR" --target surface_filling_curve --config Release -j"$(sysctl -n hw.logicalcpu 2>/dev/null || nproc)"

# Detect platform and copy to the correct Plugins subfolder
case "$(uname -s)" in
    Darwin)
        DEST="$PKG_DIR/Plugins/macOS/libsurface_filling_curve.dylib"
        SRC="$BUILD_DIR/libsurface_filling_curve.dylib"
        ;;
    Linux)
        DEST="$PKG_DIR/Plugins/Linux/x86_64/libsurface_filling_curve.so"
        SRC="$BUILD_DIR/libsurface_filling_curve.so"
        ;;
    *)
        echo "Unsupported platform — use build_plugin.bat on Windows." >&2
        exit 1
        ;;
esac

cp "$SRC" "$DEST"
echo "Installed: $DEST"

# macOS requires an ad-hoc code signature on any dylib loaded by a sandboxed
# process. Without this Unity refuses to load the library at runtime.
if [[ "$(uname -s)" == "Darwin" ]]; then
    codesign --force --sign - "$DEST"
    echo "Signed:    $DEST"
fi
