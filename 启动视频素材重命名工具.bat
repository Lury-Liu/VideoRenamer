@echo off
setlocal
set "SCRIPT_DIR=%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -STA -File "%SCRIPT_DIR%video_material_renamer.ps1"
endlocal
