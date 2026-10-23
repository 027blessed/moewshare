#!/bin/bash
# Запускает редактор с записью ошибок в .local/editor-debug.log (для диагностики).
cd "$(dirname "$0")"
LOG="$(pwd)/.local/editor-debug.log"
echo "=== $(date) ===" > "$LOG"
echo "theme.txt: $(cat "$HOME/Library/Application Support/Moewshare/theme.txt" 2>&1)" >> "$LOG"
ls -la "$HOME/Library/Application Support/Moewshare/" >> "$LOG" 2>&1
"$HOME/Library/Application Support/Moewshare/Editors/0.1.0/Moewshare.Editor.app/Contents/MacOS/Moewshare.Editor" --project "$HOME/Documents/Moewshare Projects/firs" >> "$LOG" 2>&1
echo "=== exit code: $? ===" >> "$LOG"
