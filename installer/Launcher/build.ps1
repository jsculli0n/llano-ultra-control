param(
    [Parameter(Mandatory)][string]$OutputDirectory,
    [Parameter(Mandatory)][version]$Version
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'
$workspace = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$cache = Join-Path $workspace '.native-tools'
$tag = '20261117'
$package = "llvm-mingw-$tag-ucrt-x86_64"
$hash = '4D002E330A0F703625AA5CCBC37E96F8F824413E163BFC7336774AABD2C3A412'
$toolchain = Join-Path $cache $package
$compiler = Join-Path $toolchain 'bin\x86_64-w64-mingw32-clang.exe'
$windres = Join-Path $toolchain 'bin\x86_64-w64-mingw32-windres.exe'

if (!(Test-Path -LiteralPath $compiler -PathType Leaf) -or !(Test-Path -LiteralPath $windres -PathType Leaf)) {
    New-Item -ItemType Directory -Path $cache -Force | Out-Null
    $archive = Join-Path $cache "$package.zip"
    if (!(Test-Path -LiteralPath $archive -PathType Leaf)) {
        $download = Join-Path $cache ("download-" + [Guid]::NewGuid().ToString('N') + '.zip')
        try {
            Invoke-WebRequest -UseBasicParsing -Uri "https://github.com/mstorsjo/llvm-mingw/releases/download/$tag/$package.zip" -OutFile $download -TimeoutSec 240
            if ((Get-FileHash -LiteralPath $download -Algorithm SHA256).Hash -ne $hash) {
                throw 'The native compiler download failed SHA-256 verification.'
            }
            Move-Item -LiteralPath $download -Destination $archive -Force
        }
        finally {
            if (Test-Path -LiteralPath $download) { Remove-Item -LiteralPath $download }
        }
    }
    if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $hash) {
        throw 'The cached native compiler archive failed SHA-256 verification.'
    }

    $stage = Join-Path $cache ("extract-" + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $stage | Out-Null
    try {
        & (Join-Path ([Environment]::SystemDirectory) 'tar.exe') -xf $archive -C $stage
        if ($LASTEXITCODE -ne 0) { throw 'The native compiler archive could not be extracted.' }
        $stagedToolchain = (Resolve-Path -LiteralPath (Join-Path $stage $package)).Path
        if ((Split-Path $stagedToolchain -Parent) -ne $stage -or (Split-Path $toolchain -Parent) -ne $cache) {
            throw 'Unexpected native compiler extraction path.'
        }
        try { [IO.Directory]::Move($stagedToolchain, $toolchain) }
        catch [IO.IOException] {
            if (!(Test-Path -LiteralPath $compiler -PathType Leaf) -or !(Test-Path -LiteralPath $windres -PathType Leaf)) { throw }
        }
    }
    finally {
        $resolvedStage = (Resolve-Path -LiteralPath $stage).Path
        if ((Split-Path $resolvedStage -Parent) -eq $cache -and (Split-Path $resolvedStage -Leaf) -match '^extract-[0-9a-f]{32}$') {
            Remove-Item -LiteralPath $resolvedStage -Recurse -Force
        }
    }
}

if ($Version.Build -lt 0) { throw 'The launcher version needs three numeric components.' }
$nativeVersion = [version]::new($Version.Major, $Version.Minor, $Version.Build, [Math]::Max(0, $Version.Revision))
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
$checkDirectory = Join-Path $OutputDirectory 'launcher checks'
New-Item -ItemType Directory -Path $checkDirectory -Force | Out-Null
$resource = Join-Path $OutputDirectory 'Launcher.res.o'
Push-Location -LiteralPath (Join-Path $workspace 'src\Llano.App')
try {
    & $windres -i (Join-Path $PSScriptRoot 'Launcher.rc') -O coff -o $resource "-DLLANO_VERSION=$nativeVersion" "-DLLANO_VERSION_NUMBERS=$($nativeVersion.ToString().Replace('.', ','))"
    if ($LASTEXITCODE -ne 0) { throw 'Could not compile the launcher resources.' }
}
finally { Pop-Location }

$launcher = Join-Path $checkDirectory 'LlanoUltraControl.exe'
$flags = @('-std=c17', '-O2', '-Wall', '-Wextra', '-Werror', '-DUNICODE', '-D_UNICODE', '-D_WIN32_WINNT=0x0A00',
           '-nostdlib', '-Wl,--subsystem,windows,--dynamicbase,--nxcompat,--high-entropy-va')
& $compiler @flags '-Wl,--entry,LauncherEntry' (Join-Path $PSScriptRoot 'Launcher.c') $resource -o $launcher -lkernel32 -ladvapi32 -lshell32 -luser32 -luserenv
if ($LASTEXITCODE -ne 0) { throw 'Could not build the native launcher.' }

$probe = Join-Path $checkDirectory 'LlanoUltraControl.App.exe'
& $compiler @flags '-Wl,--entry,ProbeEntry' (Join-Path $workspace 'tests\Launcher\Probe.c') -o $probe -lkernel32 -lshell32
if ($LASTEXITCODE -ne 0) { throw 'Could not build the launcher test probe.' }
New-Item -ItemType File -Path (Join-Path $checkDirectory 'launcher-check.marker') -Force | Out-Null

$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
try { $elevated = [Security.Principal.WindowsPrincipal]::new($identity).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) }
finally { $identity.Dispose() }
$profileCheck = if ([Environment]::GetFolderPath([Environment+SpecialFolder]::UserProfile)) { 'clean-profile' } else { 'clean' }
foreach ($mode in '--tray', '--telemetry-helper', '--configure-telemetry', '--remove-startup-tasks') {
    $expected = if ($elevated -or $mode -ne '--tray') { $profileCheck } else { 'inherited' }
    $start = [Diagnostics.ProcessStartInfo]::new($launcher, ('{0} {1} "argument with spaces"' -f $mode, $expected))
    $start.WorkingDirectory = $OutputDirectory
    $start.UseShellExecute = $false
    $start.EnvironmentVariables['LLANO_LAUNCHER_CHECK'] = 'caller environment'
    $start.EnvironmentVariables['DOTNET_EnableDiagnostics'] = '1'
    $process = [Diagnostics.Process]::Start($start)
    try {
        if (!$process.WaitForExit(10000)) {
            $process.Kill()
            throw "Launcher check timed out for $mode."
        }
        if ($process.ExitCode -ne 42) { throw "Launcher check failed for $mode with exit code $($process.ExitCode)." }
    }
    finally { $process.Dispose() }
}
Copy-Item -LiteralPath $launcher -Destination (Join-Path $OutputDirectory 'LlanoUltraControl.exe') -Force
Write-Output 'Native launcher environment, argument, working directory and exit code checks passed.'
