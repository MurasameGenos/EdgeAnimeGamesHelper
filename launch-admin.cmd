@echo off
set "GUIDE_BROWSER_ADMIN_EXE=%~dp0GuideBrowser.exe"
if not exist "%GUIDE_BROWSER_ADMIN_EXE%" set "GUIDE_BROWSER_ADMIN_EXE=%~dp0dist\GuideBrowser\GuideBrowser.exe"
powershell.exe -NoProfile -Command "Start-Process -FilePath $env:GUIDE_BROWSER_ADMIN_EXE -Verb RunAs -WindowStyle Normal"
