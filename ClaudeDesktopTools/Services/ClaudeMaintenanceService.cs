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

    private readonly string _transcriptsRoot;
    private readonly string _sessionsRoot;
    private readonly Func<bool> _isClaudeRunning;
    private ClaudeMaintenanceSettings _settings;

    public ClaudeMaintenanceSettings Settings => _settings;

    public ClaudeMaintenanceService()
        : this(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", "projects"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Claude", "claude-code-sessions"),
            IsClaudeProcessRunning)
    {
    }

    /// <summary>
    /// Test hook accepting custom directories and process detection delegates.
    /// </summary>
    public ClaudeMaintenanceService(string transcriptsRoot, string sessionsRoot, Func<bool>? isClaudeRunning = null)
    {
        _transcriptsRoot = transcriptsRoot;
        _sessionsRoot = sessionsRoot;
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

                // 24-hour guard: skip any file modified in the last 24 hours unconditionally
                if (file.LastWriteTime >= staleBefore || file.LastWriteTime >= activeAfter)
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
