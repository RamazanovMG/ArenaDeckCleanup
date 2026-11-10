# Runs against a temporary fake Arena folder, never the real game.
$ErrorActionPreference = 'Stop'
$source = Split-Path $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('ArenaCleanupTest-' + [guid]::NewGuid().ToString('N'))
$package = Join-Path $fixture 'package'
$game = Join-Path $fixture 'game'
New-Item -ItemType Directory -Path "$package/scripts","$package/payload","$package/.tools","$game/MTGA_Data/Managed" -Force | Out-Null
Copy-Item "$source/scripts/install.ps1","$source/scripts/uninstall.ps1" "$package/scripts"
Copy-Item "$source/scripts/Common.ps1" "$package/scripts"
Copy-Item "$source/payload/ArenaMassDelete.dll" "$package/payload"
Copy-Item "$source/.tools/bepinex" "$package/.tools" -Recurse
# Dependencies are pre-populated from the verified development download.
Set-Content "$package/scripts/Get-Dependencies.ps1" '# Offline fixture'
Set-Content "$game/MTGA.exe" 'fixture'
Set-Content "$game/MTGA_Data/Managed/Core.dll" 'fixture'
function Get-Process { param($Name, $ErrorAction) return }
& "$package/scripts/install.ps1" -GamePath $game
if (!(Test-Path "$game/DeckCleanupRuntime/install-manifest.json")) { throw 'Install failed' }
& "$package/scripts/install.ps1" -GamePath $game
$plugin = "$game/DeckCleanupRuntime/plugins/ArenaMassDelete.dll"
$original = [IO.File]::ReadAllBytes($plugin)
[IO.File]::WriteAllText($plugin, 'changed')
$rejected = $false
try { & "$package/scripts/uninstall.ps1" -GamePath $game } catch { $rejected = $true }
if (!$rejected -or !(Test-Path $plugin)) { throw 'Modified-file protection failed' }
[IO.File]::WriteAllBytes($plugin, $original)
& "$package/scripts/uninstall.ps1" -GamePath $game
if (Test-Path $plugin) { throw 'Uninstall failed' }
if (!(Test-Path "$game/MTGA.exe")) { throw 'Game file was removed' }
Write-Output 'PASS: install, reinstall, modified-file protection, uninstall, game preservation.'
Write-Output "Test files: $fixture"

