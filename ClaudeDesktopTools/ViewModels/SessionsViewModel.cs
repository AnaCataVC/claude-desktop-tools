using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.ViewModels;

public partial class SessionsViewModel : ObservableObject
{
    private readonly IClaudeMaintenanceService _maintenanceService;

    [ObservableProperty]
    private ObservableCollection<ClaudeSessionItem> _sessions = new();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public SessionsViewModel(IClaudeMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    [RelayCommand]
    public async Task LoadSessionsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = "Cargando sesiones de Claude Desktop...";

        try
        {
            var list = await _maintenanceService.GetSessionsAsync();
            Sessions.Clear();
            foreach (var item in list)
            {
                Sessions.Add(item);
            }
            StatusMessage = $"Se encontraron {Sessions.Count} sesiones.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al obtener sesiones: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
