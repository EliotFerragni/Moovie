#!/usr/bin/env bash
#
# Fails when a release tag does not match the version the repo declares. Nothing else ties the
# two together: CI uses the tag only as somewhere to upload to, so without this a release tagged
# 1.1.0 built from a tree still saying 1.0.0 would ship binaries, an About box and a macOS
# bundle all labelled 1.0.0, and nothing would complain.
#
#   ./build/check-version.sh 1.1.0
set -euo pipefail

cd "$(dirname "$0")/.."

tag="${1:-}"
if [[ -z "$tag" ]]; then
  echo "usage: build/check-version.sh <tag>" >&2
  exit 2
fi

declared="$(sed -n 's:.*<Version>\(.*\)</Version>.*:\1:p' Directory.Build.props | head -1)"
if [[ -z "$declared" ]]; then
  echo "No <Version> found in Directory.Build.props." >&2
  exit 1
fi

# Tags get written either 1.2.3 or v1.2.3; both name the same release.
if [[ "${tag#v}" != "$declared" ]]; then
  cat >&2 <<EOF
Release tag and declared version disagree:

  tag                    $tag
  Directory.Build.props  $declared

The build would be labelled $declared while the release is called $tag. Bump <Version> in
Directory.Build.props, commit it, and point the tag at that commit.
EOF
  exit 1
fi

echo "version $declared matches tag $tag"
