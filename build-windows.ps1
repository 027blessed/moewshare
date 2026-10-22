# Сборка Moewshare для Windows x64. Запуск из корня репозитория: .\build-windows.ps1
$ErrorActionPreference = 'Stop'
dotnet restore
dotnet build -c Release --no-restore
dotnet test -c Release --no-build
$out = 'dist\Moewshare'
if (Test-Path $out) { Remove-Item $out -Recurse -Force }
dotnet publish src\Moewshare.Launcher -c Release -r win-x64 --self-contained true -o $out
dotnet publish src\Moewshare.Editor   -c Release -r win-x64 --self-contained true -o "$out\Editors\0.1.0"
Compress-Archive -Path "$out\*" -DestinationPath 'dist\Moewshare-win-x64.zip' -Force
Write-Host 'Готово: dist\Moewshare-win-x64.zip (это ZIP, а не установщик)'
