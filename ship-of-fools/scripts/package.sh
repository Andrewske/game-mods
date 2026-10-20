#!/usr/bin/env bash
# Build ShipOfFoolsTweaks and bundle it with BepInEx into a drop-in Windows zip.
# Usage: scripts/package.sh            -> release/ShipOfFoolsTweaks-v<version>.zip
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
BEPINEX_BUILD="789"
BEPINEX_FILE="BepInEx-Unity.IL2CPP-win-x64-6.0.0-be.789%2B6cea286.zip"
BEPINEX_URL="https://builds.bepinex.dev/projects/bepinex_be/${BEPINEX_BUILD}/${BEPINEX_FILE}"
CACHE="$ROOT/downloads/bepinex-be${BEPINEX_BUILD}.zip"
PROJECT="$ROOT/src/ShipOfFoolsTweaks"

export PATH="$HOME/.dotnet:$PATH" DOTNET_ROOT="$HOME/.dotnet"

die() { echo "package.sh: $*" >&2; exit 1; }

plugin_version() {
  grep -oP 'Version = "\K[^"]+' "$PROJECT/Plugin.cs" || die "no Version const in $PROJECT/Plugin.cs"
}

fetch_bepinex() {
  [[ -f "$CACHE" ]] && return
  mkdir -p "$(dirname "$CACHE")"
  curl -fsSL -o "$CACHE" "$BEPINEX_URL" || die "BepInEx download failed: $BEPINEX_URL"
}

build_plugin() {
  dotnet build "$PROJECT" -c Release -nologo -v quiet >/dev/null || die "dotnet build failed in $PROJECT"
}

stage() {
  local dir="$1"
  unzip -q "$CACHE" -d "$dir"
  mkdir -p "$dir/BepInEx/plugins/ShipOfFoolsTweaks"
  cp "$PROJECT/bin/Release/net6.0/ShipOfFoolsTweaks.dll" "$dir/BepInEx/plugins/ShipOfFoolsTweaks/"
  cp "$ROOT/scripts/INSTALL.txt" "$dir/"
}

main() {
  local version out
  version="$(plugin_version)"
  out="$ROOT/release/ShipOfFoolsTweaks-v${version}.zip"
  TMP_DIR="$(mktemp -d)"
  trap 'rm -rf "$TMP_DIR"' EXIT

  fetch_bepinex
  build_plugin
  stage "$TMP_DIR"
  mkdir -p "$ROOT/release"
  rm -f "$out"
  (cd "$TMP_DIR" && zip -qr "$out" .)
  echo "$out"
}

main "$@"
