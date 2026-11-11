$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot
Add-Type -AssemblyName System.IO.Compression
New-Item -ItemType Directory -Path "$projectRoot/dist" -Force | Out-Null
function Write-Package([string]$Destination, [object[]]$Files) {
    $stream=[IO.File]::Open($Destination,[IO.FileMode]::Create)
    $zip=New-Object IO.Compression.ZipArchive($stream,[IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach($file in $Files) {
            $relative=$file.FullName.Substring($projectRoot.Length+1).Replace('\','/')
            $entry=$zip.CreateEntry("ArenaDeckCleanup/$relative")
            $inputStream=$file.OpenRead(); $outputStream=$entry.Open()
            try {$inputStream.CopyTo($outputStream)} finally {$outputStream.Dispose();$inputStream.Dispose()}
        }
    } finally {$zip.Dispose();$stream.Dispose()}
}
$publicFiles=@(Get-Item "$projectRoot/README.md","$projectRoot/Install.cmd","$projectRoot/Uninstall.cmd","$projectRoot/payload/ArenaMassDelete.dll")
foreach($name in @('Common.ps1','Get-Dependencies.ps1','Setup.ps1','install.ps1','uninstall.ps1')) { $publicFiles+=Get-Item "$PSScriptRoot/$name" }
$publicFiles += @(Get-ChildItem "$projectRoot/docs/media" -File)
Write-Package "$projectRoot/dist/ArenaDeckCleanup.zip" $publicFiles
$sourceFiles=@(Get-Item "$projectRoot/.gitignore","$projectRoot/README.md","$projectRoot/Install.cmd","$projectRoot/Uninstall.cmd")
$sourceFiles+=@(Get-ChildItem "$projectRoot/scripts","$projectRoot/src","$projectRoot/tests","$projectRoot/docs","$projectRoot/payload","$projectRoot/assets" -File)
$sourceFiles += @(Get-ChildItem "$projectRoot/docs/media" -File)
Write-Package "$projectRoot/dist/ArenaDeckCleanup-source.zip" $sourceFiles
Write-Output 'Created user and source ZIPs in dist.'

