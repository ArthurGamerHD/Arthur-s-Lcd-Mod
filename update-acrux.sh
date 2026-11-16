#!/usr/bin/env bash
# Downloads the Acrux UI SDK from the GitHub releases, checks its signature against the Acrux release key below, and
# only then replaces packages/Acrux.Sdk.*.nupkg and the Acrux.Sdk version in this folder's .csproj files.
#
# usage: ./update-acrux.sh            latest release
#        ./update-acrux.sh 0.0.2      a specific version
# needs: curl, openssl, unzip
set -euo pipefail

REPO="ArthurGamerHD/AcruxUI"
# Acrux release key (RSA-3072). Packages signed with any other key are rejected.
PUBLIC_KEY="-----BEGIN PUBLIC KEY-----
MIIBojANBgkqhkiG9w0BAQEFAAOCAY8AMIIBigKCAYEA+ML+JAGxqLMkP0z50vyy
+pYqHm+zerK4+FcuRdcZxgIyxWTOo9pIrevoBpxeYzkd++bzY4K4pyqj142Liz3z
GYn/CJqr4nyTguBjpLETbTAQdkYCTERyjhRboXHKpANwx3R31hl19LxwMJ8IK1Ik
r7bUE8iGMuWJkcevJWhkd2XC1aB27dCuiTgP7uDlniw3pKpilhQMbhplf60swt/w
5jyHJra7DRBlK0yg97619HteYu3CxWE/6X1xk16i9moD4DU4wW1VNrdlvKqExLEh
pH18W7cJLdPeFnlgN635j2mMfjX/vH6MDkoDqBW9X+758yh67hHmTXsSvcTRB2SE
lmL3AUIUcPXyNt082egyKIPUuGjiLwNv9ZzK8MrQ4N2Usr3CLgG3cfiNNykoP2Dx
656UIXIjpdbi6SZgfi4gs+WttA93bB/CcGu7u+rof5ATqYM1cy3/nD6eLbJv7YxH
ADcA8stQ/0bsiz9MFtQJEd8ED11+7WMGKa7ZNM3NxksvAgMBAAE=
-----END PUBLIC KEY-----"

HERE="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
PACKAGES="$HERE/packages"
for tool in curl openssl unzip; do
  command -v "$tool" > /dev/null || { echo "update-acrux: $tool is required" >&2; exit 1; }
done

VERSION="${1:-}"
if [ -z "$VERSION" ]; then
  VERSION="$(curl -fsSL "https://api.github.com/repos/$REPO/releases/latest" 2> /dev/null |
             sed -n 's/.*"tag_name": *"v\{0,1\}\([^"]*\)".*/\1/p' | head -1)" || true
  [ -n "$VERSION" ] || { echo "update-acrux: no published release found for $REPO (offline, or the repository is not public). Pass a version to try one directly." >&2; exit 1; }
fi
case "$VERSION" in *[!0-9A-Za-z.-]*) echo "update-acrux: bad version '$VERSION'" >&2; exit 1 ;; esac

NAME="Acrux.Sdk.$VERSION.nupkg"
if [ -f "$PACKAGES/$NAME" ]; then
  echo "Acrux SDK $VERSION is already installed."
  exit 0
fi

TMP="$(mktemp -d)"
trap 'rm -f "${TMP:?}/$NAME" "${TMP:?}/$NAME.sig" "${TMP:?}/acrux.pub"; rmdir "${TMP:?}"' EXIT
# ACRUX_SDK_URL points at a mirror (or a local folder as file://...); the signature is checked either way.
URL="${ACRUX_SDK_URL:-https://github.com/$REPO/releases/download/v$VERSION}"
echo "Downloading Acrux SDK $VERSION..."
if ! curl -fsSL -o "$TMP/$NAME" "$URL/$NAME" 2> /dev/null || ! curl -fsSL -o "$TMP/$NAME.sig" "$URL/$NAME.sig" 2> /dev/null; then
  echo "update-acrux: could not download $NAME and its signature from $URL (no such release, offline, or not public); nothing was changed." >&2
  exit 1
fi

printf '%s\n' "$PUBLIC_KEY" > "$TMP/acrux.pub"
if ! openssl dgst -sha256 -verify "$TMP/acrux.pub" -signature "$TMP/$NAME.sig" "$TMP/$NAME" > /dev/null 2>&1; then
  echo "update-acrux: SIGNATURE CHECK FAILED for $NAME; nothing was changed." >&2
  exit 1
fi
# The signature covers the bytes, not the file name: make sure a signed older package is not served as a newer one.
NUSPEC="$(unzip -p "$TMP/$NAME" Acrux.Sdk.nuspec)"
case "$NUSPEC" in
  *"<id>Acrux.Sdk</id>"*"<version>$VERSION</version>"*) ;;
  *) echo "update-acrux: $NAME does not contain Acrux.Sdk $VERSION; nothing was changed." >&2; exit 1 ;;
esac

mkdir -p "$PACKAGES"
rm -f "$PACKAGES"/Acrux.Sdk.*.nupkg "$PACKAGES"/Acrux.Sdk.*.nupkg.sig
mv "$TMP/$NAME" "$TMP/$NAME.sig" "$PACKAGES/"

find "$HERE" -maxdepth 3 -name '*.csproj' -not -path '*/bin/*' -not -path '*/obj/*' | while read -r project; do
  if grep -q 'Include="Acrux.Sdk"' "$project"; then
    sed -E -i.acrux-bak 's|(<PackageReference Include="Acrux.Sdk" Version=")[^"]*(")|\1'"$VERSION"'\2|' "$project"
    rm -f "$project.acrux-bak"
    echo "  $(basename "$project") -> Acrux.Sdk $VERSION"
  fi
done

echo "Acrux SDK $VERSION installed (signature verified). Rebuild the mod."
