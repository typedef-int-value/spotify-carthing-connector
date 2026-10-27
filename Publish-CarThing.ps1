[CmdletBinding()]
param(
    [string]$OutputDirectory = 'dist/carthing-connector'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = $PSScriptRoot
$lock = $null
$outputPath = if ([IO.Path]::IsPathRooted($OutputDirectory)) { $OutputDirectory } else { Join-Path $root $OutputDirectory }
$destinationDirectory = [IO.Path]::GetFullPath($outputPath)
$destination = Join-Path $destinationDirectory 'carthing-connector.exe'

function Assert-AppStopped {
    foreach ($app in @(Get-Process -Name 'carthing-connector' -ErrorAction SilentlyContinue)) {
        if (-not $app.Path -or [string]::Equals($app.Path, $destination, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Exit the target carthing-connector from its tray menu, or choose a different output directory.'
        }
    }
}

function Read-SourceState {
    $commit = & git -C $root rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the source commit.' }
    $status = @(& git -C $root status --porcelain=v1 --untracked-files=all)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot read the working tree status.' }
    return [ordered]@{ commit = "$commit".Trim(); dirty = $status.Count -gt 0; changes = $status }
}

function Invoke-DotNetStep([string]$LogPath, [string[]]$Arguments) {
    # Capture stderr without treating normal native diagnostic output as a PowerShell error.
    $previousPreference = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try {
        & dotnet @Arguments > $LogPath 2>&1
        $result = $LASTEXITCODE
    }
    finally { $ErrorActionPreference = $previousPreference }
    if ($result -ne 0) {
        Get-Content -LiteralPath $LogPath -Tail 30 | Write-Host
        throw "dotnet failed with exit code $result. Log: $LogPath"
    }
}

Push-Location $root
try {
    Assert-AppStopped
    $null = Get-Command dotnet -ErrorAction Stop
    $null = Get-Command git -ErrorAction Stop
    $publishRoot = Join-Path $root 'analysis/publish'
    $null = New-Item -ItemType Directory -Path $publishRoot -Force
    # Prevent two publishers from replacing the same distribution concurrently.
    $lock = [IO.File]::Open((Join-Path $publishRoot 'publish.lock'), 'OpenOrCreate', 'ReadWrite', 'None')
    $buildId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
    $buildDirectory = Join-Path $publishRoot $buildId
    $staging = Join-Path $buildDirectory 'staging'
    $null = New-Item -ItemType Directory -Path $staging -Force
    $source = Read-SourceState

    Write-Host 'Running protocol and adapter tests...'
    Invoke-DotNetStep (Join-Path $buildDirectory 'tests.log') @(
        'run', '--project', 'tests/CarThing.Core.Tests/CarThing.Core.Tests.csproj', '-c', 'Release')

    Write-Host 'Publishing the Windows x64 executable...'
    Invoke-DotNetStep (Join-Path $buildDirectory 'publish.log') @(
        'publish', 'windows-probe/CarThingProbe.csproj', '-c', 'Release', '-r', 'win-x64',
        '--self-contained', 'false', '-o', $staging)
    $candidate = Join-Path $staging 'carthing-connector.exe'
    if (-not (Test-Path -LiteralPath $candidate -PathType Leaf) -or (Get-Item -LiteralPath $candidate).Length -eq 0) {
        throw 'Publish did not produce a nonempty carthing-connector.exe.'
    }
    $after = Read-SourceState
    if (($source | ConvertTo-Json -Compress) -ne ($after | ConvertTo-Json -Compress)) {
        throw 'Git state changed during publishing. Run the script again.'
    }
    Assert-AppStopped
    $hash = (Get-FileHash -LiteralPath $candidate -Algorithm SHA256).Hash
    $record = [ordered]@{
        builtAtUtc = [DateTime]::UtcNow.ToString('O')
        source = $source
        configuration = 'Release'
        runtime = 'win-x64'
        selfContained = $false
        requiredRuntime = 'Microsoft.WindowsDesktop.App 10.0 (x64)'
        executable = [IO.Path]::GetRelativePath($root, $destination)
        sha256 = $hash
        bytes = (Get-Item -LiteralPath $candidate).Length
        logs = "analysis/publish/$buildId"
    }
    $recordPath = Join-Path $buildDirectory 'build-info.json'
    $record | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $recordPath -Encoding UTF8
    $null = New-Item -ItemType Directory -Path $destinationDirectory -Force
    # Replace on the same volume only after every build step succeeds. Keep the old EXE on failure.
    if (Test-Path -LiteralPath $destination) {
        [IO.File]::Replace($candidate, $destination, [NullString]::Value)
    }
    else { [IO.File]::Move($candidate, $destination) }
    Copy-Item -LiteralPath $recordPath -Destination ($destinationDirectory + '.build.json') -Force
    Write-Host "Published: $destination"
    Write-Host "SHA256: $hash"
    Write-Host "Build record: $recordPath"
    if ($source.dirty) { Write-Warning 'This build includes uncommitted source changes. See the build record.' }
}
finally {
    if ($null -ne $lock) { $lock.Dispose() }
    Pop-Location
}
