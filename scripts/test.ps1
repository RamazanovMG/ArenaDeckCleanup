$projectRoot = Split-Path $PSScriptRoot
. "$projectRoot/scripts/Common.ps1"
& "$projectRoot/scripts/Get-Dependencies.ps1" -Compiler
$runtimes = @(& dotnet --list-runtimes | ForEach-Object { if ($_ -match '^Microsoft.NETCore.App ([0-9.]+) \[(.+)\]$') { [pscustomobject]@{Version=[version]$Matches[1]; Base=$Matches[2]} } } | Sort-Object Version -Descending)
if (!$runtimes.Count) { throw 'Install .NET 8 or newer.' }
$runtime = Join-Path $runtimes[0].Base $runtimes[0].Version.ToString()
New-Item -ItemType Directory -Path "$projectRoot/dist" -Force | Out-Null
$argsList = @('/nologo','/target:exe','/nostdlib+',"/out:$projectRoot/dist/MassDeleteTests.dll")
foreach ($name in @('System.Private.CoreLib','System.Runtime','System.Console','System.Linq','System.Collections','System.Linq.Expressions','System.ComponentModel.TypeConverter','System.ObjectModel','netstandard')) { $argsList += "/reference:$runtime/$name.dll" }
$game = Resolve-ArenaPath ''
$json = Join-Path $game 'MTGA_Data/Managed/Newtonsoft.Json.dll'
$argsList += "/reference:$json", "$projectRoot/src/BackupRetention.cs", "$projectRoot/tests/BackupRetentionTests.cs", "$projectRoot/src/MassDeletePolicy.cs", "$projectRoot/tests/MassDeleteTests.cs"
Copy-Item -LiteralPath $json -Destination "$projectRoot/dist/Newtonsoft.Json.dll" -Force
& dotnet "$projectRoot/.tools/roslyn/tasks/netcore/bincore/csc.dll" @argsList
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
@{runtimeOptions=@{framework=@{name='Microsoft.NETCore.App'; version=$runtimes[0].Version.ToString()}}} | ConvertTo-Json -Depth 5 | Set-Content "$projectRoot/dist/MassDeleteTests.runtimeconfig.json"
& dotnet "$projectRoot/dist/MassDeleteTests.dll"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }



