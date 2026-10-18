@echo off
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0scripts\Remove-Legacy.ps1" -WorkspaceOutputs
pause
