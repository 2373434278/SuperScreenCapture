#!/usr/bin/env bash
set -euo pipefail

publish_dir="${1:-artifacts/osx-arm64}"
app_name="SuperScreenCapture"
output_dir="${2:-artifacts/${app_name}.app}"
version="${SSC_VERSION:-1.0.0}"
build_number="${SSC_BUILD_NUMBER:-$(echo "${version}" | tr -cd '0-9')}"

if [[ ! -x "${publish_dir}/SSC.App" ]]; then
  echo "Published executable not found: ${publish_dir}/SSC.App" >&2
  exit 1
fi

rm -rf "${output_dir}"
mkdir -p "${output_dir}/Contents/MacOS"
cp -R "${publish_dir}/." "${output_dir}/Contents/MacOS/"
cp "build/mac/Info.plist" "${output_dir}/Contents/Info.plist"

/usr/libexec/PlistBuddy -c "Set :CFBundleShortVersionString ${version}" "${output_dir}/Contents/Info.plist"
/usr/libexec/PlistBuddy -c "Set :CFBundleVersion ${build_number}" "${output_dir}/Contents/Info.plist"

mkdir -p "${output_dir}/Contents/Resources"
cp LICENSE NOTICE "${output_dir}/Contents/Resources/"
cp "build/mac/AppIcon.icns" "${output_dir}/Contents/Resources/AppIcon.icns"
if [[ -f "build/third-party-notices.txt" ]]; then
  cp "build/third-party-notices.txt" "${output_dir}/Contents/Resources/THIRD-PARTY-NOTICES.txt"
fi

# Use a persistent local identity when available so macOS TCC grants (screen
# recording, mic) survive reinstalls; ad-hoc signatures change per build and
# invalidate those grants. Release builds replace this step with a Developer ID
# signature and notarization.
identity="${SSC_CODESIGN_IDENTITY:-}"
if [[ -z "${identity}" ]] && security find-identity -p codesigning 2>/dev/null | grep -q '"SSC Local Signing"'; then
  identity="SSC Local Signing"
fi
if [[ -n "${identity}" ]]; then
  codesign --force --deep --sign "${identity}" "${output_dir}"
else
  codesign --force --deep --sign - "${output_dir}" >/dev/null 2>&1 || true
fi

echo "Created ${output_dir}"
