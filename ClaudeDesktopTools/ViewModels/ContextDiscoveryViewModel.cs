using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
    [NotifyPropertyChangedFor(nameof(CanSyncToDrive))]
    [NotifyCanExecuteChangedFor(nameof(SyncToDriveCommand))]
    private bool _isSyncingToDrive;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SyncButtonText))]
    [NotifyPropertyChangedFor(nameof(CanSyncToDrive))]
    [NotifyPropertyChangedFor(nameof(SyncButtonToolTip))]
    [NotifyCanExecuteChangedFor(nameof(SyncToDriveCommand))]
    private int _selectedCandidatesCount;

    [ObservableProperty]
    private string _lastSyncDisplay = string.Empty;

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

    public bool CanSyncToDrive => IsDriveConfigured && !IsSyncingToDrive && SelectedCandidatesCount > 0;

    public string SyncButtonText => SelectedCandidatesCount switch
    {
        0 => "Sincronizar a Drive (0 seleccionados)",
        1 => "Sincronizar 1 archivo sin seguimiento a Drive",
        _ => $"Sincronizar {SelectedCandidatesCount} archivos sin seguimiento a Drive"
    };

    public string SyncButtonToolTip
    {
        get
        {
            if (!IsDriveConfigured)
                return "Configura la Web App de Google Drive en Ajustes para habilitar esto.";
            if (SelectedCandidatesCount == 0)
                return "Selecciona al menos un archivo sin seguimiento para sincronizar.";
            return "Sincroniza los archivos sin seguimiento seleccionados a Google Drive.";
        }
    }

    public ContextDiscoveryViewModel(IClaudeConfigDiscoveryService discoveryService, IDriveSyncService driveSyncService)
    {
        _discoveryService = discoveryService;
        _driveSyncService = driveSyncService;

        foreach (var filter in CategoryFilters)
        {
            filter.PropertyChanged += (_, _) => ApplyCategoryFilter();
        }

        UpdateLastSyncDisplay();
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
            foreach (var c in Candidates)
            {
                c.PropertyChanged -= OnCandidatePropertyChanged;
            }
            Candidates.Clear();
            foreach (var item in report.Candidates.Where(c => !c.IsTrackedByGit))
            {
                item.PropertyChanged += OnCandidatePropertyChanged;
                Candidates.Add(item);
            }
            RebuildGroups();
            UpdateSelectionState();
            StatusMessage = $"Descubrimiento finalizado: {Candidates.Count} archivos sin seguimiento git encontrados en {report.RepositoriesScanned} repositorios.";
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
            UpdateLastSyncDisplay();
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
        UpdateSelectionState();
    }

    [RelayCommand]
    public void DeselectAll()
    {
        foreach (var candidate in Candidates) candidate.IsSelected = false;
        UpdateSelectionState();
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
        UpdateSelectionState();
    }

    public void SetGroupSelection(CandidateGroup group, bool isSelected)
    {
        foreach (var candidate in group) candidate.IsSelected = isSelected;
        UpdateSelectionState();
    }

    public void UpdateLastSyncDisplay()
    {
        var settings = _driveSyncService.Settings;
        if (settings.LastSyncAt.HasValue)
        {
            var countText = settings.LastSyncCount == 1 ? "1 archivo" : $"{settings.LastSyncCount} archivos";
            LastSyncDisplay = $"Última sincronización: {settings.LastSyncAt.Value:dd-MM-yyyy HH:mm} ({countText})";
        }
        else
        {
            LastSyncDisplay = "Última sincronización: Nunca";
        }
    }

    private void OnCandidatePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ClaudeDiscoveryCandidate.IsSelected))
        {
            UpdateSelectionState();
        }
    }

    private void UpdateSelectionState()
    {
        SelectedCandidatesCount = Candidates.Count(c => c.IsSelected);
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
