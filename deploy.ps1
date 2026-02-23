$src = 'C:\Users\sam\code\Robotstudio_mcp\robotstudio-mcp\addin\bin\Release'
$rsaddin = 'C:\Users\sam\code\Robotstudio_mcp\robotstudio-mcp\addin\RobotStudioMcpAddin.rsaddin'
$dst = 'C:\Program Files (x86)\ABB\RobotStudio 2024\Bin\Addins\RobotStudioMcpAddin'

if (-not (Test-Path $dst)) { New-Item -ItemType Directory -Path $dst -Force | Out-Null }

Copy-Item "$src\RobotStudioMcpAddin.dll" $dst -Force
Copy-Item "$src\Newtonsoft.Json.dll" $dst -Force
Copy-Item $rsaddin $dst -Force

Write-Host "Deployed:"
Get-ChildItem $dst | Format-Table Name, Length, LastWriteTime -AutoSize
