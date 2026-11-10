Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
function Resolve-ArenaPath([string]$GamePath) {
    if ($GamePath) {
        $resolved = (Resolve-Path -LiteralPath $GamePath).Path
        if (!(Test-Path -LiteralPath (Join-Path $resolved 'MTGA.exe')) -or !(Test-Path -LiteralPath (Join-Path $resolved 'MTGA_Data/Managed/Core.dll'))) { throw 'GamePath must contain MTGA.exe and MTGA_Data/Managed/Core.dll.' }
        return $resolved
    }
    $candidates = @()
    if ($env:ProgramFiles) { $candidates += Join-Path $env:ProgramFiles 'Wizards of the Coast/MTGA' }
    if (${env:ProgramFiles(x86)}) {
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Wizards of the Coast/MTGA'
        $candidates += Join-Path ${env:ProgramFiles(x86)} 'Steam/steamapps/common/MTGA'
    }
    foreach ($key in @('HKLM:/Software/Microsoft/Windows/CurrentVersion/Uninstall/*','HKLM:/Software/WOW6432Node/Microsoft/Windows/CurrentVersion/Uninstall/*','HKCU:/Software/Microsoft/Windows/CurrentVersion/Uninstall/*')) {
        Get-ItemProperty $key -ErrorAction SilentlyContinue | ForEach-Object {
            if ($_.PSObject.Properties['DisplayName'] -and $_.DisplayName -match 'MTG Arena|Magic.*Arena' -and $_.PSObject.Properties['InstallLocation'] -and $_.InstallLocation) { $candidates += $_.InstallLocation }
        }
    }
    $matches = @($candidates | Where-Object { (Test-Path -LiteralPath (Join-Path $_ 'MTGA.exe')) -and (Test-Path -LiteralPath (Join-Path $_ 'MTGA_Data/Managed/Core.dll')) } | ForEach-Object { (Resolve-Path -LiteralPath $_).Path.TrimEnd('\') } | Select-Object -Unique)
    if ($matches.Count -eq 1) { return (Resolve-Path -LiteralPath $matches[0]).Path }
    throw 'Arena installation not uniquely detected. Pass -GamePath "D:/path/to/MTGA".'
}
function Get-VerifiedArchive([string]$Url, [string]$Hash, [string]$Archive, [string]$Destination) {
    if (!(Test-Path -LiteralPath $Archive)) { Invoke-WebRequest -Uri $Url -OutFile $Archive -UseBasicParsing }
    if ((Get-FileHash -LiteralPath $Archive -Algorithm SHA256).Hash -ne $Hash) { throw "Checksum mismatch: $Archive" }
    Expand-Archive -LiteralPath $Archive -DestinationPath $Destination -Force
}

