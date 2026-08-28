#!/usr/bin/env bash
#
# Packages whatever build/publish.sh left in artifacts/ into one archive per target, under
# dist/. Used by CI to attach binaries to a GitHub release; run it by hand to produce the
# same files locally.
#
#   ./build/publish.sh all && ./build/package.sh
set -euo pipefail

cd "$(dirname "$0")/.."

if [[ ! -d artifacts ]]; then
  echo "Nothing to package: run build/publish.sh first." >&2
  exit 1
fi

rm -rf dist
mkdir -p dist

shopt -s nullglob
for dir in artifacts/*/; do
  runtime="$(basename "$dir")"

  case "$runtime" in
    linux-*)
      # tar keeps the executable bit. A plain zip does not on every extractor, and a Linux
      # binary that arrives without +x looks broken.
      archive="dist/VideoMetadataFiller-$runtime.tar.gz"
      tar -czf "$archive" -C artifacts "$runtime"
      ;;
    *)
      # Info-ZIP stores Unix permissions, so the macOS .app stays launchable. This is also why
      # releases carry these archives rather than CI artifacts, which are rezipped without them.
      archive="dist/VideoMetadataFiller-$runtime.zip"
      (cd artifacts && zip -qr "../$archive" "$runtime")
      ;;
  esac

  echo "    packaged $archive"
done

if [[ -z "$(ls -A dist)" ]]; then
  echo "artifacts/ held no target directories." >&2
  exit 1
fi
