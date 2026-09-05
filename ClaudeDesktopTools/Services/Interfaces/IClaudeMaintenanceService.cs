using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Models;

namespace ClaudeDesktopTools.Services.Interfaces;

public interface IClaudeMaintenanceService
{
    ClaudeMaintenanceSettings Settings { get; }
    void UpdateSettings(ClaudeMaintenanceSettings settings);
    Task<ClaudeMaintenanceReport> ScanAsync(CancellationToken cancellationToken = default);
    Task<List<ClaudeSessionItem>> GetStaleTranscriptsAsync(CancellationToken cancellationToken = default);
    Task<ClaudeCleanupResult> DeleteStaleTranscriptsAsync(CancellationToken cancellationToken = default);
    Task<ClaudeCleanupResult> ArchiveStaleSessionsAsync(CancellationToken cancellationToken = default);
    Task<List<ClaudeSessionItem>> GetSessionsAsync(CancellationToken cancellationToken = default);
    Task<List<ClaudeSessionItem>> GetCliSessionsAsync(CancellationToken cancellationToken = default);
    ClaudeCleanupResult CloseSession(int processId, string sessionId);
    ClaudeCleanupResult DeleteTranscript(string filePath);
    ClaudeCleanupResult DeleteTranscripts(IEnumerable<string> filePaths);
    bool IsClaudeRunning();
}
