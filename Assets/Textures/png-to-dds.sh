#!/usr/bin/env bash
TOP="$(pwd)"
TEXCONV_CMD="../texconv.exe"

find . -type d ! -path . -print0 |
while IFS= read -r -d '' dir; do
  if find "$dir" -maxdepth 1 -type f \( -iname '*.png' \) -print -quit | grep -q .; then
    printf 'Processing: %s\n' "$dir"
    ( cd "$dir" || exit 0
      set -- ./*.png
      if [ -e "$1" ]; then
        $TEXCONV_CMD ./*.png -nologo -y -f BC7_UNORM_SRGB -pmalpha 2>/dev/null || true
      fi
    )

    find "$dir" -maxdepth 1 -type f -name '*.DDS' -print0 |
    while IFS= read -r -d '' src; do
      base="$(basename "$src" .DDS)"
      dst="$TOP/${base}.dds"
      if [ -e "$dst" ]; then
        i=1
        while [ -e "${dst%.*}-$i.${dst##*.}" ]; do i=$((i+1)); done
        dst="${dst%.*}-$i.${dst##*.}"
      fi
      mv -- "$src" "$dst"
      printf 'Moved: %s -> %s\n' "$src" "$dst"
    done
  fi
done
