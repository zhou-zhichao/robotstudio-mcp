$msbuild = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe'
& $msbuild "C:\Users\sam\code\Robotstudio_mcp\robotstudio-mcp\addin\RobotStudioMcpAddin.csproj" /p:Configuration=Release /t:Rebuild /v:normal
