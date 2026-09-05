using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Helpers;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.Services;

public class ClaudeMaintenanceService : IClaudeMaintenanceService
{
    private const string SettingsKey = "ClaudeMaintenanceSettings";

    /// <summary>
    /// Guard: never touch transcripts modified in the last 24 hours even if retention is 0 days.
    /// Protects active or recently resumed sessions.
    /// </summary>
    private static readonly TimeSpan ActiveSessionGrace = TimeSpan.FromHours(24);

    /// <summary>
    /// We only inspect the start of each session JSON file to locate "isArchived",
    /// avoiding full serialization overhead or schema incompatibilities on multi-MB transcripts.
    /// </summary>
    private const int SessionHeaderChars = 1000;

    private static readonly Regex ArchivedFalseRegex = new(@"""isArchived""\s*:\s*false", RegexOptions.Compiled);
    private static readonly Regex SessionIdRegex = new(@"""sessionId""\s*:\s*""([^""]+)""", RegexOptions.Compiled);
    private static readonly Regex CwdRegex = new(@"""cwd""\s*:\s*""([^""]+)""", RegexOptions.Compiled);

    /// <summary>
    /// A registry PID is only trusted as genuinely alive when the running process's actual
    /// start time matches the recorded one within this tolerance -- protects against Windows
    /// recycling a PID onto an unrelated process after the original one exited.
    /// </summary>
    private static readonly TimeSpan ProcessStartTimeTolerance = TimeSpan.FromSeconds(2);

    private readonly string _transcriptsRoot;
    private readonly string _sessionsRoot;
    private readonly string _liveSessionsRoot;
    private readonly Func<bool> _isClaudeRunning;
    private ClaudeMaintenanceSettings _settings;

    public ClaudeMaintenanceSettings Settings => _settings;

    public ClaudeMaintenanceService()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects"),
            ResolveSessionsRoot(),
            IsClaudeProcessRunning,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions"))
    {
    }

    /// <summary>
    /// Claude Desktop's session index lives at "%APPDATA%\Claude\claude-code-sessions" for a
    /// traditional install, but a Microsoft Store (MSIX) install runs under package identity,
    /// so Windows redirects its AppData to a virtualized per-package folder instead -- the plain
    /// path never gets created there even though the app is installed and running.
    /// </summary>
    private static string ResolveSessionsRoot()
    {
        var classicRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions");
        if (Directory.Exists(classicRoot))
        {
            return classicRoot;
        }

        var packagesDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Packages");
        if (Directory.Exists(packagesDir))
        {
            foreach (var packageDir in Directory.GetDirectories(packagesDir, "Claude_*"))
            {
                var packagedRoot = Path.Combine(packageDir, "LocalCache", "Roaming", "Claude", "claude-code-sessions");
                if (Directory.Exists(packagedRoot))
                {
                    return packagedRoot;
                }
            }
        }

        return classicRoot;
    }

    /// <summary>
    /// Test hook accepting custom directories and process detection delegates.
    /// </summary>
    public ClaudeMaintenanceService(string transcriptsRoot, string sessionsRoot, Func<bool>? isClaudeRunning = null, string? liveSessionsRoot = null)
    {
        _transcriptsRoot = transcriptsRoot;
        _sessionsRoot = sessionsRoot;
        _liveSessionsRoot = liveSessionsRoot ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "sessions");
        _isClaudeRunning = isClaudeRunning ?? IsClaudeProcessRunning;
        _settings = LoadSettings();
    }

    public void UpdateSettings(ClaudeMaintenanceSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        SaveSettings();
    }

    public bool IsClaudeRunning() => _isClaudeRunning();

    public async Task<ClaudeMaintenanceReport> ScanAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var report = new ClaudeMaintenanceReport
            {
                Transcripts = MeasureStore(
                    "Transcripts de sesiones (CLI)",
                    _transcriptsRoot,
                    "*.jsonl",
                    _settings.TranscriptRetentionDays,
                    cancellationToken,
                    reclaimsDiskSpace: true),

                Sessions = MeasureStore(
                    "Índice de sesiones (Desktop)",
                    _sessionsRoot,
                    "*.json",
                    _settings.SessionRetentionDays,
                    cancellationToken,
                    reclaimsDiskSpace: false),

                ClaudeIsRunning = _isClaudeRunning()
            };

            return report;
        }, cancellationToken);
    }

    public async Task<List<ClaudeSessionItem>> GetStaleTranscriptsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<ClaudeSessionItem>();
            if (!Directory.Exists(_transcriptsRoot))
                return list;

            DateTime staleBefore = DateTime.Now.AddDays(-_settings.TranscriptRetentionDays);
            DateTime activeAfter = DateTime.Now - ActiveSessionGrace;

            foreach (var file in EnumerateFiles(_transcriptsRoot, "*.jsonl"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsEligibleForDeletion(file.LastWriteTime, staleBefore, activeAfter))
                    continue;

                list.Add(new ClaudeSessionItem
                {
                    FilePath = file.FullName,
                    SessionId = Path.GetFileNameWithoutExtension(file.Name),
                    WorkingDirectory = file.Directory?.Name ?? "Desconocido",
                    LastModified = file.LastWriteTime,
                    FileSizeBytes = file.Length,
                    IsStale = true
                });
            }

            return list;
        }, cancellationToken);
    }

    public async Task<ClaudeCleanupResult> DeleteStaleTranscriptsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new ClaudeCleanupResult();

            if (!Directory.Exists(_transcriptsRoot))
            {
                result.Skipped = true;
                result.Message = "No hay almacén de transcripts que limpiar.";
                return result;
            }

            DateTime staleBefore = DateTime.Now.AddDays(-_settings.TranscriptRetentionDays);
            DateTime activeAfter = DateTime.Now - ActiveSessionGrace;

            foreach (var file in EnumerateFiles(_transcriptsRoot, "*.jsonl"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!IsEligibleForDeletion(file.LastWriteTime, staleBefore, activeAfter))
                {
                    continue;
                }

                long size = file.Length;
                try
                {
                    file.Delete();
                    result.FilesProcessed++;
                    result.BytesFreed += size;
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{file.Name}: {ex.Message}");
                }
            }

            result.Message = result.FilesProcessed == 0
                ? (result.Failures.Count > 0
                    ? $"No se pudieron borrar archivos ({result.Failures.Count} bloqueados o en uso)."
                    : "No había transcripts fuera de la retención.")
                : (result.Failures.Count > 0
                    ? $"Se eliminaron {result.FilesProcessed} transcripts y se liberaron {result.BytesFreedDisplay} ({result.Failures.Count} bloqueados o en uso)."
                    : $"Se eliminaron {result.FilesProcessed} transcripts y se liberaron {result.BytesFreedDisplay}.");

            return result;
        }, cancellationToken);
    }

    public async Task<ClaudeCleanupResult> ArchiveStaleSessionsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var result = new ClaudeCleanupResult();

            if (!Directory.Exists(_sessionsRoot))
            {
                result.Skipped = true;
                result.Message = "No hay índice de sesiones que archivar.";
                return result;
            }

            if (_isClaudeRunning())
            {
                result.Skipped = true;
                result.Message = "Claude Desktop está abierto. Cierra la aplicación antes de archivar: mantiene estas sesiones en memoria y sobrescribiría el cambio al cerrarse.";
                return result;
            }

            DateTime staleBefore = DateTime.Now.AddDays(-_settings.SessionRetentionDays);

            foreach (var file in EnumerateFiles(_sessionsRoot, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (file.LastWriteTime >= staleBefore)
                {
                    continue;
                }

                try
                {
                    if (TryMarkArchived(file.FullName))
                    {
                        result.FilesProcessed++;
                    }
                }
                catch (Exception ex)
                {
                    result.Failures.Add($"{file.Name}: {ex.Message}");
                }
            }

            result.Message = result.FilesProcessed == 0
                ? (result.Failures.Count > 0
                    ? $"No se pudieron archivar sesiones ({result.Failures.Count} bloqueadas o en uso)."
                    : "No había sesiones fuera de la retención sin archivar.")
                : (result.Failures.Count > 0
                    ? $"Se archivaron {result.FilesProcessed} sesiones ({result.Failures.Count} bloqueadas o en uso). Salen de la lista; no libera espacio en disco."
                    : $"Se archivaron {result.FilesProcessed} sesiones. Salen de la lista de Claude Desktop; no libera espacio en disco.");

            return result;
        }, cancellationToken);
    }

    public async Task<List<ClaudeSessionItem>> GetSessionsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<ClaudeSessionItem>();
            if (!Directory.Exists(_sessionsRoot))
                return list;

            DateTime staleBefore = DateTime.Now.AddDays(-_settings.SessionRetentionDays);

            foreach (var file in EnumerateFiles(_sessionsRoot, "*.json"))
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    string text = File.ReadAllText(file.FullName);
                    int split = Math.Min(SessionHeaderChars, text.Length);
                    string head = text.Substring(0, split);

                    bool isArchived = !ArchivedFalseRegex.IsMatch(head);
                    var idMatch = SessionIdRegex.Match(head);
                    var cwdMatch = CwdRegex.Match(head);

                    list.Add(new ClaudeSessionItem
                    {
                        FilePath = file.FullName,
                        SessionId = idMatch.Success ? idMatch.Groups[1].Value : Path.GetFileNameWithoutExtension(file.Name),
                        WorkingDirectory = cwdMatch.Success ? Regex.Unescape(cwdMatch.Groups[1].Value) : "Desconocido",
                        LastModified = file.LastWriteTime,
                        FileSizeBytes = file.Length,
                        IsArchived = isArchived,
                        IsStale = file.LastWriteTime < staleBefore
                    });
                }
                catch { }
            }

            return list;
        }, cancellationToken);
    }

    /// <summary>
    /// Lists live Claude Code CLI sessions -- one per top-level transcript file directly under a
    /// project folder in %USERPROFILE%\.claude\projects (subagent transcripts, nested one level
    /// deeper under a "subagents" folder, are excluded). Unlike <see cref="GetSessionsAsync"/>,
    /// this does not depend on the separate Claude Desktop app session index, which stays empty
    /// or missing entirely for anyone running "claude" straight from a terminal.
    /// </summary>
    public async Task<List<ClaudeSessionItem>> GetCliSessionsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            var list = new List<ClaudeSessionItem>();
            if (!Directory.Exists(_transcriptsRoot))
                return list;

            DateTime staleBefore = DateTime.Now.AddDays(-_settings.SessionRetentionDays);
            var liveSessions = LoadVerifiedLiveSessions();

            string[] projectDirs;
            try
            {
                projectDirs = Directory.GetDirectories(_transcriptsRoot);
            }
            catch
            {
                return list;
            }

            foreach (var projectDir in projectDirs)
            {
                cancellationToken.ThrowIfCancellationRequested();

                string[] files;
                try
                {
                    files = Directory.GetFiles(projectDir, "*.jsonl", SearchOption.TopDirectoryOnly);
                }
                catch
                {
                    continue;
                }

                foreach (var path in files)
                {
                    var file = new FileInfo(path);
                    string sessionId = Path.GetFileNameWithoutExtension(file.Name);
                    bool isActive = liveSessions.TryGetValue(sessionId, out var live);

                    list.Add(new ClaudeSessionItem
                    {
                        FilePath = file.FullName,
                        SessionId = sessionId,
                        SessionName = isActive ? live.Name : string.Empty,
                        WorkingDirectory = isActive ? live.Cwd : Path.GetFileName(projectDir),
                        LastModified = file.LastWriteTime,
                        FileSizeBytes = file.Length,
                        IsArchived = false,
                        IsStale = file.LastWriteTime < staleBefore,
                        IsActive = isActive,
                        ProcessId = isActive ? live.Pid : null
                    });
                }
            }

            list.Sort((a, b) => b.LastModified.CompareTo(a.LastModified));
            return list;
        }, cancellationToken);
    }

    /// <summary>
    /// Ends a genuinely active CLI session by terminating its OS process. Re-verifies the PID
    /// right before killing it (the registry snapshot the caller holds may be seconds stale) so a
    /// PID Windows has since recycled onto an unrelated process is never touched.
    /// </summary>
    public ClaudeCleanupResult CloseSession(int processId, string sessionId)
    {
        var result = new ClaudeCleanupResult();
        var liveSessions = LoadVerifiedLiveSessions();

        if (!liveSessions.TryGetValue(sessionId, out var live) || live.Pid != processId)
        {
            result.Skipped = true;
            result.Message = "Esa sesión ya no está activa (el proceso terminó o cambió).";
            return result;
        }

        try
        {
            using var process = Process.GetProcessById(processId);
            process.Kill();
            result.FilesProcessed = 1;
            result.Message = "Sesión cerrada.";
        }
        catch (Exception ex)
        {
            result.Failures.Add(ex.Message);
            result.Message = $"No se pudo cerrar la sesión: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Deletes one specific transcript to reclaim its disk space -- for a session already
    /// confirmed not active (see <see cref="ClaudeSessionItem.IsActive"/>), from the Sessions
    /// view. Still enforces the same 24-hour grace guard as the bulk sweep
    /// (<see cref="DeleteStaleTranscriptsAsync"/>): a transcript this recent is never deleted
    /// regardless of the caller's reason, since it may have just stopped being active moments ago.
    /// </summary>
    public ClaudeCleanupResult DeleteTranscript(string filePath)
    {
        var result = new ClaudeCleanupResult();

        if (!File.Exists(filePath))
        {
            result.Skipped = true;
            result.Message = "El archivo ya no existe.";
            return result;
        }

        DateTime lastWrite = File.GetLastWriteTime(filePath);
        if (lastWrite >= DateTime.Now - ActiveSessionGrace)
        {
            result.Skipped = true;
            result.Message = "No se puede eliminar: se modificó hace menos de 24 horas.";
            return result;
        }

        try
        {
            long size = new FileInfo(filePath).Length;
            File.Delete(filePath);
            result.FilesProcessed = 1;
            result.BytesFreed = size;
            result.Message = $"Se liberaron {result.BytesFreedDisplay}.";
        }
        catch (Exception ex)
        {
            result.Failures.Add(ex.Message);
            result.Message = $"No se pudo eliminar: {ex.Message}";
        }

        return result;
    }

    /// <summary>
    /// Bulk variant of <see cref="DeleteTranscript"/> for the Sessions view's "delete all inactive"
    /// / "delete inactive older than N days" actions. Reuses the single-file method so the 24-hour
    /// grace guard is enforced identically for every file, whether deleted one at a time or in bulk.
    /// </summary>
    public ClaudeCleanupResult DeleteTranscripts(IEnumerable<string> filePaths)
    {
        var aggregate = new ClaudeCleanupResult();
        int skippedCount = 0;

        foreach (var filePath in filePaths)
        {
            var single = DeleteTranscript(filePath);
            if (single.Skipped)
            {
                skippedCount++;
                continue;
            }

            aggregate.FilesProcessed += single.FilesProcessed;
            aggregate.BytesFreed += single.BytesFreed;
            aggregate.Failures.AddRange(single.Failures);
        }

        aggregate.Skipped = aggregate.FilesProcessed == 0 && aggregate.Failures.Count == 0;
        aggregate.Message = aggregate.FilesProcessed == 0
            ? (skippedCount > 0
                ? $"No se eliminó nada: {skippedCount} transcript(s) protegidos (modificados hace menos de 24 horas o ya no existen)."
                : "No había transcripts para eliminar.")
            : $"Se eliminaron {aggregate.FilesProcessed} transcripts y se liberaron {aggregate.BytesFreedDisplay}."
                + (skippedCount > 0 ? $" ({skippedCount} protegidos por la ventana de 24 horas)" : "")
                + (aggregate.Failures.Count > 0 ? $" ({aggregate.Failures.Count} con errores)" : "");

        return aggregate;
    }

    /// <summary>
    /// Reads ~/.claude/sessions/&lt;pid&gt;.json (the CLI's own live-session registry) and keeps
    /// only entries whose PID is confirmed still running with a matching process start time.
    /// </summary>
    private Dictionary<string, (int Pid, string Cwd, string Name)> LoadVerifiedLiveSessions()
    {
        var result = new Dictionary<string, (int Pid, string Cwd, string Name)>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_liveSessionsRoot))
            return result;

        string[] files;
        try
        {
            files = Directory.GetFiles(_liveSessionsRoot, "*.json", SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return result;
        }

        foreach (var path in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                var root = doc.RootElement;

                if (!root.TryGetProperty("sessionId", out var sessionIdProp) ||
                    !root.TryGetProperty("pid", out var pidProp) ||
                    !root.TryGetProperty("procStart", out var procStartProp))
                {
                    continue;
                }

                string sessionId = sessionIdProp.GetString() ?? string.Empty;
                int pid = pidProp.GetInt32();
                string cwd = root.TryGetProperty("cwd", out var cwdProp) ? cwdProp.GetString() ?? string.Empty : string.Empty;
                string name = root.TryGetProperty("name", out var nameProp) ? nameProp.GetString() ?? string.Empty : string.Empty;

                if (string.IsNullOrEmpty(sessionId) || !long.TryParse(procStartProp.GetString(), out long procStartTicks))
                {
                    continue;
                }

                if (IsProcessGenuinelyAlive(pid, procStartTicks))
                {
                    result[sessionId] = (pid, cwd, name);
                }
            }
            catch
            {
                // Malformed or mid-write registry file -- skip it, not a genuinely verifiable session.
            }
        }

        return result;
    }

    /// <summary>
    /// A recorded PID alone is not proof of liveness -- Windows can reuse a PID for an unrelated
    /// process once the original exits. Requiring the actual process start time to match the one
    /// recorded when the session began rules that out.
    /// </summary>
    private static bool IsProcessGenuinelyAlive(int pid, long procStartFileTimeTicks)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            DateTime recordedStart = DateTime.FromFileTime(procStartFileTimeTicks);
            return (process.StartTime - recordedStart).Duration() < ProcessStartTimeTolerance;
        }
        catch
        {
            return false;
        }
    }

    private ClaudeStoreReport MeasureStore(
        string displayName, string root, string pattern, int retentionDays, CancellationToken cancellationToken, bool reclaimsDiskSpace = true)
    {
        var store = new ClaudeStoreReport
        {
            DisplayName = displayName,
            Exists = Directory.Exists(root),
            ReclaimsDiskSpace = reclaimsDiskSpace
        };

        if (!store.Exists)
        {
            return store;
        }

        DateTime staleBefore = DateTime.Now.AddDays(-retentionDays);

        foreach (var file in EnumerateFiles(root, pattern))
        {
            cancellationToken.ThrowIfCancellationRequested();

            store.TotalFiles++;
            store.TotalBytes += file.Length;

            if (file.LastWriteTime < staleBefore)
            {
                store.StaleFiles++;
                store.StaleBytes += file.Length;
            }
        }

        return store;
    }

    private static IEnumerable<FileInfo> EnumerateFiles(string root, string pattern)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        IEnumerator<FileInfo> enumerator;
        try
        {
            enumerator = new DirectoryInfo(root).EnumerateFiles(pattern, options).GetEnumerator();
        }
        catch (Exception)
        {
            yield break;
        }

        using (enumerator)
        {
            while (true)
            {
                FileInfo current;
                try
                {
                    if (!enumerator.MoveNext()) break;
                    current = enumerator.Current;
                }
                catch (Exception)
                {
                    break;
                }

                yield return current;
            }
        }
    }

    private static bool TryMarkArchived(string path)
    {
        DateTime originalLastWrite = File.GetLastWriteTime(path);
        string text = File.ReadAllText(path);
        int split = Math.Min(SessionHeaderChars, text.Length);
        string head = text.Substring(0, split);

        var match = ArchivedFalseRegex.Match(head);
        if (!match.Success)
        {
            return false;
        }

        head = head.Remove(match.Index, match.Length)
                   .Insert(match.Index, "\"isArchived\":true");

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
            try
            {
                if (File.Exists(tempPath)) File.Delete(tempPath);
            }
            catch { }
            throw;
        }
    }

    /// <summary>
    /// A transcript is eligible for physical deletion only once it is older than the configured
    /// retention window AND outside the unconditional 24-hour active-session grace period.
    /// </summary>
    private static bool IsEligibleForDeletion(DateTime lastWriteTime, DateTime staleBefore, DateTime activeAfter)
        => lastWriteTime < staleBefore && lastWriteTime < activeAfter;

    private static bool IsClaudeProcessRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("claude");
            try
            {
                return processes.Length > 0;
            }
            finally
            {
                foreach (var p in processes)
                {
                    p.Dispose();
                }
            }
        }
        catch (Exception)
        {
            return false;
        }
    }

    private ClaudeMaintenanceSettings LoadSettings()
    {
        try
        {
            string? json = LocalSettingsHelper.Get(SettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var loaded = JsonSerializer.Deserialize<ClaudeMaintenanceSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch { }

        return new ClaudeMaintenanceSettings();
    }

    private void SaveSettings()
    {
        try
        {
            LocalSettingsHelper.Set(SettingsKey, JsonSerializer.Serialize(_settings));
        }
        catch { }
    }
}
