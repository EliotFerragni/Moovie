#!/usr/bin/env bash
#
# Publishes a self-contained, single-file build of Moovie.
#
#   ./build/publish.sh                  # build for the machine you are on
#   ./build/publish.sh win-x64          # build for one target
#   ./build/publish.sh all              # build every target
#
# Output lands in artifacts/<runtime>/. Each build bundles the .NET runtime, so the
# result needs no installation and no .NET on the target machine.
#
# Every target cross-compiles from every host: the runtime identifier only selects
# which runtime pack is restored from NuGet, and the .app bundle below is assembled
# with mkdir/sed/mv. CI therefore builds all six targets on a Linux runner, which
# bills at a sixth of a macOS one. Codesigning and notarization are the only steps
# that would genuinely need a Mac, and these builds are deliberately unsigned.
set -euo pipefail

cd "$(dirname "$0")/.."

PROJECT="src/Moovie.App/Moovie.App.csproj"

# Directory.Build.props is the one place the version lives; the bundle borrows it from there.
VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
VERSION="${VERSION:-0.0.0}"
ALL_TARGETS=(win-x64 win-arm64 osx-x64 osx-arm64 linux-x64 linux-arm64)

detect_host() {
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64)  echo osx-arm64 ;;
    Darwin-x86_64) echo osx-x64 ;;
    Linux-aarch64) echo linux-arm64 ;;
    Linux-x86_64)  echo linux-x64 ;;
    MINGW*|MSYS*|CYGWIN*) echo win-x64 ;;
    *) echo "Unrecognised host platform; pass a runtime identifier explicitly." >&2; exit 1 ;;
  esac
}

case "${1:-}" in
  all) TARGETS=("${ALL_TARGETS[@]}") ;;
  "")  TARGETS=("$(detect_host)") ;;
  *)   TARGETS=("$1") ;;
esac

for runtime in "${TARGETS[@]}"; do
  echo "==> publishing $runtime"
  output="artifacts/$runtime"
  rm -rf "$output"

  dotnet publish "$PROJECT" \
    --configuration Release \
    --runtime "$runtime" \
    --self-contained true \
    --output "$output" \
    -p:PublishSingleFile=true \
    -p:IncludeNativeLibrariesForSelfExtract=true \
    -p:EnableCompressionInSingleFile=true \
    -p:DebugType=none \
    -p:SatelliteResourceLanguages=en

  # macOS wants an .app bundle to be treated as an application rather than a bare binary.
  if [[ "$runtime" == osx-* ]]; then
    bundle="$output/Moovie.app"
    mkdir -p "$bundle/Contents/MacOS" "$bundle/Contents/Resources"
    # Move everything, not just the executable. .NET 10 embeds the Skia, HarfBuzz and
    # Avalonia natives into the single file, but .NET 9 published them beside it, and
    # a bundle that leaves them behind breaks the moment it is moved on its own.
    find "$output" -maxdepth 1 -type f -exec mv {} "$bundle/Contents/MacOS/" \;
    chmod +x "$bundle/Contents/MacOS/Moovie"
    cp src/Moovie.App/Assets/Icon/app.icns "$bundle/Contents/Resources/"
    sed -e "s/__RUNTIME__/$runtime/" -e "s/__VERSION__/$VERSION/" \
      build/Info.plist.template > "$bundle/Contents/Info.plist"
    echo "    bundled as $bundle"
  fi

  echo "    done: $output"
done

cat <<'NOTE'

Note for macOS: these builds are unsigned, so Gatekeeper will refuse to open them
until the quarantine flag is cleared:

    xattr -dr com.apple.quarantine "Moovie.app"

NOTE
