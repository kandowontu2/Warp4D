$ErrorActionPreference = 'Stop'

$workspace = $PSScriptRoot
$projectDirectory = Join-Path $workspace 'native\Warp4D'
$project = Join-Path $projectDirectory 'Warp4D.csproj'
$core = Join-Path $projectDirectory 'MesenCore.dll'
$release = Join-Path $workspace 'release'
$publish = Join-Path $release 'publish'
$compiled = Join-Path $release 'compiled'

if (-not (Test-Path -LiteralPath $core)) {
    throw 'MesenCore.dll is missing from native\Warp4D. Restore the repository copy before building.'
}

New-Item -ItemType Directory -Path $release -Force | Out-Null
# Force compilation of embedded profile JSON, then bundle those exact outputs.
# Reusing an incremental publish can otherwise retain an older embedded resource
# after copying a generated profile with its original timestamp.
dotnet build $project -c Release -r win-x64 --self-contained true --no-incremental -o $compiled
if ($LASTEXITCODE -ne 0) {
    throw "dotnet build failed with exit code $LASTEXITCODE."
}
dotnet publish $project -c Release -r win-x64 --self-contained true --no-build "-p:OutputPath=$compiled\" -o $publish
if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE."
}

Copy-Item -LiteralPath (Join-Path $publish 'Warp4D.exe') -Destination (Join-Path $release 'Warp4D.exe') -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory 'LICENSE.txt') -Destination (Join-Path $release 'LICENSE.txt') -Force
Copy-Item -LiteralPath (Join-Path $projectDirectory 'THIRD_PARTY_NOTICES.md') -Destination (Join-Path $release 'THIRD_PARTY_NOTICES.md') -Force

$exe = Get-Item -LiteralPath (Join-Path $release 'Warp4D.exe')
Write-Host "Built $($exe.FullName) ($([Math]::Round($exe.Length / 1MB, 1)) MB)"
