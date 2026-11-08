#!/usr/bin/env bash
set -euo pipefail

root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
package_dir="$root/native/mac/SSCMacKit"
output_dir="$root/artifacts/native/osx"

mkdir -p "$output_dir"
cd "$package_dir"

swift build -c release --arch arm64
arm64_binary="$(find .build -type f -name 'libSSCMacKit.dylib' -not -path '*.dSYM*' -path '*arm64*' -print -quit)"
if [[ -z "$arm64_binary" ]]; then
  echo "arm64 dylib not found" >&2
  exit 1
fi

swift build -c release --arch x86_64
x86_binary="$(find .build -type f -name 'libSSCMacKit.dylib' -not -path '*.dSYM*' -path '*x86_64*' -print -quit)"
if [[ -z "$x86_binary" ]]; then
  echo "x86_64 dylib not found" >&2
  exit 1
fi

lipo -create "$arm64_binary" "$x86_binary" -output "$output_dir/libSSCMacKit.dylib"
cp "$package_dir/Sources/SSCMacKitC/include/sscmac.h" "$output_dir/sscmac.h"

echo "Created $output_dir/libSSCMacKit.dylib"
lipo -info "$output_dir/libSSCMacKit.dylib"
