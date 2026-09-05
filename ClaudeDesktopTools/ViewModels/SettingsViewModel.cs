using System;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IClaudeMaintenanceService _maintenanceService;
    private readonly IDriveSyncService _driveSyncService;

    [ObservableProperty]
    private int _transcriptRetentionDays;

    [ObservableProperty]
    private int _sessionRetentionDays;

    [ObservableProperty]
    private string _saveMessage = string.Empty;

    [ObservableProperty]
    private string _driveWebAppUrl;

    [ObservableProperty]
    private string _driveAuthToken;

    [ObservableProperty]
    private string _driveDestinationPrefix;

    [ObservableProperty]
    private string _driveClaudeConfigBucketName;

    [ObservableProperty]
    private string _driveNoRepoBucketName;

    [ObservableProperty]
    private bool _isTestingDriveConnection;

    [ObservableProperty]
    private string _driveStatusMessage = string.Empty;

    [ObservableProperty]
    private string _driveSaveMessage = string.Empty;

    public SettingsViewModel(IClaudeMaintenanceService maintenanceService, IDriveSyncService driveSyncService)
    {
        _maintenanceService = maintenanceService;
        _driveSyncService = driveSyncService;

        _transcriptRetentionDays = _maintenanceService.Settings.TranscriptRetentionDays;
        _sessionRetentionDays = _maintenanceService.Settings.SessionRetentionDays;

        _driveWebAppUrl = _driveSyncService.Settings.WebAppUrl;
        _driveAuthToken = _driveSyncService.Settings.AuthToken;
        _driveDestinationPrefix = _driveSyncService.Settings.DestinationPrefix;
        _driveClaudeConfigBucketName = _driveSyncService.Settings.ClaudeConfigBucketName;
        _driveNoRepoBucketName = _driveSyncService.Settings.NoRepoBucketName;
    }

    /// <summary>Single source of truth for turning the Drive form fields into settings, so every save path (global, Drive-only, test-connection) persists the same shape.</summary>
    private DriveSyncSettings BuildDriveSettingsFromForm() => new()
    {
        WebAppUrl = DriveWebAppUrl.Trim(),
        AuthToken = DriveAuthToken.Trim(),
        DestinationPrefix = string.IsNullOrWhiteSpace(DriveDestinationPrefix) ? "claude-md-unversioned" : DriveDestinationPrefix.Trim(),
        ClaudeConfigBucketName = string.IsNullOrWhiteSpace(DriveClaudeConfigBucketName) ? "_claude-config" : DriveClaudeConfigBucketName.Trim(),
        NoRepoBucketName = string.IsNullOrWhiteSpace(DriveNoRepoBucketName) ? "_sin-repo" : DriveNoRepoBucketName.Trim()
    };

    [RelayCommand]
    public void SaveSettings()
    {
        _maintenanceService.UpdateSettings(new ClaudeMaintenanceSettings
        {
            TranscriptRetentionDays = TranscriptRetentionDays,
            SessionRetentionDays = SessionRetentionDays
        });

        _driveSyncService.UpdateSettings(BuildDriveSettingsFromForm());

        SaveMessage = "Configuración guardada correctamente.";
    }

    /// <summary>Saves only the Drive card's fields, independent of the retention settings above -- so editing Drive settings has its own visible confirmation right where the fields are.</summary>
    [RelayCommand]
    public void SaveDriveSettings()
    {
        _driveSyncService.UpdateSettings(BuildDriveSettingsFromForm());
        DriveSaveMessage = $"Configuración de Drive guardada correctamente ({DateTime.Now:HH:mm:ss}).";
    }

    [RelayCommand]
    public async Task TestDriveConnectionAsync()
    {
        if (IsTestingDriveConnection) return;

        // Persist first so the test uses whatever is currently typed in the form.
        _driveSyncService.UpdateSettings(BuildDriveSettingsFromForm());

        IsTestingDriveConnection = true;
        DriveStatusMessage = "Probando conexión...";
        try
        {
            var result = await _driveSyncService.TestConnectionAsync();
            DriveStatusMessage = result.Message;
        }
        catch (Exception ex)
        {
            DriveStatusMessage = $"Error al probar la conexión: {ex.Message}";
        }
        finally
        {
            IsTestingDriveConnection = false;
        }
    }
}
