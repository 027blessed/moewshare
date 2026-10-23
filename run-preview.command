#!/bin/bash
# Пересобирает .app (без тестов) и открывает обновлённый лаунчер.
cd "$(dirname "$0")"
export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet:/opt/homebrew/bin:/usr/local/bin"
SKIP_TESTS=1 ./build-macos.sh osx-arm64 > .local/run-preview.log 2>&1
echo "exit: $?" >> .local/run-preview.log
open dist/osx-arm64/Moewshare.Launcher.app
