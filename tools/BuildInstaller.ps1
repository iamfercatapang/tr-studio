# Publishes the app with its own .NET runtime, then builds the installer into artifacts\.
# Needs Inno Setup 6 and ffmpeg.exe in third-party\ffmpeg\ so video backgrounds work without TRCC.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$publish = Join-Path $root 'artifacts\publish'
$ffmpeg = Join-Path $root 'third-party\ffmpeg'

$compiler = @(
    (Get-Command iscc -ErrorAction SilentlyContinue).Source,
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
    'C:\Program Files (x86)\Inno Setup 6\ISCC.exe',
    'C:\Program Files\Inno Setup 6\ISCC.exe'
) | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
if (-not $compiler) { throw 'Inno Setup 6 is not installed.' }
if (-not (Test-Path "$ffmpeg\ffmpeg.exe")) { throw "ffmpeg.exe is missing from $ffmpeg." }

if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $root 'src\CoreVision.App') -c Release -r win-x64 --self-contained true -o $publish --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item "$ffmpeg\*" $publish -Recurse

# The installer takes the app's own version, so the in-app updater and the file name always agree.
$version = ([xml](Get-Content (Join-Path $root 'src\CoreVision.App\CoreVision.App.csproj'))).Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
if (-not $version) { throw 'No <Version> in CoreVision.App.csproj.' }
& $compiler "/DAppVersion=$version" (Join-Path $root 'installer\TRStudio.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer build failed.' }
