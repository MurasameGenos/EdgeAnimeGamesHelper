[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$dependency = (Get-Content -LiteralPath (Join-Path $repoRoot 'dependencies.json') -Raw -Encoding UTF8 | ConvertFrom-Json).webview2
if ($dependency.version -notmatch '^\d+\.\d+\.\d+\.\d+$' -or $dependency.sha256 -notmatch '^[a-fA-F0-9]{64}$') { throw 'Invalid pinned WebView2 dependency.' }
$cache = Join-Path $repoRoot ('.deps\WebView2\' + $dependency.version)
$sdkDirectory = Join-Path $cache 'sdk'
$packagePath = Join-Path $cache 'sdk.nupkg'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
if (-not (Test-Path -LiteralPath $packagePath)) {
    Write-Host ('Downloading official Microsoft.Web.WebView2 ' + $dependency.version)
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -UseBasicParsing -Uri $dependency.source -OutFile $packagePath
}
$actualHash = (Get-FileHash -LiteralPath $packagePath -Algorithm SHA256).Hash
if ($actualHash -ne $dependency.sha256) { throw 'WebView2 package hash does not match dependencies.json. Cached package retained for inspection.' }
if (-not (Test-Path -LiteralPath (Join-Path $sdkDirectory 'lib\net462\Microsoft.Web.WebView2.WinForms.dll'))) {
    if (Test-Path -LiteralPath $sdkDirectory) { throw 'Incomplete WebView2 SDK directory; inspect and remove it before restoring.' }
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::ExtractToDirectory($packagePath, $sdkDirectory)
}
Write-Output $sdkDirectory
