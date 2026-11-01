#Requires -Version 7.4
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PublishDirectory,
    [string] $IsccPath = '',
    [string] $WebView2BootstrapperPath = '',
    [string] $OutputDirectory = ''
)
. "$PSScriptRoot/Release.Common.ps1"
if (-not $IsWindows) { throw 'Inno Setup must run on Windows.' }
if (-not $IsccPath) {
    $tool = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($tool) { $IsccPath = $tool.Source }
    foreach ($version in @('7', '6')) {
        foreach ($base in @($env:ProgramFiles, ${env:ProgramFiles(x86)}, "$env:LOCALAPPDATA\Programs")) {
            if (-not $base) { continue }
            $candidate = Join-Path $base "Inno Setup $version/ISCC.exe"
            if (-not $IsccPath -and (Test-Path -LiteralPath $candidate -PathType Leaf)) { $IsccPath = $candidate }
        }
    }
}
if (-not $IsccPath -or -not (Test-Path -LiteralPath $IsccPath -PathType Leaf)) {
    throw 'Inno Setup 7 or 6.4+ compiler ISCC.exe is unavailable. Install it from https://jrsoftware.org/isdl.php or supply -IsccPath. No installer was produced.'
}
$IsccPath = (Resolve-Path -LiteralPath $IsccPath).Path
Write-Host "Inno Setup compiler: $IsccPath"
$publish = Assert-ChihArtifactPath $PublishDirectory
& "$PSScriptRoot/Test-Publish.ps1" -PublishDirectory $publish
if (-not (Test-Path -LiteralPath (Join-Path $publish 'payload-manifest.json'))) { throw 'Use Publish-Windows.ps1 to generate a verified payload manifest first.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $ChihRoot 'artifacts/installer' }
$output = Assert-ChihArtifactPath $OutputDirectory
if ($output.StartsWith($publish + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $publish.StartsWith($output + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
    $output.Equals($publish, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Installer output and publish payload must be separate directories, never nested.'
}
New-Item -ItemType Directory -Path $output -Force | Out-Null
$version = (Get-Item -LiteralPath (Join-Path $publish 'ChihDesktop.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Published executable must have a release version.' }
& "$PSScriptRoot/Test-AppIcon.ps1" -Executable (Join-Path $publish 'ChihDesktop.exe')
$installer = Join-Path $output "ChihDesktop-Setup-$version-win-x64.exe"
if (Test-Path -LiteralPath $installer) { throw 'Installer already exists. Choose a fresh -OutputDirectory to avoid overwriting it.' }
if (-not $WebView2BootstrapperPath) {
    $cache = Join-Path $ChihRoot ('artifacts/prerequisites/' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $WebView2BootstrapperPath = Join-Path $cache 'MicrosoftEdgeWebview2Setup.exe'
    # Official Microsoft Evergreen bootstrapper. Never execute it on the build machine.
    Invoke-WebRequest -Uri 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile $WebView2BootstrapperPath
}
$bootstrapper = (Resolve-Path -LiteralPath $WebView2BootstrapperPath).Path
$signature = Get-AuthenticodeSignature -LiteralPath $bootstrapper
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|,\s*)O=Microsoft Corporation(,|$)') {
    throw 'Bootstrapper must have a valid Microsoft Corporation Authenticode signature. Refusing to bundle untrusted executable.'
}
$bootstrapperHash = (Get-FileHash -LiteralPath $bootstrapper -Algorithm SHA256).Hash
Invoke-ChihTool $IsccPath @("/DPublishDir=$publish", "/DInstallerOutput=$output", "/DWebViewBootstrapper=$bootstrapper",
    "/DBootstrapperSHA256=$bootstrapperHash", "/DAppVersion=$version", (Join-Path $ChihRoot 'installer/ChihDesktop.iss'))
if (-not (Test-Path -LiteralPath $installer -PathType Leaf)) { throw 'Compiler returned success but installer is missing.' }
& "$PSScriptRoot/Test-AppIcon.ps1" -Executable $installer
$hash = (Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash
"$hash  $([IO.Path]::GetFileName($installer))" | Set-Content -LiteralPath ($installer + '.sha256') -Encoding ascii
Write-Host "Installer: $installer"
Write-Host "Bytes: $((Get-Item -LiteralPath $installer).Length)"
Write-Host "SHA256: $hash"
Assert-ChihSprites
