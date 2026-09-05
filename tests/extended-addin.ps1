param([string]$RobotStudioBin = 'C:\Program Files (x86)\ABB\RobotStudio 2024\Bin')
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
Add-Type -Path (Join-Path $repo 'addin\RapidPrecheck.cs')
function Assert-Valid([string]$Source, [bool]$Expected) {
    $issues = [RobotStudioMcpAddin.RapidPrecheck]::Check($Source)
    if (($issues.Count -eq 0) -ne $Expected) { throw "Unexpected precheck result: $Source" }
}
Assert-Valid "MODULE M`nPROC main()`n! IF THEN ENDPROC`nTPWrite `"IF THEN PROC ENDIF !`";`nENDPROC`nENDMODULE" $true
Assert-Valid "MODULE M`nPROC main()`nIF TRUE TPWrite `"ok`";`nENDPROC`nENDMODULE" $true
Assert-Valid "MODULE M`nPROC main()`nIF TRUE`nTHEN`nWaitTime 1;`nENDIF`nENDPROC`nENDMODULE" $true
Assert-Valid "MODULE M`nPROC main()`nIF TRUE THEN`nENDPROC`nENDMODULE" $false
Assert-Valid "MODULE M`nPROC main()`nTPWrite `"unclosed`nENDPROC`nENDMODULE" $false
Assert-Valid "MODULE M`nTRAP handler`nRETURN;`nENDTRAP`nENDMODULE" $true
Assert-Valid (Get-Content (Join-Path $repo 'experiments\readme_wordmark\DrawRobotStudioMcp.mod') -Raw) $true
Write-Output '7 RAPID structural/lexical cases passed.'

# Load only SDK metadata dependencies; these tests do not connect to a controller.
foreach ($name in @('ABB.Robotics.Controllers.PC', 'ABB.Robotics.Math', 'ABB.Robotics.RobotStudio', 'ABB.Robotics.RobotStudio.Stations')) {
    [Reflection.Assembly]::LoadFrom((Join-Path $RobotStudioBin ($name + '.dll'))) | Out-Null
}
[Reflection.Assembly]::LoadFrom((Join-Path $repo 'artifacts\2024\Newtonsoft.Json.dll')) | Out-Null
$assembly = [Reflection.Assembly]::LoadFrom((Join-Path $repo 'artifacts\2024\RobotStudioMcpAddin.dll'))
$method = $assembly.GetType('RobotStudioMcpAddin.Addin').GetMethod('SafeChild', [Reflection.BindingFlags]'NonPublic,Static')
$testRoot = Join-Path $repo ('artifacts\file-scope-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $testRoot | Out-Null
Set-Content -LiteralPath (Join-Path $testRoot 'valid.txt') -Value 'valid'
$actual = $method.Invoke($null, @([string]$testRoot, 'valid.txt'))
if ($actual -ne (Join-Path $testRoot 'valid.txt')) { throw 'Valid file path rejected.' }
foreach ($relative in @('..\outside.txt', 'C:\Windows\win.ini', '\\server\share', 'valid.txt:stream', '.\valid.txt', 'valid.txt.')) {
    $rejected = $false
    try { $method.Invoke($null, @([string]$testRoot, [string]$relative)) | Out-Null }
    catch { $rejected = $_.Exception.InnerException -is [ArgumentException] }
    if (!$rejected) { throw "Unsafe path not rejected: $relative" }
}
$outside = Join-Path $repo ('artifacts\outside-test-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $outside | Out-Null
Set-Content -LiteralPath (Join-Path $outside 'hidden.txt') -Value 'outside'
New-Item -ItemType Junction -Path (Join-Path $testRoot 'link') -Target $outside | Out-Null
$rejected = $false
try { $method.Invoke($null, @([string]$testRoot, 'link\hidden.txt')) | Out-Null }
catch { $rejected = $_.Exception.InnerException -is [ArgumentException] }
if (!$rejected) { throw 'Junction traversal not rejected.' }
Write-Output '8 controller-file boundary cases passed.'

$validate = $assembly.GetType('RobotStudioMcpAddin.Addin').GetMethod('ValidateExtendedInput', [Reflection.BindingFlags]'NonPublic,Static')
$cases = @(
    @('/speed/override', '{"percent":101}'),
    @('/speed/override', '{"percent":1.5}'),
    @('/program/load', '{"backupId":"abc","taskNam":"T_ROB2"}'),
    @('/robot/pose', '{"frame":"wrong"}'),
    @('/targets/create', '{"targetName":"p1","xMm":0,"yMm":0}')
)
foreach ($case in $cases) {
    $inputObject = [Newtonsoft.Json.Linq.JObject]::Parse($case[1])
    $rejected = $false
    try { $validate.Invoke($null, @([string]$case[0], $inputObject)) | Out-Null }
    catch { $rejected = $_.Exception.InnerException -is [ArgumentException] }
    if (!$rejected) { throw "Invalid HTTP input not rejected: $($case[1])" }
}
Write-Output '5 direct-HTTP schema boundary cases passed.'
