using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.ViewModels;

public partial class ProcessMonitorViewModel : ObservableObject
{
    private readonly IProcessMonitorService _processMonitorService;
    private CancellationTokenSource? _feedbackCts;

    [ObservableProperty]
    private ObservableCollection<ClaudeProcessInfo> _processes = new();

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private string _totalWorkingSetDisplay = "0 MB";

    [ObservableProperty]
    private string _totalCpuPercentDisplay = "0.0%";

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private string _actionFeedbackMessage = string.Empty;

    public ProcessMonitorViewModel(IProcessMonitorService processMonitorService)
    {
        _processMonitorService = processMonitorService;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        var snapshot = await Task.Run(() => _processMonitorService.GetClaudeProcesses());

        SyncProcesses(snapshot.Processes);

        ProcessCount = snapshot.ProcessCount;
        TotalWorkingSetDisplay = ClaudeStoreReport.FormatBytes(snapshot.TotalWorkingSetBytes);
        TotalCpuPercentDisplay = $"{snapshot.TotalCpuPercent:0.0}%";
        StatusMessage = ProcessCount == 0
            ? "No hay procesos de Claude en ejecución."
            : $"{ProcessCount} proceso(s) de Claude encontrados.";
    }

    /// <summary>
    /// Reconciles incoming snapshot with the bound collection in-place.
    /// Prevents layout thrashing, avoids focus loss for assistive tech, and maintains click integrity.
    /// </summary>
    public void SyncProcesses(List<ClaudeProcessInfo> incoming)
    {
        var incomingKeys = incoming.Select(p => (p.Pid, p.StartTime)).ToHashSet();

        // 1. Remove processes that exited
        for (int i = Processes.Count - 1; i >= 0; i--)
        {
            var current = Processes[i];
            if (!incomingKeys.Contains((current.Pid, current.StartTime)))
            {
                Processes.RemoveAt(i);
            }
        }

        // 2. Update existing items in-place or insert new ones
        for (int i = 0; i < incoming.Count; i++)
        {
            var newItem = incoming[i];
            var existing = Processes.FirstOrDefault(p => p.Pid == newItem.Pid && p.StartTime == newItem.StartTime);
            if (existing != null)
            {
                existing.UpdateMetrics(newItem.WorkingSetBytes, newItem.CpuPercent, newItem.IsLowPriority);
            }
            else
            {
                Processes.Insert(Math.Min(i, Processes.Count), newItem);
            }
        }
    }

    [RelayCommand]
    public async Task TrimWorkingSetAsync(ClaudeProcessInfo item)
    {
        bool ok = await Task.Run(() => _processMonitorService.TrimWorkingSet(item.Pid, item.StartTime));
        SetActionFeedback(ok
            ? $"RAM recortada del proceso {item.Pid} ({item.ProcessName})."
            : $"No se pudo recortar RAM del proceso {item.Pid} (¿ya no existe?).");
        await RefreshAsync();
    }

    [RelayCommand]
    public async Task ToggleLowPriorityAsync(ClaudeProcessInfo item)
    {
        bool targetLow = !item.IsLowPriority;
        bool ok = await Task.Run(() => _processMonitorService.SetLowPriority(item.Pid, targetLow, item.StartTime));
        SetActionFeedback(ok
            ? $"Prioridad {(targetLow ? "baja" : "normal")} aplicada al proceso {item.Pid}."
            : $"No se pudo cambiar la prioridad del proceso {item.Pid} (¿ya no existe?).");
        await RefreshAsync();
    }

    private void SetActionFeedback(string message)
    {
        _feedbackCts?.Cancel();
        _feedbackCts = new CancellationTokenSource();
        var token = _feedbackCts.Token;

        ActionFeedbackMessage = message;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(4000, token);
                if (App.MainDispatcherQueue != null && !App.MainDispatcherQueue.HasThreadAccess)
                {
                    App.MainDispatcherQueue.TryEnqueue(() => ActionFeedbackMessage = string.Empty);
                }
                else
                {
                    ActionFeedbackMessage = string.Empty;
                }
            }
            catch (TaskCanceledException) { }
        }, token);
    }
}
