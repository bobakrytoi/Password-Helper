<#
.SYNOPSIS
    Build / test / run / publish helper for Password helper.

.EXAMPLE
    .\build.ps1 build            # Debug build
    .\build.ps1 build -Release   # Release build
    .\build.ps1 test             # run unit tests
    .\build.ps1 run              # build + launch (tray app)
    .\build.ps1 publish          # framework-dependent Release into .\dist
    .\build.ps1 publish -SelfContained   # self-contained single-file .exe into .\dist
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [ValidateSet('build', 'test', 'run', 'publish', 'clean')]
    [string]$Task = 'build',

    [switch]$Release,
    [switch]$SelfContained
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$proj = Join-Path $root 'src\PasswordHelper\PasswordHelper.csproj'
$testProj = Join-Path $root 'tests\PasswordHelper.Tests\PasswordHelper.Tests.csproj'
$config = if ($Release -or $Task -eq 'publish') { 'Release' } else { 'Debug' }

# Prefer a system dotnet with a 10.x SDK; otherwise fall back to the per-user install.
function Resolve-Dotnet {
    $candidates = @(
        (Get-Command dotnet -ErrorAction SilentlyContinue).Source,
        (Join-Path $env:LOCALAPPDATA 'Microsoft\dotnet\dotnet.exe')
    )
    foreach ($c in $candidates) {
        if ($c -and (Test-Path $c)) {
            $sdks = & $c --list-sdks 2>$null
            if ($sdks -match '^10\.') { return $c }
        }
    }
    throw "No .NET 10 SDK found. Install it from https://dot.net (channel 10.0)."
}

$dotnet = Resolve-Dotnet
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_NOLOGO = '1'
Write-Host "Using dotnet: $dotnet ($config)" -ForegroundColor Cyan

switch ($Task) {
    'build'   { & $dotnet build $proj -c $config }
    'test'    { & $dotnet test  $testProj -c $config }
    'run'     { & $dotnet run   --project $proj -c $config }
    'clean'   {
        Get-ChildItem $root -Include bin, obj -Recurse -Directory | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
        Remove-Item (Join-Path $root 'dist') -Recurse -Force -ErrorAction SilentlyContinue
        Write-Host 'Cleaned bin/obj/dist.' -ForegroundColor Green
    }
    'publish' {
        $dist = Join-Path $root 'dist'
        $args = @($proj, '-c', 'Release', '-r', 'win-x64', '-o', $dist)
        if ($SelfContained) {
            $args += @('--self-contained', 'true',
                       '-p:PublishSingleFile=true',
                       '-p:IncludeNativeLibrariesForSelfExtract=true',
                       '-p:EnableCompressionInSingleFile=true')
            Write-Host 'Publishing SELF-CONTAINED single file (no .NET runtime required on target).' -ForegroundColor Yellow
        } else {
            $args += @('--self-contained', 'false')
            Write-Host 'Publishing framework-dependent (requires the .NET 10 Desktop Runtime on target).' -ForegroundColor Yellow
        }
        & $dotnet publish @args
        Write-Host "Output: $dist\PasswordHelper.exe" -ForegroundColor Green
    }
}
