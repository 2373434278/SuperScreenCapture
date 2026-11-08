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

# GitHub 的 Intel macOS 运行器偶发 hdiutil "Resource busy"，
# 通过短暂等待重试，避免整条发布流水线因瞬时资源占用失败。
created=false
for attempt in 1 2 3; do
  rm -f "${output_dmg}"
  if hdiutil create \
    -volname "${volume_name}" \
    -srcfolder "${staging}" \
    -ov \
    -format UDZO \
    "${output_dmg}"; then
    created=true
    break
  fi

  echo "hdiutil attempt ${attempt} failed; retrying in 10s" >&2
  sleep 10
done

if [[ "${created}" != true ]]; then
  echo "Unable to create DMG: ${output_dmg}" >&2
  exit 1
fi

echo "Created ${output_dmg}"
