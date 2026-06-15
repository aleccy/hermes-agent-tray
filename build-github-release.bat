param(
    [string]$Tag = ""
)

$ErrorActionPreference = "Stop"

# Get version from project
$csproj = Get-Content "HermesAgentTray.csproj" -Raw
if ($csproj -match '<Version>([^<]+)</Version>') {
    $Version = $Matches[1]
} elseif ($csproj -match '<AssemblyVersion>([^<]+)</AssemblyVersion>') {
    $Version = $Matches[1]
} else {
    $Version = "1.0.0"
}

if ($Tag -eq "") {
    $Tag = "v$Version"
}

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Building HermesAgentTray Release" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "  Tag:     $Tag" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

# Clean previous builds
if (Test-Path "bin\Release\publish-release") {
    Remove-Item "bin\Release\publish-release" -Recurse -Force
}

# Build self-contained single file
Write-Host "`n[1/3] Publishing self-contained..." -ForegroundColor Yellow
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:ReadyToRun=false `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:Version=$Version `
    -o ".\bin\Release\publish-release\"

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

# Create release package
$DistDir = ".\dist\$Tag"
if (Test-Path $DistDir) {
    Remove-Item $DistDir -Recurse -Force
}
New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

# Copy exe and README
Copy-Item "bin\Release\publish-release\HermesAgentTray.exe" $DistDir
if (Test-Path "README.md") {
    Copy-Item "README.md" $DistDir
}

# Create zip
$ZipName = "HermesAgentTray-$Tag-win-x64.zip"
$ZipPath = ".\dist\$ZipName"
if (Test-Path $ZipPath) {
    Remove-Item $ZipPath -Force
}

Write-Host "`n[2/3] Creating zip package..." -ForegroundColor Yellow
Compress-Archive -Path "$DistDir\*" -DestinationPath $ZipPath -Force

# Generate checksum
Write-Host "`n[3/3] Generating checksum..." -ForegroundColor Yellow
$Hash = (Get-FileHash $ZipPath -Algorithm SHA256).Hash.ToLower()
$HashFile = ".\dist\$ZipName.sha256"
"$Hash  $ZipName" | Out-File $HashFile -Encoding utf8

# Summary
$ZipSize = (Get-Item $ZipPath).Length / 1MB
Write-Host "`n========================================" -ForegroundColor Green
Write-Host "  Release build complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Package:  $ZipPath" -ForegroundColor White
Write-Host "  Size:     $([math]::Round($ZipSize, 1)) MB" -ForegroundColor White
Write-Host "  SHA256:   $Hash" -ForegroundColor White
Write-Host ""
Write-Host "  GitHub Release command:" -ForegroundColor Yellow
Write-Host "  gh release create $Tag $ZipPath $HashFile --title `"HermesAgentTray $Tag`" --notes `"Release $Tag`"" -ForegroundColor White
Write-Host ""
Write-Host "  Or upload manually at:" -ForegroundColor Yellow
Write-Host "  https://github.com/<owner>/<repo>/releases/new?tag=$Tag" -ForegroundColor White
