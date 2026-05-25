#!/usr/bin/env bash
set -euo pipefail

INI_FILE="./Arthur-s-Lcd-Mod/mdk.local.ini"
OUT="./SE.Source/"

# read binarypath from [mdk] section
MANAGED="$(awk -F= '
  /^\[mdk\]/ { in_mdk=1; next }
  /^\[/ && in_mdk { in_mdk=0 }
  in_mdk && $1 ~ /binarypath[ \t]*/ {
    gsub(/^[ \t]+|[ \t]+$/,"",$2); print $2; exit
  }
' "$INI_FILE")"

if [ -z "$MANAGED" ]; then
  echo "Error: binarypath not found in $INI_FILE" >&2
  exit 1
fi

mkdir -p "$OUT"

for f in "$MANAGED"/*.dll; do
  [ -e "$f" ] || continue
  if ! ilspycmd -p --nested-directories -r "$MANAGED" -o "$OUT" "$f"; then
    echo "Warning: failed to decompile $f — skipping" >&2
  fi
done
