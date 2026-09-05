using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Helpers;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.Services;

/// <summary>
/// Uploads discovered CLAUDE.md/references files to a Google Drive folder via a Google Apps
/// Script Web App bridge (same contract as the "Work Activity Panel" sync feature: filename,
/// relativePath, mimeType, data (base64) and authToken form fields, posted to a single /exec URL).
/// This avoids OAuth entirely -- the shared secret token is checked inside the deployed script.
/// </summary>
public class DriveSyncService : IDriveSyncService
{
    private const string SettingsKey = "DriveSyncSettings";
    private const long MaxFileSizeBytes = 5 * 1024 * 1024;

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(30) };

    private DriveSyncSettings _settings;

    public DriveSyncSettings Settings => _settings;
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_settings.WebAppUrl);

    public DriveSyncService()
    {
        _settings = LoadSettings();
    }

    public void UpdateSettings(DriveSyncSettings settings)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        try
        {
            LocalSettingsHelper.Set(SettingsKey, JsonSerializer.Serialize(_settings));
        }
        catch { }
    }

    public async Task<DriveSyncResult> TestConnectionAsync(CancellationToken cancellationToken = default)
    {
        var result = new DriveSyncResult();
        if (!IsConfigured)
        {
            result.Message = "Ingresa la URL de la Web App antes de probar la conexión.";
            return result;
        }

        string relativePath = CombineDrivePath(_settings.DestinationPrefix, "_healthcheck/connection-test.txt");
        byte[] probe = Encoding.UTF8.GetBytes($"Conexión verificada desde ClaudeDesktopTools el {DateTime.Now:yyyy-MM-dd HH:mm:ss}.");

        var (success, message) = await PostFileAsync("connection-test.txt", relativePath, "text/plain", probe, cancellationToken);
        result.Uploaded = success ? 1 : 0;
        result.Failed = success ? 0 : 1;
        result.Message = success
            ? "Conexión exitosa: se creó un archivo de prueba en Drive (_healthcheck/connection-test.txt)."
            : $"Falló la conexión: {message}";
        return result;
    }

    public async Task<DriveSyncResult> SyncCandidatesAsync(IEnumerable<ClaudeDiscoveryCandidate> candidates, IProgress<DriveSyncProgress>? progress = null, CancellationToken cancellationToken = default)
    {
        var result = new DriveSyncResult();
        if (!IsConfigured)
        {
            result.Message = "Configura la URL de la Web App en Ajustes antes de sincronizar.";
            return result;
        }

        // Only untracked files are worth backing up -- anything already committed to git
        // already has its history preserved in its own repository.
        var toSync = candidates.Where(c => !c.IsTrackedByGit).ToList();

        progress?.Report(DriveSyncProgress.Initial(toSync.Count));

        int current = 0;
        foreach (var candidate in toSync)
        {
            cancellationToken.ThrowIfCancellationRequested();

            current++;
            string fileName = Path.GetFileName(candidate.FilePath);
            progress?.Report(DriveSyncProgress.FileStep(current, toSync.Count, fileName, candidate.RelativePath, DriveSyncStepStatus.Uploading, result.Uploaded, result.Failed));

            byte[] bytes;
            try
            {
                bytes = await File.ReadAllBytesAsync(candidate.FilePath, cancellationToken);
            }
            catch (Exception ex)
            {
                result.Failed++;
                result.Errors.Add($"{candidate.RelativePath}: no se pudo leer ({ex.Message})");
                progress?.Report(DriveSyncProgress.FileStep(current, toSync.Count, fileName, candidate.RelativePath, DriveSyncStepStatus.FileFailed, result.Uploaded, result.Failed));
                continue;
            }

            if (bytes.Length > MaxFileSizeBytes)
            {
                result.Failed++;
                result.Errors.Add($"{candidate.RelativePath}: excede el tamaño máximo permitido ({MaxFileSizeBytes / 1024 / 1024} MB)");
                progress?.Report(DriveSyncProgress.FileStep(current, toSync.Count, fileName, candidate.RelativePath, DriveSyncStepStatus.FileFailed, result.Uploaded, result.Failed));
                continue;
            }

            string relativePath = BuildDriveRelativePath(candidate, _settings.DestinationPrefix, _settings.NoRepoBucketName, _settings.ClaudeConfigBucketName);
            string mimeType = candidate.Category == ClaudeDiscoveryCategory.Hook ? "text/plain" : "text/markdown";
            var (success, message) = await PostFileAsync(fileName, relativePath, mimeType, bytes, cancellationToken);

            if (success)
            {
                result.Uploaded++;
            }
            else
            {
                result.Failed++;
                result.Errors.Add($"{candidate.RelativePath}: {message}");
            }

            progress?.Report(DriveSyncProgress.FileStep(current, toSync.Count, fileName, candidate.RelativePath, success ? DriveSyncStepStatus.FileUploaded : DriveSyncStepStatus.FileFailed, result.Uploaded, result.Failed));

            await Task.Delay(300, cancellationToken);
        }

        progress?.Report(DriveSyncProgress.Finished(toSync.Count, result.Uploaded, result.Failed));

        result.Message = toSync.Count == 0
            ? "No hay archivos sin seguimiento para sincronizar."
            : result.Failed == 0
                ? $"Se sincronizaron {result.Uploaded} archivos sin seguimiento a Google Drive."
                : $"Se sincronizaron {result.Uploaded} de {toSync.Count} archivos ({result.Failed} con errores).";

        return result;
    }

    /// <summary>
    /// Recreates the file's project-relative path under a destination prefix so files with
    /// the same name from different repos never collide in Drive.
    /// </summary>
    public static string BuildDriveRelativePath(
        ClaudeDiscoveryCandidate candidate,
        string destinationPrefix,
        string noRepoBucketName = "_sin-repo",
        string claudeConfigBucketName = "_claude-config")
    {
        string projectSegment;
        if (!string.IsNullOrEmpty(candidate.RepositoryRoot))
        {
            projectSegment = Path.GetFileName(candidate.RepositoryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        else if (candidate.Category != ClaudeDiscoveryCategory.Context)
        {
            // Skills/agents/scheduled tasks/hooks with no repo root are global ~/.claude config,
            // not a loose file -- group them under their own bucket instead of the no-repo one.
            projectSegment = string.IsNullOrWhiteSpace(claudeConfigBucketName) ? "_claude-config" : claudeConfigBucketName.Trim();
        }
        else
        {
            projectSegment = string.IsNullOrWhiteSpace(noRepoBucketName) ? "_sin-repo" : noRepoBucketName.Trim();
        }

        string relative = candidate.RelativePath.Replace('\\', '/');
        return CombineDrivePath(destinationPrefix, $"{projectSegment}/{relative}");
    }

    public static string CombineDrivePath(string destinationPrefix, string suffix)
    {
        string prefix = string.IsNullOrWhiteSpace(destinationPrefix) ? "claude-md-unversioned" : destinationPrefix.Trim('/');
        return $"{prefix}/{suffix}";
    }

    public static (bool Success, string Message) ParseResponse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var status = doc.RootElement.TryGetProperty("status", out var statusProp) ? statusProp.GetString() : null;
            if (status == "success")
            {
                return (true, "OK");
            }

            string message = doc.RootElement.TryGetProperty("message", out var messageProp)
                ? messageProp.GetString() ?? "Error desconocido"
                : "Error desconocido";
            return (false, message);
        }
        catch (Exception ex)
        {
            return (false, $"Respuesta inválida del servidor: {ex.Message}");
        }
    }

    private async Task<(bool Success, string Message)> PostFileAsync(
        string fileName, string relativePath, string mimeType, byte[] data, CancellationToken cancellationToken)
    {
        var form = new Dictionary<string, string>
        {
            ["filename"] = fileName,
            ["relativePath"] = relativePath,
            ["mimeType"] = mimeType,
            ["data"] = Convert.ToBase64String(data),
            ["authToken"] = _settings.AuthToken
        };

        try
        {
            using var response = await HttpClient.PostAsync(_settings.WebAppUrl, new FormUrlEncodedContent(form), cancellationToken);
            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            return ParseResponse(body);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static DriveSyncSettings LoadSettings()
    {
        try
        {
            string? json = LocalSettingsHelper.Get(SettingsKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                var loaded = JsonSerializer.Deserialize<DriveSyncSettings>(json);
                if (loaded != null)
                {
                    return loaded;
                }
            }
        }
        catch { }

        return new DriveSyncSettings();
    }
}
