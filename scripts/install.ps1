# 이음매 설치 - 관리자 PowerShell 에서 실행
#   1) 앱(NativeAOT)·탐색기 확장(Rust) 빌드
#   2) 설치 폴더(%LOCALAPPDATA%\Programs\ieummae)에 복사
#   3) 자체 서명 인증서 만들고 신뢰 등록, sparse package 서명·등록 → Win11 우클릭 메뉴
#   4) 아이콘 표시 등록 (HKLM - 관리자 필요), 탐색기 다시 시작
# -BuildOnly : 1)·2)만, 설치 폴더 대신 out\install 에 (시스템 변경 없음 - 확인용)
param([switch]$BuildOnly, [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\ieummae"))
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $BuildOnly -and -not $admin) { throw "관리자 PowerShell 에서 실행 (인증서 신뢰·아이콘 표시 등록에 필요)" }
if ($BuildOnly) { $InstallDir = Join-Path $root "out\install" }

# 1) 빌드
& (Join-Path $PSScriptRoot "publish.ps1") | Out-Host
$cargo = Join-Path $env:USERPROFILE ".cargo\bin\cargo.exe"
if (-not (Test-Path $cargo)) { $cargo = "cargo" }
Push-Location (Join-Path $root "shell")
& $cargo build --release
if ($LASTEXITCODE -ne 0) { Pop-Location; throw "cargo build 실패" }
Pop-Location

# 2) 복사 - 실행 중인 캐시는 먼저 종료 (파일 잠금)
Get-Process ieummae-cache -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force (Join-Path $InstallDir "overlays") | Out-Null
Copy-Item (Join-Path $root "out\app\*") $InstallDir -Recurse -Force -Exclude *.pdb
$shellOut = Join-Path $root "shell\target\release"
Copy-Item (Join-Path $shellOut "ieummae_menu.dll"), (Join-Path $shellOut "ieummae_overlay.dll"), (Join-Path $shellOut "ieummae-cache.exe") $InstallDir -Force
Copy-Item (Join-Path $root "package\overlays\*.ico") (Join-Path $InstallDir "overlays") -Force
"설치 폴더 준비 - $InstallDir"
if ($BuildOnly) { return }

# 3) 인증서 + 패키지 등록 (우클릭 메뉴)
$pfx = Join-Path $root "package\ieummae.pfx"
if (-not (Test-Path $pfx)) { & (Join-Path $root "package\new-cert.ps1") | Out-Host }
& (Join-Path $root "package\build-package.ps1") | Out-Host
Get-AppxPackage -Name "badul13.ieummae" | Remove-AppxPackage
Add-AppxPackage -Path (Join-Path $root "out\package\ieummae.msix") -ExternalLocation $InstallDir
"우클릭 메뉴 등록"

# 4) 아이콘 표시 - CLSID 는 현재 사용자, 표시 목록은 HKLM (탐색기는 앞 15개만 씀 - 이름 앞 공백으로 순서 앞쪽)
$dll = Join-Path $InstallDir "ieummae_overlay.dll"
$overlays = [ordered]@{
    "1Conflict" = "{2D77E08E-C1BB-4777-991F-7F663A75BA5B}"
    "2Modified" = "{05EC32BD-A419-4539-A6AE-9B60DDA05C95}"
    "3Added"    = "{557BC428-426C-4932-9FFB-BF3DA931FF33}"
    "4Normal"   = "{970154D1-6709-4CD0-9D88-7FF061ECC42E}"
}
$list = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers"
foreach ($name in $overlays.Keys) {
    $clsid = $overlays[$name]
    $key = "HKCU:\Software\Classes\CLSID\$clsid\InprocServer32"
    New-Item -Path $key -Force | Out-Null
    Set-Item -Path $key -Value $dll
    New-ItemProperty -Path $key -Name ThreadingModel -Value Apartment -Force | Out-Null
    $entry = Join-Path $list "   ieummae$name"
    New-Item -Path $entry -Force | Out-Null
    Set-Item -Path $entry -Value $clsid
}
"아이콘 표시 등록"

# 아이콘 표시는 탐색기를 다시 시작해야 반영
Stop-Process -Name explorer -Force
"설치 완료 - 탐색기 다시 시작됨"
