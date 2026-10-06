param([string]$WorkDirectory,[string]$MSBuild)
$ErrorActionPreference='Stop'
$workspaceRoot=Split-Path (Split-Path $PSScriptRoot -Parent) -Parent
if(!$WorkDirectory){$WorkDirectory=Join-Path $workspaceRoot 'artifacts/native-core/reproducible-chr-fix'}
$WorkDirectory=[IO.Path]::GetFullPath($WorkDirectory)
if(Test-Path -LiteralPath $WorkDirectory){throw 'Use a fresh work directory; existing source and build evidence are preserved.'}
if(!$MSBuild){
    $vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
    $MSBuild=@(& $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -find 'MSBuild/**/Bin/MSBuild.exe' | Select-Object -First 1)[0]
}
if(!$MSBuild -or !(Test-Path -LiteralPath $MSBuild)){throw 'Visual Studio C++ Build Tools with the v143 toolset and Windows SDK are required.'}
& git clone --depth 1 --branch 0.9.9 https://github.com/SourMesen/Mesen.git $WorkDirectory
if($LASTEXITCODE){throw 'Cannot fetch pinned Mesen source.'}
$revision=(& git -C $WorkDirectory rev-parse HEAD).Trim()
if($revision -ne 'f3a18bed018fa853627e0e15d02a3f2ba4960222'){throw 'Unexpected upstream revision.'}
$patch=Join-Path $PSScriptRoot 'mesen-0.9.9-chr-ram.patch'
& git -C $WorkDirectory apply --check $patch
if($LASTEXITCODE){throw 'Native patch validation failed.'}
& git -C $WorkDirectory apply $patch
if($LASTEXITCODE){throw 'Native patch application failed.'}
$buildLog=$WorkDirectory+'.build.log'
& $MSBuild (Join-Path $WorkDirectory 'Mesen.sln') /t:InteropDLL /p:Configuration=Release /p:Platform=x64 /p:PlatformToolset=v143 /m:4 /nologo /verbosity:minimal *> $buildLog
if($LASTEXITCODE){throw "Native build failed; see $buildLog."}
$library=Join-Path $WorkDirectory 'bin/x64/Release/MesenCore.dll'
& (Join-Path $PSScriptRoot 'test-chr-classification.ps1') -Library $library -ExpectedChecks 24576
Get-FileHash -LiteralPath $library -Algorithm SHA256
Write-Output 'Built and tested only; the application library was not replaced automatically.'
