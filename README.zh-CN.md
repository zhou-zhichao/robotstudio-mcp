# RobotStudio Agent Bridge

[English](README.md) · [简体中文](README.zh-CN.md) · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Français](README.fr.md) · [Deutsch](README.de.md)

**通过 Skills、本地 CLI 或 MCP，让 AI 助手操作 ABB RobotStudio。**

读取工作站、上传 RAPID 程序、运行仿真，再根据状态、日志和截图检查结果。本研究项目以一个 C# HTTP 插件为基础，提供两种入口：由仓库 Skill 指导使用的独立 Node.js CLI，以及可选的 TypeScript MCP 服务。


<!-- BEGIN EXAMPLE SCENES -->

## 示例场景

以下原始截图来自 master thesis 答辩 slides，展示已有的 RobotStudio 仿真实验，并非对新 CLI 或 RobotStudio 2025／2026 的新增兼容性测试。

### 数字绘制与跨机器人迁移

slides 对比了 IRB120 与 IRB2400 上的“34”绘制任务。这个场景用于检验 RAPID 运动程序生成、工件坐标系设置，以及换用机器人后的绘制位置与尺寸调整。

| IRB120 | IRB2400 |
|:---:|:---:|
| <img src="docs/images/examples/drawing-irb120.png" alt="IRB120" width="420"> | <img src="docs/images/examples/drawing-irb2400.png" alt="IRB2400" width="420"> |

### 传送带抓取与托盘堆垛

工作站包含真空吸盘、传送带和两个托盘，用于抓取放置及堆垛实验。结果图展示了实验记录中的 14 块橙色方块金字塔。

| 工作站全景 | 实验堆垛结果 |
|:---:|:---:|
| <img src="docs/images/examples/palletizing-station.png" alt="工作站全景" width="420"> | <img src="docs/images/examples/orange-pyramid-result.png" alt="实验堆垛结果" width="420"> |

<!-- END EXAMPLE SCENES -->

## 可以做什么

| 类别 | 命令 |
|---|---|
| 工作站与机器人 | `get_station_status`, `get_robot_joints` |
| 仿真与执行 | `control_simulation`, `control_rapid_execution`, `get_rapid_execution_status` |
| RAPID 源码与诊断 | `upload_rapid_module`, `get_rapid_module_source`, `list_rapid_modules`, `get_execution_errors` |
| 变量与 I/O | `read_rapid_variable`, `set_rapid_variable`, `list_rapid_variables`, `get_io_signals`, `set_io_signal` |
| 场景与图像 | `get_scene_objects`, `get_screenshot` |

## 架构

```text
AI agent -- Skill --> Node.js CLI ------+
                                       |
AI agent -- MCP ---> TypeScript server -+--> HTTP :8080 --> C# add-in --> ABB SDK
```

两种入口共用插件和控制器逻辑。CLI 只需要 Node.js 18+，不需要安装 npm 依赖或注册 MCP 服务；C# 插件仍然必需。仓库 Skill 负责说明操作流程，不替代 SDK。

## 版本兼容性

| 版本 | 状态 |
|---|---|
| 2024 | 现有基线，有历史实验记录，已通过本地编译。本次更新没有重新运行机器人实验。 |
| 2025 | 参考 Elias 和 LiskinLabs，使用 .NET Framework 4.8 和 2025 宿主程序集。本项目尚未针对 2025 编译或实测。 |
| 2026.1+ | 仅提供 .NET 10 实验工程，尚未使用 2026 SDK 编译，也未进行运行验证。 |

2025 的参考实现是 [Elias](https://github.com/eliasbitsch/abb-robotstudio-mcp) 和 [LiskinLabs](https://github.com/LiskinLabs/abb-robotstudio-mcp)。采用的设计与待完成工作见[兼容性方案](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)。

## 快速开始

在 Windows 上准备 RobotStudio 2024、.NET Framework 4.8 目标框架开发工具、Visual Studio Build Tools／MSBuild、Node.js 18+ 和 NuGet CLI。机器人操作需要带虚拟控制器的工作站。以下命令在 PowerShell 中运行。

```powershell
git clone https://github.com/zhou-zhichao/robotstudio-mcp.git
cd robotstudio-mcp
nuget install addin/packages.config -OutputDirectory addin/packages
```

### 1. 构建并安装插件

安装前关闭 RobotStudio。如果安装目录要求管理员权限，请在管理员 PowerShell 中运行部署命令。构建只输出到 `artifacts/2024`，不会自动安装插件。

```powershell
.\build.ps1
.\deploy.ps1
```

### 2. 使用本地 CLI／Skill

启动 RobotStudio，打开带虚拟控制器的工作站，确认插件已加载。在仓库根目录运行以下命令；每次截图使用一个新文件名。

```powershell
node scripts/robotstudio.mjs health
node scripts/robotstudio.mjs get_station_status
node scripts/robotstudio.mjs get_screenshot --output artifacts/view.png
```

在 Codex 中打开本仓库后，可调用 `$robotstudio`。[仓库 Skill](.agents/skills/robotstudio/SKILL.md) 也可供其他本地代理读取。远程或云端终端的 `localhost` 并不是运行 RobotStudio 的电脑。

### 3. 使用 MCP（可选）

先构建可选的 MCP 服务，再按所用 MCP 客户端的配置方式添加下面的 STDIO 服务定义。把示例路径替换成你电脑上的绝对路径。当前 MCP 服务连接 `http://localhost:8080`。

```powershell
npm --prefix src install
npm --prefix src run build
```

```json
{
  "mcpServers": {
    "robotstudio": {
      "command": "node",
      "args": ["C:/path/to/robotstudio-mcp/src/dist/server.js"]
    }
  }
}
```

## 上传 RAPID 模块

按下方内容创建 UTF-8 编码的 `params.json`，并准备名为 `program.mod`、包含完整 `MODULE Demo ... ENDMODULE` 的 RAPID 源文件。此示例只上传，不启动程序。`replaceExisting:false` 避免进入批量清理流程；模块已存在或符号冲突时，上传可能失败。

```json
{
  "moduleName": "Demo",
  "taskName": "T_ROB1",
  "replaceExisting": false
}
```

```powershell
node scripts/robotstudio.mjs upload_rapid_module --params-file params.json --code-file program.mod
```

## CLI 参数

| 参数 | 行为 |
|---|---|
| `--params-file` | 从 UTF-8 JSON 文件读取参数。 |
| `--code-file` | 从文件读取 RAPID 源码，保留引号；仅用于上传。 |
| `--output` | 保存 JSON 或截图 PNG，不覆盖已有文件；截图必须指定。 |
| `--url / ROBOTSTUDIO_API_BASE` | 选择插件的 HTTP 地址，默认 `http://127.0.0.1:8080`。 |
| `--timeout` | 请求超时，单位毫秒，范围 1–300000。 |
| `--help / --describe` | 列出命令或查看准确的参数定义。 |

成功结果以 JSON 写入 stdout；失败以 JSON 写入 stderr，并返回非零退出码。截图返回绝对路径，可用图片查看器或代理的图像工具打开。

```powershell
node scripts/robotstudio.mjs --help
node scripts/robotstudio.mjs --describe upload_rapid_module
```

## 使用前需要了解的行为

- 替换前备份受影响模块。默认的 `replaceExisting:true` 会尝试删除所选任务中除名称为 `BASE`、`user` 外的模块，并不只替换指定模块；目前没有自动回滚。
- 仿真 reset 包含演示场景专用的方块清理，不等于完整恢复工作站。
- CLI 原始场景坐标和包围盒使用米；MCP 输出可能转换成毫米。计算位置前先核对单位。
- 写操作超时不代表未执行。重试前检查实际状态；CLI 不会自动重试。
- 项目基线是虚拟控制器仿真，当前验证不能证明适合直接控制真实硬件。

## 构建其他版本

明确指定年份；默认仍为 2024。`-RobotStudioBin` 可覆盖 SDK 路径，`-MSBuildPath` 可指定 Framework 编译器。部署会核对构建信息；`-WhatIf` 只预览安装，前提是已经成功构建。

```powershell
.\build.ps1 -RobotStudioVersion 2025 -RobotStudioBin 'C:\Program Files (x86)\ABB\RobotStudio 2025\Bin'
.\deploy.ps1 -RobotStudioVersion 2025 -WhatIf
```

2026 还需要匹配的 .NET 10 SDK 程序集、.NET 10 开发工具链，并在构建和部署时都加上 `-Experimental`。仅修改年份不能完成迁移。

## 验证情况

实现检查期间，7 项模拟 HTTP／CLI 测试、Skill 格式校验、TypeScript 编译和 2024 插件编译均通过。下面的测试不操作 RobotStudio。编译仍有仿真与 Mastership 旧 API 的弃用警告；2025／2026 仍待宿主验证。

```powershell
node --test tests/robotstudio-cli.test.mjs
npm --prefix src run build
```

## 文档与源码

- [操作流程](.agents/skills/robotstudio/SKILL.md)
- [RAPID 示例](docs/RAPID_EXAMPLES.md)
- [HTTP 接口参考](docs/HTTP_API.md)
- [兼容性与接口设计](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)
- [包含失败过程的实验记录](docs/DEVELOPMENT_LOG.md)
- [CLI 实现](scripts/robotstudio.mjs)
- [C# 插件](addin/RobotStudioAddin.cs)

## 许可证

本项目声明采用 MIT 许可证。
