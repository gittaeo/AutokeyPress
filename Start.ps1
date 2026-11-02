$ErrorActionPreference = 'Stop'
try {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        Start-Process powershell.exe -Verb RunAs -ArgumentList ('-NoProfile -ExecutionPolicy Bypass -File "' + $PSCommandPath + '"') -Wait
        exit
    }
    if (-not [Environment]::Is64BitOperatingSystem -or $env:PROCESSOR_ARCHITECTURE -eq 'ARM64') {
        throw 'This package requires Windows x64 (Intel/AMD).'
    }
    $app = Join-Path $PSScriptRoot 'AutoKeyPress.exe'
    if (-not (Test-Path -LiteralPath $app)) {
        throw 'Download the release ZIP and extract ALL files before starting. The source ZIP is not an executable package.'
    }
    if (-not (Test-Path 'HKLM:\SYSTEM\CurrentControlSet\Services\usbip2_ude')) {
        & (Join-Path $PSScriptRoot 'HidRuntime\Install-Driver.ps1')
    }
    Start-Process -FilePath $app -WorkingDirectory $PSScriptRoot
} catch {
    Write-Host $_.Exception.Message -ForegroundColor Red
    exit 1
}

