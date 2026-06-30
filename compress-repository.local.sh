#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd -- "$(dirname -- "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$SCRIPT_DIR"
REPO_NAME="$(basename "$REPO_ROOT")"
OUTPUT_ZIP="${1:-$REPO_ROOT/${REPO_NAME}.zip}"

if [[ "$OUTPUT_ZIP" != /* ]]; then
    OUTPUT_ZIP="$(pwd)/$OUTPUT_ZIP"
fi

if ! command -v rsync >/dev/null 2>&1; then
    echo "Error: rsync is required." >&2
    exit 1
fi

if command -v zip >/dev/null 2>&1; then
    ZIP_TOOL="zip"
elif command -v 7z >/dev/null 2>&1; then
    ZIP_TOOL="7z"
elif command -v 7zz >/dev/null 2>&1; then
    ZIP_TOOL="7zz"
elif command -v bsdtar >/dev/null 2>&1; then
    ZIP_TOOL="bsdtar"
else
    echo "Error: zip, 7z, 7zz, or bsdtar is required." >&2
    exit 1
fi

mkdir -p -- "$(dirname -- "$OUTPUT_ZIP")"

if [[ -e "$OUTPUT_ZIP" ]]; then
    rm -f -- "$OUTPUT_ZIP"
fi

TMP_DIR="$(mktemp -d)"
cleanup() {
    rm -rf -- "$TMP_DIR"
}
trap cleanup EXIT

STAGE_DIR="$TMP_DIR/$REPO_NAME"
mkdir -p -- "$STAGE_DIR"

rsync -a --delete \
    --filter=':- .gitignore' \
    --exclude='.git/' \
    --exclude='*.zip' \
    --exclude='bin/' \
    --exclude='obj/' \
    --exclude='Assets/' \
    "$REPO_ROOT/" "$STAGE_DIR/"

find "$STAGE_DIR" -type f -ipath '*/Content/*' -iname '*.dds' -delete
find "$STAGE_DIR" -depth -type d -empty -delete

(
    cd "$TMP_DIR"
    case "$ZIP_TOOL" in
        zip)
            zip -qr "$OUTPUT_ZIP" "$REPO_NAME"
            ;;
        7z|7zz)
            "$ZIP_TOOL" a -tzip "$OUTPUT_ZIP" "$REPO_NAME" >/dev/null
            ;;
        bsdtar)
            bsdtar -a -cf "$OUTPUT_ZIP" "$REPO_NAME"
            ;;
    esac
)

echo "Created: $OUTPUT_ZIP"
