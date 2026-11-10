param([switch]$Compiler)
. "$PSScriptRoot/Common.ps1"
$root = Split-Path $PSScriptRoot
$tools = Join-Path $root '.tools'
New-Item -ItemType Directory -Path $tools -Force | Out-Null
Get-VerifiedArchive 'https://github.com/BepInEx/BepInEx/releases/download/v5.4.23.5/BepInEx_win_x64_5.4.23.5.zip' '82F9878551030F54657792C0740D9D51A09500EEAE1FBA21106B0C441E6732C4' "$tools/bepinex.zip" "$tools/bepinex"
if ($Compiler) {
    Get-VerifiedArchive 'https://api.nuget.org/v3-flatcontainer/microsoft.net.compilers.toolset/4.14.0/microsoft.net.compilers.toolset.4.14.0.nupkg' '941A9CF3EA618D88D01A3DD6B1A45A06BCF07716A9F81CE4031CAA3EDD24A845' "$tools/roslyn.zip" "$tools/roslyn"
}
