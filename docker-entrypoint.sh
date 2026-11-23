#!/bin/sh
set -eu
mkdir -p /data/uploads /data/genres
cp -rn /app/media-seed/uploads/. /data/uploads/
cp -rn /app/media-seed/genres/. /data/genres/
exec dotnet ComicApi.dll
