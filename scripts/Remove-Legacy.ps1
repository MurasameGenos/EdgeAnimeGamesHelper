[CmdletBinding(SupportsShouldProcess = $true)]
param([switch]$WorkspaceOutputs)
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$localRoot = [System.IO.Path]::GetFullPath($env:LOCALAPPDATA)
$legacyDirectory = [System.IO.Path]::GetFullPath((Join-Path $localRoot 'EdgeAnimeGamesHelper\Native'))
$expectedDirectory = [System.IO.Path]::Combine($localRoot, 'EdgeAnimeGamesHelper', 'Native')
if ($legacyDirectory -ne $expectedDirectory) { throw 'Unexpected legacy install directory.' }
if (Test-Path -LiteralPath $legacyDirectory) {
    $legacyItem = Get-Item -LiteralPath $legacyDirectory
    if (($legacyItem.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing to delete through a legacy directory link.' }
}
$legacyExe = Join-Path $legacyDirectory 'EdgeAnimeHelper.exe'
foreach ($legacyProcess in @(Get-Process -Name EdgeAnimeHelper -ErrorAction SilentlyContinue)) {
    if ($legacyProcess.Path -and [System.IO.Path]::GetFullPath($legacyProcess.Path) -eq $legacyExe) {
        if ($PSCmdlet.ShouldProcess($legacyExe, 'Stop legacy native messaging helper')) { Stop-Process -Id $legacyProcess.Id -Force }
    }
}
foreach ($registryKey in @(
    'HKCU:\Software\Microsoft\Edge\NativeMessagingHosts\com.edgeanimegames.helper',
    'HKCU:\Software\Google\Chrome\NativeMessagingHosts\com.edgeanimegames.helper',
    'HKCU:\Software\Chromium\NativeMessagingHosts\com.edgeanimegames.helper')) {
    if ((Test-Path -LiteralPath $registryKey) -and $PSCmdlet.ShouldProcess($registryKey, 'Unregister legacy browser native messaging host')) {
        Remove-Item -LiteralPath $registryKey -Force
    }
}
# Only the two files installed by the old installer are removed. User browser
# data and the shared WebView2 Runtime are outside this allowlist.
foreach ($legacyName in @('EdgeAnimeHelper.exe', 'com.edgeanimegames.helper.json')) {
    $legacyFile = Join-Path $legacyDirectory $legacyName
    if ((Test-Path -LiteralPath $legacyFile) -and $PSCmdlet.ShouldProcess($legacyFile, 'Delete installed legacy helper file')) {
        Remove-Item -LiteralPath $legacyFile -Force
    }
}
if ((Test-Path -LiteralPath $legacyDirectory) -and @(Get-ChildItem -LiteralPath $legacyDirectory -Force).Count -eq 0) {
    if ($PSCmdlet.ShouldProcess($legacyDirectory, 'Remove empty legacy helper directory')) { Remove-Item -LiteralPath $legacyDirectory -Force }
}
if ($WorkspaceOutputs) {
    foreach ($relativeOutput in @('dist\EdgeAnimeGamesHelper', 'dist\native', 'dist\EdgeAnimeGamesHelper-win-x64.zip')) {
        $legacyOutput = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $relativeOutput))
        $outputBoundary = [System.IO.Path]::GetFullPath((Join-Path $repoRoot 'dist')) + [System.IO.Path]::DirectorySeparatorChar
        if (-not $legacyOutput.StartsWith($outputBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Legacy output is outside dist.' }
        if ((Test-Path -LiteralPath $legacyOutput) -and $PSCmdlet.ShouldProcess($legacyOutput, 'Delete generated legacy extension build output')) {
            Remove-Item -LiteralPath $legacyOutput -Recurse -Force
        }
    }
    $artifactDirectory = Join-Path $repoRoot 'artifacts'
    $artifactBoundary = [System.IO.Path]::GetFullPath($artifactDirectory) + [System.IO.Path]::DirectorySeparatorChar
    $generatedNames = @('test-native', 'browser-integration-result.json', 'browser-test-continue', 'browser-test-ready.json', 'fixture-running.png', 'popup-running.png')
    if (Test-Path -LiteralPath $artifactDirectory) {
        $generatedNames += @(Get-ChildItem -LiteralPath $artifactDirectory -Directory | Where-Object { $_.Name -match '^chromium-profile-\d+$' } | Select-Object -ExpandProperty Name)
    }
    foreach ($generatedName in $generatedNames) {
        $generatedPath = [System.IO.Path]::GetFullPath((Join-Path $artifactDirectory $generatedName))
        if (-not $generatedPath.StartsWith($artifactBoundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Legacy test output is outside artifacts.' }
        if ((Test-Path -LiteralPath $generatedPath) -and $PSCmdlet.ShouldProcess($generatedPath, 'Remove generated legacy extension test data')) {
            Remove-Item -LiteralPath $generatedPath -Recurse -Force
        }
    }
    foreach ($oldDirectoryName in @('extension', 'native')) {
        $emptyDirectory = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $oldDirectoryName))
        if ([System.IO.Path]::GetDirectoryName($emptyDirectory) -ne $repoRoot) { throw 'Unexpected source directory.' }
        if ((Test-Path -LiteralPath $emptyDirectory) -and @(Get-ChildItem -LiteralPath $emptyDirectory -File -Recurse -Force).Count -eq 0) {
            if ($PSCmdlet.ShouldProcess($emptyDirectory, 'Remove empty legacy source directory')) { Remove-Item -LiteralPath $emptyDirectory -Recurse -Force }
        }
    }
}
Write-Host 'Legacy native host cleanup completed. WebView2 browser data and shared runtime are retained.'
