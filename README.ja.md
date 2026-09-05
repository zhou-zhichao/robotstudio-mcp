# RobotStudio Agent Bridge

[English](README.md) · [简体中文](README.zh-CN.md) · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Français](README.fr.md) · [Deutsch](README.de.md)

**Skills、ローカル CLI、MCP を通じて AI アシスタントと ABB RobotStudio を接続します。**

ステーションの確認、RAPID ソースのアップロード、シミュレーションの実行、状態・ログ・画像による結果確認を行えます。この研究プロジェクトは C# 製 HTTP アドインを基盤に、リポジトリの Skill に従って使う独立した Node.js CLI と、任意で利用できる TypeScript MCP サーバーを提供します。

## 主な機能

| 分類 | コマンド |
|---|---|
| ステーションとロボット | `get_station_status`, `get_robot_joints` |
| シミュレーションと実行 | `control_simulation`, `control_rapid_execution`, `get_rapid_execution_status` |
| RAPID ソースと診断 | `upload_rapid_module`, `get_rapid_module_source`, `list_rapid_modules`, `get_execution_errors` |
| 変数と I/O | `read_rapid_variable`, `set_rapid_variable`, `list_rapid_variables`, `get_io_signals`, `set_io_signal` |
| シーンと画像 | `get_scene_objects`, `get_screenshot` |

## 構成

```text
AI agent -- Skill --> Node.js CLI ------+
                                       |
AI agent -- MCP ---> TypeScript server -+--> HTTP :8080 --> C# add-in --> ABB SDK
```

両方の入口でアドインとコントローラーの処理を共有します。CLI は Node.js 18+ のみで動作し、npm パッケージのインストールや MCP 登録は不要です。アドインは引き続き必要です。Skill は操作手順を定義するもので、SDK の代わりにはなりません。

## 対応バージョン

| バージョン | 状態 |
|---|---|
| 2024 | 現在の基準環境。過去の実験記録があり、ローカルビルドに成功しています。今回の更新ではロボット動作を再検証していません。 |
| 2025 | Elias と LiskinLabs を参考に、.NET Framework 4.8 と 2025 のホストアセンブリを使用します。本プロジェクトでは 2025 向けのビルド・実行は未検証です。 |
| 2026.1+ | 実験用 .NET 10 プロジェクトのみ提供。2026 SDK でのビルド・実行ともに未検証です。 |

2025 の参考実装は [Elias](https://github.com/eliasbitsch/abb-robotstudio-mcp) と [LiskinLabs](https://github.com/LiskinLabs/abb-robotstudio-mcp) です。採用した設計と残作業は[互換性計画](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)を参照してください。

## クイックスタート

Windows に RobotStudio 2024、.NET Framework 4.8 のターゲティングツール、Visual Studio Build Tools／MSBuild、Node.js 18+、NuGet CLI を用意してください。ロボット操作には仮想コントローラーを含むステーションが必要です。以下は PowerShell で実行します。

```powershell
git clone https://github.com/zhou-zhichao/robotstudio-mcp.git
cd robotstudio-mcp
nuget install addin/packages.config -OutputDirectory addin/packages
```

### 1. アドインのビルドとインストール

インストール前に RobotStudio を終了してください。インストール先に管理者権限が必要な場合は、管理者 PowerShell で配置コマンドを実行します。ビルドは `artifacts/2024` に出力するだけで、自動インストールはしません。

```powershell
.\build.ps1
.\deploy.ps1
```

### 2. ローカル CLI／Skill を使う

RobotStudio を起動し、仮想コントローラーを含むステーションを開いてアドインの読み込みを確認します。以下はリポジトリのルートで実行してください。スクリーンショットには毎回新しいファイル名を使います。

```powershell
node scripts/robotstudio.mjs health
node scripts/robotstudio.mjs get_station_status
node scripts/robotstudio.mjs get_screenshot --output artifacts/view.png
```

Codex では、このリポジトリ内で `$robotstudio` を呼び出せます。[Skill](.agents/skills/robotstudio/SKILL.md) はほかのローカルエージェントでも参照できます。リモート／クラウド端末の `localhost` は RobotStudio が動く PC ではありません。

### 3. MCP を使う（任意）

任意の MCP サーバーをビルドし、利用する MCP クライアントの設定方法に従って以下の STDIO 定義を追加します。サンプルのパスは PC 上の絶対パスに置き換えてください。現在の MCP サーバーは `http://localhost:8080` に接続します。

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

## RAPID モジュールのアップロード

以下の内容で UTF-8 の `params.json` を作成し、完全な RAPID ブロック `MODULE Demo ... ENDMODULE` を含む `program.mod` を用意します。この例はアップロードのみで、実行は開始しません。`replaceExisting:false` は広範囲の削除を避けますが、既存モジュールやシンボルの競合がある場合は失敗することがあります。

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

## CLI オプション

| オプション | 動作 |
|---|---|
| `--params-file` | UTF-8 JSON ファイルから引数を読み込みます。 |
| `--code-file` | 引用符を保持して RAPID ファイルを読み込みます。アップロード専用です。 |
| `--output` | JSON または PNG を保存します。上書きしません。スクリーンショットでは必須です。 |
| `--url / ROBOTSTUDIO_API_BASE` | アドインの HTTP 接続先。既定値は `http://127.0.0.1:8080` です。 |
| `--timeout` | タイムアウトをミリ秒で指定します（1–300000）。 |
| `--help / --describe` | コマンド一覧または正確なパラメーター定義を表示します。 |

成功時は stdout に JSON を出力します。失敗時は stderr に JSON を出力し、終了コードは 0 以外になります。返された PNG の絶対パスを画像ビューアーやエージェントの画像ツールで開いてください。

```powershell
node scripts/robotstudio.mjs --help
node scripts/robotstudio.mjs --describe upload_rapid_module
```

## 把握しておくべき動作

- 置換前に対象モジュールをバックアップしてください。既定の `replaceExisting:true` は、選択したタスクで名前が `BASE` または `user` 以外のモジュールの削除を試みます。指定モジュールだけの置換ではなく、自動ロールバックもありません。
- シミュレーションの reset にはデモ専用の箱の削除処理が含まれ、ステーション全体の復元ではありません。
- CLI の生のシーン座標と境界ボックスはメートル単位です。MCP 表示はミリメートルに変換する場合があります。計算前に単位を確認してください。
- タイムアウトしても書き込みが反映されている場合があります。再試行前に状態を確認してください。CLI は自動再試行しません。
- 本プロジェクトの基準は仮想コントローラーでのシミュレーションです。現在の検証は実機への適合性を証明するものではありません。

## 別バージョン向けのビルド

年を明示的に指定します。既定値は 2024 です。`-RobotStudioBin` で SDK の場所、`-MSBuildPath` で Framework コンパイラーを指定できます。配置時にビルド情報を照合します。`-WhatIf` は配置のプレビューで、先にビルドが成功している必要があります。

```powershell
.\build.ps1 -RobotStudioVersion 2025 -RobotStudioBin 'C:\Program Files (x86)\ABB\RobotStudio 2025\Bin'
.\deploy.ps1 -RobotStudioVersion 2025 -WhatIf
```

2026 には対応する .NET 10 SDK アセンブリと開発ツールが必要で、ビルド・配置の両方に `-Experimental` を指定します。年を変えるだけでは移行は完了しません。

## 検証状況

実装確認では 7 件のモック HTTP／CLI テスト、Skill 形式検証、TypeScript コンパイル、2024 アドインのビルドが成功しました。以下のテストは RobotStudio を操作しません。シミュレーション／Mastership の非推奨 API 警告が残っており、2025／2026 のホスト検証は未実施です。

```powershell
node --test tests/robotstudio-cli.test.mjs
npm --prefix src run build
```

## ドキュメントとソース

- [操作手順](.agents/skills/robotstudio/SKILL.md)
- [RAPID サンプル](docs/RAPID_EXAMPLES.md)
- [HTTP API リファレンス](docs/HTTP_API.md)
- [互換性とインターフェース設計](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)
- [失敗を含む実験記録](docs/DEVELOPMENT_LOG.md)
- [CLI 実装](scripts/robotstudio.mjs)
- [C# アドイン](addin/RobotStudioAddin.cs)

## ライセンス

本プロジェクトが表明するライセンスは MIT です。
