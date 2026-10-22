#!/usr/bin/env bash
# Сборка Moewshare для macOS. Запускать на Mac, из корня репозитория:
#   ./build-macos.sh              # Apple Silicon (osx-arm64)
#   ./build-macos.sh osx-x64      # Intel
# Результат: dist/<rid>/Moewshare.Launcher.app, dist/<rid>/Moewshare.Editor.app и ZIP.
# Редактор дополнительно копируется туда, где его ищет лаунчер:
#   ~/Library/Application Support/Moewshare/Editors/0.1.0/Moewshare.Editor.app
set -euo pipefail
cd "$(dirname "$0")"
export NUGET_PACKAGES="${NUGET_PACKAGES:-$(pwd)/.nuget/packages}"
RID="${1:-osx-arm64}"
VER="0.1.0"

dotnet restore
dotnet build -c Release --no-restore
[ "${SKIP_TESTS:-0}" = "1" ] || dotnet test -c Release --no-build

make_app() {   # $1 = проект, $2 = имя (совпадает с AssemblyName)
  local proj="$1" name="$2" out="dist/$RID/$2.app"
  rm -rf "$out"; mkdir -p "$out/Contents/MacOS" "$out/Contents/Resources"
  dotnet publish "$proj" -c Release -r "$RID" --self-contained true -o "$out/Contents/MacOS"
  if [ -f "$proj/Assets/AppIcon.icns" ]; then
    cp "$proj/Assets/AppIcon.icns" "$out/Contents/Resources/AppIcon.icns"
  fi
  cat > "$out/Contents/Info.plist" <<PLIST
<?xml version="1.0" encoding="UTF-8"?>
<!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
<plist version="1.0"><dict>
  <key>CFBundleName</key><string>$name</string>
  <key>CFBundleDisplayName</key><string>$name</string>
  <key>CFBundleIdentifier</key><string>app.moewshare.$(echo "$name" | tr 'A-Z' 'a-z' | sed 's/moewshare\.//')</string>
  <key>CFBundleExecutable</key><string>$name</string>
  <key>CFBundleIconFile</key><string>AppIcon</string>
  <key>CFBundlePackageType</key><string>APPL</string>
  <key>CFBundleShortVersionString</key><string>$VER</string>
  <key>CFBundleVersion</key><string>$VER</string>
  <key>LSMinimumSystemVersion</key><string>11.0</string>
  <key>NSHighResolutionCapable</key><true/>
</dict></plist>
PLIST
  chmod +x "$out/Contents/MacOS/$name"
  codesign --force --deep -s - "$out"     # ad-hoc подпись (нужна на Apple Silicon); это не нотаризация
}

make_app src/Moewshare.Editor   Moewshare.Editor
make_app src/Moewshare.Launcher Moewshare.Launcher

DEST="$HOME/Library/Application Support/Moewshare/Editors/$VER"
mkdir -p "$DEST"; rm -rf "$DEST/Moewshare.Editor.app"
cp -R "dist/$RID/Moewshare.Editor.app" "$DEST/"

( cd "dist/$RID" && rm -f "../Moewshare-$RID.zip" && zip -qry "../Moewshare-$RID.zip" Moewshare.Launcher.app Moewshare.Editor.app )
echo "Готово: dist/$RID/ и dist/Moewshare-$RID.zip (это ZIP с .app, не установщик)."
echo "Редактор установлен в: $DEST"
echo "Запуск: open dist/$RID/Moewshare.Launcher.app"
