param([Parameter(Mandatory)][string]$Destination)

$ErrorActionPreference = 'Stop'
$version = '2.6.0'
$destinationPath = (Resolve-Path -LiteralPath $Destination).Path
$assets = @(
    @{ Name = 'PresentMon.exe'; Url = "https://github.com/GameTechDev/PresentMon/releases/download/v$version/PresentMon-$version-x64.exe"; Hash = 'B2A706BC6AD475749E3B7E3409263AA1E6906D45BDCF993F6DBC0F660188F1AF' },
    @{ Name = 'PresentMon-LICENSE.txt'; Url = "https://raw.githubusercontent.com/GameTechDev/PresentMon/v$version/LICENSE.txt"; Hash = '4C949341B1893C8C6AD82F7FB4EEDF622CD1FD9C22A9AF8F19B2DAC19D1947B6' }
)
foreach ($asset in $assets) {
    $target = Join-Path $destinationPath $asset.Name
    if ((Test-Path -LiteralPath $target) -and (Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -eq $asset.Hash) { continue }
    $temporary = Join-Path $destinationPath ("presentmon-" + [Guid]::NewGuid().ToString('N') + '.tmp')
    try {
        Invoke-WebRequest -Uri $asset.Url -OutFile $temporary -UseBasicParsing
        if ((Get-FileHash -LiteralPath $temporary -Algorithm SHA256).Hash -ne $asset.Hash) { throw "PresentMon verification failed for $($asset.Name)." }
        Move-Item -LiteralPath $temporary -Destination $target -Force
    }
    finally {
        if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force }
    }
}
