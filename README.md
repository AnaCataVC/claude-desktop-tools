# Claude Desktop Tools

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4?logo=windows&logoColor=white)](https://microsoft.github.io/microsoft-ui-xaml/)
[![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-2.4-0078D4)](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
[![xUnit](https://img.shields.io/badge/Tests-xUnit%20(93%20passing)-brightgreen)](https://xunit.net/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

Native Windows 11 desktop application crafted with **WinUI 3**, **.NET 9**, Fluent Design, and Mica backdrop. It acts as an autonomous visual command center to audit local storage, safely maintain session indexes, prune stale transcripts, back up steering configurations to the cloud, and discover project-level AI context (`CLAUDE.md`, skills, agents, hooks) for both **Claude Code (CLI)** and **Claude Desktop**.

---

## 🌐 Language Navigation
- [English Documentation](#english)
- [Documentación en Español](#español)

---

<a name="english"></a>
## 🇬🇧 English

### Overview & Purpose
When collaborating intensively with Claude Code CLI and Claude Desktop across repositories, local storage and session lists accumulate without bound:
- **CLI Transcripts (`%USERPROFILE%\.claude\projects\**\*.jsonl`):** Full prompt/response exchanges consume hundreds of megabytes or gigabytes of disk space.
- **Desktop Session Index (`%APPDATA%\Claude\claude-code-sessions\*.json`):** Unbounded sessions clutter the user interface.
- **Unversioned Steering & Context Loss:** Ephemeral instructions, agent rosters, skills, and custom hooks are easily lost upon workstation failure.

`Claude Desktop Tools` establishes an engineering separation between **reclaiming physical disk space** and **pruning session lists**, protected by robust safety guardrails and automated cloud backup.

### Critical Safety Invariants & Guardrails
1. **Dynamic Safe Paths:** Paths resolved dynamically via `Environment.SpecialFolder.UserProfile` and `ApplicationData`. No hardcoded computer paths. For the Desktop session index specifically, `ClaudeMaintenanceService.ResolveSessionsRoot()` also falls back to the MSIX-virtualized `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code-sessions` when the classic path doesn't exist -- a Microsoft Store install of Claude Desktop runs under package identity, so Windows never creates the plain path there even while the app is genuinely in use.
2. **24-Hour Active Session Grace Window:** Transcripts touched within the last 24 hours (`ActiveSessionGrace = TimeSpan.FromHours(24)`) are **NEVER deleted**, preserving active or recently resumed CLI tasks even if retention is configured to 0 days.
3. **In-Memory Process Collision Lock:** Detects running `claude.exe` processes (`Process.GetProcessesByName("claude")`) to refuse session archiving that would otherwise be silently overwritten upon Claude shutdown.
4. **Surgical In-Place Header Mutation:** Analyzes only the first 1,000 characters (`SessionHeaderChars`) to flip `"isArchived": false` to `"isArchived": true` atomically using a sibling `.tmp` file and preserving `LastWriteTime`.
5. **Modal Deletion Guard:** All destructive actions require explicit user confirmation through a WinUI 3 `ContentDialog` guarded by `SemaphoreSlim(1, 1)` (`_dialogLock`) to prevent collision crashes.
6. **Context Discovery & Secret Scanning:** Multi-zone BFS traversal (`CLAUDE.md`, `references/`, `skills/`, `agents/`, `hooks/`) with strict exclusion of internal cache/memory directories, batched `git ls-files` checking (chunks of 50), and regex filters for SSH private keys, AWS tokens, GitHub PATs, and Slack secrets.
7. **Cloud Backup with Live Progress, Grouped and Selective:** `DriveSyncService` backs up discovered, unversioned AI context files to Google Drive through an `IProgress<DriveSyncProgress>` callback (per-file percentage, current file name, uploaded/failed counters) with user-cancellable transfers. Orphan files (no owning Git repo) keep their full path relative to the scan root, so files with the same name from different folders land at distinct Drive destinations instead of overwriting each other. The two bucket names those orphans land under (`_sin-repo` for CLAUDE.md/references, `_claude-config` for skills/agents/hooks) are configurable in Settings, with a dedicated save button and confirmation message scoped to the Drive card. There is no local change-detection: every untracked candidate is re-uploaded on every sync, and whether Drive ends up overwritten-in-place versus duplicated depends entirely on the receiving Google Apps Script's own upsert-by-path logic (outside this repo). The discovery view groups results by category (CLAUDE.md, Skills, Agents, Scheduled Tasks, Hooks) instead of one mixed list, and every file has its own checkbox (checked by default) -- "Sincronizar a Drive" only sends what's still checked, with "Seleccionar todo" / "Ninguno" available both globally and per category.
8. **Live CLI Session Explorer with Verified Liveness:** The Sessions view lists Claude Code CLI sessions (`GetCliSessionsAsync`) by reading top-level transcripts under `%USERPROFILE%\.claude\projects\<project-folder>\<sessionId>.jsonl` (subagent transcripts under nested `subagents/` folders are excluded). This is intentionally separate from the Desktop app's own session index (`%APPDATA%\Claude\claude-code-sessions`, used only by the Dashboard's archiving flow) — that store stays empty for anyone running `claude` straight from a terminal instead of through Claude Desktop's own launcher. A session is only badged "Activa" after cross-referencing it against `~/.claude/sessions/<pid>.json` (Claude Code's own live-session registry) **and** confirming the recorded PID is still running with a matching process start time — immune to Windows recycling that PID onto an unrelated process. The matching "Cerrar sesión" action terminates that verified process directly, behind a `ContentDialog` confirmation. A session that is *not* active offers "Liberar espacio" instead, deleting its transcript file directly to reclaim disk space -- still subject to the same 24-hour grace guard as the Dashboard's bulk sweep (`DeleteTranscript`). Beyond single sessions, a bulk-delete bar above the list offers "Eliminar todas las inactivas" and "Eliminar inactivas de más de N días" (default 7), both routed through `DeleteTranscripts` -- a per-file wrapper around `DeleteTranscript` that aggregates bytes freed and still enforces the 24-hour grace guard file-by-file, so a mixed selection only ever removes what's actually safe to remove.
9. **Process Resource Monitor Scoped to Real CLI Sessions, Labeled by Working Directory:** The "Monitoreo de Recursos" tab (`ProcessMonitorView`) lists running Claude Code CLI processes with live RAM (`WorkingSet64`) and CPU% (derived from the delta of `TotalProcessorTime` between two scans, not a single instantaneous read), refreshed every 2 seconds. Since `Process.GetProcessesByName("claude")` also name-matches the packaged Claude Desktop app and every one of its Electron helper subprocesses, the scan excludes anything whose executable lives under `Program Files\WindowsApps\` -- the install path shared by the desktop app and its helpers, never used by a real CLI install. Each surviving row is labeled by the process' live working directory (read straight from its PEB via P/Invoke, x64-only, falling back to `PID {n}` when unavailable) instead of just the repeated process name -- reliable for CLI sessions launched directly from a terminal, though sessions spawned by Claude Desktop's own Cowork/agent-mode plugin infrastructure share one non-representative working directory instead of their actual repo (see `docs/learning/claude-process-resource-monitoring-and-control.md`). Two per-process actions are offered: "Limpiar RAM" trims the process working set (`EmptyWorkingSet` via `psapi.dll`, returning idle physical pages to the OS without killing the process) and "Liberar/Restaurar CPU" toggles `Process.PriorityClass` between `Normal` and `BelowNormal`. Both actions re-resolve the process by pid and re-verify its name is still `"claude"` at the moment of the click -- not just at scan time -- so a pid Windows has recycled onto an unrelated process is never touched. Both are reversible and lossless, so neither goes through the `ContentDialog` confirmation guard used by the destructive actions above.

### Project Architecture
```text
claude-desktop-tools/
│── ClaudeDesktopTools.sln
│── installer/                        # Inno Setup Windows Installer Script
│   └── ClaudeDesktopTools.iss
│── docs/
│   ├── external-references/
│   │   ├── claude-desktop-tools-architecture.md
│   │   ├── claude-process-monitor-stress-test.md
│   │   └── vanilla-product-landing-page-patterns.md
│   └── learning/
│       ├── claude-local-session-and-transcript-maintenance.md
│       ├── claude-process-resource-monitoring-and-control.md
│       ├── unversioned-ai-context-discovery-and-secret-filtering.md
│       └── winui3-contentdialog-concurrency-and-dialog-lock.md
│── ClaudeDesktopTools/               # WinUI 3 Unpackaged Executable
│   ├── Assets/                       # Application icons and branding
│   ├── Helpers/                      # LocalSettingsHelper, configuration
│   ├── Models/                       # Maintenance, Discovery, DriveSync & ProcessMonitor contracts
│   ├── Services/                     # Maintenance, ConfigDiscovery, DriveSync & ProcessMonitor services
│   ├── ViewModels/                   # Reactive MVVM ViewModels (CommunityToolkit.Mvvm)
│   └── Views/                        # Fluent Design Views (Dashboard, Sessions, Context, ProcessMonitor, Settings)
└── ClaudeDesktopTools.Tests/         # Pure .NET 9 xUnit Test Suite (93 tests passing)
```

### Installation & Local Setup
#### Option 1: Official Windows Installer (Recommended)
Download the latest `ClaudeDesktopToolsSetup-1.3.0.exe` from [GitHub Releases](https://github.com/AnaCataVC/claude-desktop-tools/releases) and follow the modern setup wizard.

#### Option 2: Build and Run from Source
Prerequisites: Windows 11 (build 22000 or newer) and [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (`9.0.317` or newer).
```powershell
# Restore dependencies and build solution
dotnet build ClaudeDesktopTools.sln -p:Platform=x64

# Run automated tests (93 passing)
dotnet test ClaudeDesktopTools.sln

# Launch application
dotnet run --project ClaudeDesktopTools\ClaudeDesktopTools.csproj
```

---

<a name="español"></a>
## 🇪🇸 Español

### Descripción y Propósito
Al colaborar intensamente con Claude Code (CLI) y Claude Desktop en múltiples repositorios, el almacenamiento local y las listas de sesiones crecen sin límite:
- **Transcripts CLI (`%USERPROFILE%\.claude\projects\**\*.jsonl`):** Conversaciones completas que acumulan cientos de megabytes o gigabytes en disco.
- **Índice de Sesiones Desktop (`%APPDATA%\Claude\claude-code-sessions\*.json`):** Cientos de sesiones antiguas que saturan la interfaz de Claude.
- **Pérdida de Contexto no Versionado:** Instrucciones personalizadas, habilidades (`skills`), agentes y hooks que se pierden al formatear el equipo.

`Claude Desktop Tools` establece una clara distinción entre **recuperar espacio físico en disco** y **ordenar las listas de sesiones**, protegido por guardas de seguridad inviolables y respaldo en la nube.

### Invariantes de Seguridad Críticas
1. **Rutas Dinámicas Seguras:** Resueltas dinámicamente mediante `UserProfile` y `ApplicationData`. Sin rutas absolutas fijas en el código. Para el índice de sesiones de Desktop en particular, `ClaudeMaintenanceService.ResolveSessionsRoot()` también recurre a la ruta virtualizada por MSIX `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code-sessions` cuando la ruta clásica no existe -- una instalación de Claude Desktop vía Microsoft Store corre bajo identidad de paquete, así que Windows nunca crea la ruta clásica ahí aunque la app esté en uso real.
2. **Guarda Inviolable de 24 Horas:** Cualquier transcript modificado en las últimas 24 horas **jamás se elimina**, protegiendo sesiones activas o recién retomadas incluso si la retención se ajusta a 0 días.
3. **Bloqueo por Proceso Activo (`claude.exe`):** Detecta si Claude Desktop está abierto para impedir el archivado de sesiones en disco que sería sobrescrito por la memoria de Claude al cerrarse.
4. **Mutación Atómica In-Place de Headers:** Examina únicamente los primeros 1.000 caracteres (`SessionHeaderChars`) para cambiar `"isArchived": false` a `"isArchived": true` mediante un archivo temporal hermano (`.tmp`) y preservando la fecha `LastWriteTime`.
5. **Protección Modal de Borrado:** La eliminación permanente de transcripts exige confirmación en un `ContentDialog` nativo protegido por un semáforo `SemaphoreSlim(1, 1)` para prevenir colisiones en WinUI 3.
6. **Descubrimiento de Contexto y Filtro de Secretos:** Exploración BFS de archivos `CLAUDE.md`, referencias, habilidades, agentes y hooks con verificación Git por lotes de 50 archivos y filtros regex contra claves SSH, AWS, tokens PAT de GitHub y Slack.
7. **Respaldo en la Nube con Progreso en Vivo, Agrupado y Selectivo:** `DriveSyncService` respalda hacia Google Drive los archivos de contexto IA descubiertos y sin versionar, reportando avance por archivo mediante `IProgress<DriveSyncProgress>` (porcentaje, archivo actual, contadores de subidos/fallidos) con cancelación disponible para el usuario. Los archivos huérfanos (sin repo Git) conservan su ruta completa relativa a la raíz del escaneo, por lo que archivos con el mismo nombre en carpetas distintas terminan en destinos distintos de Drive en vez de sobrescribirse entre sí. Las dos carpetas donde caen esos huérfanos (`_sin-repo` para CLAUDE.md/references, `_claude-config` para skills/agentes/hooks) son configurables en Ajustes, con un botón de guardado y mensaje de confirmación propios de la tarjeta de Drive. No hay detección de cambios local: cada archivo sin seguimiento se vuelve a subir en cada sincronización, y que Drive termine sobrescribiendo en el mismo lugar o duplicando depende enteramente de la lógica de upsert-por-ruta del Google Apps Script receptor (fuera de este repo). La vista de descubrimiento agrupa los resultados por categoría (CLAUDE.md, Skills, Agentes, Tareas Programadas, Hooks) en vez de una sola lista mezclada, y cada archivo tiene su propio checkbox (marcado por defecto) -- "Sincronizar a Drive" solo envía lo que sigue marcado, con "Seleccionar todo" / "Ninguno" disponibles tanto de forma global como por categoría.
8. **Explorador de Sesiones CLI con Verificación de Actividad Real:** La vista Sesiones lista sesiones de Claude Code CLI (`GetCliSessionsAsync`) leyendo los transcripts de nivel superior en `%USERPROFILE%\.claude\projects\<carpeta-proyecto>\<sessionId>.jsonl` (excluyendo transcripts de subagentes en carpetas `subagents/` anidadas). Esto es intencionalmente independiente del índice propio de sesiones de Claude Desktop (`%APPDATA%\Claude\claude-code-sessions`, usado solo por el flujo de archivado del Dashboard) — ese índice queda vacío para quien usa `claude` directo desde una terminal en vez del lanzador propio de Claude Desktop. Una sesión se marca "Activa" solo tras cruzarla contra `~/.claude/sessions/<pid>.json` (el registro propio de sesiones vivas de Claude Code) **y** confirmar que el PID registrado sigue corriendo con el mismo instante de inicio de proceso — inmune a que Windows reasigne ese PID a un proceso no relacionado. La acción "Cerrar sesión" termina ese proceso ya verificado, detrás de una confirmación en `ContentDialog`. Una sesión que *no* está activa ofrece en su lugar "Liberar espacio", que elimina directamente su archivo de transcript para recuperar espacio en disco — sujeto a la misma guarda de 24 horas que el barrido masivo del Dashboard (`DeleteTranscript`). Además, una barra de acciones masivas sobre la lista permite "Eliminar todas las inactivas" y "Eliminar inactivas de más de N días" (7 por defecto), ambas mediante `DeleteTranscripts` — un envoltorio por archivo sobre `DeleteTranscript` que suma los bytes liberados y sigue respetando la guarda de 24 horas archivo por archivo, de modo que una selección mixta solo elimina lo que realmente es seguro eliminar.
9. **Monitor de Recursos Acotado a Sesiones CLI Reales, Etiquetado por Carpeta de Trabajo:** La pestaña "Monitoreo de Recursos" (`ProcessMonitorView`) lista los procesos de Claude Code CLI en ejecución con su RAM (`WorkingSet64`) y CPU% en vivo (calculado como el delta de `TotalProcessorTime` entre dos escaneos, no una lectura instantánea), refrescado cada 2 segundos. Como `Process.GetProcessesByName("claude")` también matchea la app de escritorio empaquetada y todos sus subprocesos Electron helper, el escaneo excluye cualquier ejecutable ubicado bajo `Program Files\WindowsApps\` — la ruta de instalación compartida por la app de escritorio y sus helpers, nunca usada por una instalación real del CLI. Cada fila restante se etiqueta con el working directory real del proceso (leído directamente de su PEB vía P/Invoke, solo x64, con retorno a `PID {n}` cuando no se puede resolver) en vez de repetir el nombre del proceso — confiable para sesiones CLI lanzadas directo en terminal, aunque las sesiones lanzadas por el modo Cowork/agent-mode propio de Claude Desktop comparten un mismo working directory no representativo de su repo real (ver `docs/learning/claude-process-resource-monitoring-and-control.md`). Ofrece dos acciones por proceso: "Limpiar RAM" recorta el working set físico del proceso (`EmptyWorkingSet` vía `psapi.dll`, con advertencia técnica sobre la repaginación de memoria bajo demanda) y "Liberar/Restaurar CPU" alterna `Process.PriorityClass` entre `Normal` y `BelowNormal`. Ambas acciones resuelven el proceso nuevamente por su PID y reverifican que su nombre siga siendo `"claude"` y su `StartTime` coincida en el momento del clic — no solo al escanear — de modo que un PID que Windows haya reasignado a un proceso no relacionado nunca se toca. Ambas son reversibles y sin pérdida de datos, así que ninguna pasa por la confirmación `ContentDialog` que sí usan las acciones destructivas anteriores.

### Instalación y Ejecución Local
#### Opción 1: Instalador Oficial para Windows (Recomendado)
Descarga el archivo `ClaudeDesktopToolsSetup-1.3.0.exe` desde [GitHub Releases](https://github.com/AnaCataVC/claude-desktop-tools/releases) y sigue el asistente interactivo.

#### Opción 2: Compilar y Ejecutar desde el Código Fuente
```powershell
# Compilar la solución en plataforma x64
dotnet build ClaudeDesktopTools.sln -p:Platform=x64

# Ejecutar la suite de 93 pruebas unitarias
dotnet test ClaudeDesktopTools.sln

# Ejecutar la aplicación de escritorio
dotnet run --project ClaudeDesktopTools\ClaudeDesktopTools.csproj
```

---

## 📄 Key Learnings & Architectural Decisions
- **Separación de Semánticas Operativas:** Archivar sesiones en Claude Desktop solo actualiza la propiedad `"isArchived": true` en el encabezado (orden visual, 0 bytes liberados). Solo la purga física de archivos `.jsonl` de CLI recupera espacio en disco.
- **Resistencia ante Corrupción de Schemas:** Mutar la cabecera mediante expresiones regulares y swaps atómicos evita deserializar árboles JSON complejos que podrían perder propiedades no documentadas en futuras versiones de Claude.
- **Estabilidad de UI en WinUI 3:** Adquisición de `DispatcherQueue` antes de la instanciación de ventanas y serialización de modales mediante semáforos previene fallos `STATUS_STOWED_EXCEPTION`.
- **CPU% Requiere Delta, no una Lectura Única:** `Process.TotalProcessorTime` es un contador acumulado desde el inicio del proceso; el CPU% en vivo se calcula comparando dos muestras separadas por el intervalo de refresco, siguiendo el mismo enfoque que usa el Administrador de Tareas de Windows.
- **Inmunidad ante Reciclaje de PIDs y Reconciliación UI:** La clave compuesta `(PID, StartTime)` previene que Windows distorsione los deltas de CPU% al reciclar identificadores de procesos terminados. La reconciliación *in-place* en `ObservableCollection` elimina el layout thrashing y preserva el foco de accesibilidad en WinUI 3.
