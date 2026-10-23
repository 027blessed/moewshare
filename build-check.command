#!/bin/bash
# Проверка сборки и тестов Moewshare. Результат пишется в .local/build-check.log
cd "$(dirname "$0")"
export PATH="$PATH:/usr/local/share/dotnet:$HOME/.dotnet:/opt/homebrew/bin:/usr/local/bin"
export NUGET_PACKAGES="$(pwd)/.nuget/packages"
LOG=".local/build-check.log"
{
  echo "=== $(date) ==="
  which dotnet; dotnet --version
  dotnet build Moewshare.slnx -c Debug 2>&1 | grep -E "error|warning CS8|Build succeeded|Ошибок|Error|сборка" | sort -u | head -150
  echo "=== BUILD EXIT: ${PIPESTATUS[0]} ==="
  dotnet test Moewshare.slnx --no-build 2>&1 | tail -40
  echo "=== DONE ==="
} > "$LOG" 2>&1
echo "Готово. Лог: $LOG"
