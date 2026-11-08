#!/usr/bin/env bash
set -euo pipefail

app_path="${1:?usage: create-dmg.sh <SuperScreenCapture.app> [output.dmg]}"
app_name="$(basename "${app_path}" .app)"
output_dmg="${2:-artifacts/${app_name}.dmg}"
volume_name="${SSC_VOLUME_NAME:-SuperScreenCapture}"

if [[ ! -d "${app_path}/Contents" ]]; then
  echo "App bundle not found: ${app_path}" >&2
  exit 1
fi

staging="$(mktemp -d)"
trap 'rm -rf "${staging}"' EXIT

cp -R "${app_path}" "${staging}/"
ln -s /Applications "${staging}/Applications"

mkdir -p "$(dirname "${output_dmg}")"
rm -f "${output_dmg}"
hdiutil create \
  -volname "${volume_name}" \
  -srcfolder "${staging}" \
  -ov \
  -format UDZO \
  "${output_dmg}"

echo "Created ${output_dmg}"
