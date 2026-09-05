using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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

    [ObservableProperty]
    private int _bulkDeleteOlderThanDays = 7;

    public SessionsViewModel(IClaudeMaintenanceService maintenanceService)
    {
        _maintenanceService = maintenanceService;
    }

    [RelayCommand]
    public async Task LoadSessionsAsync()
    {
        if (IsLoading) return;

        IsLoading = true;
        StatusMessage = "Cargando sesiones de Claude Code...";

        try
        {
            var list = await _maintenanceService.GetCliSessionsAsync();
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

    /// <summary>Caller (the view) is responsible for confirming with the user before invoking this -- it kills a real OS process.</summary>
    [RelayCommand]
    public async Task CloseSessionAsync(ClaudeSessionItem item)
    {
        if (item.ProcessId is not int pid) return;

        var result = _maintenanceService.CloseSession(pid, item.SessionId);
        StatusMessage = result.Message;
        if (!result.Skipped && result.Failures.Count == 0)
        {
            await LoadSessionsAsync();
        }
    }

    /// <summary>Caller (the view) is responsible for confirming with the user before invoking this -- it deletes the transcript permanently.</summary>
    [RelayCommand]
    public async Task DeleteTranscriptAsync(ClaudeSessionItem item)
    {
        var result = _maintenanceService.DeleteTranscript(item.FilePath);
        StatusMessage = result.Message;
        if (!result.Skipped && result.Failures.Count == 0)
        {
            await LoadSessionsAsync();
        }
    }

    public List<ClaudeSessionItem> GetInactiveSessionsPreview() =>
        Sessions.Where(s => !s.IsActive).ToList();

    public List<ClaudeSessionItem> GetInactiveSessionsOlderThanPreview(int days) =>
        Sessions.Where(s => !s.IsActive && (DateTime.Now - s.LastModified).TotalDays >= days).ToList();

    /// <summary>Caller (the view) is responsible for confirming with the user before invoking this -- it permanently deletes every listed transcript (still subject to the 24-hour grace guard per file).</summary>
    [RelayCommand]
    public async Task DeleteInactiveSessionsAsync(IEnumerable<ClaudeSessionItem> targets)
    {
        var result = _maintenanceService.DeleteTranscripts(targets.Select(t => t.FilePath));
        StatusMessage = result.Message;
        if (!result.Skipped)
        {
            await LoadSessionsAsync();
        }
    }
}
