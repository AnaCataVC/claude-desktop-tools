using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services;
using Xunit;

namespace ClaudeDesktopTools.Tests;

public class ProcessMonitorServiceTests : IDisposable
{
    // Points the scan at "ping" instead of "claude" so tests can exercise the real Process APIs
    // against a genuinely spawnable process, without needing an actual claude.exe running.
    private readonly ProcessMonitorService _service = new("ping");
    private Process? _dummy;

    private Process StartDummyPing()
    {
        _dummy = Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        _dummy.Refresh();
        return _dummy;
    }

    public void Dispose()
    {
        if (_dummy is { HasExited: false })
        {
            try { _dummy.Kill(); } catch { }
        }
        _dummy?.Dispose();
    }

    [Fact]
    public void GetClaudeProcesses_ListsARunningMatchByNameWithPidAndRam()
    {
        var dummy = StartDummyPing();

        var snapshot = _service.GetClaudeProcesses();

        var item = Assert.Single(snapshot.Processes, p => p.Pid == dummy.Id);
        Assert.Equal(dummy.Id, item.Pid);
        Assert.True(item.WorkingSetBytes > 0);
    }

    [Fact]
    public void GetClaudeProcesses_ReportsZeroCpuOnTheFirstScan()
    {
        var dummy = StartDummyPing();

        var snapshot = _service.GetClaudeProcesses();

        var item = Assert.Single(snapshot.Processes, p => p.Pid == dummy.Id);
        Assert.Equal(0, item.CpuPercent);
    }

    [Fact]
    public void GetClaudeProcesses_ReturnsEmptyWhenNothingMatchesTheName()
    {
        var service = new ProcessMonitorService("a-name-nothing-runs-as");

        var snapshot = service.GetClaudeProcesses();

        Assert.Empty(snapshot.Processes);
        Assert.Equal(0, snapshot.ProcessCount);
        Assert.Equal(0, snapshot.TotalWorkingSetBytes);
    }

    [Fact]
    public void TrimWorkingSet_SucceedsForAGenuinelyMatchingProcess()
    {
        var dummy = StartDummyPing();

        Assert.True(_service.TrimWorkingSet(dummy.Id));
    }

    [Fact]
    public void TrimWorkingSet_RefusesAProcessWhoseNameDoesNotMatch()
    {
        // Default-named service only ever acts on "claude" -- a real "ping" process must be refused.
        var defaultService = new ProcessMonitorService();
        var dummy = StartDummyPing();

        Assert.False(defaultService.TrimWorkingSet(dummy.Id));
    }

    [Fact]
    public void TrimWorkingSet_RefusesWhenThePidDoesNotExist()
    {
        Assert.False(_service.TrimWorkingSet(999999));
    }

    [Fact]
    public void TrimWorkingSet_RefusesProcessWithMismatchedStartTime()
    {
        var dummy = StartDummyPing();

        Assert.False(_service.TrimWorkingSet(dummy.Id, expectedStartTime: dummy.StartTime.AddHours(-1)));
    }

    [Fact]
    public void SetLowPriority_TogglesTheRealProcessPriorityAndBackAgain()
    {
        var dummy = StartDummyPing();

        Assert.True(_service.SetLowPriority(dummy.Id, lowPriority: true));
        dummy.Refresh();
        Assert.Equal(ProcessPriorityClass.BelowNormal, dummy.PriorityClass);

        Assert.True(_service.SetLowPriority(dummy.Id, lowPriority: false));
        dummy.Refresh();
        Assert.Equal(ProcessPriorityClass.Normal, dummy.PriorityClass);
    }

    [Fact]
    public void SetLowPriority_RefusesProcessWithMismatchedStartTime()
    {
        var dummy = StartDummyPing();

        Assert.False(_service.SetLowPriority(dummy.Id, lowPriority: true, expectedStartTime: dummy.StartTime.AddHours(-1)));
    }

    [Fact]
    public void SetLowPriority_RefusesAProcessWhoseNameDoesNotMatch()
    {
        var defaultService = new ProcessMonitorService();
        var dummy = StartDummyPing();

        Assert.False(defaultService.SetLowPriority(dummy.Id, lowPriority: true));
    }

    [Fact]
    public void SetLowPriority_RefusesWhenThePidDoesNotExist()
    {
        Assert.False(_service.SetLowPriority(999999, lowPriority: true));
    }

    [Theory]
    [InlineData(0, 1000, 4, 0)]      // no CPU time consumed
    [InlineData(1000, 1000, 1, 100)] // one full core busy for the whole wall-clock window
    [InlineData(1000, 1000, 4, 25)]  // same CPU time, spread across 4 logical processors
    [InlineData(2000, 1000, 1, 200)] // more CPU time than wall time (multi-threaded work), stays uncapped
    public void ComputeCpuPercent_MatchesTheExpectedRatio(int cpuMs, int wallMs, int processorCount, double expected)
    {
        var result = ProcessMonitorService.ComputeCpuPercent(
            TimeSpan.FromMilliseconds(cpuMs), TimeSpan.FromMilliseconds(wallMs), processorCount);

        Assert.Equal(expected, result, precision: 3);
    }

    [Fact]
    public void ComputeCpuPercent_ReturnsZeroForANonPositiveWallDelta()
    {
        Assert.Equal(0, ProcessMonitorService.ComputeCpuPercent(TimeSpan.FromMilliseconds(500), TimeSpan.Zero, 4));
    }

    [Fact]
    public void ComputeCpuPercent_NeverGoesNegativeForAShrinkingCpuTime()
    {
        // Guards against a negative delta (e.g. a pid reused between samples) producing a negative percentage.
        var result = ProcessMonitorService.ComputeCpuPercent(TimeSpan.FromMilliseconds(-500), TimeSpan.FromMilliseconds(1000), 1);

        Assert.Equal(0, result);
    }

    [Theory]
    [InlineData(@"C:\Program Files\WindowsApps\Claude_1.0.0_x64__abc\app\Claude.exe", @"C:\Program Files\WindowsApps\", true)]
    [InlineData(@"c:\program files\windowsapps\Claude_1.0.0_x64__abc\app\Claude.exe", @"C:\Program Files\WindowsApps\", true)] // case-insensitive
    [InlineData(@"C:\Users\Username\.local\bin\claude.exe", @"C:\Program Files\WindowsApps\", false)]
    [InlineData(@"C:\Users\Username\AppData\Roaming\Claude\claude-code\2.1.260\claude.exe", @"C:\Program Files\WindowsApps\", false)]
    [InlineData(null, @"C:\Program Files\WindowsApps\", false)]
    public void IsUnderPackagedAppsRoot_OnlyMatchesThePackagedDesktopAppInstallDirectory(string? modulePath, string root, bool expected)
    {
        Assert.Equal(expected, ProcessMonitorService.IsUnderPackagedAppsRoot(modulePath, root));
    }

    [Fact]
    public void TryReadCurrentDirectory_ReadsTheRealWorkingDirectoryOfARunningProcess()
    {
        var expectedDirectory = Path.GetTempPath().TrimEnd('\\');
        var dummy = Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false,
            WorkingDirectory = expectedDirectory
        })!;

        try
        {
            bool ok = ProcessMonitorService.TryReadCurrentDirectory(dummy.Id, out var currentDirectory);

            if (!Environment.Is64BitProcess || !Environment.Is64BitOperatingSystem)
            {
                // The PEB-reading fast path only supports native x64 -- everywhere else it must refuse, not guess.
                Assert.False(ok);
                return;
            }

            Assert.True(ok);
            Assert.Equal(expectedDirectory, currentDirectory?.TrimEnd('\\'), ignoreCase: true);
        }
        finally
        {
            try { dummy.Kill(); } catch { }
            dummy.Dispose();
        }
    }

    [Fact]
    public void TryReadCurrentDirectory_RefusesAPidThatDoesNotExist()
    {
        Assert.False(ProcessMonitorService.TryReadCurrentDirectory(999999, out var currentDirectory));
        Assert.Null(currentDirectory);
    }
}

public class ClaudeProcessInfoTests
{
    [Fact]
    public void WorkingSetDisplay_DelegatesToTheSharedByteFormatter()
    {
        const long bytes = 256L * 1024 * 1024;
        var info = new ClaudeProcessInfo { WorkingSetBytes = bytes };

        Assert.Equal(ClaudeStoreReport.FormatBytes(bytes), info.WorkingSetDisplay);
    }

    [Fact]
    public void CpuPercentDisplay_FormatsWithOneDecimal()
    {
        var info = new ClaudeProcessInfo { CpuPercent = 12.345 };

        Assert.Equal($"{12.345:0.0}%", info.CpuPercentDisplay);
    }

    [Theory]
    [InlineData(false, "Liberar CPU")]
    [InlineData(true, "Restaurar CPU")]
    public void PriorityToggleLabel_ReflectsCurrentPriority(bool isLowPriority, string expected)
    {
        var info = new ClaudeProcessInfo { IsLowPriority = isLowPriority };

        Assert.Equal(expected, info.PriorityToggleLabel);
    }

    [Fact]
    public void SessionLabel_UsesTheWorkingDirectoryFolderNameWhenAvailable()
    {
        var info = new ClaudeProcessInfo { Pid = 123, WorkingDirectory = @"C:\Users\Username\Repos\claude-desktop-tools" };

        Assert.Equal("claude-desktop-tools", info.SessionLabel);
    }

    [Fact]
    public void SessionLabel_FallsBackToThePidWhenNoWorkingDirectoryWasResolved()
    {
        var info = new ClaudeProcessInfo { Pid = 456, WorkingDirectory = null };

        Assert.Equal("PID 456", info.SessionLabel);
    }

    [Fact]
    public void UpdateMetrics_UpdatesValuesAndRaisesPropertyChangedEvents()
    {
        var info = new ClaudeProcessInfo { WorkingSetBytes = 100, CpuPercent = 5.0, IsLowPriority = false };
        var changed = new System.Collections.Generic.List<string>();
        info.PropertyChanged += (s, e) => { if (e.PropertyName != null) changed.Add(e.PropertyName); };

        info.UpdateMetrics(200, 10.0, true);

        Assert.Equal(200, info.WorkingSetBytes);
        Assert.Equal(10.0, info.CpuPercent);
        Assert.True(info.IsLowPriority);

        Assert.Contains(nameof(ClaudeProcessInfo.WorkingSetBytes), changed);
        Assert.Contains(nameof(ClaudeProcessInfo.WorkingSetDisplay), changed);
        Assert.Contains(nameof(ClaudeProcessInfo.CpuPercent), changed);
        Assert.Contains(nameof(ClaudeProcessInfo.CpuPercentDisplay), changed);
        Assert.Contains(nameof(ClaudeProcessInfo.IsLowPriority), changed);
        Assert.Contains(nameof(ClaudeProcessInfo.PriorityToggleLabel), changed);
    }
}

public class ClaudeProcessSnapshotTests
{
    [Fact]
    public void Totals_AggregateAcrossEveryProcess()
    {
        var snapshot = new ClaudeProcessSnapshot
        {
            Processes = new()
            {
                new ClaudeProcessInfo { Pid = 1, WorkingSetBytes = 100 * 1024 * 1024, CpuPercent = 5.5 },
                new ClaudeProcessInfo { Pid = 2, WorkingSetBytes = 200 * 1024 * 1024, CpuPercent = 10.0 },
            }
        };

        Assert.Equal(2, snapshot.ProcessCount);
        Assert.Equal(300L * 1024 * 1024, snapshot.TotalWorkingSetBytes);
        Assert.Equal(15.5, snapshot.TotalCpuPercent, precision: 3);
    }

    [Fact]
    public void Totals_AreZeroForAnEmptySnapshot()
    {
        var snapshot = new ClaudeProcessSnapshot();

        Assert.Equal(0, snapshot.ProcessCount);
        Assert.Equal(0, snapshot.TotalWorkingSetBytes);
        Assert.Equal(0, snapshot.TotalCpuPercent);
    }
}
