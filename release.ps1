$ErrorActionPreference = 'Stop'
$workspace = $PSScriptRoot
$artifacts = Join-Path $workspace 'artifacts'
[xml]$properties = Get-Content -LiteralPath (Join-Path $workspace 'Directory.Build.props') -Raw
$version = $properties.Project.PropertyGroup.Version
$name = "LlanoUltraControl-$version-win-x64"
$destination = Join-Path $artifacts "release-$version"
$stage = Join-Path $artifacts ("release-build-" + [Guid]::NewGuid().ToString('N'))
$payload = Join-Path $stage 'portable'
$zip = Join-Path $stage "$name-portable.zip"
$msi = Join-Path $stage "$name.msi"
$sdk = Join-Path $workspace '.dotnet\dotnet.exe'
if (!(Test-Path -LiteralPath $sdk -PathType Leaf)) { $sdk = 'dotnet' }

$previousPublish = $env:LLANO_PUBLISH_DIR
$previousPackages = $env:NUGET_PACKAGES
$previousCli = $env:DOTNET_CLI_HOME
New-Item -ItemType Directory -Path $stage -Force | Out-Null
Push-Location $workspace
try {
    $env:LLANO_PUBLISH_DIR = $payload
    $env:NUGET_PACKAGES = Join-Path $workspace '.nuget\packages'
    $env:DOTNET_CLI_HOME = Join-Path $workspace '.dotnet-home'
    & .\build.cmd --portable
    if ($LASTEXITCODE -ne 0) { throw 'Portable build failed.' }

    $manifest = Get-Content -LiteralPath (Join-Path $payload 'LlanoUltraControl.deps.json') -Raw | ConvertFrom-Json
    foreach ($entry in $manifest.libraries.PSObject.Properties.Name) {
        if ($entry -match '^runtimepack\.(Microsoft\.(?:NETCore|WindowsDesktop)\.App\.Runtime\.win-x64)/(.+)$') {
            $pack = Join-Path $env:NUGET_PACKAGES (Join-Path $Matches[1].ToLowerInvariant() $Matches[2])
            $label = if ($Matches[1].Contains('WindowsDesktop')) { 'WindowsDesktop' } else { 'NETRuntime' }
            $notices = Get-ChildItem -LiteralPath $pack -File | Where-Object Name -Match '^(LICENSE|THIRD-PARTY-NOTICES)(\.TXT)?$'
            if (!$notices) { throw "Runtime licence missing in $pack." }
            foreach ($notice in $notices) {
                Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $payload "$label-$($notice.Name)")
            }
        }
    }

    & $sdk restore installer/Llano.Installer.wixproj --configfile NuGet.config
    if ($LASTEXITCODE -ne 0) { throw 'Installer restore failed.' }
    & $sdk build installer/Llano.Installer.wixproj -c Release --no-restore -o $stage "-p:PublishDirectory=$payload"
    if ($LASTEXITCODE -ne 0) { throw 'Installer build or validation failed.' }

    Compress-Archive -Path (Join-Path $payload '*') -DestinationPath $zip
    $checksums = foreach ($file in @($zip, $msi)) {
        $hash = Get-FileHash -LiteralPath $file -Algorithm SHA256
        '{0}  {1}' -f $hash.Hash.ToLowerInvariant(), (Split-Path $file -Leaf)
    }
    $checksums | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding ASCII
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    Copy-Item -LiteralPath $zip, $msi, (Join-Path $stage 'SHA256SUMS.txt'), (Join-Path $workspace 'CHANGELOG.md') -Destination $destination -Force
    Get-ChildItem -LiteralPath $destination -File | Select-Object Name, Length
}
finally {
    Pop-Location
    $env:LLANO_PUBLISH_DIR = $previousPublish
    $env:NUGET_PACKAGES = $previousPackages
    $env:DOTNET_CLI_HOME = $previousCli
    $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
    $resolvedArtifacts = (Resolve-Path -LiteralPath $artifacts).Path
    if ((Split-Path $resolvedStage -Parent) -eq $resolvedArtifacts -and (Split-Path $resolvedStage -Leaf) -match '^release-build-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolvedStage -Recurse -Force
    }
}
