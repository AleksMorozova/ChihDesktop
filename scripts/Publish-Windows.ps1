#Requires -Version 7.4
[CmdletBinding()]
param([string] $OutputDirectory = '')
. "$PSScriptRoot/Release.Common.ps1"
$dotnet = Require-ChihTool 'dotnet'
$npm = Require-ChihTool 'npm'
if (-not $IsWindows) { throw 'WPF production publishing must run on Windows.' }
Assert-ChihSprites
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $ChihRoot ('artifacts/publish/win-x64/' + [guid]::NewGuid().ToString('N')) }
$output = Assert-ChihArtifactPath $OutputDirectory
if ((Test-Path -LiteralPath $output) -and @(Get-ChildItem -LiteralPath $output -Force).Count) {
    throw 'Publish destination must be empty. Choose a new artifacts path; this script never recursively deletes output or user data.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
Push-Location $ChihRoot
try {
    # Always restore the exact frontend lockfile, rather than trusting an existing node_modules.
    Invoke-ChihTool $npm @('--prefix', 'ui/chih-admin', 'ci', '--no-audit', '--no-fund')
    Invoke-ChihTool $npm @('--prefix', 'ui/chih-admin', 'run', 'build')
    Invoke-ChihTool $dotnet @('restore', 'ChihDesktop.slnx')
    Invoke-ChihTool $dotnet @('build', 'ChihDesktop.slnx', '-c', 'Release', '--no-restore')
    # Restore RID assets explicitly; the publish is never solution-wide or a SpriteBuilder invocation.
    Invoke-ChihTool $dotnet @('restore', 'ChihDesktop.csproj', '-r', 'win-x64', '-p:SelfContained=true')
    Invoke-ChihTool $dotnet @('publish', 'ChihDesktop.csproj', '-c', 'Release', '-r', 'win-x64', '--self-contained', 'true',
        '-p:PublishProfile=WinX64', '-p:DebugType=none', '-p:DebugSymbols=false', '--no-restore', '-o', $output)
    Copy-Item -LiteralPath 'LICENSE', 'ASSET-LICENSE.md' -Destination $output
    & "$PSScriptRoot/Write-DependencyNotices.ps1" -PublishDirectory $output
    & "$PSScriptRoot/Test-Publish.ps1" -PublishDirectory $output
    $manifest = @(Get-ChildItem -LiteralPath $output -File -Recurse | Sort-Object FullName | ForEach-Object {
        [ordered]@{ Path = [IO.Path]::GetRelativePath($output, $_.FullName).Replace('\', '/'); SHA256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }
    })
    ConvertTo-Json -InputObject $manifest -Depth 4 | Set-Content -LiteralPath (Join-Path $output 'payload-manifest.json') -Encoding utf8NoBOM
    & "$PSScriptRoot/Test-Publish.ps1" -PublishDirectory $output
    Assert-ChihSprites
    Write-Host "Verified self-contained publish: $output"
} finally { Pop-Location }
