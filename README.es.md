# RobotStudio Agent Bridge

[English](README.md) · [简体中文](README.zh-CN.md) · [Español](README.es.md) · [Português (Brasil)](README.pt-BR.md) · [日本語](README.ja.md) · [한국어](README.ko.md) · [Français](README.fr.md) · [Deutsch](README.de.md)

**Conecta asistentes de IA con ABB RobotStudio mediante Skills, una CLI local o MCP.**

Inspecciona una estación, carga código RAPID, ejecuta una simulación y comprueba el resultado con estados, registros e imágenes. Este proyecto de investigación ofrece un complemento HTTP en C# y dos interfaces: una CLI independiente en Node.js guiada por una Skill del repositorio y un servidor MCP opcional en TypeScript.

## Funciones

| Área | Comandos |
|---|---|
| Estación y robot | `get_station_status`, `get_robot_joints` |
| Simulación y ejecución | `control_simulation`, `control_rapid_execution`, `get_rapid_execution_status` |
| Código RAPID y diagnóstico | `upload_rapid_module`, `get_rapid_module_source`, `list_rapid_modules`, `get_execution_errors` |
| Variables y E/S | `read_rapid_variable`, `set_rapid_variable`, `list_rapid_variables`, `get_io_signals`, `set_io_signal` |
| Escena e imágenes | `get_scene_objects`, `get_screenshot` |

## Arquitectura

```text
AI agent -- Skill --> Node.js CLI ------+
                                       |
AI agent -- MCP ---> TypeScript server -+--> HTTP :8080 --> C# add-in --> ABB SDK
```

Ambas interfaces comparten el complemento y la lógica del controlador. La CLI necesita Node.js 18+, sin paquetes npm ni registro MCP. El complemento sigue siendo necesario. La Skill describe el flujo de trabajo; no sustituye al SDK.

## Compatibilidad

| Versión | Estado |
|---|---|
| 2024 | Base actual, con experimentos históricos y compilación local satisfactoria. Esta actualización no repitió las pruebas del robot. |
| 2025 | Configuración basada en Elias y LiskinLabs: .NET Framework 4.8 y ensamblados de RobotStudio 2025. Sin compilación ni pruebas de ejecución propias para 2025. |
| 2026.1+ | Solo un proyecto experimental .NET 10. Sin compilar con el SDK 2026 ni probar en ejecución. |

Las referencias para 2025 son [Elias](https://github.com/eliasbitsch/abb-robotstudio-mcp) y [LiskinLabs](https://github.com/LiskinLabs/abb-robotstudio-mcp). Consulta el [plan de compatibilidad](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md) para conocer el diseño adoptado y el trabajo pendiente.

## Inicio rápido

En Windows, instala RobotStudio 2024, las herramientas de destino .NET Framework 4.8, Visual Studio Build Tools/MSBuild, Node.js 18+ y NuGet CLI. Las operaciones del robot requieren una estación con controlador virtual. Ejecuta los comandos en PowerShell.

```powershell
git clone https://github.com/zhou-zhichao/robotstudio-mcp.git
cd robotstudio-mcp
nuget install addin/packages.config -OutputDirectory addin/packages
```

### 1. Compilar e instalar el complemento

Cierra RobotStudio antes de instalar. Ejecuta el despliegue desde PowerShell como administrador si la carpeta lo requiere. La compilación solo genera archivos en `artifacts/2024`; no instala el complemento.

```powershell
.\build.ps1
.\deploy.ps1
```

### 2. Usar la CLI local / Skill

Inicia RobotStudio, abre una estación con controlador virtual y comprueba que el complemento se cargue. Ejecuta lo siguiente desde la raíz del repositorio. Utiliza un nombre nuevo para cada captura.

```powershell
node scripts/robotstudio.mjs health
node scripts/robotstudio.mjs get_station_status
node scripts/robotstudio.mjs get_screenshot --output artifacts/view.png
```

En Codex, invoca `$robotstudio` desde este repositorio. Otros agentes locales pueden leer la [Skill](.agents/skills/robotstudio/SKILL.md). El `localhost` de una terminal remota o en la nube no corresponde al equipo con RobotStudio.

### 3. Usar MCP (opcional)

Compila el servidor MCP opcional y añade esta definición STDIO mediante la configuración de tu cliente MCP. Sustituye la ruta de ejemplo por una ruta absoluta de tu equipo. El servidor MCP se conecta actualmente a `http://localhost:8080`.

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

## Cargar un módulo RAPID

Crea `params.json` en UTF-8 con el contenido siguiente y un archivo `program.mod` con un bloque RAPID completo `MODULE Demo ... ENDMODULE`. El ejemplo solo carga el módulo; no inicia la ejecución. `replaceExisting:false` evita la limpieza general, pero la carga puede fallar si ya existe el módulo o hay símbolos en conflicto.

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

## Opciones de la CLI

| Opción | Comportamiento |
|---|---|
| `--params-file` | Lee argumentos de un archivo JSON UTF-8. |
| `--code-file` | Lee código RAPID de un archivo, conservando las comillas. Solo para cargas. |
| `--output` | Guarda JSON o PNG; nunca sobrescribe. Obligatorio para capturas. |
| `--url / ROBOTSTUDIO_API_BASE` | Selecciona el origen HTTP del complemento. Predeterminado: `http://127.0.0.1:8080`. |
| `--timeout` | Tiempo de espera en milisegundos (1–300000). |
| `--help / --describe` | Muestra los comandos o la definición exacta de parámetros. |

Los resultados correctos se escriben como JSON en stdout. Los errores van a stderr como JSON y devuelven un código de salida distinto de cero. Abre la ruta PNG devuelta con un visor o la herramienta de imágenes del agente.

```powershell
node scripts/robotstudio.mjs --help
node scripts/robotstudio.mjs --describe upload_rapid_module
```

## Comportamientos importantes

- Haz una copia de los módulos afectados antes de reemplazarlos. Con `replaceExisting:true`, valor predeterminado, se intenta eliminar los módulos de la tarea salvo los llamados `BASE` o `user`, no solo el indicado. No hay reversión automática.
- El reinicio de simulación incluye limpieza de cajas específica de la demostración; no restaura toda la estación.
- Las posiciones y dimensiones de escena de la CLI están en metros; MCP puede convertirlas a milímetros. Comprueba las unidades.
- Una escritura puede haberse aplicado aunque venza el tiempo de espera. Comprueba el estado antes de repetirla; la CLI no reintenta automáticamente.
- La base del proyecto es la simulación con controlador virtual. Estas pruebas no acreditan su uso con hardware real.

## Compilar para otra versión

Selecciona el año explícitamente; el valor predeterminado es 2024. `-RobotStudioBin` permite indicar el SDK y `-MSBuildPath` el compilador Framework. El despliegue comprueba los metadatos; `-WhatIf` muestra una vista previa y requiere una compilación correcta previa.

```powershell
.\build.ps1 -RobotStudioVersion 2025 -RobotStudioBin 'C:\Program Files (x86)\ABB\RobotStudio 2025\Bin'
.\deploy.ps1 -RobotStudioVersion 2025 -WhatIf
```

2026 requiere ensamblados SDK .NET 10 compatibles, herramientas de desarrollo .NET 10 y `-Experimental` tanto al compilar como al instalar. Cambiar únicamente el año no completa la migración.

## Validación

Pasaron siete pruebas HTTP/CLI simuladas, la validación de la Skill, la compilación TypeScript y la compilación del complemento 2024. Las pruebas siguientes no controlan RobotStudio. Persisten avisos de API obsoletas de simulación/Mastership; falta verificar 2025/2026 en RobotStudio.

```powershell
node --test tests/robotstudio-cli.test.mjs
npm --prefix src run build
```

## Documentación y código

- [Flujo de trabajo](.agents/skills/robotstudio/SKILL.md)
- [Ejemplos RAPID](docs/RAPID_EXAMPLES.md)
- [Referencia HTTP](docs/HTTP_API.md)
- [Compatibilidad y diseño de interfaces](docs/COMPATIBILITY_AND_AGENT_INTERFACES.md)
- [Historial de experimentos y fallos](docs/DEVELOPMENT_LOG.md)
- [Implementación CLI](scripts/robotstudio.mjs)
- [Complemento C#](addin/RobotStudioAddin.cs)

## Licencia

MIT, según la declaración del proyecto.
