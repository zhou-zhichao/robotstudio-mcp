# RobotStudio Agent Bridge

[English](README.md) · [简体中文](README.zh-CN.md) · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Français](README.fr.md) · [Deutsch](README.de.md)

**KI-Assistenten über Skills, eine lokale CLI oder MCP mit ABB RobotStudio verbinden.**

Stationen untersuchen, RAPID-Quellcode hochladen, Simulationen ausführen und Ergebnisse anhand von Status, Protokollen und Bildern prüfen. Dieses Forschungsprojekt bietet ein HTTP-Add-in in C# mit zwei Zugängen: eine eigenständige Node.js-CLI mit einer Skill-Anleitung im Repository und einen optionalen MCP-Server in TypeScript.

## Funktionen

| Bereich | Befehle |
|---|---|
| Station und Roboter | `get_station_status`, `get_robot_joints` |
| Simulation und Ausführung | `control_simulation`, `control_rapid_execution`, `get_rapid_execution_status` |
| RAPID-Quellcode und Diagnose | `upload_rapid_module`, `get_rapid_module_source`, `list_rapid_modules`, `get_execution_errors` |
| Variablen und E/A | `read_rapid_variable`, `set_rapid_variable`, `list_rapid_variables`, `get_io_signals`, `set_io_signal` |
| Szene und Bilder | `get_scene_objects`, `get_screenshot` |

## Architektur

```text
AI agent -- Skill --> Node.js CLI ------+
                                       |
AI agent -- MCP ---> TypeScript server -+--> HTTP :8080 --> C# add-in --> ABB SDK
```

Beide Zugänge nutzen dasselbe Add-in und dieselbe Steuerungslogik. Die CLI benötigt Node.js 18+, aber keine npm-Pakete oder MCP-Registrierung. Das Add-in bleibt erforderlich. Der Skill beschreibt den Ablauf und ersetzt nicht das SDK.

## Kompatibilität

| Version | Status |
|---|---|
| 2024 | Bisherige Referenzversion mit früheren Experimenten und erfolgreichem lokalem Build. Dieses Update hat keine Roboterabläufe erneut getestet. |
| 2025 | Konfiguration nach Elias und LiskinLabs: .NET Framework 4.8 und Host-Assemblies von RobotStudio 2025. Hier weder für 2025 kompiliert noch zur Laufzeit getestet. |
| 2026.1+ | Nur ein experimentelles .NET-10-Projekt. Nicht mit dem SDK 2026 kompiliert oder zur Laufzeit getestet. |

Als Referenzen für 2025 dienen [Elias](https://github.com/eliasbitsch/abb-robotstudio-mcp) und [LiskinLabs](https://github.com/LiskinLabs/abb-robotstudio-mcp). Übernommene Ansätze und offene Arbeiten stehen im [Kompatibilitätsplan](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md).

## Schnellstart

Unter Windows werden RobotStudio 2024, die Targeting-Tools für .NET Framework 4.8, Visual Studio Build Tools/MSBuild, Node.js 18+ und NuGet CLI benötigt. Roboteroperationen erfordern eine Station mit virtueller Steuerung. Die folgenden Befehle werden in PowerShell ausgeführt.

```powershell
git clone https://github.com/zhou-zhichao/robotstudio-mcp.git
cd robotstudio-mcp
nuget install addin/packages.config -OutputDirectory addin/packages
```

### 1. Add-in erstellen und installieren

RobotStudio vor der Installation schließen. Falls das Zielverzeichnis es erfordert, die Bereitstellung in einer PowerShell mit Administratorrechten ausführen. Der Build erzeugt nur Dateien in `artifacts/2024` und installiert das Add-in nicht automatisch.

```powershell
.\build.ps1
.\deploy.ps1
```

### 2. Lokale CLI / Skill verwenden

RobotStudio starten, eine Station mit virtueller Steuerung öffnen und prüfen, ob das Add-in geladen wurde. Die Befehle im Stammverzeichnis des Repositorys ausführen. Für jeden Screenshot einen neuen Dateinamen wählen.

```powershell
node scripts/robotstudio.mjs health
node scripts/robotstudio.mjs get_station_status
node scripts/robotstudio.mjs get_screenshot --output artifacts/view.png
```

In Codex kann in diesem Repository `$robotstudio` aufgerufen werden. Andere lokale Agenten können den [Skill](.agents/skills/robotstudio/SKILL.md) ebenfalls lesen. `localhost` in einer Remote- oder Cloud-Shell bezeichnet nicht den PC mit RobotStudio.

### 3. MCP verwenden (optional)

Den optionalen MCP-Server erstellen und die folgende STDIO-Definition über die Konfiguration des jeweiligen MCP-Clients hinzufügen. Den Beispielpfad durch einen absoluten Pfad auf dem eigenen PC ersetzen. Der MCP-Server verbindet sich derzeit mit `http://localhost:8080`.

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

## Ein RAPID-Modul hochladen

Eine UTF-8-Datei `params.json` mit folgendem Inhalt sowie `program.mod` mit einem vollständigen RAPID-Block `MODULE Demo ... ENDMODULE` anlegen. Das Beispiel lädt nur hoch und startet keine Ausführung. `replaceExisting:false` vermeidet die umfassende Bereinigung; vorhandene Module oder Namenskonflikte können den Upload jedoch scheitern lassen.

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

## CLI-Optionen

| Option | Verhalten |
|---|---|
| `--params-file` | Argumente aus einer UTF-8-JSON-Datei lesen. |
| `--code-file` | RAPID-Quellcode aus einer Datei lesen und Anführungszeichen erhalten. Nur für Uploads. |
| `--output` | JSON oder PNG speichern, ohne vorhandene Dateien zu überschreiben. Für Screenshots erforderlich. |
| `--url / ROBOTSTUDIO_API_BASE` | HTTP-Ursprung des Add-ins wählen. Standard: `http://127.0.0.1:8080`. |
| `--timeout` | Zeitlimit in Millisekunden (1–300000). |
| `--help / --describe` | Befehle oder die genaue Parameterdefinition anzeigen. |

Erfolgreiche Ergebnisse erscheinen als JSON auf stdout. Fehler erscheinen als JSON auf stderr mit einem Exitcode ungleich null. Den zurückgegebenen absoluten PNG-Pfad mit einem Bildbetrachter oder dem Bildwerkzeug des Agenten öffnen.

```powershell
node scripts/robotstudio.mjs --help
node scripts/robotstudio.mjs --describe upload_rapid_module
```

## Wichtige Verhaltensweisen

- Betroffene Module vor dem Ersetzen sichern. Die Voreinstellung `replaceExisting:true` versucht, Module der gewählten Task außer denen namens `BASE` oder `user` zu löschen, nicht nur das angegebene Modul. Ein automatisches Rollback ist nicht vorhanden.
- Der Simulations-Reset enthält eine demospezifische Bereinigung von Boxen und stellt nicht die gesamte Station wieder her.
- Rohe Szenenpositionen und Begrenzungsrahmen der CLI verwenden Meter; MCP kann sie in Millimeter umrechnen. Einheiten vor Berechnungen prüfen.
- Ein Schreibvorgang kann trotz Timeout wirksam geworden sein. Vor einem erneuten Versuch den Zustand prüfen; die CLI wiederholt Anfragen nicht automatisch.
- Die Projektbasis ist die Simulation mit virtueller Steuerung. Die bisherigen Tests belegen keine Eignung für reale Hardware.

## Für eine andere Version erstellen

Das Jahr ausdrücklich wählen; Standard bleibt 2024. `-RobotStudioBin` legt den SDK-Pfad fest, `-MSBuildPath` den Framework-Compiler. Die Bereitstellung prüft Build-Metadaten. `-WhatIf` zeigt eine Vorschau und setzt einen erfolgreichen Build voraus.

```powershell
.\build.ps1 -RobotStudioVersion 2025 -RobotStudioBin 'C:\Program Files (x86)\ABB\RobotStudio 2025\Bin'
.\deploy.ps1 -RobotStudioVersion 2025 -WhatIf
```

2026 benötigt zusätzlich passende .NET-10-SDK-Assemblies, die .NET-10-Entwicklungswerkzeuge und `-Experimental` beim Build und bei der Bereitstellung. Die Änderung des Jahres allein schließt die Migration nicht ab.

## Validierung

Sieben simulierte HTTP/CLI-Tests, die Skill-Formatprüfung, die TypeScript-Kompilierung und der Build des 2024-Add-ins waren erfolgreich. Die folgenden Tests steuern RobotStudio nicht. Warnungen zu veralteten Simulations-/Mastership-APIs bestehen weiter; Host-Tests für 2025/2026 stehen aus.

```powershell
node --test tests/robotstudio-cli.test.mjs
npm --prefix src run build
```

## Dokumentation und Quellcode

- [Arbeitsablauf](.agents/skills/robotstudio/SKILL.md)
- [RAPID-Beispiele](docs/RAPID_EXAMPLES.md)
- [HTTP-Referenz](docs/HTTP_API.md)
- [Kompatibilität und Schnittstellen](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)
- [Experimente einschließlich Fehlschlägen](docs/DEVELOPMENT_LOG.md)
- [CLI-Implementierung](scripts/robotstudio.mjs)
- [C#-Add-in](addin/RobotStudioAddin.cs)

## Lizenz

MIT, wie vom Projekt angegeben.
