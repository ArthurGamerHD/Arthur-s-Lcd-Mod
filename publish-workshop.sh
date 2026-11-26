#!/usr/bin/env bash
# Uploads the MDK-packed mod folder to its Steam Workshop item with steamcmd (an update of an existing item: title,
# description, tags and visibility are left as they are on the Workshop).
#
# usage: publish-workshop.sh [options]
#   -b, --build          build the project in Release first (MDK packs and deploys the mod folder)
#   -m, --message TEXT   change note (default: the subject of the "Release <Version>" commit, else
#                        "<Version>: <subject of HEAD>")
#   -n, --dry-run        stage the upload and print the item file; send nothing
#       --preview        also replace the Workshop preview image with the folder's thumb.png
#       --allow-dirty    publish while the repository has uncommitted changes
#       --folder DIR     publish this mod folder instead of the one MDK builds for the current branch
#       --id ID          publish to this Workshop item instead of the one in the folder's modinfo.sbmi
#       --project FILE   the mod's .csproj (default: the one this copy of the script is for)
#
# The folder is the one MDK deploys to: mdk.local.ini's output (default ~/.config/SpaceEngineers/Mods), named by the
# project, or by mdk.ini's [mdk-branch:<current branch>] pattern. Its modinfo.sbmi (written by the game when the item
# was first published) names the Workshop item; modinfo.sbmi itself is not uploaded.
#
# Configuration, from the environment or the untracked workshop.local.ini next to this script:
#   steam_user = <Steam account name>     (STEAM_USER)
#   steamcmd   = <path to steamcmd>       (STEAMCMD; default: steamcmd on PATH, or ~/steamcmd/steamcmd.sh)
# The first run asks for the password and Steam Guard code; steamcmd keeps the login for later runs.
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
DEFAULT_PROJECT="$SCRIPT_DIR/Arthur-s-Lcd-Mod/Arthur-s-Lcd-Mod.csproj"
APP_ID=244850

die() { echo "publish-workshop: $*" >&2; exit 1; }

BUILD=0 DRY_RUN=0 PREVIEW=0 ALLOW_DIRTY=0
MESSAGE="" FOLDER="" ITEM_ID="" PROJECT="$DEFAULT_PROJECT"
while [ $# -gt 0 ]; do
  case "$1" in
    -b|--build) BUILD=1 ;;
    -m|--message) [ $# -ge 2 ] || die "$1 needs a value"; MESSAGE="$2"; shift ;;
    -n|--dry-run) DRY_RUN=1 ;;
    --preview) PREVIEW=1 ;;
    --allow-dirty) ALLOW_DIRTY=1 ;;
    --folder) [ $# -ge 2 ] || die "$1 needs a value"; FOLDER="$2"; shift ;;
    --id) [ $# -ge 2 ] || die "$1 needs a value"; ITEM_ID="$2"; shift ;;
    --project) [ $# -ge 2 ] || die "$1 needs a value"; PROJECT="$2"; shift ;;
    -h|--help) sed -n '2,/^set -euo/p' "${BASH_SOURCE[0]}" | sed '$d; s/^# \{0,1\}//'; exit 0 ;;
    *) die "unknown option '$1' (see --help)" ;;
  esac
  shift
done

[ -f "$PROJECT" ] || die "no project at $PROJECT"
PROJECT="$(cd "$(dirname "$PROJECT")" && pwd)/$(basename "$PROJECT")"
PROJECT_DIR="$(dirname "$PROJECT")"
PROJECT_NAME="$(basename "$PROJECT" .csproj)"

# Reads "key = value" from an ini file; with a section, only inside [section].
ini_value() {
  local file="$1" key="$2" section="${3:-}"
  [ -f "$file" ] || return 0
  awk -v key="$key" -v section="$section" '
    /^[[:space:]]*[;#]/ { next }
    /^[[:space:]]*\[/ { current = $0; gsub(/^[[:space:]]*\[|\][[:space:]]*$/, "", current); next }
    section != "" && current != section { next }
    {
      line = $0
      eq = index(line, "=")
      if (eq == 0) next
      name = substr(line, 1, eq - 1); value = substr(line, eq + 1)
      gsub(/^[[:space:]]+|[[:space:]]+$/, "", name); gsub(/^[[:space:]]+|[[:space:]]+$/, "", value)
      if (name == key) { print value; exit }
    }' "$file"
}

LOCAL_CONFIG="$SCRIPT_DIR/workshop.local.ini"
STEAM_USER="${STEAM_USER:-$(ini_value "$LOCAL_CONFIG" steam_user)}"
STEAMCMD="${STEAMCMD:-$(ini_value "$LOCAL_CONFIG" steamcmd)}"
STEAMCMD="${STEAMCMD/#\~/$HOME}"

REPO="$(git -C "$PROJECT_DIR" rev-parse --show-toplevel)"
BRANCH="$(git -C "$REPO" rev-parse --abbrev-ref HEAD)"
if [ $ALLOW_DIRTY -eq 0 ] && [ -n "$(git -C "$REPO" status --porcelain)" ]; then
  die "$REPO has uncommitted changes; commit them, or pass --allow-dirty"
fi

VERSION="$(grep -o '<Version>[^<]*</Version>' "$PROJECT_DIR/Directory.Build.props" "$PROJECT" 2>/dev/null |
  head -1 | sed 's/.*<Version>\(.*\)<\/Version>.*/\1/')"

if [ -z "$FOLDER" ]; then
  OUTPUT="$(ini_value "$PROJECT_DIR/mdk.local.ini" output mdk)"
  OUTPUT="${OUTPUT:-$HOME/.config/SpaceEngineers/Mods}"
  case "$OUTPUT" in /*) ;; *) OUTPUT="$PROJECT_DIR/$OUTPUT" ;; esac
  NAME="$(ini_value "$PROJECT_DIR/mdk.ini" pattern "mdk-branch:$BRANCH")"
  NAME="${NAME:-\$MDK_PROJECT\$}"
  NAME="${NAME//\$MDK_PROJECT\$/$PROJECT_NAME}"
  NAME="${NAME//\$MDK_BRANCH\$/$BRANCH}"
  case "$NAME" in *'$MDK_'*) die "mdk.ini's pattern '$NAME' uses a macro this script does not expand; pass --folder" ;; esac
  FOLDER="$OUTPUT/$NAME"
fi

if [ $BUILD -eq 1 ]; then
  echo "Building $PROJECT_NAME (Release)..."
  dotnet build "$PROJECT" -c Release -nologo -v q | grep -E ' error |Build succeeded' | sort -u || true
  [ "${PIPESTATUS[0]}" -eq 0 ] || die "the build failed"
fi

[ -d "$FOLDER" ] || die "no mod folder at $FOLDER (build first, or pass --folder)"
[ -d "$FOLDER/Data" ] || die "$FOLDER has no Data folder; it does not look like a packed mod"

if [ -z "$ITEM_ID" ]; then
  [ -f "$FOLDER/modinfo.sbmi" ] || die "$FOLDER has no modinfo.sbmi; pass --id with the Workshop item"
  ITEM_ID="$(tr -d '\r\n' < "$FOLDER/modinfo.sbmi" |
    grep -o '<WorkshopId>[[:space:]]*<Id>[0-9]*</Id>[[:space:]]*<ServiceName>Steam</ServiceName>' |
    grep -o '<Id>[0-9]*</Id>' | head -1 | tr -dc '0-9')"
  [ -n "$ITEM_ID" ] && [ "$ITEM_ID" != 0 ] || die "$FOLDER/modinfo.sbmi names no Steam Workshop item; pass --id"
fi

if [ -z "$MESSAGE" ]; then
  # A version's release commit says what the version is; HEAD may be an unrelated later commit.
  [ -z "$VERSION" ] || MESSAGE="$(git -C "$REPO" log -1 --format=%s --grep="^Release $VERSION\b" -E)"
  [ -n "$MESSAGE" ] || MESSAGE="$(git -C "$REPO" log -1 --format=%s)"
  [ -z "$VERSION" ] || [[ "$MESSAGE" == "Release $VERSION"* ]] || MESSAGE="$VERSION: $MESSAGE"
fi

STAGE="$(mktemp -d "${TMPDIR:-/tmp}/publish-workshop.XXXXXX")"
trap 'rm -rf "${STAGE:?}"' EXIT
rsync -a --exclude 'modinfo.sbmi' "$FOLDER/" "$STAGE/content/"

vdf_escape() { printf '%s' "$1" | sed 's/\\/\\\\/g; s/"/\\"/g'; }
{
  echo '"workshopitem"'
  echo '{'
  echo "  \"appid\" \"$APP_ID\""
  echo "  \"publishedfileid\" \"$ITEM_ID\""
  echo "  \"contentfolder\" \"$(vdf_escape "$STAGE/content")\""
  echo "  \"changenote\" \"$(vdf_escape "$MESSAGE")\""
  if [ $PREVIEW -eq 1 ]; then
    [ -f "$FOLDER/thumb.png" ] || die "--preview: $FOLDER has no thumb.png"
    cp "$FOLDER/thumb.png" "$STAGE/preview.png"
    echo "  \"previewfile\" \"$(vdf_escape "$STAGE/preview.png")\""
  fi
  echo '}'
} > "$STAGE/item.vdf"

FILES="$(find "$STAGE/content" -type f | wc -l)"
SIZE="$(du -sh "$STAGE/content" | cut -f1)"
echo "Item:    https://steamcommunity.com/sharedfiles/filedetails/?id=$ITEM_ID"
echo "Folder:  $FOLDER ($FILES files, $SIZE)"
echo "Branch:  $BRANCH @ $(git -C "$REPO" rev-parse --short HEAD)"
echo "Note:    $MESSAGE"

if [ $DRY_RUN -eq 1 ]; then
  echo "Dry run, item file:"
  sed 's/^/  /' "$STAGE/item.vdf"
  exit 0
fi

if [ -z "$STEAMCMD" ]; then
  STEAMCMD="$(command -v steamcmd || true)"
  [ -n "$STEAMCMD" ] || [ ! -x "$HOME/steamcmd/steamcmd.sh" ] || STEAMCMD="$HOME/steamcmd/steamcmd.sh"
fi
[ -n "$STEAMCMD" ] && [ -x "$STEAMCMD" ] || die "steamcmd not found. Install it (Arch: the AUR 'steamcmd' package; or
  mkdir -p ~/steamcmd && curl -sSL https://steamcdn-a.akamaihd.net/client/installer/steamcmd_linux.tar.gz | tar -xz -C ~/steamcmd)
and/or set 'steamcmd = <path>' in $LOCAL_CONFIG"
[ -n "$STEAM_USER" ] || die "no Steam account: set 'steam_user = <name>' in $LOCAL_CONFIG (or STEAM_USER)"

# steamcmd's exit code does not reflect a failed upload; its output does.
# Its result line can come on stderr, and color codes surround it.
"$STEAMCMD" +login "$STEAM_USER" +workshop_build_item "$STAGE/item.vdf" +quit 2>&1 | tee "$STAGE/steamcmd.log"
if grep -q 'Committing update.*Success\.' "$STAGE/steamcmd.log" && ! grep -q 'ERROR!' "$STAGE/steamcmd.log"; then
  echo "Published $ITEM_ID."
else
  die "steamcmd did not report success (see its output above)"
fi
