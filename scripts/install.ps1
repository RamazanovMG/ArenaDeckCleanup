param([string]$GamePath)
$projectRoot = Split-Path $PSScriptRoot
. "$projectRoot/scripts/Common.ps1"
$game = Resolve-ArenaPath $GamePath
if (Get-Process MTGA -ErrorAction SilentlyContinue) { throw 'Close Arena before installation.' }
if (!(Test-Path "$projectRoot/payload/ArenaMassDelete.dll")) { throw 'The download is incomplete. Download and extract the app again.' }
& "$projectRoot/scripts/Get-Dependencies.ps1"
$runtime = Join-Path $game 'DeckCleanupRuntime'
$manifestPath = Join-Path $runtime 'install-manifest.json'
$previous = if (Test-Path -LiteralPath $manifestPath) { Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json } else { $null }
$entries = @()
foreach ($file in Get-ChildItem "$projectRoot/.tools/bepinex/BepInEx/core" -Filter '*.dll') { $entries += @{ Source=$file.FullName; Relative="DeckCleanupRuntime/core/$($file.Name)" } }
$configPath = "$projectRoot/.tools/doorstop_config.ini"
$config = (Get-Content "$projectRoot/.tools/bepinex/doorstop_config.ini" -Raw).Replace('BepInEx\core\BepInEx.Preloader.dll','DeckCleanupRuntime\core\BepInEx.Preloader.dll')
[IO.File]::WriteAllText($configPath, $config, (New-Object Text.UTF8Encoding($false)))
$entries += @{Source="$projectRoot/.tools/bepinex/winhttp.dll"; Relative='winhttp.dll'}, @{Source=$configPath; Relative='doorstop_config.ini'}, @{Source="$projectRoot/payload/ArenaMassDelete.dll"; Relative='DeckCleanupRuntime/plugins/ArenaMassDelete.dll'}
foreach ($entry in $entries) {
    $entry.Hash = (Get-FileHash -LiteralPath $entry.Source).Hash
    $target = Join-Path $game $entry.Relative
    if (Test-Path -LiteralPath $target) {
        $old = @(if ($previous) { $previous.Files | Where-Object Relative -eq $entry.Relative })
        if ($old.Count -ne 1 -or (Get-FileHash -LiteralPath $target).Hash -ne $old[0].Hash) { throw "Unmanaged or modified file; not overwriting: $target" }
    }
}
foreach ($entry in $entries) {
    $target = Join-Path $game $entry.Relative
    New-Item -ItemType Directory -Path (Split-Path $target) -Force | Out-Null
    Copy-Item -LiteralPath $entry.Source -Destination $target -Force
}
@{ Files=@($entries | ForEach-Object { @{Relative=$_.Relative; Hash=$_.Hash} }) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath
Write-Output 'Installed. Open Arena > Decks > F8. Data: %LOCALAPPDATA%/ArenaDeckCleanup'



