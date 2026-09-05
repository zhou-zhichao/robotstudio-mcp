# RobotStudio Agent Bridge

[English](README.md) · [简体中文](README.zh-CN.md) · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Français](README.fr.md) · [Deutsch](README.de.md)

<!-- BEGIN HERO DEMO -->

[![RobotStudio에서 robotstudio-mcp를 그리는 IRB120](docs/media/robotstudio-mcp-demo.gif)](docs/media/robotstudio-mcp-demo.mp4)

RobotStudio 2024 녹화: IRB120 가상 컨트롤러로 `robotstudio-mcp`를 그립니다. 재생 속도가 1배속에서 4배속으로 서서히 올라갔다가 1배속으로 돌아오며, 완성된 글자를 3초간 보여 준 뒤 반복합니다.

[MP4 보기 / 다운로드](docs/media/robotstudio-mcp-demo.mp4) · [정지 이미지](docs/media/robotstudio-mcp-result.png)

<!-- END HERO DEMO -->

**Skills, 로컬 CLI 또는 MCP로 AI 어시스턴트를 ABB RobotStudio에 연결합니다.**

스테이션 확인, RAPID 소스 업로드, 시뮬레이션 실행 후 상태·로그·이미지로 결과를 확인할 수 있습니다. 이 연구 프로젝트는 C# HTTP 애드인을 기반으로 두 가지 인터페이스를 제공합니다. 저장소 Skill의 안내에 따라 사용하는 독립 Node.js CLI와 선택적으로 사용하는 TypeScript MCP 서버입니다.


<!-- BEGIN EXAMPLE SCENES -->

## 예제 장면

석사 학위 논문 심사 발표 슬라이드의 원본 스크린샷입니다. 과거 RobotStudio 시뮬레이션 실험을 보여 주며 새 CLI 또는 RobotStudio 2025/2026의 호환성 테스트가 아닙니다.

### 숫자 그리기와 다른 로봇으로의 이전

슬라이드는 IRB120과 IRB2400에서 “34”를 그린 결과를 비교합니다. RAPID 동작 생성, 워크오브젝트 배치, 다른 로봇에 맞춘 그림 크기 조정을 다룬 장면입니다.

| IRB120 | IRB2400 |
|:---:|:---:|
| <img src="docs/images/examples/drawing-irb120.png" alt="IRB120" width="420"> | <img src="docs/images/examples/drawing-irb2400.png" alt="IRB2400" width="420"> |

### 컨베이어 픽앤플레이스와 팔레타이징

진공 그리퍼, 컨베이어, 팔레트 두 개가 있는 셀에서 이송과 적재를 실험합니다. 결과 이미지는 실험에서 기록한 주황색 블록 14개의 피라미드입니다.

| 작업 셀 전경 | 기록된 적재 결과 |
|:---:|:---:|
| <img src="docs/images/examples/palletizing-station.png" alt="작업 셀 전경" width="420"> | <img src="docs/images/examples/orange-pyramid-result.png" alt="기록된 적재 결과" width="420"> |

<!-- END EXAMPLE SCENES -->

## 주요 기능

| 분류 | 명령 |
|---|---|
| 스테이션과 로봇 | `get_station_status`, `get_robot_joints` |
| 시뮬레이션과 실행 | `control_simulation`, `control_rapid_execution`, `get_rapid_execution_status` |
| RAPID 소스와 진단 | `upload_rapid_module`, `get_rapid_module_source`, `list_rapid_modules`, `get_execution_errors` |
| 변수와 I/O | `read_rapid_variable`, `set_rapid_variable`, `list_rapid_variables`, `get_io_signals`, `set_io_signal` |
| 장면과 이미지 | `get_scene_objects`, `get_screenshot` |

## 구조

```text
AI agent -- Skill --> Node.js CLI ------+
                                       |
AI agent -- MCP ---> TypeScript server -+--> HTTP :8080 --> C# add-in --> ABB SDK
```

두 인터페이스는 동일한 애드인과 컨트롤러 로직을 공유합니다. CLI에는 Node.js 18+만 필요하며 npm 패키지 설치나 MCP 등록은 필요하지 않습니다. 애드인은 여전히 필요합니다. Skill은 작업 절차를 설명하며 SDK를 대체하지 않습니다.

## 버전 호환성

| 버전 | 상태 |
|---|---|
| 2024 | 현재 기준 환경입니다. 과거 실험 기록이 있으며 로컬 빌드에 성공했습니다. 이번 업데이트에서 로봇 동작을 다시 검증하지는 않았습니다. |
| 2025 | Elias와 LiskinLabs를 참고하여 .NET Framework 4.8 및 2025 호스트 어셈블리를 사용합니다. 이 프로젝트에서는 2025 빌드와 실행을 검증하지 않았습니다. |
| 2026.1+ | 실험용 .NET 10 프로젝트만 제공합니다. 2026 SDK로 컴파일하거나 실행을 검증하지 않았습니다. |

2025 참고 구현은 [Elias](https://github.com/eliasbitsch/abb-robotstudio-mcp)와 [LiskinLabs](https://github.com/LiskinLabs/abb-robotstudio-mcp)입니다. 채택한 설계와 남은 작업은 [호환성 계획](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)을 참고하세요.

## 빠른 시작

Windows에 RobotStudio 2024, .NET Framework 4.8 타기팅 도구, Visual Studio Build Tools/MSBuild, Node.js 18+, NuGet CLI를 준비하세요. 로봇 작업에는 가상 컨트롤러가 포함된 스테이션이 필요합니다. 아래 명령은 PowerShell에서 실행합니다.

```powershell
git clone https://github.com/zhou-zhichao/robotstudio-mcp.git
cd robotstudio-mcp
nuget install addin/packages.config -OutputDirectory addin/packages
```

### 1. 애드인 빌드 및 설치

설치 전에 RobotStudio를 종료하세요. 설치 폴더에 관리자 권한이 필요한 경우 관리자 PowerShell에서 배포 명령을 실행하세요. 빌드는 `artifacts/2024`에 파일만 생성하며 애드인을 자동으로 설치하지 않습니다.

```powershell
.\build.ps1
.\deploy.ps1
```

### 2. 로컬 CLI / Skill 사용

RobotStudio를 시작하고 가상 컨트롤러가 포함된 스테이션을 연 다음 애드인이 로드되었는지 확인하세요. 아래 명령은 저장소 루트에서 실행합니다. 스크린샷마다 새 파일 이름을 사용하세요.

```powershell
node scripts/robotstudio.mjs health
node scripts/robotstudio.mjs get_station_status
node scripts/robotstudio.mjs get_screenshot --output artifacts/view.png
```

Codex에서는 이 저장소 안에서 `$robotstudio`를 호출할 수 있습니다. 다른 로컬 에이전트도 [Skill](.agents/skills/robotstudio/SKILL.md)을 읽을 수 있습니다. 원격 또는 클라우드 터미널의 `localhost`는 RobotStudio를 실행하는 PC가 아닙니다.

### 3. MCP 사용 (선택 사항)

선택 사항인 MCP 서버를 빌드한 후 사용하는 MCP 클라이언트의 설정 방식에 따라 아래 STDIO 정의를 추가하세요. 예시 경로를 PC의 실제 절대 경로로 바꾸세요. 현재 MCP 서버는 `http://localhost:8080`에 연결합니다.

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

## RAPID 모듈 업로드

아래 내용으로 UTF-8 `params.json` 파일을 만들고 완전한 RAPID 블록 `MODULE Demo ... ENDMODULE`이 포함된 `program.mod`를 준비하세요. 이 예시는 업로드만 하며 실행을 시작하지 않습니다. `replaceExisting:false`는 광범위한 삭제를 피하지만 기존 모듈이나 심볼이 충돌하면 업로드에 실패할 수 있습니다.

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

## CLI 옵션

| 옵션 | 동작 |
|---|---|
| `--params-file` | UTF-8 JSON 파일에서 인자를 읽습니다. |
| `--code-file` | 따옴표를 유지하며 RAPID 파일을 읽습니다. 업로드 전용입니다. |
| `--output` | JSON 또는 PNG를 저장하며 덮어쓰지 않습니다. 스크린샷에는 필수입니다. |
| `--url / ROBOTSTUDIO_API_BASE` | 애드인의 HTTP 주소를 선택합니다. 기본값: `http://127.0.0.1:8080`. |
| `--timeout` | 요청 제한 시간입니다. 단위는 밀리초이며 범위는 1–300000입니다. |
| `--help / --describe` | 명령 목록 또는 정확한 파라미터 정의를 표시합니다. |

성공 결과는 stdout에 JSON으로 출력합니다. 실패 시 stderr에 JSON을 출력하고 0이 아닌 종료 코드를 반환합니다. 반환된 PNG 절대 경로를 이미지 뷰어나 에이전트의 이미지 도구로 여세요.

```powershell
node scripts/robotstudio.mjs --help
node scripts/robotstudio.mjs --describe upload_rapid_module
```

## 알아두어야 할 동작

- 교체 전에 영향을 받는 모듈을 백업하세요. 기본값인 `replaceExisting:true`는 선택한 태스크에서 이름이 `BASE` 또는 `user`인 모듈을 제외한 모듈들의 삭제를 시도합니다. 지정한 모듈만 교체하는 것이 아니며 자동 롤백도 없습니다.
- 시뮬레이션 reset에는 데모용 상자 정리가 포함되어 있습니다. 스테이션 전체 복원 기능은 아닙니다.
- CLI의 원시 장면 좌표와 바운딩 박스는 미터 단위입니다. MCP는 이를 밀리미터로 변환할 수 있으므로 계산 전에 단위를 확인하세요.
- 시간이 초과되어도 쓰기가 적용되었을 수 있습니다. 재시도 전에 실제 상태를 확인하세요. CLI는 자동 재시도하지 않습니다.
- 프로젝트의 기준은 가상 컨트롤러 시뮬레이션입니다. 현재 테스트는 실제 하드웨어 사용 적합성을 입증하지 않습니다.

## 다른 버전으로 빌드

연도를 명시적으로 지정하세요. 기본값은 2024입니다. `-RobotStudioBin`으로 SDK 위치를, `-MSBuildPath`로 Framework 컴파일러를 지정할 수 있습니다. 배포 시 빌드 정보를 확인합니다. `-WhatIf`는 설치 미리보기이며 먼저 빌드가 성공해야 합니다.

```powershell
.\build.ps1 -RobotStudioVersion 2025 -RobotStudioBin 'C:\Program Files (x86)\ABB\RobotStudio 2025\Bin'
.\deploy.ps1 -RobotStudioVersion 2025 -WhatIf
```

2026에는 일치하는 .NET 10 SDK 어셈블리와 개발 도구가 추가로 필요하며 빌드와 배포 모두에 `-Experimental`을 지정해야 합니다. 연도만 변경해서는 마이그레이션이 완료되지 않습니다.

## 검증 상태

구현 검토에서 모의 HTTP/CLI 테스트 7개, Skill 형식 검사, TypeScript 컴파일, 2024 애드인 빌드가 통과했습니다. 아래 테스트는 RobotStudio를 제어하지 않습니다. 시뮬레이션/Mastership의 사용 중단 예정 API 경고가 남아 있으며 2025/2026 호스트 검증은 아직 수행하지 않았습니다.

```powershell
node --test tests/robotstudio-cli.test.mjs
npm --prefix src run build
```

## 문서 및 소스

- [작업 절차](.agents/skills/robotstudio/SKILL.md)
- [RAPID 예제](docs/RAPID_EXAMPLES.md)
- [HTTP API 참조](docs/HTTP_API.md)
- [호환성과 인터페이스 설계](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)
- [실패를 포함한 실험 기록](docs/DEVELOPMENT_LOG.md)
- [CLI 구현](scripts/robotstudio.mjs)
- [C# 애드인](addin/RobotStudioAddin.cs)

## 라이선스

프로젝트에서 명시한 라이선스는 MIT입니다.
