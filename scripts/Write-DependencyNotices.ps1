#Requires -Version 7.4
[CmdletBinding()]
param([Parameter(Mandatory)][string] $PublishDirectory)
. "$PSScriptRoot/Release.Common.ps1"
$output = Assert-ChihArtifactPath $PublishDirectory
$notices = Join-Path $output 'licenses'
New-Item -ItemType Directory -Path $notices -Force | Out-Null
# Copy all available NuGet license/notice files from the actual resolved dependency graph,
# including runtime packs. Never copy package content wholesale (it can contain build tools).
$assets = Get-Content (Join-Path $ChihRoot 'obj/project.assets.json') -Raw | ConvertFrom-Json -AsHashtable
$records = [Collections.Generic.List[object]]::new()
# Runtime packs are recorded in runtimeconfig, not necessarily in assets.libraries.
$runtime = Get-Content (Join-Path $output 'ChihDesktop.runtimeconfig.json') -Raw | ConvertFrom-Json
foreach ($framework in $runtime.runtimeOptions.includedFrameworks) {
    $id = ($framework.name + '.Runtime.win-x64').ToLowerInvariant()
    $assets.libraries["$id/$($framework.version)"] = @{ type = 'package'; path = "$id/$($framework.version)" }
}
foreach ($entry in $assets.libraries.GetEnumerator()) {
    if ($entry.Value.type -ne 'package') { continue }
    $packageRoot = $null
    foreach ($cache in $assets.packageFolders.Keys) {
        $candidate = Join-Path $cache $entry.Value.path
        if (Test-Path -LiteralPath $candidate) { $packageRoot = $candidate; break }
    }
    if (-not $packageRoot) { throw "Dependency cache missing: $($entry.Key)" }
    $nuspec = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.nuspec')[0]
    [xml] $spec = Get-Content -LiteralPath $nuspec.FullName -Raw
    $license = $spec.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="license"]')
    $licenseUrl = $spec.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="licenseUrl"]')
    $copyright = $spec.SelectSingleNode('//*[local-name()="metadata"]/*[local-name()="copyright"]')
    $id = $entry.Key.Replace('/', '-')
    $copied = [Collections.Generic.List[string]]::new()
    foreach ($file in Get-ChildItem -LiteralPath $packageRoot -File -Recurse | Where-Object { $_.Name -match '(?i)^(license|licence|copying|notice|third[-._ ]?party[-._ ]?notices)(\..*)?$' }) {
        $relative = [IO.Path]::GetRelativePath($packageRoot, $file.FullName)
        $destination = Join-Path $notices (Join-Path $id $relative)
        New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($destination)) -Force | Out-Null
        Copy-Item -LiteralPath $file.FullName -Destination $destination
        $copied.Add([IO.Path]::GetRelativePath($output, $destination).Replace('\', '/'))
    }
    $records.Add([ordered]@{ Package = $entry.Key; License = $(if ($license) { $license.InnerText } else { '' }); LicenseUrl = $(if ($licenseUrl) { $licenseUrl.InnerText } else { '' }); Copyright = $(if ($copyright) { $copyright.InnerText } else { '' }); BundledNotices = @($copied) })
}
Copy-Item -LiteralPath (Join-Path $ChihRoot 'installer/licenses/Apache-2.0.txt') -Destination (Join-Path $notices 'Apache-2.0.txt')
foreach ($package in @('react', 'react-dom', 'scheduler', 'zod')) {
    $folder = Join-Path $ChihRoot "ui/chih-admin/node_modules/$package"
    $metadata = Get-Content (Join-Path $folder 'package.json') -Raw | ConvertFrom-Json
    $destination = Join-Path $notices "frontend-$package-LICENSE.txt"
    Copy-Item -LiteralPath (Join-Path $folder 'LICENSE') -Destination $destination
    $records.Add([ordered]@{ Package = "$package/$($metadata.version)"; License = $metadata.license; BundledNotices = @("licenses/frontend-$package-LICENSE.txt") })
}
ConvertTo-Json -InputObject @($records) -Depth 6 | Set-Content -LiteralPath (Join-Path $notices 'dependency-licenses.json') -Encoding utf8NoBOM
