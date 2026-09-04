using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Models;

namespace ClaudeDesktopTools.Services.Interfaces;

public interface IDriveSyncService
{
    DriveSyncSettings Settings { get; }
    bool IsConfigured { get; }

    void UpdateSettings(DriveSyncSettings settings);
    Task<DriveSyncResult> TestConnectionAsync(CancellationToken cancellationToken = default);
    Task<DriveSyncResult> SyncCandidatesAsync(IEnumerable<ClaudeDiscoveryCandidate> candidates, CancellationToken cancellationToken = default);
}
