#Requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PublishDirectory)
. "$PSScriptRoot/Release.Common.ps1"
$output = Assert-ChihArtifactPath $PublishDirectory
if (-not (Test-Path -LiteralPath $output -PathType Container)) { throw 'Publish directory is missing.' }
$files = @(Get-ChildItem -LiteralPath $output -Force -Recurse)
foreach ($file in $files) {
    if ($file.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw 'Payload cannot contain reparse points.' }
    $path = [IO.Path]::GetRelativePath($output, $file.FullName).Replace('\', '/')
    if ($path -match '(?i)(^|/)(references|backups|logs|node_modules|\.git|\.aws|\.codex|WebView2|TestResults)(/|$)|\.(jpg|jpeg|pdb|map|log|bak|pfx|p12|key|pem|db|sqlite|sqlite3)(-(wal|shm|journal))?$|\.(cs|csproj|slnx|ts|tsx|ps1|iss|props|targets|nuspec|nupkg|dmp|zip|msi|msix)$|\.runtimeconfig\.dev\.json$|(^|/)(reminders[^/]*|user-settings[^/]*|appsettings[^/]*|settings|pet-settings)\.json$|(^|/)\.env($|\.)|\.(invalid-|tmp-)|pre-v03-backup|schema-v0-backup|schedule-v1-backup|chih-preview|SpriteBuilder|WalkAlignment|testhost|xunit') {
        throw "Private/development file in payload: $path"
    }
}
foreach ($required in @('ChihDesktop.exe', 'ChihDesktop.dll', 'ChihDesktop.Core.dll', 'ChihDesktop.Infrastructure.dll',
    'ChihDesktop.runtimeconfig.json', 'ChihDesktop.deps.json', 'coreclr.dll', 'hostfxr.dll', 'hostpolicy.dll',
    'System.Private.CoreLib.dll', 'PresentationFramework.dll', 'WindowsBase.dll', 'e_sqlite3.dll', 'WebView2Loader.dll',
    'Microsoft.EntityFrameworkCore.Sqlite.dll', 'Microsoft.Web.WebView2.Wpf.dll', 'AdminUi/index.html',
    'LICENSE', 'ASSET-LICENSE.md', 'licenses/dependency-licenses.json', 'licenses/Apache-2.0.txt')) {
    if (-not (Test-Path -LiteralPath (Join-Path $output $required) -PathType Leaf)) { throw "Missing publish dependency: $required" }
}
$config = Get-Content (Join-Path $output 'ChihDesktop.runtimeconfig.json') -Raw | ConvertFrom-Json -AsHashtable
if ($config.runtimeOptions.ContainsKey('framework') -or $config.runtimeOptions.ContainsKey('frameworks') -or
    -not ($config.runtimeOptions.includedFrameworks.name -contains 'Microsoft.WindowsDesktop.App')) { throw 'Payload is not self-contained WPF.' }
$licenses = @(Get-Content (Join-Path $output 'licenses/dependency-licenses.json') -Raw | ConvertFrom-Json)
foreach ($framework in $config.runtimeOptions.includedFrameworks) {
    $package = "$($framework.name).Runtime.win-x64/$($framework.version)"
    $record = @($licenses | Where-Object { $_.Package -ieq $package })
    if ($record.Count -ne 1 -or -not $record[0].BundledNotices.Count) { throw "Runtime notices missing: $package" }
}
foreach ($binary in @('ChihDesktop.exe', 'coreclr.dll', 'e_sqlite3.dll', 'WebView2Loader.dll')) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $output $binary))
    $pe = [BitConverter]::ToInt32($bytes, 0x3c)
    if ([BitConverter]::ToUInt16($bytes, $pe + 4) -ne 0x8664) { throw "Expected x64 PE: $binary" }
}
$index = Get-Content (Join-Path $output 'AdminUi/index.html') -Raw
foreach ($match in [regex]::Matches($index, '(?:src|href)="([^"]+)"')) {
    $asset = $match.Groups[1].Value
    if ($asset -match '^(https?:|//)' -or $asset -match '\.\.') { throw "Nonlocal frontend asset: $asset" }
    if (-not (Test-Path -LiteralPath (Join-Path $output ('AdminUi/' + $asset.TrimStart('/'))))) { throw "Missing frontend asset: $asset" }
}
if (-not @(Get-ChildItem (Join-Path $output 'AdminUi/assets') -Filter '*.css').Count -or
    -not @(Get-ChildItem (Join-Path $output 'AdminUi/assets') -Filter '*.js').Count) { throw 'Production CSS/JS missing.' }
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $output 'ChihDesktop.dll')))
$resources = [Resources.ResourceReader]::new($assembly.GetManifestResourceStream('ChihDesktop.g.resources'))
try {
    $sprites = @{}
    $items = $resources.GetEnumerator()
    while ($items.MoveNext()) { if ($items.Key -like 'assets/sprites/*.png') { $sprites[$items.Key] = $items.Value } }
    $baseline = @(Get-Content (Join-Path $ChihRoot 'tools/PersistenceChecks/sprite-baseline.json') -Raw | ConvertFrom-Json)
    if ($sprites.Count -ne 15) { throw 'Expected 15 embedded sprites.' }
    foreach ($frame in $baseline) {
        $stream = $sprites['assets/sprites/' + $frame.File.ToLowerInvariant()]
        if (-not $stream) { throw "Embedded sprite missing: $($frame.File)" }
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream))
        if ($hash -ne $frame.SHA256) { throw "Embedded sprite changed: $($frame.File)" }
    }
} finally { $resources.Dispose() }
# Exercise the exact published native SQLite library without opening any database.
$native = [Runtime.InteropServices.NativeLibrary]::Load((Join-Path $output 'e_sqlite3.dll'))
try { [void][Runtime.InteropServices.NativeLibrary]::GetExport($native, 'sqlite3_open_v2') }
finally { [Runtime.InteropServices.NativeLibrary]::Free($native) }
$manifestPath = Join-Path $output 'payload-manifest.json'
if (Test-Path -LiteralPath $manifestPath) {
    $manifest = @(Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json)
    $actual = @($files | Where-Object { -not $_.PSIsContainer -and $_.FullName -ne $manifestPath })
    if ($manifest.Count -ne $actual.Count) { throw 'Payload manifest file count mismatch.' }
    $seen = @{}
    foreach ($entry in $manifest) {
        if ($seen.ContainsKey($entry.Path) -or $entry.Path -match '(^/|[\\:]|(^|/)\.\.(/|$))') { throw 'Invalid payload manifest path.' }
        $seen[$entry.Path] = $true
        if ((Get-FileHash -LiteralPath (Join-Path $output $entry.Path) -Algorithm SHA256).Hash -ne $entry.SHA256) { throw "Payload bytes changed: $($entry.Path)" }
    }
}
Assert-ChihSprites
& "$PSScriptRoot/Test-AppIcon.ps1" -Executable (Join-Path $output 'ChihDesktop.exe')
Write-Host "PASS: self-contained WPF, bundled frontend, native SQLite, 15 embedded sprites and payload privacy ($output)"
