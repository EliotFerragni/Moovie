#!/usr/bin/env bash
#
# Builds a Docker image and writes it out as a file you can carry to another machine.
#
#   ./build/package-docker.sh              # for this machine's architecture
#   ./build/package-docker.sh arm64        # for an ARM NAS
#
# The result lands in dist/ as a gzipped tarball. On the target machine:
#
#   docker load -i moovie-<version>-<arch>.docker.tar.gz
#
# No registry is involved and the target never needs the source or the .NET SDK.
set -euo pipefail

cd "$(dirname "$0")/.."

case "${1:-$(uname -m)}" in
  amd64|x86_64) ARCH=amd64; RUNTIME=linux-x64 ;;
  arm64|aarch64) ARCH=arm64; RUNTIME=linux-arm64 ;;
  *) echo "Unknown architecture '${1:-$(uname -m)}'. Use amd64 or arm64." >&2; exit 1 ;;
esac

VERSION="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
VERSION="${VERSION:-0.0.0}"
IMAGE="moovie:$VERSION"
OUTPUT="dist/moovie-$VERSION-$ARCH.docker.tar.gz"

echo "==> publishing $RUNTIME"
./build/publish.sh "$RUNTIME"

echo "==> building $IMAGE for linux/$ARCH"
# The context is the published output, which is one file. Nothing else is sent to the builder.
# Some Docker setups cannot resolve DNS inside a build container. DOCKER_BUILD_NETWORK=host is
# the way out of that; it is not needed normally.
docker buildx build \
  ${DOCKER_BUILD_NETWORK:+--network="$DOCKER_BUILD_NETWORK"} \
  --platform "linux/$ARCH" \
  --tag "$IMAGE" \
  --file docker/Dockerfile \
  --load \
  "artifacts/$RUNTIME"

mkdir -p dist
echo "==> writing $OUTPUT"
docker save "$IMAGE" | gzip > "$OUTPUT"

cat <<NOTE

Done: $OUTPUT ($(du -h "$OUTPUT" | cut -f1))

Copy it to the NAS, then:

    docker load -i $(basename "$OUTPUT")
    docker run -d --name moovie \\
      -p 8080:8080 \\
      -v /volume1/media:/media \\
      -v /volume1/docker/moovie:/config \\
      --user "\$(id -u):\$(id -g)" \\
      $IMAGE

Then open http://<the NAS>:8080/. The files it edits are the NAS's own.
NOTE
