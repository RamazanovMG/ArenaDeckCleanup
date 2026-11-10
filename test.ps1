. "$PSScriptRoot/scripts/Common.ps1"
& "$PSScriptRoot/scripts/Get-Dependencies.ps1" -Compiler
$runtimes = @(& dotnet --list-runtimes | ForEach-Object { if ($_ -match '^Microsoft.NETCore.App ([0-9.]+) \[(.+)\]$') { [pscustomobject]@{Version=[version]$Matches[1]; Base=$Matches[2]} } } | Sort-Object Version -Descending)
if (!$runtimes.Count) { throw 'Install .NET 8 or newer.' }
$runtime = Join-Path $runtimes[0].Base $runtimes[0].Version.ToString()
New-Item -ItemType Directory -Path "$PSScriptRoot/dist" -Force | Out-Null
$argsList = @('/nologo','/target:exe','/nostdlib+',"/out:$PSScriptRoot/dist/MassDeleteTests.dll")
foreach ($name in @('System.Private.CoreLib','System.Runtime','System.Console','System.Linq','System.Collections','netstandard')) { $argsList += "/reference:$runtime/$name.dll" }
$argsList += "$PSScriptRoot/src/MassDeletePolicy.cs", "$PSScriptRoot/tests/MassDeleteTests.cs"
& dotnet "$PSScriptRoot/.tools/roslyn/tasks/netcore/bincore/csc.dll" @argsList
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
@{runtimeOptions=@{framework=@{name='Microsoft.NETCore.App'; version=$runtimes[0].Version.ToString()}}} | ConvertTo-Json -Depth 5 | Set-Content "$PSScriptRoot/dist/MassDeleteTests.runtimeconfig.json"
& dotnet "$PSScriptRoot/dist/MassDeleteTests.dll"
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
