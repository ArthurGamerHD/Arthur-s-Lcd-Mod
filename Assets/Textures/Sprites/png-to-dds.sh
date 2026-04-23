#!/usr/bin/env bash
set -euo pipefail

for f in *.png; do
  [ -e "$f" ] || continue
  out="${f%.*}.dds"

  magick "$f" \
    -define dds:compression=none \
    "$out"
done
