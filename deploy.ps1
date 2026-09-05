[CmdletBinding(SupportsShouldProcess)]
param(
    [ValidateSet('2024', '2025', '2026')][string]$RobotStudioVersion = '2024',
    [string]$RobotStudioBin,
    [switch]$Experimental
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\robotstudio-paths.ps1"
if ($RobotStudioVersion -eq '2026' -and -not $Experimental) { throw '2026 deployment requires -Experimental; host compatibility remains unverified.' }
$bin = Resolve-RobotStudioBin $RobotStudioVersion $RobotStudioBin
$sourceDir = Join-Path $PSScriptRoot "artifacts\$RobotStudioVersion"
$info = Get-Content -LiteralPath (Join-Path $sourceDir 'build-info.json') -Raw | ConvertFrom-Json
if ($info.robotStudioVersion -ne $RobotStudioVersion -or $info.sdkBin -ne $bin) { throw 'Build metadata does not match the selected host. Rebuild against this installation.' }
$files = @('RobotStudioMcpAddin.dll', 'Newtonsoft.Json.dll', 'RobotStudioMcpAddin.rsaddin')
foreach ($file in $files) {
    if (-not (Test-Path -LiteralPath (Join-Path $sourceDir $file))) { throw "Missing artifact: $file. Run build.ps1 first." }
}
$destination = Join-Path $bin 'Addins\RobotStudioMcpAddin'
if ($PSCmdlet.ShouldProcess($destination, 'Install add-in (close RobotStudio first)')) {
    New-Item -ItemType Directory -Path $destination -Force | Out-Null
    foreach ($file in $files) { Copy-Item -LiteralPath (Join-Path $sourceDir $file) -Destination $destination -Force }
    Write-Output "Deployed to $destination. Verify loading and workflows in the selected host."
}
