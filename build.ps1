param([string]$OutputDirectory = (Join-Path $PSScriptRoot 'dist\AutoKeyPress-win-x64'))
$ErrorActionPreference = 'Stop'
dotnet publish (Join-Path $PSScriptRoot 'src\AutoKeyPress.csproj') -c Release -r win-x64 --self-contained true -p:RuntimeFrameworkVersion=8.0.33 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $OutputDirectory --nologo
if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
Copy-Item (Join-Path $PSScriptRoot 'Start.cmd'), (Join-Path $PSScriptRoot 'Start.ps1'), (Join-Path $PSScriptRoot 'README.md') -Destination $OutputDirectory
Copy-Item (Join-Path $PSScriptRoot 'HidRuntime') -Destination $OutputDirectory -Recurse -Force
Copy-Item (Join-Path $PSScriptRoot 'licenses') -Destination $OutputDirectory -Recurse -Force
$manifest = Get-Content (Join-Path $OutputDirectory 'HidRuntime\package-manifest.json') -Raw | ConvertFrom-Json
foreach ($entry in $manifest.files.PSObject.Properties) {
    $actual = (Get-FileHash (Join-Path $OutputDirectory ('HidRuntime\' + $entry.Name)) -Algorithm SHA256).Hash
    if ($actual -ne $entry.Value.sha256) { throw ('Hash mismatch: ' + $entry.Name) }
}
$zip = Join-Path (Split-Path $OutputDirectory -Parent) 'AutoKeyPress-win-x64.zip'
Compress-Archive -Path (Join-Path $OutputDirectory '*') -DestinationPath $zip -Force
Get-FileHash -LiteralPath $zip -Algorithm SHA256

