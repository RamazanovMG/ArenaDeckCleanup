param([string]$GamePath)
$projectRoot = Split-Path $PSScriptRoot
. "$projectRoot/scripts/Common.ps1"
$game = Resolve-ArenaPath $GamePath
if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) { throw 'Install the .NET 8 runtime or SDK before building.' }
& "$projectRoot/scripts/Get-Dependencies.ps1" -Compiler
$managed = Join-Path $game 'MTGA_Data/Managed'
New-Item -ItemType Directory -Path "$projectRoot/dist" -Force | Out-Null
$refs = @('mscorlib','netstandard','System','System.Core','Core','SharedClientCore','UnityEngine','UnityEngine.CoreModule','UnityEngine.IMGUIModule','UnityEngine.TextRenderingModule','UnityEngine.UI','UnityEngine.UIModule','Unity.InputSystem','Newtonsoft.Json','Wizards.Arena.Promises')
$compilerArgs = @('/nologo','/target:library','/nostdlib+','/optimize+',"/out:$projectRoot/dist/ArenaMassDelete.dll")
foreach ($name in $refs) { $compilerArgs += "/reference:$managed/$name.dll" }
$compilerArgs += "/reference:$projectRoot/.tools/bepinex/BepInEx/core/BepInEx.dll"
$compilerArgs += "$projectRoot/src/MassDeletePolicy.cs", "$projectRoot/src/ArenaMassDelete.cs"
& dotnet "$projectRoot/.tools/roslyn/tasks/netcore/bincore/csc.dll" @compilerArgs
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed against this Arena version.' }
New-Item -ItemType Directory -Path "$projectRoot/payload" -Force | Out-Null
Copy-Item -LiteralPath "$projectRoot/dist/ArenaMassDelete.dll" -Destination "$projectRoot/payload/ArenaMassDelete.dll" -Force

