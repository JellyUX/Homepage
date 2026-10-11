# Builds the plugin and deploys it to the test Jellyfin container.
# Run from the project root: .\docker\scripts\deploy-plugin.ps1

param(
    [string]$Configuration = "Release",
    [string]$PluginName = "Jellyfin.Plugin.JuxHomepage"
)

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$dockerDir = Split-Path -Parent $scriptDir
$repoRoot = Split-Path -Parent $dockerDir
$pluginsDir = Join-Path $dockerDir "plugins"
$buildOutputDir = Join-Path $repoRoot "src\$PluginName\bin\$Configuration\net9.0"

# Jellyfin loads the plugin from the "<Name>_<Version>" folder created by the catalog install
# (it holds meta.json). Deploying anywhere else leaves a second, competing copy of the plugin.
$installedDirs = @(Get-ChildItem -Path $pluginsDir -Directory -Filter "JellyUX Homepage_*")
if ($installedDirs.Count -gt 1) {
    Write-Error "Several installed JellyUX Homepage folders found in ${pluginsDir}: $($installedDirs.Name -join ', '). Remove the stale ones first."
    exit 1
}
if ($installedDirs.Count -eq 1) {
    $pluginOutputDir = $installedDirs[0].FullName
} else {
    $pluginOutputDir = Join-Path $pluginsDir $PluginName
}

Write-Host "Building $PluginName ($Configuration)..."
dotnet build "$repoRoot\src\$PluginName\$PluginName.csproj" -c $Configuration
if ($LASTEXITCODE -ne 0) { Write-Error "Build failed."; exit 1 }

Write-Host "Deploying to $pluginOutputDir..."
New-Item -ItemType Directory -Force $pluginOutputDir | Out-Null
Copy-Item "$buildOutputDir\$PluginName.dll", "$buildOutputDir\$PluginName.pdb" $pluginOutputDir -Force

Write-Host "Restarting jellyux-test container..."
docker compose -f "$dockerDir\docker-compose.yml" restart jellyfin
if ($LASTEXITCODE -ne 0) { Write-Error "Container restart failed."; exit 1 }

Write-Host "Done. Jellyfin restarting at http://localhost:8096"
