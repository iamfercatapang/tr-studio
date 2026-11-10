# Publishes the app with its own .NET runtime, then builds the installer into artifacts\.
# Needs Inno Setup 6 and ffmpeg.exe in third-party\ffmpeg\ so video backgrounds work without TRCC.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$ffmpeg = Join-Path $root 'third-party\ffmpeg'
# PawnIO 2.2.0's signed setup from https://github.com/namazso/PawnIO.Setup/releases, with its GPL-2.0 COPYING as PawnIO-LICENSE.txt.
$pawnio = Join-Path $root 'third-party\pawnio'
$pawnioSha256 = '1f519a22e47187f70a1379a48ca604981c4fcf694f4e65b734aaa74a9fba3032'

$compiler = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup 6 is not installed.' }
if (-not (Test-Path "$ffmpeg\ffmpeg.exe")) { throw "ffmpeg.exe is missing from $ffmpeg." }
if (-not (Test-Path "$pawnio\PawnIO_setup.exe")) { throw "PawnIO_setup.exe is missing from $pawnio." }
if ((Get-FileHash "$pawnio\PawnIO_setup.exe" -Algorithm SHA256).Hash -ne $pawnioSha256) { throw 'PawnIO_setup.exe is not the expected 2.2.0 build.' }
if ((Get-AuthenticodeSignature "$pawnio\PawnIO_setup.exe").Status -ne 'Valid') { throw 'PawnIO_setup.exe is not validly signed.' }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\CoreVision.App') -c Release -r win-x64 --self-contained true -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item "$ffmpeg\*" $publish -Recurse
Copy-Item "$pawnio\PawnIO-LICENSE.txt" $publish # the setup itself goes to the installer's temp folder, not the app folder

# The installer takes the app's own version, so the in-app updater and the file name always agree.
$version = ([xml](Get-Content (Join-Path $root 'src\CoreVision.App\CoreVision.App.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in CoreVision.App.csproj.' }
& $compiler "/DAppVersion=$version" (Join-Path $root 'installer\TRStudio.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
