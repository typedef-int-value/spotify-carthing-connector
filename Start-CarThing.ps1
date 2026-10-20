param(
    [string]$DeviceAddress = "30E3D604D71F"
)

$ErrorActionPreference = "Stop"
$executable = Join-Path $PSScriptRoot "dist\CarThingProbe\CarThingProbe.exe"
if (-not (Test-Path -LiteralPath $executable)) {
    throw "Missing executable: $executable"
}
Push-Location $PSScriptRoot
try {
    & $executable --bridge $DeviceAddress
    if ($LASTEXITCODE -ne 0) {
        throw "CarThingProbe exited with code $LASTEXITCODE"
    }
}
finally {
    Pop-Location
}
