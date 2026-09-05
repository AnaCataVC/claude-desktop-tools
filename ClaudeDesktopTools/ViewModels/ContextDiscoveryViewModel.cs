using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.ViewModels;

public partial class ContextDiscoveryViewModel : ObservableObject
{
    private readonly IClaudeConfigDiscoveryService _discoveryService;
    private readonly IDriveSyncService _driveSyncService;
    private CancellationTokenSource? _syncCts;
    private List<CandidateGroup> _allGroups = new();

    [ObservableProperty]
    private ObservableCollection<ClaudeDiscoveryCandidate> _candidates = new();

    /// <summary>Candidates grouped by category (CLAUDE.md, Skills, Agents, Scheduled Tasks, Hooks) for the view, after applying <see cref="CategoryFilters"/>.</summary>
    [ObservableProperty]
    private ObservableCollection<CandidateGroup> _groupedCandidates = new();

    /// <summary>One checkable entry per category, controlling which groups appear in <see cref="GroupedCandidates"/>.</summary>
    public ObservableCollection<CategoryFilterOption> CategoryFilters { get; } = new(
        ClaudeDiscoveryCategory.DisplayOrder.Select(c => new CategoryFilterOption(c)));

    /// <summary>Categories available for the "select only this category" quick actions.</summary>
    public IReadOnlyList<string> AvailableCategories => ClaudeDiscoveryCategory.DisplayOrder;

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _targetDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private int _maxDepth = 3;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SyncToDriveCommand))]
    private bool _isSyncingToDrive;

    [ObservableProperty]
    private string _driveSyncStatusMessage = string.Empty;

    [ObservableProperty]
    private double _driveSyncProgressPercentage;

    [ObservableProperty]
    private int _driveSyncCurrentCount;

    [ObservableProperty]
    private int _driveSyncTotalCount;

    [ObservableProperty]
    private string _driveSyncProgressPercentageText = "0%";

    [ObservableProperty]
    private string _driveSyncProgressDetail = string.Empty;

    [ObservableProperty]
    private bool _isDriveSyncIndeterminate;

    public bool IsDriveConfigured => _driveSyncService.IsConfigured;

    public bool CanSyncToDrive => IsDriveConfigured && !IsSyncingToDrive;

    public ContextDiscoveryViewModel(IClaudeConfigDiscoveryService discoveryService, IDriveSyncService driveSyncService)
    {
        _discoveryService = discoveryService;
        _driveSyncService = driveSyncService;

        foreach (var filter in CategoryFilters)
        {
            filter.PropertyChanged += (_, _) => ApplyCategoryFilter();
        }
    }

    [RelayCommand]
    public async Task DiscoverAsync()
    {
        if (IsScanning) return;

        IsScanning = true;
        StatusMessage = "Explorando repositorios y contextos de IA...";

        try
        {
            var report = await _discoveryService.DiscoverAsync(TargetDirectory, MaxDepth);
            Candidates.Clear();
            foreach (var item in report.Candidates)
            {
                Candidates.Add(item);
            }
            RebuildGroups();
            StatusMessage = $"Descubrimiento finalizado: {report.Candidates.Count} archivos encontrados ({report.UntrackedCandidatesCount} sin seguimiento git) en {report.RepositoriesScanned} repositorios.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error durante el descubrimiento: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSyncToDrive))]
    public async Task SyncToDriveAsync()
    {
        if (IsSyncingToDrive) return;

        IsSyncingToDrive = true;
        IsDriveSyncIndeterminate = true;
        DriveSyncStatusMessage = "Sincronizando con Google Drive...";
        DriveSyncProgressDetail = "Iniciando sincronización...";
        DriveSyncProgressPercentage = 0;
        DriveSyncProgressPercentageText = "0%";
        DriveSyncCurrentCount = 0;
        DriveSyncTotalCount = 0;

        _syncCts = new CancellationTokenSource();
        var progress = new Progress<DriveSyncProgress>(OnDriveSyncProgress);

        try
        {
            var selected = Candidates.Where(c => c.IsSelected).ToList();
            var result = await _driveSyncService.SyncCandidatesAsync(selected, progress, _syncCts.Token);
            DriveSyncStatusMessage = result.Message;
        }
        catch (OperationCanceledException)
        {
            DriveSyncStatusMessage = "Sincronización cancelada.";
        }
        catch (Exception ex)
        {
            DriveSyncStatusMessage = $"Error durante la sincronización: {ex.Message}";
        }
        finally
        {
            IsSyncingToDrive = false;
            _syncCts?.Dispose();
            _syncCts = null;
        }
    }

    [RelayCommand]
    public void CancelDriveSync()
    {
        _syncCts?.Cancel();
    }

    private void RebuildGroups()
    {
        _allGroups = CandidateGroup.BuildFrom(Candidates).ToList();
        ApplyCategoryFilter();
    }

    private void ApplyCategoryFilter()
    {
        GroupedCandidates.Clear();
        foreach (var group in _allGroups)
        {
            var filter = CategoryFilters.FirstOrDefault(f => f.Category == group.Category);
            if (filter is null || filter.IsChecked)
            {
                GroupedCandidates.Add(group);
            }
        }
    }

    [RelayCommand]
    public void SelectAll()
    {
        foreach (var candidate in Candidates) candidate.IsSelected = true;
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var candidate in Candidates) candidate.IsSelected = false;
    }

    /// <summary>Deselects every candidate already tracked by git, leaving untracked candidates' selection untouched.</summary>
    [RelayCommand]
    public void DeselectGitTracked()
    {
        foreach (var candidate in Candidates.Where(c => c.IsTrackedByGit)) candidate.IsSelected = false;
    }

    /// <summary>Shows every category's group again after some were hidden via <see cref="CategoryFilters"/>.</summary>
    [RelayCommand]
    public void ShowAllCategories()
    {
        foreach (var filter in CategoryFilters) filter.IsChecked = true;
    }

    /// <summary>Marks only the given category's candidates as selected for sync, deselecting every other category.</summary>
    public void SelectOnlyCategory(string category)
    {
        foreach (var candidate in Candidates) candidate.IsSelected = candidate.Category == category;
    }

    public void SetGroupSelection(CandidateGroup group, bool isSelected)
    {
        foreach (var candidate in group) candidate.IsSelected = isSelected;
    }

    private void OnDriveSyncProgress(DriveSyncProgress progress)
    {
        IsDriveSyncIndeterminate = progress.Status == DriveSyncStepStatus.Starting && progress.Total == 0;
        DriveSyncCurrentCount = progress.Current;
        DriveSyncTotalCount = progress.Total;
        DriveSyncProgressPercentage = progress.Percentage;
        DriveSyncProgressPercentageText = $"{progress.Percentage}%";
        DriveSyncProgressDetail = progress.Detail;
    }
}
