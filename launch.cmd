@echo off
if exist "%~dp0GuideBrowser.exe" (
  start "" "%~dp0GuideBrowser.exe"
) else (
  start "" "%~dp0dist\GuideBrowser\GuideBrowser.exe"
)
