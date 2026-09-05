using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
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

    // "claude" name-matches both the actual CLI and the packaged desktop app (plus every one of its
    // Electron helper subprocesses -- gpu-process, renderer, utility, crashpad-handler...). All of those
    // share the app's install path, so excluding that path is what keeps this view to real CLI sessions.
    private static readonly string PackagedAppsRoot =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "WindowsApps") + Path.DirectorySeparatorChar;

    // processName is injectable so tests can point the scan at a real, spawnable process (e.g. "ping")
    // instead of requiring an actual claude.exe to be running.
    public ProcessMonitorService(string processName = "claude")
    {
        _processName = processName;
    }

    [DllImport("psapi.dll", SetLastError = true)]
    private static extern bool EmptyWorkingSet(IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint desiredAccess, bool inheritHandle, int processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReadProcessMemory(IntPtr hProcess, IntPtr baseAddress, byte[] buffer, int size, out int bytesRead);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWow64Process(IntPtr hProcess, [MarshalAs(UnmanagedType.Bool)] out bool wow64Process);

    [DllImport("ntdll.dll")]
    private static extern int NtQueryInformationProcess(IntPtr processHandle, int processInformationClass, ref ProcessBasicInformation processInformation, int processInformationLength, out int returnLength);

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessBasicInformation
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2_0;
        public IntPtr Reserved2_1;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmRead = 0x0010;

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

                    if (IsPackagedDesktopAppProcess(process))
                    {
                        // The desktop app (or one of its Electron helper subprocesses) also name-matches "claude" -- not a CLI session.
                        continue;
                    }

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

                    TryReadCurrentDirectory(pid, out var workingDirectory);

                    result.Add(new ClaudeProcessInfo
                    {
                        Pid = pid,
                        ProcessName = process.ProcessName,
                        WorkingSetBytes = process.WorkingSet64,
                        CpuPercent = Math.Max(0, Math.Round(cpuPercent, 1)),
                        StartTime = startTime,
                        IsLowPriority = process.PriorityClass is ProcessPriorityClass.BelowNormal or ProcessPriorityClass.Idle,
                        WorkingDirectory = workingDirectory
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

    private bool IsPackagedDesktopAppProcess(Process process)
    {
        try
        {
            return IsUnderPackagedAppsRoot(process.MainModule?.FileName, PackagedAppsRoot);
        }
        catch
        {
            // Access denied / cross-bitness module read -- can't confirm it's the packaged app, so don't hide it.
            return false;
        }
    }

    internal static bool IsUnderPackagedAppsRoot(string? modulePath, string packagedAppsRoot) =>
        modulePath != null && modulePath.StartsWith(packagedAppsRoot, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Reads another process' live current working directory straight out of its PEB (Windows never exposed
    /// a documented API for this -- it's the same technique tools like Sysinternals rely on).
    /// x64-only fast path: bails out on a 32-bit build/OS or a bitness-mismatched target rather than risk
    /// decoding garbage from offsets that only hold for a native x64 PEB.
    /// </summary>
    public static bool TryReadCurrentDirectory(int pid, out string? currentDirectory)
    {
        currentDirectory = null;

        if (!Environment.Is64BitProcess || !Environment.Is64BitOperatingSystem)
        {
            return false;
        }

        IntPtr hProcess = OpenProcess(ProcessQueryInformation | ProcessVmRead, false, pid);
        if (hProcess == IntPtr.Zero) return false;

        try
        {
            if (!IsWow64Process(hProcess, out bool isWow64) || isWow64)
            {
                // Target isn't a native x64 process -- the offsets below only hold for a native x64 PEB.
                return false;
            }

            var pbi = new ProcessBasicInformation();
            int status = NtQueryInformationProcess(hProcess, 0, ref pbi, Marshal.SizeOf<ProcessBasicInformation>(), out _);
            if (status != 0 || pbi.PebBaseAddress == IntPtr.Zero) return false;

            // x64 PEB layout: ProcessParameters pointer at offset 0x20.
            if (!TryReadPointer(hProcess, pbi.PebBaseAddress + 0x20, out IntPtr processParameters) || processParameters == IntPtr.Zero)
                return false;

            // x64 RTL_USER_PROCESS_PARAMETERS layout: CurrentDirectory.DosPath (a UNICODE_STRING) at offset 0x38.
            var unicodeString = new byte[16];
            if (!ReadProcessMemory(hProcess, processParameters + 0x38, unicodeString, unicodeString.Length, out _))
                return false;

            ushort length = BitConverter.ToUInt16(unicodeString, 0);
            var buffer = new IntPtr(BitConverter.ToInt64(unicodeString, 8));
            if (length == 0 || buffer == IntPtr.Zero) return false;

            var chars = new byte[length];
            if (!ReadProcessMemory(hProcess, buffer, chars, chars.Length, out _))
                return false;

            currentDirectory = Encoding.Unicode.GetString(chars);
            return true;
        }
        catch
        {
            return false;
        }
        finally
        {
            CloseHandle(hProcess);
        }
    }

    private static bool TryReadPointer(IntPtr hProcess, IntPtr address, out IntPtr value)
    {
        value = IntPtr.Zero;
        var buffer = new byte[8];
        if (!ReadProcessMemory(hProcess, address, buffer, buffer.Length, out _)) return false;
        value = new IntPtr(BitConverter.ToInt64(buffer, 0));
        return true;
    }
}
