using System;
using System.Collections.Generic;

namespace ClaudeDesktopTools.Models;

public class ClaudeDiscoveryCandidate
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string RepositoryRoot { get; set; } = string.Empty;
    public bool IsTrackedByGit { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime LastModified { get; set; }
    public bool HasDetectedSecrets { get; set; }

    public string SizeDisplay => ClaudeStoreReport.FormatBytes(FileSizeBytes);
    public string StatusBadge => IsTrackedByGit ? "Git Tracked" : "Sin seguimiento";
}

public class ClaudeDiscoveryReport
{
    public List<ClaudeDiscoveryCandidate> Candidates { get; set; } = new();
    public int RepositoriesScanned { get; set; }
    public int UntrackedCandidatesCount { get; set; }
    public int SecretFilteredCount { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.Now;
}
