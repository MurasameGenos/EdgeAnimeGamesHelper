[CmdletBinding()]
param([switch]$SkipTests, [switch]$Package, [string]$OutputRoot)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
if (-not $OutputRoot) { $OutputRoot = Join-Path $repoRoot 'dist' }
$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$outputDirectory = Join-Path $OutputRoot 'GuideBrowser'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The .NET Framework C# compiler is missing. Windows 10/11 x64 with .NET Framework 4.8 is required.' }
$sdkDirectory = & (Join-Path $PSScriptRoot 'Restore-WebView2.ps1')
New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
$references = @(
    '/r:System.Windows.Forms.dll', '/r:System.Drawing.dll', '/r:System.Web.Extensions.dll',
    ('/r:' + (Join-Path $sdkDirectory 'lib\net462\Microsoft.Web.WebView2.Core.dll')),
    ('/r:' + (Join-Path $sdkDirectory 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'))
)
$sources = @(Get-ChildItem -LiteralPath (Join-Path $repoRoot 'app') -Filter '*.cs' | ForEach-Object { $_.FullName })
$compilerArguments = @('/nologo', '/target:winexe', '/platform:x64', '/langversion:5', '/optimize+', '/warn:4', '/warnaserror+', '/utf8output', '/codepage:65001',
    ('/out:' + (Join-Path $outputDirectory 'GuideBrowser.exe')),
    ('/win32manifest:' + (Join-Path $repoRoot 'app\app.manifest')),
    ('/resource:' + (Join-Path $repoRoot 'app\VideoBridge.js') + ',EdgeAnimeGamesHelper.VideoBridge.js'),
    ('/resource:' + (Join-Path $repoRoot 'tests\fixtures\guide.webm') + ',EdgeAnimeGamesHelper.TestVideo.webm')) + $references + $sources
& $compiler @compilerArguments
if ($LASTEXITCODE -ne 0) { throw 'C# application build failed. Close the running app, or build to a separate directory with -OutputRoot.' }
foreach ($assembly in @('Microsoft.Web.WebView2.Core.dll', 'Microsoft.Web.WebView2.WinForms.dll')) {
    Copy-Item -LiteralPath (Join-Path $sdkDirectory ('lib\net462\' + $assembly)) -Destination $outputDirectory -Force
}
Copy-Item -LiteralPath (Join-Path $sdkDirectory 'runtimes\win-x64\native\WebView2Loader.dll') -Destination $outputDirectory -Force
Copy-Item -LiteralPath (Join-Path $sdkDirectory 'LICENSE.txt') -Destination (Join-Path $outputDirectory 'WebView2-LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $sdkDirectory 'NOTICE.txt') -Destination (Join-Path $outputDirectory 'WebView2-NOTICE.txt') -Force
foreach ($file in @('README.md', 'LICENSE', 'THIRD_PARTY_NOTICES.md', 'launch.cmd', 'launch-admin.cmd')) { Copy-Item -LiteralPath (Join-Path $repoRoot $file) -Destination $outputDirectory -Force }
New-Item -ItemType Directory -Path (Join-Path $outputDirectory 'docs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $repoRoot 'docs\VALIDATION.md') -Destination (Join-Path $outputDirectory 'docs\VALIDATION.md') -Force
if (-not $SkipTests) { & (Join-Path $PSScriptRoot 'Test.ps1') -ExecutablePath (Join-Path $outputDirectory 'GuideBrowser.exe') }
if ($Package) {
    $archive = Join-Path $OutputRoot 'GuideBrowser-win-x64.zip'
    Compress-Archive -LiteralPath $outputDirectory -DestinationPath $archive -Force
    $version = (Get-Content -LiteralPath (Join-Path $repoRoot 'package.json') -Raw -Encoding UTF8 | ConvertFrom-Json).version
    $versionedArchive = Join-Path $OutputRoot ('GuideBrowser-' + $version + '-win-x64.zip')
    Copy-Item -LiteralPath $archive -Destination $versionedArchive -Force
    Write-Host ('Versioned package: ' + $versionedArchive)
    Write-Host ('Package: ' + $archive)
}
Write-Host ('Built standalone browser: ' + (Join-Path $outputDirectory 'GuideBrowser.exe'))
