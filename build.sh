#!/bin/sh
# Build with Docker (on the NAS or any machine with Docker), no .NET SDK needed.
# Usage:   sh build.sh
# Result:  ./dist/IndexJelly_<version>/  (copy this folder into the Jellyfin plugins directory)
# The version is read from the csproj, so it only has to be bumped there (and in meta.json).
set -e
cd "$(dirname "$0")"
VERSION=$(sed -n 's:.*<AssemblyVersion>\(.*\)</AssemblyVersion>.*:\1:p' Jellyfin.Plugin.IndexJelly.csproj)

if command -v dotnet >/dev/null 2>&1; then
  dotnet publish Jellyfin.Plugin.IndexJelly.csproj -c Release -o bin/publish
else
  docker run --rm -v "$PWD":/src -w /src mcr.microsoft.com/dotnet/sdk:10.0 \
    dotnet publish Jellyfin.Plugin.IndexJelly.csproj -c Release -o /src/bin/publish
fi

OUT=dist/IndexJelly_$VERSION
rm -rf "$OUT" && mkdir -p "$OUT"
cp bin/publish/Jellyfin.Plugin.IndexJelly.dll "$OUT"/
cp meta.json "$OUT"/
cp assets/thumb.png "$OUT"/
echo "Done: $OUT"
ls -l "$OUT"
