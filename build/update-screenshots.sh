#!/usr/bin/env bash
# Regenerate README screenshots from the headless UI test harness.
# Usage: bash build/update-screenshots.sh
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
dotnet="${DOTNET:-$HOME/.dotnet/dotnet}"
cd "$root"

echo "Rendering settings screens..."
SSC_UPDATE_SCREENSHOTS=1 "$dotnet" test tests/SSC.UI.Tests/SSC.UI.Tests.csproj \
  -c Debug --nologo --filter "FullyQualifiedName~ScreenshotTour"

echo "Rendering remaining windows..."
"$dotnet" test tests/SSC.UI.Tests/SSC.UI.Tests.csproj -c Debug --nologo

tmp="${TMPDIR:-/tmp}"
copy() {
  local source="$tmp/$1"
  local target="$root/assets/screenshots/$2"
  if [[ -f "$source" ]]; then
    cp "$source" "$target"
    echo "  $2"
  else
    echo "  missing $source (skipped)"
  fi
}

# capture-overlay keeps a real desktop capture; the headless frame is synthetic.
copy "ssc-ui-annotation-editor.png" "annotation-editor.png"
copy "ssc-ui-recording-setup.png" "recording-setup.png"
copy "ssc-ui-history-drawer.png" "history-drawer.png"
copy "ssc-ui-ocr-result.png" "ocr-result.png"
copy "ssc-ui-upload.png" "upload-dialog.png"

echo "Screenshots updated in assets/screenshots."
