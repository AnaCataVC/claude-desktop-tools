using System;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly IClaudeMaintenanceService _maintenanceService;

    [ObservableProperty]
    private int _transcriptRetentionDays;

    [ObservableProperty]
    private int _sessionRetentionDays;

    [ObservableProperty]
    private string _saveMessage = string.Empty;

    public SettingsViewModel(IClaudeMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
        _transcriptRetentionDays = _maintenanceService.Settings.TranscriptRetentionDays;
        _sessionRetentionDays = _maintenanceService.Settings.SessionRetentionDays;
    }

    [RelayCommand]
    public void SaveSettings()
    {
        _maintenanceService.UpdateSettings(new ClaudeMaintenanceSettings
        {
            TranscriptRetentionDays = TranscriptRetentionDays,
            SessionRetentionDays = SessionRetentionDays
        });
        SaveMessage = "Configuración guardada correctamente.";
    }
}
