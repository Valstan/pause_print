param(
  [switch]$SelfContained,
  [switch]$SingleFile
)

$Configuration = "Release"
$Rid = "win-x64"
$scFlag = $false
if ($SelfContained) { $scFlag = $true }

Write-Host "Restoring..."
& .\.dotnet\dotnet restore

Write-Host "Publishing PausePrint.UI..."
$proj = "src/" + "PausePrint.UI/" + "PausePrint.UI.csproj"
$args = @("publish", $proj, "-c", $Configuration, "-r", $Rid, "--no-restore")
if ($scFlag) { $args += @("--self-contained", "true") } else { $args += @("--self-contained", "false") }
if ($SingleFile) { $args += @("-p:PublishSingleFile=true", "-p:IncludeAllContentForSelfExtract=true") }
& .\.dotnet\dotnet $args

Write-Host "Done. Output in src/PausePrint.UI/bin/$Configuration/$Rid/publish"


