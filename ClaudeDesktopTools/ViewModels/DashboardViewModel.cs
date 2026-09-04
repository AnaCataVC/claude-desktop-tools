using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.ViewModels;

public partial class DashboardViewModel : ObservableObject
{
    private readonly IClaudeMaintenanceService _maintenanceService;
    private readonly DispatcherQueue _dispatcherQueue;

    [ObservableProperty]
    private ClaudeMaintenanceReport _report = new();

    [ObservableProperty]
    private bool _isScanning;

    [ObservableProperty]
    private bool _isArchiving;

    [ObservableProperty]
    private bool _isDeleting;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    public DashboardViewModel(IClaudeMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread() ?? DispatcherQueue.GetForCurrentThread();
    }

    [RelayCommand]
    public async Task ScanAsync()
    {
        if (IsScanning || IsArchiving || IsDeleting) return;

        IsScanning = true;
        StatusMessage = "Escaneando almacenamiento local de Claude...";

        try
        {
            var rep = await _maintenanceService.ScanAsync();
            Report = rep;
            StatusMessage = $"Escaneo completado. {rep.TotalReclaimableDisplay} recuperables en transcripts.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error durante el escaneo: {ex.Message}";
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    public async Task ArchiveStaleSessionsAsync()
    {
        if (IsScanning || IsArchiving || IsDeleting) return;

        IsArchiving = true;
        StatusMessage = "Archivando sesiones inactivas de Claude Desktop...";

        try
        {
            var result = await _maintenanceService.ArchiveStaleSessionsAsync();
            StatusMessage = result.Message;
            await ScanAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al archivar sesiones: {ex.Message}";
        }
        finally
        {
            IsArchiving = false;
        }
    }

    [RelayCommand]
    public async Task<ClaudeCleanupResult> DeleteStaleTranscriptsAsync()
    {
        if (IsScanning || IsArchiving || IsDeleting)
        {
            return new ClaudeCleanupResult { Skipped = true, Message = "Hay otra operación en curso." };
        }

        IsDeleting = true;
        StatusMessage = "Eliminando transcripts fuera de retención...";

        try
        {
            var result = await _maintenanceService.DeleteStaleTranscriptsAsync();
            StatusMessage = result.Message;
            await ScanAsync();
            return result;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error al eliminar transcripts: {ex.Message}";
            return new ClaudeCleanupResult { Message = ex.Message };
        }
        finally
        {
            IsDeleting = false;
        }
    }
}
