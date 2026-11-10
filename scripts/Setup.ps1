param([ValidateSet('Install','Uninstall')][string]$Mode, [switch]$Elevated)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Windows.Forms
[System.Windows.Forms.Application]::EnableVisualStyles()
function Tell([string]$Text, [string]$Title = 'Arena Deck Cleanup') {
    [void][System.Windows.Forms.MessageBox]::Show($Text, $Title, 'OK', 'Information')
}
$root = Split-Path $PSScriptRoot
$logFolder = Join-Path $env:TEMP 'ArenaDeckCleanup'
New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
$logPath = Join-Path $logFolder 'setup.log'
try {
    if (Get-Process MTGA -ErrorAction SilentlyContinue) { Tell 'Please close Arena, then double-click the button again.'; exit 1 }
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $admin = (New-Object Security.Principal.WindowsPrincipal($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
    if (!$admin) {
        if ($Elevated) { throw 'Windows permission was not granted.' }
        $arguments = '-NoProfile -ExecutionPolicy Bypass -STA -WindowStyle Hidden -File "' + $PSCommandPath + '" -Mode ' + $Mode + ' -Elevated'
        try { Start-Process -FilePath 'powershell.exe' -ArgumentList $arguments -Verb RunAs -WindowStyle Hidden -ErrorAction Stop | Out-Null }
        catch { Tell 'Setup was cancelled. Nothing was changed.' }
        exit
    }
    . "$PSScriptRoot/Common.ps1"
    try { $game = Resolve-ArenaPath '' }
    catch {
        $picker = New-Object System.Windows.Forms.FolderBrowserDialog
        $picker.Description = 'Choose the Arena folder containing MTGA.exe.'
        $picker.ShowNewFolderButton = $false
        if ($picker.ShowDialog() -ne 'OK') { $picker.Dispose(); exit }
        try { $game = Resolve-ArenaPath $picker.SelectedPath }
        catch { Tell 'Arena was not found in that folder. Try again and choose the folder containing MTGA.exe.'; exit 1 }
        finally { $picker.Dispose() }
    }
    if ($Mode -eq 'Uninstall' -and !(Test-Path -LiteralPath (Join-Path $game 'DeckCleanupRuntime/install-manifest.json'))) {
        Tell 'Deck Cleanup is already removed, or was not installed in this Arena folder.'; exit
    }
    $form = New-Object System.Windows.Forms.Form
    $form.Text = 'Arena Deck Cleanup'
    $form.Width = 450; $form.Height = 160
    $form.StartPosition = 'CenterScreen'; $form.FormBorderStyle = 'FixedDialog'
    $form.ControlBox = $false
    $label = New-Object System.Windows.Forms.Label
    $label.Text = if ($Mode -eq 'Install') { 'Installing... This may take a minute. Please keep Arena closed.' } else { 'Removing Deck Cleanup... Your backups will be kept.' }
    $label.AutoSize = $false; $label.Dock = 'Fill'; $label.Padding = New-Object System.Windows.Forms.Padding(24)
    $form.Controls.Add($label); $form.Show(); $form.Refresh()
    [System.Windows.Forms.Application]::DoEvents()
    try {
        if ($Mode -eq 'Install') { & "$root/scripts/install.ps1" -GamePath $game *> $logPath }
        else { & "$root/scripts/uninstall.ps1" -GamePath $game *> $logPath }
    } finally { $form.Close(); $form.Dispose() }
    if ($Mode -eq 'Install') { Tell "Installed!`r`n`r`nOpen Arena, go to Decks, and press F8." }
    else { Tell 'Removed. Your decks and saved backups have been kept.' }
} catch {
    $failure = $_
    $failure | Out-String | Add-Content -LiteralPath $logPath
    $friendly = 'Setup could not finish. Check your internet connection and try again.'
    if ($failure.Exception.Message -match 'modified|Unmanaged|changed') { $friendly = 'Another Arena add-on or a changed file is in the way. Setup stopped to protect it. Share the details file below when asking for help.' }
    elseif ($failure.Exception.Message -match 'denied|permission') { $friendly = 'Windows did not allow the change. Run the button again and allow the Windows permission request.' }
    elseif ($failure.Exception.Message -match 'incomplete') { $friendly = 'Some files are missing. Download the app again and choose Extract All before opening Install.' }
    Tell ($friendly + "`r`n`r`nDetails: " + $logPath) 'Setup needs attention'
    exit 1
}

