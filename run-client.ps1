# Build and launch Vintage Story with this mod loaded (no debugger attached).
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "VS Adjacent Ignition Mod\VS Adjacent Ignition Mod\VS Adjacent Ignition Mod.csproj"
$vintageStory = [Environment]::GetEnvironmentVariable("VINTAGE_STORY", "User")

if ([string]::IsNullOrWhiteSpace($vintageStory)) {
    throw "VINTAGE_STORY environment variable is not set. Point it at your Vintage Story install folder."
}

$gameDll = Join-Path $vintageStory "Vintagestory.dll"
if (-not (Test-Path $gameDll)) {
    throw "Vintage Story not found at: $gameDll"
}

dotnet build $project -c Debug
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

$modPath = Join-Path $PSScriptRoot "VS Adjacent Ignition Mod\VS Adjacent Ignition Mod\bin\Debug\Mods"
$assetsPath = Join-Path $PSScriptRoot "VS Adjacent Ignition Mod\VS Adjacent Ignition Mod\assets"

Push-Location $vintageStory
try {
    dotnet $gameDll --tracelog --addModPath $modPath --addOrigin $assetsPath
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
