#!/usr/bin/env bash

set -u

TOP="$(pwd -P)"
SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd -P)"

# Find texconv without depending on the directory currently being scanned.
# Priority:
#   1. TEXCONV_CMD environment variable
#   2. texconv.exe / texconv on PATH
#   3. texconv.exe next to this script
if [[ -n "${TEXCONV_CMD:-}" ]]; then
  TEXCONV="$TEXCONV_CMD"
elif command -v texconv.exe >/dev/null 2>&1; then
  TEXCONV="$(command -v texconv.exe)"
elif command -v texconv >/dev/null 2>&1; then
  TEXCONV="$(command -v texconv)"
elif [[ -f "$SCRIPT_DIR/texconv.exe" ]]; then
  TEXCONV="$SCRIPT_DIR/texconv.exe"
else
  printf 'Error: texconv was not found. Put texconv.exe next to this script, add it to PATH, or set TEXCONV_CMD.\n' >&2
  exit 1
fi

move_dds() {
  local src="$1"
  local base dst i

  base="$(basename "$src")"
  base="${base%.*}"
  dst="$TOP/${base}.dds"

  if [[ -e "$dst" ]]; then
    i=1
    while [[ -e "$TOP/${base}-$i.dds" ]]; do
      ((i++))
    done
    dst="$TOP/${base}-$i.dds"
  fi

  mv -- "$src" "$dst"
  printf 'Moved: %s -> %s\n' "$src" "$dst"
}

process_png() {
  local input="$1"
  local dir name abs_input output

  dir="$(cd -- "$(dirname -- "$input")" 2>/dev/null && pwd -P)" || {
    printf 'Error: cannot access: %s\n' "$input" >&2
    return 1
  }
  name="$(basename -- "$input")"
  abs_input="$dir/$name"

  if [[ ! -f "$abs_input" ]]; then
    printf 'Error: file not found: %s\n' "$input" >&2
    return 1
  fi

  case "${name,,}" in
    *.png) ;;
    *)
      printf 'Error: not a PNG file: %s\n' "$input" >&2
      return 1
      ;;
  esac

  printf 'Processing: %s\n' "$abs_input"

  # Run texconv from the image directory and pass only the basename.
  # This avoids sending Unix absolute paths (/home/..., /tmp/...) to the
  # Windows texconv.exe command-line parser while keeping TEXCONV itself
  if ! (
    cd -- "$dir" || exit 1
    "$TEXCONV" "$name" -nologo -y -f BC7_UNORM -pmalpha
  ); then
    printf 'Conversion failed: %s\n' "$abs_input" >&2
    return 1
  fi

  output=""
  if [[ -f "$dir/${name%.*}.DDS" ]]; then
    output="$dir/${name%.*}.DDS"
  elif [[ -f "$dir/${name%.*}.dds" ]]; then
    output="$dir/${name%.*}.dds"
  fi

  if [[ -z "$output" ]]; then
    printf 'Error: texconv produced no DDS for: %s\n' "$abs_input" >&2
    return 1
  fi

  move_dds "$output"
}

if (( $# > 1 )); then
  printf 'Usage: %s [file.png]\n' "$(basename -- "$0")" >&2
  exit 2
fi

if (( $# == 1 )); then
  # One argument: convert only that file.
  process_png "$1"
else
  # No argument: recursively convert every PNG below the current directory.
  while IFS= read -r -d '' file; do
    process_png "$file" || true
  done < <(find . -type f -iname '*.png' -print0)
fi
