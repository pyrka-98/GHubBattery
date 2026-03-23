$projectDir = Join-Path $PSScriptRoot "src"
$publishDir = Join-Path $PSScriptRoot "publish"

Write-Host "Building GHubBattery..."

dotnet publish "$projectDir\GHubBattery.csproj" /p:PublishProfile=win-x64 --nologo

if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed."
    exit 1
}

$exe = Join-Path $publishDir "GHubBattery.exe"

if (Test-Path $exe) {
    $size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
    Write-Host "Done! $exe"
    Write-Host "Size: $size MB"
} else {
    Write-Host "Exe not found at expected path."
}