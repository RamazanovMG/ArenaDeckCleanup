param([string]$GamePath)
. "$PSScriptRoot/scripts/Common.ps1"
$game = Resolve-ArenaPath $GamePath
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 runtime or SDK before building.' }
& "$PSScriptRoot/scripts/Get-Dependencies.ps1" -Compiler
$managed = Join-Path $game 'MTGA_Data/Managed'
New-Item -ItemType Directory -Path "$PSScriptRoot/dist" -Force | Out-Null
$refs = @('mscorlib','netstandard','System','System.Core','Core','SharedClientCore','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.TextRenderingModule','UnityEngine.UI','UnityEngine.UIModule','Unity.InputSystem','Newtonsoft.Json','Wizards.Arena.Promises')
$compilerArgs = @('/nologo','/target:library','/nostdlib+','/optimize+',"/out:$PSScriptRoot/dist/ArenaMassDelete.dll")
foreach ($name in $refs) { $compilerArgs += "/reference:$managed/$name.dll" }
$compilerArgs += "/reference:$PSScriptRoot/.tools/bepinex/BepInEx/core/BepInEx.dll"
$compilerArgs += "$PSScriptRoot/src/MassDeletePolicy.cs", "$PSScriptRoot/src/ArenaMassDelete.cs"
& dotnet "$PSScriptRoot/.tools/roslyn/tasks/netcore/bincore/csc.dll" @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed against this Arena version.' }
New-Item -ItemType Directory -Path "$PSScriptRoot/payload" -Force | Out-Null
Copy-Item -LiteralPath "$PSScriptRoot/dist/ArenaMassDelete.dll" -Destination "$PSScriptRoot/payload/ArenaMassDelete.dll" -Force
