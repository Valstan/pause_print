param(
  [string]$PublishDir,
  [string]$OutDir = ".\out"
)

$ScriptRoot = $PSScriptRoot
$Root = Split-Path -Parent $ScriptRoot
if (-not $PublishDir) { $PublishDir = Join-Path $Root "src\PausePrint.UI\bin\Release\net8.0-windows\win-x64\publish" }

if (!(Test-Path $OutDir)) { New-Item -ItemType Directory -Path $OutDir | Out-Null }

$ToolPath = Join-Path $Root ".tools"
if (!(Test-Path $ToolPath)) { New-Item -ItemType Directory -Path $ToolPath | Out-Null }

$DotnetLocal = Join-Path $Root ".dotnet\dotnet"
if (Test-Path $DotnetLocal) { $Dotnet = $DotnetLocal } else { $Dotnet = "dotnet" }

$env:WIX_VARIABLES = "PublishDir=$PublishDir"

$WixExe = Join-Path $ToolPath "wix.exe"
if (!(Test-Path $WixExe)) {
  Write-Host "Installing WiX CLI as local dotnet tool..."
  & $Dotnet tool install --tool-path $ToolPath wix | Out-Host
}

$ProductWxs = Join-Path $ScriptRoot "wix\Product.wxs"
$PublishNative = Join-Path $Root "native\PortMonitor\x64\Release"
if (Test-Path $PublishNative) {
  Copy-Item (Join-Path $PublishNative "PausePrintPortMonitor.dll") (Join-Path $PublishDir "PausePrintPortMonitor.dll") -Force -ErrorAction SilentlyContinue
}
& $WixExe build $ProductWxs -d PublishDir=$PublishDir -o (Join-Path $OutDir "PausePrint.msi") | Out-Host

Write-Host "MSI: $(Join-Path $OutDir 'PausePrint.msi')"


