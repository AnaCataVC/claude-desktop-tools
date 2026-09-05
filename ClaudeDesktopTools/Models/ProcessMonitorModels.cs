using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace ClaudeDesktopTools.Models;

public sealed class ClaudeProcessInfo : INotifyPropertyChanged
{
    private long _workingSetBytes;
    private double _cpuPercent;
    private bool _isLowPriority;

    public event PropertyChangedEventHandler? PropertyChanged;

    public int Pid { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public DateTime StartTime { get; init; }

    public long WorkingSetBytes
    {
        get => _workingSetBytes;
        set
        {
            if (_workingSetBytes != value)
            {
                _workingSetBytes = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(WorkingSetDisplay));
            }
        }
    }

    public double CpuPercent
    {
        get => _cpuPercent;
        set
        {
            if (Math.Abs(_cpuPercent - value) > 0.001)
            {
                _cpuPercent = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CpuPercentDisplay));
            }
        }
    }

    public bool IsLowPriority
    {
        get => _isLowPriority;
        set
        {
            if (_isLowPriority != value)
            {
                _isLowPriority = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(PriorityToggleLabel));
            }
        }
    }

    public string WorkingSetDisplay => ClaudeStoreReport.FormatBytes(WorkingSetBytes);
    public string CpuPercentDisplay => $"{CpuPercent:0.0}%";
    public string PriorityToggleLabel => IsLowPriority ? "Restaurar CPU" : "Liberar CPU";

    public void UpdateMetrics(long workingSetBytes, double cpuPercent, bool isLowPriority)
    {
        WorkingSetBytes = workingSetBytes;
        CpuPercent = cpuPercent;
        IsLowPriority = isLowPriority;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class ClaudeProcessSnapshot
{
    public List<ClaudeProcessInfo> Processes { get; init; } = new();
    public int ProcessCount => Processes.Count;
    public long TotalWorkingSetBytes => Processes.Sum(p => p.WorkingSetBytes);
    public double TotalCpuPercent => Processes.Sum(p => p.CpuPercent);
}
