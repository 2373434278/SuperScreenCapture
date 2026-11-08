#!/usr/bin/env bash
set -euo pipefail

# Signs and notarizes macOS artifacts when credentials are present.
# Without credentials the script exits successfully so forks and local builds
# still produce unsigned bundles. See README for the required secrets.
#
# Usage:
#   sign-and-notarize.sh <SuperScreenCapture.app>            # sign + notarize app
#   sign-and-notarize.sh <SuperScreenCapture.app> <app.dmg>  # sign + notarize DMG
#
# Run the app form before creating the DMG, then the DMG form afterwards.
#
# Required for signing:  MACOS_CERT_P12 (base64), MACOS_CERT_PASSWORD
# Optional keychain:     MACOS_KEYCHAIN_PASSWORD
# Required notarizing:   APPLE_ID, APPLE_TEAM_ID, APPLE_APP_SPECIFIC_PASSWORD

app_path="${1:?usage: sign-and-notarize.sh <SuperScreenCapture.app> [output.dmg]}"
dmg_path="${2:-}"

if [[ -z "${MACOS_CERT_P12:-}" ]]; then
  echo "MACOS_CERT_P12 not set; skipping signing and notarization."
  exit 0
fi

keychain="ssc-signing.keychain-db"
keychain_password="${MACOS_KEYCHAIN_PASSWORD:-$(openssl rand -hex 16)}"
cert_path="$(mktemp -t ssc-cert).p12"
notarize_zip=""

cleanup() {
  security delete-keychain "${keychain}" >/dev/null 2>&1 || true
  rm -f "${cert_path}"
  rm -f "${notarize_zip}"
}
trap cleanup EXIT

printf '%s' "${MACOS_CERT_P12}" | base64 --decode > "${cert_path}"

security create-keychain -p "${keychain_password}" "${keychain}"
security set-keychain-settings -lut 21600 "${keychain}"
security unlock-keychain -p "${keychain_password}" "${keychain}"
security import "${cert_path}" -k "${keychain}" -P "${MACOS_CERT_PASSWORD:-}" -T /usr/bin/codesign
security set-key-partition-list -S apple-tool:,apple:,codesign: -s -k "${keychain_password}" "${keychain}" >/dev/null
security list-keychains -d user -s "${keychain}" $(security list-keychains -d user | tr -d '"')

identity="$(security find-identity -v -p codesigning "${keychain}" | sed -n 's/.*"\(.*\)"/\1/p' | head -1)"
if [[ -z "${identity}" ]]; then
  echo "No Developer ID identity found in imported certificate." >&2
  exit 1
fi

has_notary_credentials=false
if [[ -n "${APPLE_ID:-}" && -n "${APPLE_TEAM_ID:-}" && -n "${APPLE_APP_SPECIFIC_PASSWORD:-}" ]]; then
  has_notary_credentials=true
fi

notarize() {
  local target="$1"
  echo "Submitting ${target} for notarization"
  xcrun notarytool submit "${target}" \
    --apple-id "${APPLE_ID}" \
    --team-id "${APPLE_TEAM_ID}" \
    --password "${APPLE_APP_SPECIFIC_PASSWORD}" \
    --wait
}

if [[ -n "${dmg_path}" ]]; then
  if [[ ! -f "${dmg_path}" ]]; then
    echo "DMG not found: ${dmg_path}" >&2
    exit 1
  fi

  echo "Signing DMG with ${identity}"
  codesign --force --timestamp --sign "${identity}" "${dmg_path}"

  if [[ "${has_notary_credentials}" == true ]]; then
    notarize "${dmg_path}"
    xcrun stapler staple "${dmg_path}"
  else
    echo "Apple notarization credentials not set; DMG is signed but not notarized."
  fi
else
  echo "Signing app with ${identity}"
  codesign --force --deep --options runtime --timestamp --sign "${identity}" "${app_path}"
  codesign --verify --deep --strict --verbose=2 "${app_path}"

  if [[ "${has_notary_credentials}" == true ]]; then
    notarize_zip="$(mktemp -d)/$(basename "${app_path}" .app).zip"
    ditto -c -k --keepParent "${app_path}" "${notarize_zip}"
    notarize "${notarize_zip}"
    xcrun stapler staple "${app_path}"
  else
    echo "Apple notarization credentials not set; app is signed but not notarized."
  fi
fi

echo "Signing complete"
