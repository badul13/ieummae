# sparse package 만들기 - 매니페스트 + 로고만 담은 msix, 인증서 있으면 서명
# 결과: out/package/ieummae.msix
param([string]$Pfx = (Join-Path $PSScriptRoot "ieummae.pfx"), [string]$Password = "ieummae")
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$kit = Get-ChildItem "${env:ProgramFiles(x86)}\Windows Kits\10\bin\*\x64\makeappx.exe" | Sort-Object FullName | Select-Object -Last 1
if (-not $kit) { throw "Windows SDK makeappx.exe 없음" }
$bin = $kit.DirectoryName

$stage = Join-Path $root "out\package\stage"
Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force (Join-Path $stage "Assets") | Out-Null
Copy-Item (Join-Path $PSScriptRoot "AppxManifest.xml") $stage
Copy-Item (Join-Path $PSScriptRoot "Assets\*.png") (Join-Path $stage "Assets")

$msix = Join-Path $root "out\package\ieummae.msix"
& "$bin\makeappx.exe" pack /d $stage /p $msix /nv /o | Out-Host
if ($LASTEXITCODE -ne 0) { throw "makeappx 실패" }

if (Test-Path $Pfx) {
    & "$bin\signtool.exe" sign /fd SHA256 /f $Pfx /p $Password $msix | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "signtool 실패" }
    "서명 완료 - $msix"
} else {
    "서명 안 함 (인증서 없음, new-cert.ps1 먼저) - $msix"
}
