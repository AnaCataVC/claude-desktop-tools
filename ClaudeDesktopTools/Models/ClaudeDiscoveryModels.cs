using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;

namespace ClaudeDesktopTools.Models;

public static class ClaudeDiscoveryCategory
{
    public const string Context = "Contexto (CLAUDE.md)";
    public const string Skill = "Skill";
    public const string Agent = "Agente";
    public const string ScheduledTask = "Tarea Programada";
    public const string Hook = "Hook";

    /// <summary>Display order used when grouping candidates in the UI.</summary>
    public static readonly string[] DisplayOrder = { Context, Skill, Agent, ScheduledTask, Hook };
}

public class ClaudeDiscoveryCandidate : INotifyPropertyChanged
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

    private bool _isSelected = true;

    /// <summary>Whether this file is included the next time "Sincronizar a Drive" runs. Defaults to selected.</summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>One category's worth of candidates (e.g. all Skills), for grouping in the discovery view.</summary>
public class CandidateGroup : ObservableCollection<ClaudeDiscoveryCandidate>
{
    public string Category { get; }

    public CandidateGroup(string category, IEnumerable<ClaudeDiscoveryCandidate> items) : base(items)
    {
        Category = category;
    }

    public static List<CandidateGroup> BuildFrom(IEnumerable<ClaudeDiscoveryCandidate> candidates)
    {
        var byCategory = candidates.ToLookup(c => c.Category);
        var groups = new List<CandidateGroup>();
        foreach (var category in ClaudeDiscoveryCategory.DisplayOrder)
        {
            if (byCategory[category].Any())
            {
                groups.Add(new CandidateGroup(category, byCategory[category]));
            }
        }
        return groups;
    }
}

public class ClaudeDiscoveryReport
{
    public List<ClaudeDiscoveryCandidate> Candidates { get; set; } = new();
    public int RepositoriesScanned { get; set; }
    public int UntrackedCandidatesCount { get; set; }
    public DateTime ScannedAt { get; set; } = DateTime.Now;
}
