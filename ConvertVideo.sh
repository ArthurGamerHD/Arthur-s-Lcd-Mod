#!/usr/bin/env bash
set -euo pipefail

usage() {
  cat >&2 <<EOF
Usage: $0 [options] <input> [output.mkv]

Converts any video ffmpeg can read into the Media Player's format:
Matroska with MPEG-1 video, MS-ADPCM audio and optional SRT subtitles.

Options:
  -w <width>    Maximum width (default 1024; smaller videos are not upscaled)
  -f <fps>      Frame rate (default 60)
  -q <1-31>     Video quality, lower is better and larger (default 4)
  -s <file>     Subtitle file (.srt/.vtt/.ass) to embed; defaults to <input>.srt
                or the first text subtitle track inside the input
  -n            No subtitles
EOF
  exit 1
}

WIDTH=1024
FPS=30
QUALITY=4
SUBTITLES=""
NO_SUBTITLES=0

while getopts "w:f:q:s:nh" opt; do
  case "$opt" in
    w) WIDTH="$OPTARG" ;;
    f) FPS="$OPTARG" ;;
    q) QUALITY="$OPTARG" ;;
    s) SUBTITLES="$OPTARG" ;;
    n) NO_SUBTITLES=1 ;;
    *) usage ;;
  esac
done
shift $((OPTIND - 1))

[ $# -ge 1 ] && [ $# -le 2 ] || usage

INPUT="$1"
OUTPUT="${2:-${INPUT%.*}.mkv}"

for tool in ffmpeg ffprobe; do
  command -v "$tool" >/dev/null || { echo "Error: $tool is not installed" >&2; exit 1; }
done

[ -f "$INPUT" ] || { echo "Error: $INPUT not found" >&2; exit 1; }

if [ "$(realpath -m "$INPUT")" = "$(realpath -m "$OUTPUT")" ]; then
  OUTPUT="${INPUT%.*}.lcd.mkv"
fi

ARGS=(-hide_banner -y -i "$INPUT")
MAPS=(-map 0:v:0)

if [ -n "$(ffprobe -v error -select_streams a -show_entries stream=index -of csv=p=0 "$INPUT")" ]; then
  MAPS+=(-map 0:a:0)
fi

if [ "$NO_SUBTITLES" -eq 0 ]; then
  if [ -z "$SUBTITLES" ] && [ -f "${INPUT%.*}.srt" ]; then
    SUBTITLES="${INPUT%.*}.srt"
  fi

  if [ -n "$SUBTITLES" ]; then
    [ -f "$SUBTITLES" ] || { echo "Error: $SUBTITLES not found" >&2; exit 1; }
    ARGS+=(-i "$SUBTITLES")
    MAPS+=(-map 1:s:0)
  else
    TEXT_TRACK="$(ffprobe -v error -select_streams s \
      -show_entries stream=codec_name -of csv=p=0 "$INPUT" |
      grep -nE '^(subrip|srt|ass|ssa|webvtt|mov_text|text)$' | head -n1 | cut -d: -f1 || true)"
    if [ -n "$TEXT_TRACK" ]; then
      MAPS+=(-map "0:s:$((TEXT_TRACK - 1))")
    fi
  fi
fi

ffmpeg "${ARGS[@]}" "${MAPS[@]}" \
  -vf "scale='min($WIDTH,iw)':-2:flags=lanczos,fps=$FPS,format=yuv420p" \
  -c:v mpeg1video -strict unofficial -q:v "$QUALITY" -bf 0 -g $((FPS * 3)) \
  -c:a adpcm_ms -ar 24000 -ac 1 \
  -c:s srt \
  -map_metadata -1 -map_chapters -1 \
  "$OUTPUT"

echo "Wrote $OUTPUT"
