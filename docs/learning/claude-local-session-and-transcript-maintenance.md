# Engineering Learning: Safe Local Maintenance of Claude CLI Transcripts & Desktop Sessions

> **Date:** 2026-09-04  
> **Status:** Implemented in `ClaudeDesktopTools.Services.ClaudeMaintenanceService` & `DashboardView`  
> **Target Framework:** .NET 9 (`net9.0-windows10.0.26100.0`, Unpackaged)  
> **Origin:** Decoupled and elevated from `work-activity-panel`

---

## 1. Context & Architectural Challenge

When collaborating heavily with Claude Code (CLI) and Claude Desktop across multiple repositories, local disk usage and UI session lists grow without bound:
1. **Claude CLI Transcripts (`%USERPROFILE%\.claude\projects\**\*.jsonl`):**
   - Claude CLI stores complete prompt-response exchanges as uncompressed `.jsonl` files organized by project hash or sanitized working directory.
   - Long-lived development workflows easily accumulate hundreds of megabytes or gigabytes of stale session transcripts that are never automatically pruned.
2. **Claude Desktop Session Index (`%APPDATA%\Claude\claude-code-sessions\*.json`):**
   - The Claude Desktop app maintains individual JSON session descriptors containing working directories (`cwd`), session IDs, transcript excerpts, and an `isArchived` boolean flag.
   - Over time, hundreds of old sessions clutter the Claude Desktop session list, slowing mental triage.

### The Semantic Distinction: Reclaiming Disk Space vs. Pruning Session Lists
A crucial architectural challenge is that these two stores serve distinct operational purposes and require different maintenance semantics:
- **Transcripts:** Deleting `.jsonl` files **permanently destroys conversation history** but **directly recovers disk space**.
- **Desktop Sessions:** Flagging sessions as archived (`"isArchived": true`) **cleans the UI list** in Claude Desktop but **reclaims zero bytes on disk**.

Presenting both operations as a generic "cleanup" without distinguishing their effects leads to severe user confusion (e.g. users expecting gigabytes freed after archiving sessions, or unexpectedly losing resumable CLI conversations).

---

## 2. Failure Modes & Safety Guardrails

### 2.1 The "Active Session Deletion" Race Condition
If a user runs transcript cleanup while working on an active CLI session or resuming a task from earlier in the day, a naive timestamp filter (e.g., deleting files older than retention days) could purge the live session file if the retention threshold is set aggressively (or to 0 days).

**Engineered Solution (`ActiveSessionGrace`):**
`ClaudeMaintenanceService` enforces a hardcoded 24-hour grace guard:
```csharp
private static readonly TimeSpan ActiveSessionGrace = TimeSpan.FromHours(24);
```
During transcript deletion, any file touched within the last 24 hours (`file.LastWriteTime >= DateTime.Now - ActiveSessionGrace`) is unconditionally skipped, regardless of whether `TranscriptRetentionDays` is configured to 0. This ensures active and recently resumed sessions are never deleted under the user.

### 2.2 The In-Memory Process Overwrite Collision
Claude Desktop loads session metadata into memory during startup and flushes state back to disk upon window close or session transitions. If an external utility modifies session files on disk while Claude Desktop is running, Claude Desktop will silently overwrite those files on exit with its in-memory snapshot, rendering external archiving completely ineffective or causing state desynchronization.

**Engineered Solution (`IsClaudeProcessRunning`):**
`ArchiveStaleSessionsAsync` probes for running Claude processes (`Process.GetProcessesByName("claude")`). If active, the operation is refused cleanly:
```csharp
if (_isClaudeRunning())
{
    result.Skipped = true;
    result.Message = "Claude Desktop está abierto. Cierra la aplicación antes de archivar: mantiene estas sesiones en memoria y sobrescribiría el cambio al cerrarse.";
    return result;
}
```
Deletion of stale CLI transcripts remains safe while Claude is open because of the 24-hour grace window and because CLI sessions do not hold foreign project transcripts open. All process handles are cleanly disposed in a `finally` block to eliminate handle leaks.

### 2.3 Surgical Header Mutation Without Full JSON Deserialization
Claude session JSON files can reach multiple megabytes when containing embedded prompt snapshots. Furthermore, their JSON schemas may evolve across Claude Desktop releases. Full deserialization and reserialization via `JsonSerializer` risks dropping unknown fields or introducing formatting artifacts.

**Engineered Solution (`SessionHeaderChars`, Regex Replacement, Atomic Swap & Timestamp Preservation):**
The `isArchived` property is located in the object header, preceding the large transcript body. `ClaudeMaintenanceService` isolates a 1,000-character prefix (`SessionHeaderChars`), matches `"isArchived": false` (accounting for variable spacing), and performs an in-place string replacement. Furthermore, to prevent altering file modification timestamps (which would trigger unwanted backups in external sync engines) and protect against unexpected power outages during writing, the file is written to a temporary sibling file and swapped atomically:
```csharp
private static readonly Regex ArchivedFalseRegex = new(@"""isArchived""\s*:\s*false", RegexOptions.Compiled);

private static bool TryMarkArchived(string path)
{
    DateTime originalLastWrite = File.GetLastWriteTime(path);
    string text = File.ReadAllText(path);
    int split = Math.Min(SessionHeaderChars, text.Length);
    string head = text.Substring(0, split);

    var match = ArchivedFalseRegex.Match(head);
    if (!match.Success) return false;

    head = head.Remove(match.Index, match.Length).Insert(match.Index, "\"isArchived\":true");

    string tempPath = path + ".tmp." + Guid.NewGuid().ToString("N");
    try
    {
        File.WriteAllText(tempPath, head + text.Substring(split));
        File.Move(tempPath, path, overwrite: true);
        File.SetLastWriteTime(path, originalLastWrite);
        return true;
    }
    catch (Exception)
    {
        try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
        throw;
    }
}
```

### 2.4 Modal User Confirmation Dialog in WinUI 3
Because deleting transcripts is irreversible, the UI mandates an explicit confirmation dialog before invoking `DeleteStaleTranscriptsAsync`. The dialog explains the exact implications and reiterates the 24-hour protection window, wrapped in `_dialogLock` (`SemaphoreSlim(1, 1)`) to prevent dialog collision crashes in WinUI 3.

---

## 3. The Sessions View Reads CLI Transcripts, Not the Desktop Index (added 2026-09-04)

`SessionsViewModel` originally called `GetSessionsAsync()` -- the same Desktop session index (`%APPDATA%\Claude\claude-code-sessions\*.json`) described above. On a machine where `claude` is only ever launched from a terminal (never through Claude Desktop's own launcher), that folder does not exist at all, so the Sessions view always reported zero sessions regardless of how many CLI sessions were genuinely open.

**Fix (`GetCliSessionsAsync`):** a separate method lists one entry per top-level transcript file directly under a project folder in `%USERPROFILE%\.claude\projects\<project-folder>\<sessionId>.jsonl`, using `Directory.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly)` so subagent transcripts (nested one level deeper under `<sessionId>\subagents\*.jsonl`) are excluded. `SessionsViewModel.LoadSessionsAsync` now calls this instead of `GetSessionsAsync`.

The original `GetSessionsAsync()` (Desktop index) was deliberately left untouched: `DashboardViewModel.GetStaleDesktopSessionsPreviewAsync` still depends on it to preview which Desktop-indexed sessions `ArchiveStaleSessionsAsync` would flag -- that archiving flow targets `_sessionsRoot`, a completely different store from the CLI transcripts the Sessions view now shows. The two methods intentionally source from different stores; do not merge them.

`IsStale` on a `GetCliSessionsAsync` result reuses `SessionRetentionDays` (the same setting the Dashboard uses for the Desktop index) purely as a freshness cutoff for the "Archivable" badge on non-active sessions -- a recently-touched transcript alone never implies liveness (see 3.1).

### 3.1 Verifying Real Liveness: `~/.claude/sessions/<pid>.json` (added 2026-09-04)

An initial approach considered opening each transcript with `FileShare.None` to detect whether the writing process still held it open. Empirically this **does not work**: the CLI opens/appends/closes the transcript per event rather than holding a persistent handle, so even a transcript being actively written passes an exclusive-open attempt with no error. Windows Restart Manager (`RmRegisterResources`/`RmGetList`) was tried next and also reported zero owning processes for the same live file, for the same reason -- there is no handle open at the moment of the check.

**What actually works:** Claude Code itself maintains a live-session registry at `~/.claude/sessions/<pid>.json` (one file per running CLI process), containing `pid`, `sessionId`, the *real* (unsanitized) `cwd`, and `procStart` (the process's start time as a `FILETIME` tick count encoded as a string). `ClaudeMaintenanceService.LoadVerifiedLiveSessions()` reads this registry and, for each entry, calls `Process.GetProcessById(pid)` and compares its actual `StartTime` against the recorded `procStart` (2-second tolerance). Only entries that match are trusted -- this specifically defends against Windows recycling a PID onto an unrelated process once the original one exits, which a bare "is a process with this PID running?" check would miss. `GetCliSessionsAsync` cross-references each transcript's `sessionId` against this verified registry to set `IsActive` and `ProcessId`, and additionally borrows the registry's real `cwd` for active sessions (nicer than decoding the sanitized project folder name).

### 3.2 Closing a Session (`CloseSession`)

`ClaudeMaintenanceService.CloseSession(processId, sessionId)` re-runs the same verified-liveness check immediately before killing the process -- the caller's snapshot of the registry may already be a few seconds stale by the time the user confirms the action, and re-verifying is what stops a recycled PID from ever being touched. The UI (`SessionsView`) requires an explicit `ContentDialog` confirmation before invoking it: this terminates a real OS process immediately and unconditionally, with no chance to save in-progress work in that session.

### 3.3 Freeing Space for a Non-Active Session (`DeleteTranscript`)

A session that `IsActive` marks false in the Sessions view cannot be closed (there is no process to terminate), but its transcript can still be occupying disk space. `ClaudeMaintenanceService.DeleteTranscript(filePath)` deletes that one specific file on request from the Sessions view -- enforcing the exact same `ActiveSessionGrace` (24-hour) guard as the Dashboard's bulk `DeleteStaleTranscriptsAsync` sweep, so a transcript that stopped being active moments ago still cannot be deleted through this path either. Unlike the bulk sweep, it does **not** additionally require the file to be older than `TranscriptRetentionDays` -- a one-off, explicit, per-row delete is a deliberate user action, not an automatic sweep, so only the inviolable 24-hour floor applies.

### 3.4 Bulk-Deleting Inactive Sessions (`DeleteTranscripts`)

The Sessions view also offers two bulk actions above the list -- "Eliminar todas las inactivas" and "Eliminar inactivas de más de N días" -- both backed by `ClaudeMaintenanceService.DeleteTranscripts(IEnumerable<string> filePaths)`. It is a thin per-file wrapper around the same `DeleteTranscript`, so the 24-hour grace guard is enforced independently for every file in the batch rather than once for the whole selection: a mixed batch (some files older than the grace window, some not) deletes only the ones that are actually safe, and the aggregate result reports how many were skipped versus deleted so the user isn't left wondering why the count is lower than expected. `SessionsViewModel.GetInactiveSessionsPreview()` / `GetInactiveSessionsOlderThanPreview(days)` build the exact candidate list shown in the confirmation dialog before the action runs, reusing the same `PreviewDialogHelper` (extracted from `DashboardView`) used by the Dashboard's own bulk sweep.

### 3.5 The Desktop Session Index Under a Packaged (MSIX) Install

The Dashboard's "Sesiones Desktop" card reported "Directorio no encontrado" for a user who genuinely does use Claude Desktop, with two sessions open at the time. The plain `%APPDATA%\Claude\claude-code-sessions` path really didn't exist -- but the cause wasn't absence of Claude Desktop, it was *how* it's installed. This machine's Claude Desktop is a Microsoft Store (MSIX) package (`Claude_1.46388.3.0_x64__pzs8sxrjxfjjc`, running from `C:\Program Files\WindowsApps\...`), and MSIX packages run under package identity: Windows transparently redirects that app's own view of `%APPDATA%`/`%LOCALAPPDATA%` to a virtualized per-package folder, so the plain path is never created regardless of how much the app is used. The real data sits at `%LOCALAPPDATA%\Packages\Claude_<packageFamilyNameSuffix>\LocalCache\Roaming\Claude\claude-code-sessions` -- confirmed present and containing the app's other state files (`config.json`, `window-state.json`, etc.) on the same machine where the plain path was empty.

`ClaudeMaintenanceService.ResolveSessionsRoot()` (used only by the parameterless constructor, so tests supplying an explicit path are unaffected) now checks the classic path first and, if it doesn't exist, globs `%LOCALAPPDATA%\Packages` for a `Claude_*` directory and looks for `<match>\LocalCache\Roaming\Claude\claude-code-sessions` there. The package family name suffix is generated by Windows per publisher/machine and isn't hardcoded -- only the `Claude_*` prefix is matched.

---

## 4. Path Privacy & Multi-Environment Portability

Per strict security guidelines, all paths are resolved dynamically at runtime:
- Transcripts: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects")`
- Sessions: `ClaudeMaintenanceService.ResolveSessionsRoot()` -- the classic `%APPDATA%\Claude\claude-code-sessions` when it exists, otherwise the MSIX-virtualized `%LOCALAPPDATA%\Packages\Claude_*\LocalCache\Roaming\Claude\claude-code-sessions` (see 3.5)

No personal username paths or machine-specific directories are hardcoded in source code or documentation. In documentation and UI messages, agnostic placeholders (`%USERPROFILE%\.claude\projects` and `%APPDATA%\Claude\claude-code-sessions`) are used exclusively.
