using System;
using System.Collections.Generic;

namespace ClaudeDesktopTools.Models;

public static class ClaudeDiscoveryCategory
{
    public const string Context = "Contexto (CLAUDE.md)";
    public const string Skill = "Skill";
    public const string Agent = "Agente";
    public const string ScheduledTask = "Tarea Programada";
    public const string Hook = "Hook";
}

public class ClaudeDiscoveryCandidate
{
    public string FilePath { get; set; } = string.Empty;
    public string RelativePath { get; set; } = string.Empty;
    public string RepositoryRoot { get; set; } = string.Empty;
    public string Category { get; set; } = ClaudeDiscoveryCategory.Context;
    public bool IsTrackedByGit { get; set; }
    public long FileSizeBytes { get; set; }
    public DateTime LastModified { get; set; }

    public string SizeDisplay => ClaudeStoreReport.FormatBytes(FileSizeBytes);
    public string StatusBadge => IsTrackedByGit ? "Git Tracked" : "Sin seguimiento";
}

public class ClaudeDiscoveryReport
{
    public List<ClaudeDiscoveryCandidate> Candidates { get; set; } = new();
    public int RepositoriesScanned { get; set; }
    public int UntrackedCandidatesCount { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.Now;
}
