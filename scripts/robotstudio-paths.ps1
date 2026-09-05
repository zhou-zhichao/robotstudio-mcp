function Resolve-RobotStudioBin {
    param([string]$Version, [string]$BinPath)
    if (-not $BinPath) {
        foreach ($programRoot in @(${env:ProgramFiles(x86)}, $env:ProgramFiles)) {
            if (-not $programRoot) { continue }
            $candidate = Join-Path $programRoot "ABB\RobotStudio $Version\Bin"
            if (Test-Path -LiteralPath $candidate -PathType Container) { $BinPath = $candidate; break }
        }
    }
    if (-not $BinPath -or -not (Test-Path -LiteralPath $BinPath -PathType Container)) {
        throw "RobotStudio $Version SDK assemblies not found. Specify -RobotStudioBin with the matching host Bin directory."
    }
    $resolvedBin = (Resolve-Path -LiteralPath $BinPath).Path
    foreach ($assembly in @('ABB.Robotics.RobotStudio', 'ABB.Robotics.RobotStudio.Stations', 'ABB.Robotics.Controllers.PC', 'ABB.Robotics.Math')) {
        if (-not (Test-Path -LiteralPath (Join-Path $resolvedBin "$assembly.dll") -PathType Leaf)) { throw "Missing SDK assembly: $assembly.dll in $resolvedBin" }
    }
    return $resolvedBin
}
