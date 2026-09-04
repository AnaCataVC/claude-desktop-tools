# Claude Desktop Tools

[![.NET 9](https://img.shields.io/badge/.NET-9.0-512BD4?logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI-3-0078D4?logo=windows&logoColor=white)](https://microsoft.github.io/microsoft-ui-xaml/)
[![Windows App SDK](https://img.shields.io/badge/Windows%20App%20SDK-2.4-0078D4)](https://learn.microsoft.com/windows/apps/windows-app-sdk/)
[![xUnit](https://img.shields.io/badge/Tests-xUnit%20(34%20passing)-brightgreen)](https://xunit.net/)
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
1. **Dynamic Safe Paths:** Paths resolved dynamically via `Environment.SpecialFolder.UserProfile` and `ApplicationData`. No hardcoded computer paths.
2. **24-Hour Active Session Grace Window:** Transcripts touched within the last 24 hours (`ActiveSessionGrace = TimeSpan.FromHours(24)`) are **NEVER deleted**, preserving active or recently resumed CLI tasks even if retention is configured to 0 days.
3. **In-Memory Process Collision Lock:** Detects running `claude.exe` processes (`Process.GetProcessesByName("claude")`) to refuse session archiving that would otherwise be silently overwritten upon Claude shutdown.
4. **Surgical In-Place Header Mutation:** Analyzes only the first 1,000 characters (`SessionHeaderChars`) to flip `"isArchived": false` to `"isArchived": true` atomically using a sibling `.tmp` file and preserving `LastWriteTime`.
5. **Modal Deletion Guard:** All destructive actions require explicit user confirmation through a WinUI 3 `ContentDialog` guarded by `SemaphoreSlim(1, 1)` (`_dialogLock`) to prevent collision crashes.
6. **Context Discovery & Secret Scanning:** Multi-zone BFS traversal (`CLAUDE.md`, `references/`, `skills/`, `agents/`, `hooks/`) with strict exclusion of internal cache/memory directories, batched `git ls-files` checking (chunks of 50), and regex filters for SSH private keys, AWS tokens, GitHub PATs, and Slack secrets.
7. **Cloud Backup & Integrity Check:** `DriveSyncService` enables backing up discovered AI context files to Google Drive with SHA-256 cryptographic hashing to avoid redundant uploads.

### Project Architecture
```text
claude-desktop-tools/
│── ClaudeDesktopTools.sln
│── installer/                        # Inno Setup Windows Installer Script
│   └── ClaudeDesktopTools.iss
│── docs/
│   ├── external-references/
│   │   └── claude-desktop-tools-architecture.md
│   └── learning/
│       ├── claude-local-session-and-transcript-maintenance.md
│       ├── unversioned-ai-context-discovery-and-secret-filtering.md
│       └── winui3-contentdialog-concurrency-and-dialog-lock.md
│── ClaudeDesktopTools/               # WinUI 3 Unpackaged Executable
│   ├── Assets/                       # Application icons and branding
│   ├── Helpers/                      # LocalSettingsHelper, configuration
│   ├── Models/                       # Maintenance, Discovery & DriveSync domain contracts
│   ├── Services/                     # ClaudeMaintenanceService, ClaudeConfigDiscoveryService, DriveSyncService
│   ├── ViewModels/                   # Reactive MVVM ViewModels (CommunityToolkit.Mvvm)
│   └── Views/                        # Fluent Design Views (Dashboard, Sessions, Context, Settings)
└── ClaudeDesktopTools.Tests/         # Pure .NET 9 xUnit Test Suite (34 tests passing)
```

### Installation & Local Setup
#### Option 1: Official Windows Installer (Recommended)
Download the latest `ClaudeDesktopToolsSetup-1.0.0.exe` from [GitHub Releases](https://github.com/AnaCataVC/claude-desktop-tools/releases) and follow the modern setup wizard.

#### Option 2: Build and Run from Source
Prerequisites: Windows 11 (build 22000 or newer) and [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0) (`9.0.317` or newer).
```powershell
# Restore dependencies and build solution
dotnet build ClaudeDesktopTools.sln -p:Platform=x64

# Run automated tests (34 passing)
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
1. **Rutas Dinámicas Seguras:** Resueltas dinámicamente mediante `UserProfile` y `ApplicationData`. Sin rutas absolutas fijas en el código.
2. **Guarda Inviolable de 24 Horas:** Cualquier transcript modificado en las últimas 24 horas **jamás se elimina**, protegiendo sesiones activas o recién retomadas incluso si la retención se ajusta a 0 días.
3. **Bloqueo por Proceso Activo (`claude.exe`):** Detecta si Claude Desktop está abierto para impedir el archivado de sesiones en disco que sería sobrescrito por la memoria de Claude al cerrarse.
4. **Mutación Atómica In-Place de Headers:** Examina únicamente los primeros 1.000 caracteres (`SessionHeaderChars`) para cambiar `"isArchived": false` a `"isArchived": true` mediante un archivo temporal hermano (`.tmp`) y preservando la fecha `LastWriteTime`.
5. **Protección Modal de Borrado:** La eliminación permanente de transcripts exige confirmación en un `ContentDialog` nativo protegido por un semáforo `SemaphoreSlim(1, 1)` para prevenir colisiones en WinUI 3.
6. **Descubrimiento de Contexto y Filtro de Secretos:** Exploración BFS de archivos `CLAUDE.md`, referencias, habilidades, agentes y hooks con verificación Git por lotes de 50 archivos y filtros regex contra claves SSH, AWS, tokens PAT de GitHub y Slack.
7. **Respaldo en la Nube con Verificación Hash:** `DriveSyncService` respalda las configuraciones descubiertas hacia Google Drive calculando hashes SHA-256 para evitar cargas duplicadas.

### Instalación y Ejecución Local
#### Opción 1: Instalador Oficial para Windows (Recomendado)
Descarga el archivo `ClaudeDesktopToolsSetup-1.0.0.exe` desde [GitHub Releases](https://github.com/AnaCataVC/claude-desktop-tools/releases) y sigue el asistente interactivo.

#### Opción 2: Compilar y Ejecutar desde el Código Fuente
```powershell
# Compilar la solución en plataforma x64
dotnet build ClaudeDesktopTools.sln -p:Platform=x64

# Ejecutar la suite de 34 pruebas unitarias
dotnet test ClaudeDesktopTools.sln

# Ejecutar la aplicación de escritorio
dotnet run --project ClaudeDesktopTools\ClaudeDesktopTools.csproj
```

---

## 📄 Key Learnings & Architectural Decisions
- **Separación de Semánticas Operativas:** Archivar sesiones en Claude Desktop solo actualiza la propiedad `"isArchived": true` en el encabezado (orden visual, 0 bytes liberados). Solo la purga física de archivos `.jsonl` de CLI recupera espacio en disco.
- **Resistencia ante Corrupción de Schemas:** Mutar la cabecera mediante expresiones regulares y swaps atómicos evita deserializar árboles JSON complejos que podrían perder propiedades no documentadas en futuras versiones de Claude.
- **Estabilidad de UI en WinUI 3:** Adquisición de `DispatcherQueue` antes de la instanciación de ventanas y serialización de modales mediante semáforos previene fallos `STATUS_STOWED_EXCEPTION`.
