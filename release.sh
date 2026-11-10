#!/bin/sh
# Builds a release by hand: the plugin zip, its release notes, and the new version in manifest.json.
# Usage:  sh release.sh
# Then:   upload dist/indexjelly_<version>.zip to a GitHub Release tagged v<version>, commit manifest.json.
set -e
cd "$(dirname "$0")"

VERSION=$(sed -n 's:.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*:\1:p' Jellyfin.Plugin.IndexJelly.csproj)
TAG="v${VERSION%.0}"                       # 1.5.0.0 -> v1.5.0
ZIP="indexjelly_$VERSION.zip"
REPO="burgatshow/indexjelly"

if command -v dotnet >/dev/null 2>&1; then
  dotnet publish Jellyfin.Plugin.IndexJelly.csproj -c Release -o bin/publish
else
  docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
    dotnet publish Jellyfin.Plugin.IndexJelly.csproj -c Release -o /src/bin/publish
fi

mkdir -p dist
python3 scripts/release.py package --version "$VERSION" --dll bin/publish/Jellyfin.Plugin.IndexJelly.dll --out "dist/$ZIP"
python3 scripts/release.py notes --version "$VERSION" > dist/release-notes.md
python3 scripts/release.py manifest --version "$VERSION" --zip "dist/$ZIP" \
  --url "https://github.com/$REPO/releases/download/$TAG/$ZIP"

echo
echo "Ready: dist/$ZIP"
echo "1. GitHub → Releases → Draft a new release, tag $TAG, title 'IndexJelly $TAG',"
echo "   notes from dist/release-notes.md, attach dist/$ZIP, publish."
echo "2. git add manifest.json && git commit -m 'manifest: add $VERSION' && git push"
