#!/bin/bash
# Полная пересборка: сборка + тесты + .app, затем запуск редактора с журналом ошибок.
cd "$(dirname "$0")"
export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet:/opt/homebrew/bin:/usr/local/bin"
LOG=".local/rebuild.log"
./build-macos.sh osx-arm64 > "$LOG" 2>&1
echo "=== build exit: $? ===" >> "$LOG"
./run-editor-debug.command
