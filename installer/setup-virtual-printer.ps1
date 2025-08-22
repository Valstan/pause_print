param(
  [string]$TargetPrinter = "Pantum M6500 Series",
  [string]$VirtualPrinter = "PausePrint Virtual",
  [string]$ShareName = "PausePrint_Target"
)

Write-Host "Configuring virtual printer forwarding to: $TargetPrinter"

# Ensure the target printer exists
$tp = Get-Printer -Name $TargetPrinter -ErrorAction SilentlyContinue
if (-not $tp) { throw "Target printer not found: $TargetPrinter" }

# Share target printer locally (if not already)
if (-not $tp.Shared) {
  Write-Host "Sharing target printer as: $ShareName"
  Set-Printer -Name $TargetPrinter -Shared $true -ShareName $ShareName
}

$server = $env:COMPUTERNAME
$portName = "\\$server\$ShareName"

# Create a local port that points to the shared printer
if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {
  Write-Host "Creating local port: $portName"
  try {
    Add-PrinterPort -Name $portName -ErrorAction Stop
  } catch {
    Write-Warning "Add-PrinterPort failed, falling back to registry creation of Local Port."
    $regPath = 'HKLM:\SYSTEM\CurrentControlSet\Control\Print\Monitors\Local Port\Ports'
    if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
    New-ItemProperty -Path $regPath -Name $portName -PropertyType String -Value "" -Force | Out-Null
    # Restart Print Spooler to pick up new local port
    Write-Host "Restarting Spooler..."
    Stop-Service -Name Spooler -Force -ErrorAction SilentlyContinue
    Start-Sleep -Seconds 2
    Start-Service -Name Spooler
  }
}

# Verify port exists after fallback
if (-not (Get-PrinterPort -Name $portName -ErrorAction SilentlyContinue)) {
  throw "Port '$portName' still not visible. Run PowerShell as Administrator and retry."
}

# Create virtual printer using the same driver as the target
if (-not (Get-Printer -Name $VirtualPrinter -ErrorAction SilentlyContinue)) {
  Write-Host "Creating printer: $VirtualPrinter"
  Add-Printer -Name $VirtualPrinter -DriverName $tp.DriverName -PortName $portName
}

Write-Host "Done. Use '$VirtualPrinter' in the app for reliable interception."


