param([string]$GamePath)
$projectRoot = Split-Path $PSScriptRoot
. "$projectRoot/scripts/Common.ps1"
$game = Resolve-ArenaPath $GamePath
if (Get-Process MTGA -ErrorAction SilentlyContinue) { throw 'Close Arena before uninstalling.' }
$manifestPath = Join-Path $game 'DeckCleanupRuntime/install-manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$targets = @()
foreach ($entry in $manifest.Files) {
    $relative = $entry.Relative.Replace('\','/')
    if ($relative -notmatch '^(winhttp\.dll|doorstop_config\.ini|DeckCleanupRuntime/(core/[^/]+\.dll|plugins/ArenaMassDelete\.dll))$') { throw 'Unexpected manifest path.' }
    $target = [IO.Path]::GetFullPath((Join-Path $game $relative))
    if (!$target.StartsWith($game.TrimEnd('\') + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Target outside Arena installation.' }
    if (Test-Path -LiteralPath $target) {
        if ((Get-FileHash -LiteralPath $target).Hash -ne $entry.Hash) { throw "Modified file; not removing: $target" }
        $targets += $target
    }
}
foreach ($target in $targets) { Remove-Item -LiteralPath $target }
Remove-Item -LiteralPath $manifestPath
Write-Output 'Plugin removed. Backups and preferences retained in LocalAppData.'

