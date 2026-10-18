[CmdletBinding()]
param([switch]$InteractiveFocus, [switch]$HeadlessOnly, [string]$ExecutablePath)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$testFile = Join-Path $repoRoot 'tests\video-bridge.mjs'
if (Test-Path -LiteralPath $testFile) {
    & node --test $testFile
    if ($LASTEXITCODE -ne 0) { throw 'Video bridge tests failed.' }
}
$executable = if ($ExecutablePath) { [System.IO.Path]::GetFullPath($ExecutablePath) } else { Join-Path $repoRoot 'dist\GuideBrowser\GuideBrowser.exe' }
if (-not (Test-Path -LiteralPath $executable)) { throw 'Build the app first.' }
$start = New-Object System.Diagnostics.ProcessStartInfo
$start.FileName = $executable
$start.Arguments = if ($HeadlessOnly) { '--test-game-input' } else { '--self-test' }
$start.UseShellExecute = $false
$start.CreateNoWindow = $true
$start.RedirectStandardOutput = $true
$start.RedirectStandardError = $true
$start.StandardOutputEncoding = New-Object System.Text.UTF8Encoding($false)
$start.StandardErrorEncoding = New-Object System.Text.UTF8Encoding($false)
$start.EnvironmentVariables['EAGH_TEST_ARTIFACTS'] = Join-Path $repoRoot 'artifacts'
if ($InteractiveFocus) { $start.EnvironmentVariables['EAGH_TEST_WAIT_FOR_FOCUS_MS'] = '30000' }
$appProcess = New-Object System.Diagnostics.Process
$appProcess.StartInfo = $start
[void]$appProcess.Start()
$stdout = $appProcess.StandardOutput.ReadToEndAsync()
$stderr = $appProcess.StandardError.ReadToEndAsync()
if (-not $appProcess.WaitForExit(60000)) { $appProcess.Kill(); throw 'WebView2 application self-test timed out.' }
$report = $stdout.Result
$errorOutput = $stderr.Result
$artifacts = Join-Path $repoRoot 'artifacts'
New-Item -ItemType Directory -Path $artifacts -Force | Out-Null
$reportFile = if ($HeadlessOnly) { 'game-input-test.json' } else { 'app-self-test.json' }
[System.IO.File]::WriteAllText((Join-Path $artifacts $reportFile), $report, (New-Object System.Text.UTF8Encoding($false)))
if ($appProcess.ExitCode -ne 0) { Write-Output $report; throw ('Application self-test failed. ' + $errorOutput) }
$result = $report | ConvertFrom-Json
if (-not $result.ok) { throw 'Application did not report successful tests.' }
if ($HeadlessOnly) { Write-Host ('PASS: ' + $result.tests.Count + ' deterministic game input checks (synthetic foreground/key state).') }
else { Write-Host ('PASS: ' + $result.tests.Count + ' standalone app checks (actual WebView2, HWND, video, global hotkeys and cleanup).') }
Write-Host ('Report: ' + (Join-Path $artifacts $reportFile))
$appProcess.Dispose()
