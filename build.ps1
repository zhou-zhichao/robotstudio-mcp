param(
    [ValidateSet('2024', '2025', '2026')][string]$RobotStudioVersion = '2024',
    [string]$RobotStudioBin,
    [string]$MSBuildPath,
    [switch]$Experimental
)
$ErrorActionPreference = 'Stop'
. "$PSScriptRoot\scripts\robotstudio-paths.ps1"
if ($RobotStudioVersion -eq '2026' -and -not $Experimental) {
    throw '2026 is an uncompiled migration target. Pass -Experimental to attempt a build against installed .NET 10 SDK assemblies.'
}
$bin = Resolve-RobotStudioBin $RobotStudioVersion $RobotStudioBin
$outputDir = Join-Path $PSScriptRoot "artifacts\$RobotStudioVersion"
$metadataPath = Join-Path $outputDir 'build-info.json'
if (Test-Path -LiteralPath $metadataPath) { Remove-Item -LiteralPath $metadataPath }
if ($RobotStudioVersion -eq '2026') {
    & dotnet build "$PSScriptRoot\addin\RobotStudioMcpAddin.Net10.csproj" -c Release "-p:RobotStudioBin=$bin" "-p:OutputPath=$outputDir"
} else {
    if (-not (Test-Path -LiteralPath "$PSScriptRoot\addin\packages\Newtonsoft.Json.13.0.3\lib\net45\Newtonsoft.Json.dll")) {
        throw 'Restore dependencies first: nuget install addin/packages.config -OutputDirectory addin/packages (from repository root).'
    }
    if (-not $MSBuildPath) {
        $command = Get-Command MSBuild.exe -ErrorAction SilentlyContinue
        if ($command) { $MSBuildPath = $command.Source }
        else {
            $vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
            if (Test-Path -LiteralPath $vswhere) {
                $MSBuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
            }
        }
    }
    if (-not $MSBuildPath) { throw 'Install Visual Studio Build Tools or provide -MSBuildPath.' }
    & $MSBuildPath "$PSScriptRoot\addin\RobotStudioMcpAddin.csproj" /t:Rebuild /p:Configuration=Release "/p:RobotStudioVersion=$RobotStudioVersion" "/p:RobotStudioBin=$bin" "/p:OutputPath=$outputDir" "/p:IntermediateOutputPath=obj\$RobotStudioVersion\"
}
if ($LASTEXITCODE -ne 0) { throw "Build failed with exit code $LASTEXITCODE. No deployment performed." }
[xml]$manifest = Get-Content -LiteralPath "$PSScriptRoot\addin\RobotStudioMcpAddin.rsaddin"
if ($RobotStudioVersion -eq '2026') {
    $minimum = $manifest.CreateElement('MinimumHostVersion', $manifest.DocumentElement.NamespaceURI)
    $minimum.InnerText = '26.1'
    $manifest.DocumentElement.AppendChild($minimum) | Out-Null
}
$manifest.Save((Join-Path $outputDir 'RobotStudioMcpAddin.rsaddin'))
@{ robotStudioVersion = $RobotStudioVersion; sdkBin = $bin; evidence = 'compiled'; runtimeTested = $false } |
    ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputDir 'build-info.json') -Encoding UTF8
Write-Output "Built $outputDir. Runtime compatibility is not established. Run deploy.ps1 separately."
