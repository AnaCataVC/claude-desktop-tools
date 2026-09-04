using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Models;

namespace ClaudeDesktopTools.Services.Interfaces;

public interface IClaudeConfigDiscoveryService
{
    Task<ClaudeDiscoveryReport> DiscoverAsync(string rootPath, int maxDepth = 4, CancellationToken cancellationToken = default);
    Task<string?> GetGitRepoRootAsync(string directory, CancellationToken cancellationToken = default);
    Task<HashSet<string>> GetTrackedFilesAsync(string repoRoot, IEnumerable<string> relativeFilePaths, CancellationToken cancellationToken = default);
}
