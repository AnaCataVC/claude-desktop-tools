using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.Services;

public sealed class ProcessMonitorService : IProcessMonitorService
{
    private readonly string _processName;

    // Rebuilt on every scan, keyed by (Pid, StartTime) so a recycled PID between scans
    // never calculates an invalid delta against an unrelated predecessor.
    private Dictionary<(int Pid, DateTime StartTime), (TimeSpan CpuTime, DateTime SampledAt)> _previousSamples = new();

    private static readonly TimeSpan ProcessStartTimeTolerance = TimeSpan.FromSeconds(2);

    // processName is injectable so tests can point the scan at a real, spawnable process (e.g. "ping")
    // instead of requiring an actual claude.exe to be running.
    public ProcessMonitorService(string processName = "claude")
    {
        _processName = processName;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    public ClaudeProcessSnapshot GetClaudeProcesses()
    {
        Process[] processes;
        try
        {
            processes = Process.GetProcessesByName(_processName);
        }
        catch (Exception)
        {
            return new ClaudeProcessSnapshot();
        }

        var nextSamples = new Dictionary<(int, DateTime), (TimeSpan, DateTime)>();
        var result = new List<ClaudeProcessInfo>();
        var now = DateTime.UtcNow;

        try
        {
            foreach (var process in processes)
            {
                try
                {
                    process.Refresh();
                    var pid = process.Id;
                    var startTime = process.StartTime;
                    var cpuTime = process.TotalProcessorTime;
                    double cpuPercent = 0;

                    var key = (pid, startTime);
                    if (_previousSamples.TryGetValue(key, out var previous))
                    {
                        cpuPercent = ComputeCpuPercent(cpuTime - previous.CpuTime, now - previous.SampledAt, Environment.ProcessorCount);
                    }

                    nextSamples[key] = (cpuTime, now);

                    result.Add(new ClaudeProcessInfo
                    {
                        Pid = pid,
                        ProcessName = process.ProcessName,
                        WorkingSetBytes = process.WorkingSet64,
                        CpuPercent = Math.Max(0, Math.Round(cpuPercent, 1)),
                        StartTime = startTime,
                        IsLowPriority = process.PriorityClass is ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle
                    });
                }
                catch (Exception)
                {
                    // Exited mid-scan or access denied -- skip it, doesn't invalidate the rest of the snapshot.
                }
            }
        }
        finally
        {
            foreach (var p in processes) p.Dispose();
        }

        _previousSamples = nextSamples;
        return new ClaudeProcessSnapshot { Processes = result.OrderByDescending(p => p.WorkingSetBytes).ToList() };
    }

    public bool TrimWorkingSet(int pid, DateTime? expectedStartTime = null)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!IsTargetProcess(process, expectedStartTime)) return false;
            return EmptyWorkingSet(process.Handle);
        }
        catch (Exception)
        {
            return false;
        }
    }

    public bool SetLowPriority(int pid, bool lowPriority, DateTime? expectedStartTime = null)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            if (!IsTargetProcess(process, expectedStartTime)) return false;
            process.PriorityClass = lowPriority ? ProcessPriorityClass.BelowNormal : ProcessPriorityClass.Normal;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    // Re-checked at action time (not just at scan time) so a pid recycled by Windows onto an unrelated process never gets touched.
    private bool IsTargetProcess(Process process, DateTime? expectedStartTime)
    {
        if (!process.ProcessName.Equals(_processName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (expectedStartTime.HasValue)
        {
            return (process.StartTime - expectedStartTime.Value).Duration() < ProcessStartTimeTolerance;
        }

        return true;
    }

    public static double ComputeCpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
    {
        if (wallDelta.TotalMilliseconds <= 0) return 0;
        return Math.Max(0, 100.0 * cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * processorCount));
    }
}
