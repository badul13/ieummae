# NativeAOT 배포 - out/app/ieummae.exe (+ Skia·HarfBuzz 네이티브 DLL)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

# .NET 10 SDK - 사용자 영역 설치본이 있으면 우선
$userDotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet"
if (Test-Path (Join-Path $userDotnet "dotnet.exe")) { $env:PATH = "$userDotnet;$env:PATH"; $env:DOTNET_ROOT = $userDotnet }
# ILCompiler 가 PATH 에서 vswhere.exe 탐색 - VS Installer 폴더 추가
$vs = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer"
if (Test-Path $vs) { $env:PATH = "$env:PATH;$vs" }

dotnet publish (Join-Path $root "src\Ieummae.App") -c Release -o (Join-Path $root "out\app")
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Get-Item (Join-Path $root "out\app\ieummae.exe") | Select-Object Name, @{ n = "MB"; e = { [math]::Round($_.Length / 1MB, 1) } }
