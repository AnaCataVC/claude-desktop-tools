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

## 3. Path Privacy & Multi-Environment Portability

Per strict security guidelines, all paths are resolved dynamically at runtime:
- Transcripts: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects")`
- Sessions: `Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions")`

No personal username paths or machine-specific directories are hardcoded in source code or documentation. In documentation and UI messages, agnostic placeholders (`%USERPROFILE%\.claude\projects` and `%APPDATA%\Claude\claude-code-sessions`) are used exclusively.
