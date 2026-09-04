# Technical Research & Ecosystem Benchmarking: Claude Desktop Tools

> **Date:** 2026-09-04  
> **Status:** Completed  
> **Target Framework:** .NET 9 (`net9.0-windows10.0.26100.0`), Unpackaged (`<WindowsPackageType>None</WindowsPackageType>`)  
> **UI Stack:** WinUI 3 (Windows App SDK 2.4 / 1.6), Fluent Design, Mica Backdrop  
> **Pattern:** Clean Architecture MVVM (`CommunityToolkit.Mvvm`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`)

---

## 1. Executive Summary & Objective

The objective is to establish `claude-desktop-tools`, an independent desktop workstation hub designed for Windows 11 to safely manage, audit, and clean local Claude Code (CLI) transcripts and Claude Desktop sessions, as well as discover and analyze local AI context files (`CLAUDE.md`, uncommitted guidelines, references).

The foundational business logic was previously decoupled from `work-activity-panel`. This document establishes the architectural baseline, technology benchmarks, safety invariants, and folder topology.

---

## 2. Technology Stack Evaluation & Trade-offs

### 2.1 UI Framework: WinUI 3 vs. WPF vs. Avalonia / Webview2

| Criteria | WinUI 3 (Windows App SDK 2.4 / 1.6) | WPF (.NET 9) | Avalonia / Webview2 |
| :--- | :--- | :--- | :--- |
| **Windows 11 Native Integration** | **Native** (Mica Alt, Acrylic, Fluent Controls, Windows 11 WinRT APIs) | Emulated via third-party libraries (WPF-UI / MicaWPF) | Emulated or non-native HTML rendering |
| **Packaging & Distribution** | **Unpackaged supported** (`WindowsPackageType=None`, standalone exe + runtime) | Native standalone exe | Standalone or Webview runtime |
| **Performance & Resource Footprint**| High (DirectX/DirectComposition rendering via Composition APIs) | Moderate (MilCore legacy pipeline) | Variable (Chromium memory overhead in Webview2) |
| **Community & Tooling** | Active Microsoft investment; `Microsoft.WindowsAppSDK` 2.4.0 / 1.6 | Mature but in maintenance mode | Cross-platform focus, less Windows-specialized |

**Decision:** WinUI 3 unpackaged with Windows App SDK 2.4.0 (matching modern developer tools on Windows 11) provides native Mica backdrop, high-DPI scaling, and modern Fluent Design controls.

### 2.2 MVVM & Dependency Injection

- **`CommunityToolkit.Mvvm` (8.4.0):** Roslyn Source Generators for `[ObservableProperty]`, `[RelayCommand]`, and event handling. Eliminates boilerplate `INotifyPropertyChanged` and ensures high performance without reflection.
- **`Microsoft.Extensions.Hosting` & `Microsoft.Extensions.DependencyInjection` (9.0.2):** Standard lifecycle management for services, configuration, logging, and view models.

### 2.3 Testing Framework

- **`xUnit` (2.9.x) + `Moq` (4.20.x):** Unit testing of file manipulation invariants, grace windows, process collision guards, and regex header replacement.
- Testing project targeting `net9.0` with references to service contracts and logic to avoid WinRT runtime initialization overhead in CI.

---

## 3. Core Safety Guardrails & Invariants

1. **Dynamic Path Resolution:**
   - Transcripts: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects")`
   - Sessions: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions")`
   - Zero hardcoded user profiles or drive letters.

2. **24-Hour Active Session Grace Guard (`ActiveSessionGrace = TimeSpan.FromHours(24)`):**
   - Transcripts touched within the last 24 hours are never deleted, even if the retention threshold is set to 0 days.

3. **Running Process Lock Guard (`claude.exe`):**
   - Probing `Process.GetProcessesByName("claude")` before attempting in-place session flag mutations prevents in-memory overwrite collisions upon Claude Desktop shutdown.

4. **Surgical In-Place Header Mutation:**
   - Reading the initial 1,000 characters (`SessionHeaderChars`), regex substitution of `"isArchived": false` -> `"isArchived": true`, writing to sibling `.tmp` file, and atomic file replacement with `LastWriteTime` preservation.

5. **Modal Confirmation Guard & Semaphore (`_dialogLock`):**
   - Single active `ContentDialog` guarded by `SemaphoreSlim(1, 1)` prevents WinUI 3 dialog collision crashes (`0x80004005`).

6. **Context Discovery & Secret Scanning:**
   - BFS traversal bounded by depth and exclusion list (`.git`, `node_modules`, `.claude/projects/**/memory`, `.claude/plans`, `.claude/security`).
   - Batched Git check (`git ls-files` in chunks of 50) and regex secret filters (SSH keys, AWS tokens, GitHub PATs, Slack webhooks).

---

## 4. Architectural Alternatives Evaluated

- **Option A (Monolithic Single-Project Architecture):**
  - Place all UI, services, models, and CLI runners into one project.
  - *Cons:* Harder to run unit tests in `net9.0` without WinRT dependencies; tight coupling between UI and file system IO.
- **Option B (Clean Modular Architecture - Recommended):**
  - `ClaudeDesktopTools` (WinUI 3 App): Unpackaged executable, MainWindow, Views, ViewModels, SystemBackdrop (Mica), Dialogs, Navigation. Includes Core Services and Models.
  - `ClaudeDesktopTools.Tests` (xUnit): Comprehensive automated test suite verifying all 6 safety invariants, referencing models and services cleanly.
