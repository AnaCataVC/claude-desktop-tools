using System;
using System.Collections.ObjectModel;
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

    [ObservableProperty]
    private ObservableCollection<ClaudeDiscoveryCandidate> _candidates = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private string _targetDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

    [ObservableProperty]
    private int _maxDepth = 3;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _isSyncingToDrive;

    [ObservableProperty]
    private string _driveSyncStatusMessage = string.Empty;

    public bool IsDriveConfigured => _driveSyncService.IsConfigured;

    public ContextDiscoveryViewModel(IClaudeConfigDiscoveryService discoveryService, IDriveSyncService driveSyncService)
    {
        _discoveryService = discoveryService;
        _driveSyncService = driveSyncService;
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

    [RelayCommand]
    public async Task SyncToDriveAsync()
    {
        if (IsSyncingToDrive) return;

        IsSyncingToDrive = true;
        DriveSyncStatusMessage = "Sincronizando con Google Drive...";

        try
        {
            var result = await _driveSyncService.SyncCandidatesAsync(Candidates);
            DriveSyncStatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            DriveSyncStatusMessage = $"Error durante la sincronización: {ex.Message}";
        }
        finally
        {
            IsSyncingToDrive = false;
        }
    }
}
