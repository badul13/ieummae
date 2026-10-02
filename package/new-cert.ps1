# 자체 서명 인증서 - 본인 PC 전용 패키지 서명용 (한 번만)
# 1) 현재 사용자 저장소에 코드 서명 인증서 생성, pfx 로 내보냄 (package/ieummae.pfx, gitignore)
# 2) 공개 인증서를 로컬 컴퓨터 "신뢰할 수 있는 사람"에 등록 - 관리자 권한 필요
param([string]$Password = "ieummae")
$ErrorActionPreference = "Stop"
$pfx = Join-Path $PSScriptRoot "ieummae.pfx"
$cer = Join-Path $PSScriptRoot "ieummae.cer"

$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object Subject -eq "CN=ieummae" | Select-Object -First 1
if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject "CN=ieummae" -KeyUsage DigitalSignature `
        -FriendlyName "ieummae package signing" -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") -NotAfter (Get-Date).AddYears(10)
}
$secure = ConvertTo-SecureString $Password -AsPlainText -Force
Export-PfxCertificate -Cert $cert -FilePath $pfx -Password $secure | Out-Null
Export-Certificate -Cert $cert -FilePath $cer | Out-Null

$admin = ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if ($admin) {
    Import-Certificate -FilePath $cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null
    "인증서 생성·신뢰 등록 완료"
} else {
    "인증서 생성 완료. 신뢰 등록은 관리자 PowerShell 에서: Import-Certificate -FilePath `"$cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
}
