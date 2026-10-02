# 이음매 제거 - 관리자 PowerShell 에서 실행
# 패키지(우클릭 메뉴)·아이콘 표시 등록 해제, 캐시 종료, 탐색기 다시 시작 후 설치 폴더 삭제
# 인증서는 남김 (-RemoveCert 로 함께 삭제)
param([switch]$RemoveCert, [string]$InstallDir = (Join-Path $env:LOCALAPPDATA "Programs\ieummae"))
$ErrorActionPreference = "Stop"
$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $admin) { throw "관리자 PowerShell 에서 실행" }

Get-AppxPackage -Name "badul13.ieummae" | Remove-AppxPackage
$list = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\ShellIconOverlayIdentifiers"
Get-ChildItem $list | Where-Object { $_.PSChildName.Trim().StartsWith("ieummae") } | Remove-Item -Recurse -Force
foreach ($clsid in "{2D77E08E-C1BB-4777-991F-7F663A75BA5B}", "{05EC32BD-A419-4539-A6AE-9B60DDA05C95}", "{557BC428-426C-4932-9FFB-BF3DA931FF33}", "{970154D1-6709-4CD0-9D88-7FF061ECC42E}") {
    Remove-Item "HKCU:\Software\Classes\CLSID\$clsid" -Recurse -Force -ErrorAction SilentlyContinue
}
Get-Process ieummae-cache, ieummae -ErrorAction SilentlyContinue | Stop-Process -Force

# 탐색기가 오버레이 DLL 을 놓아야 폴더를 지울 수 있음
Stop-Process -Name explorer -Force
Start-Sleep -Seconds 2
if (-not (Get-Process explorer -ErrorAction SilentlyContinue)) { Start-Process explorer }
Remove-Item $InstallDir -Recurse -Force -ErrorAction SilentlyContinue

if ($RemoveCert) {
    Get-ChildItem Cert:\LocalMachine\TrustedPeople, Cert:\CurrentUser\My | Where-Object Subject -eq "CN=ieummae" | Remove-Item
}
"제거 완료"
