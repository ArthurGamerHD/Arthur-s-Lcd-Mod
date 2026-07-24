#!/usr/bin/env bash
set -euo pipefail

INI_FILE="./Arthur-s-Lcd-Mod/mdk.local.ini"
LOCAL_SOURCE_LINK="./SE.Source"

# read binarypath from [mdk] section
MANAGED="$(awk -F= '
  /^\[mdk\]/ { in_mdk=1; next }
  /^\[/ && in_mdk { in_mdk=0 }
  in_mdk && $1 ~ /binarypath[ \t]*/ {
    gsub(/^[ \t]+|[ \t\r]+$/,"",$2); print $2; exit
  }
' "$INI_FILE")"

if [ -z "$MANAGED" ]; then
  echo "Error: binarypath not found in $INI_FILE" >&2
  exit 1
fi

OUT="${MANAGED%/}/../SE.Source"

mkdir -p "$OUT"

if [ -L "$LOCAL_SOURCE_LINK" ]; then
  ln -sfn "$OUT" "$LOCAL_SOURCE_LINK"
elif [ ! -e "$LOCAL_SOURCE_LINK" ]; then
  ln -s "$OUT" "$LOCAL_SOURCE_LINK"
else
  echo "Warning: $LOCAL_SOURCE_LINK already exists and is not a symlink; leaving it unchanged" >&2
fi

for f in "$MANAGED"/*.dll; do
  [ -e "$f" ] || continue
  if ! ilspycmd -p --nested-directories -r "$MANAGED" -o "$OUT" "$f"; then
    echo "Warning: failed to decompile $f — skipping" >&2
  fi
done
