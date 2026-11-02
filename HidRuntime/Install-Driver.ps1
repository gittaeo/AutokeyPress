#Requires -RunAsAdministrator
$ErrorActionPreference = 'Stop'
$installer = Join-Path $PSScriptRoot 'USBip-0.9.8.1-x64.exe'
$signature = Get-AuthenticodeSignature -LiteralPath $installer
if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notlike '*Cloudyne Systems*') {
    throw 'The USB/IP installer signature is invalid or unexpected. Installation stopped.'
}
$manifest = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'package-manifest.json') -Raw | ConvertFrom-Json
$expected = $manifest.files.'USBip-0.9.8.1-x64.exe'.sha256
if ((Get-FileHash -LiteralPath $installer -Algorithm SHA256).Hash -ne $expected) {
    throw 'The USB/IP installer hash does not match the prepared package.'
}
# Installs the signed client driver + VC runtime. No automatic restart or GUI component.
$arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/TYPE=compact', '/NORESTART', '/RESTARTEXITCODE=3010', '/TASKS=vcredist', ('/LOG="' + (Join-Path $PSScriptRoot 'driver-install.log') + '"'))
$process = Start-Process -FilePath $installer -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
Write-Output ('InstallerExitCode: ' + $process.ExitCode)
if ($process.ExitCode -notin @(0, 3010)) { throw ('Driver installation failed. Inspect driver-install.log. Exit code: ' + $process.ExitCode) }
if ($process.ExitCode -eq 3010) { Write-Output 'Windows restart required. No automatic restart was performed.' }
