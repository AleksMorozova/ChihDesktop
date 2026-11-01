# Shared only by explicit release scripts; never invoked by application startup.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$ChihRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

function Require-ChihTool([string] $Name) {
    $tool = Get-Command $Name -ErrorAction SilentlyContinue
    if (-not $tool) { throw "Required tool '$Name' is unavailable. See docs/INSTALLER.md." }
    return $tool.Source
}

function Invoke-ChihTool([string] $Tool, [string[]] $Arguments) {
    & $Tool @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Tool failed (exit $LASTEXITCODE): $Tool" }
}

function Assert-ChihArtifactPath([string] $Path) {
    $full = [IO.Path]::GetFullPath($Path)
    $allowed = [IO.Path]::GetFullPath((Join-Path $ChihRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
    if (-not $full.StartsWith($allowed, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release outputs must be inside this workspace artifacts directory, never a user profile or installed application.'
    }
    # Reject junction/symlink parents as well as the final path.
    $parent = $full
    while ($parent -and $parent.Length -ge $allowed.Length - 1) {
        if ((Test-Path -LiteralPath $parent) -and ((Get-Item -LiteralPath $parent -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Release output has a reparse point: $parent"
        }
        $parent = [IO.Path]::GetDirectoryName($parent)
    }
    return $full
}

function Assert-ChihSprites {
    $baseline = @(Get-Content -LiteralPath (Join-Path $ChihRoot 'tools/PersistenceChecks/sprite-baseline.json') -Raw | ConvertFrom-Json)
    if ($baseline.Count -ne 15 -or @(Get-ChildItem (Join-Path $ChihRoot 'Assets/Sprites/*.png')).Count -ne 15) { throw 'Expected exactly 15 production sprites.' }
    foreach ($frame in $baseline) {
        if ((Get-FileHash -LiteralPath (Join-Path $ChihRoot "Assets/Sprites/$($frame.File)") -Algorithm SHA256).Hash -ne $frame.SHA256) {
            throw "Production sprite changed: $($frame.File)"
        }
    }
}
