param(
    [string]$Dotnet = 'dotnet',
    [string]$GameDir = 'D:\SteamLibrary\steamapps\common\SpaceEngineers2\Game2',
    [string]$OutputDirectory = (Split-Path $PSScriptRoot -Parent)
)
$ErrorActionPreference = 'Stop'
& $Dotnet build (Join-Path $PSScriptRoot 'AutoSorter.csproj') -c Release "-p:GameDir=$GameDir" --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
& $Dotnet run --project (Join-Path $PSScriptRoot 'tests/AutoSorter.Tests.csproj') -c Release
if ($LASTEXITCODE -ne 0) { throw 'Tests failed' }
$binaryDir = Join-Path $PSScriptRoot 'bin/Release/net9.0'
$stageRoot = Join-Path ([IO.Path]::GetTempPath()) ('AutoSorter-package-' + [guid]::NewGuid())
$stage = Join-Path $stageRoot 'AutoSorter'
New-Item -ItemType Directory -Path $stage -Force | Out-Null
# Explicit allowlist: never redistribute a game DLL or local research output.
$files = @('AutoSorter.dll', 'AutoSorter.deps.json', 'AutoSorter.runtimeconfig.json',
    'Mono.Cecil.dll', 'Mono.Cecil.Mdb.dll', 'Mono.Cecil.Pdb.dll', 'Mono.Cecil.Rocks.dll',
    'MonoMod.Backports.dll', 'MonoMod.Core.dll', 'MonoMod.Iced.dll', 'MonoMod.ILHelpers.dll',
    'MonoMod.RuntimeDetour.dll', 'MonoMod.Utils.dll')
foreach ($name in $files) { Copy-Item -LiteralPath (Join-Path $binaryDir $name) -Destination $stage }
foreach ($name in @('AutoSorter.json', 'README.md', 'README-RU.md', 'DESCRIPTION-EN.md', 'RELEASE-NOTES.md', 'CHANGELOG.md', 'LICENSE', 'LICENSE-BuildPlanner.txt', 'THIRD-PARTY-NOTICES.txt', 'VALIDATION.md')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $stage
}
Get-ChildItem -LiteralPath $stage -File | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash, $_.Name
} | Set-Content -LiteralPath (Join-Path $stage 'SHA256SUMS.txt') -Encoding utf8
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
$zip = Join-Path $OutputDirectory 'AutoSorter-0.3.0.zip'
Compress-Archive -LiteralPath $stage -DestinationPath $zip -Force
Write-Output "Package: $zip"
# Leave only this small staging directory for inspection; no recursive cleanup of computed paths.

