using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using ClaudeDesktopTools.Helpers;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services;
using Xunit;

namespace ClaudeDesktopTools.Tests;

public class ClaudeMaintenanceServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _transcriptsRoot;
    private readonly string _sessionsRoot;
    private readonly string _liveSessionsRoot;
    private readonly ClaudeMaintenanceService _service;

    public ClaudeMaintenanceServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ClaudeDesktopTools_MaintTests_" + Guid.NewGuid().ToString("N"));
        _transcriptsRoot = Path.Combine(_testDir, "projects");
        _sessionsRoot = Path.Combine(_testDir, "claude-code-sessions");
        _liveSessionsRoot = Path.Combine(_testDir, "sessions");
        Directory.CreateDirectory(_transcriptsRoot);
        Directory.CreateDirectory(_sessionsRoot);
        Directory.CreateDirectory(_liveSessionsRoot);

        LocalSettingsHelper.SettingsFilePath = Path.Combine(_testDir, "test_settings.json");

        // By default, claude.exe is simulated as not running
        _service = new ClaudeMaintenanceService(_transcriptsRoot, _sessionsRoot, () => false, _liveSessionsRoot);
    }

    /// <summary>Writes a ~/.claude/sessions/&lt;pid&gt;.json-shaped registry entry for a real process.</summary>
    private void WriteLiveSessionRegistry(string sessionId, int pid, DateTime processStartTime, string cwd = @"C:\test-repo", string name = "")
    {
        long fileTimeTicks = processStartTime.ToFileTime();
        string json = $"{{\"pid\":{pid},\"sessionId\":\"{sessionId}\",\"cwd\":\"{cwd.Replace("\\", "\\\\")}\",\"procStart\":\"{fileTimeTicks}\",\"name\":\"{name}\"}}";
        File.WriteAllText(Path.Combine(_liveSessionsRoot, $"{pid}.json"), json);
    }

    /// <summary>Spawns a real, harmless, long-running child process to verify PID/start-time checks against an actual OS process.</summary>
    private static Process StartDummyProcess()
    {
        var process = Process.Start(new ProcessStartInfo("ping.exe", "-n 60 127.0.0.1")
        {
            CreateNoWindow = true,
            UseShellExecute = false
        })!;
        process.Refresh();
        return process;
    }

    public void Dispose()
    {
        LocalSettingsHelper.ResetToDefaultPath();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }

    private string WriteTranscript(string name, int ageInDays, string content = "{}")
    {
        var projectDir = Path.Combine(_transcriptsRoot, "C--test-repo");
        Directory.CreateDirectory(projectDir);

        var path = Path.Combine(projectDir, name);
        File.WriteAllText(path, content);
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageInDays));
        return path;
    }

    private string WriteSession(string name, int ageInDays, bool archived)
    {
        var path = Path.Combine(_sessionsRoot, name);
        var flag = archived ? "true" : "false";
        File.WriteAllText(path, "{\"sessionId\":\"s1\",\"cwd\":\"C:\\\\test\",\"isArchived\":" + flag + ",\"transcript\":\"body\"}");
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-ageInDays));
        return path;
    }

    [Fact]
    public async Task ScanAsync_SeparatesStaleFilesFromTheTotal()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 30 });
        WriteTranscript("old.jsonl", ageInDays: 90);
        WriteTranscript("recent.jsonl", ageInDays: 1);

        var report = await _service.ScanAsync();

        Assert.Equal(2, report.Transcripts.TotalFiles);
        Assert.Equal(1, report.Transcripts.StaleFiles);
    }

    [Fact]
    public async Task ScanAsync_ReportsAMissingStoreInsteadOfThrowing()
    {
        var service = new ClaudeMaintenanceService(
            Path.Combine(_testDir, "missing-transcripts"),
            Path.Combine(_testDir, "missing-sessions"));

        var report = await service.ScanAsync();

        Assert.False(report.Transcripts.Exists);
        Assert.Equal(0, report.Transcripts.TotalFiles);
    }

    [Fact]
    public async Task DeleteStaleTranscriptsAsync_KeepsFilesInsideTheRetention()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 30 });
        var stale = WriteTranscript("old.jsonl", ageInDays: 90);
        var fresh = WriteTranscript("recent.jsonl", ageInDays: 2);

        var result = await _service.DeleteStaleTranscriptsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.False(File.Exists(stale));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public async Task DeleteStaleTranscriptsAsync_KeepsARecentlyWrittenFileEvenWithZeroRetention()
    {
        // 24-hour inviolable grace window protects live/recent sessions even if retention is 0
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 0 });
        var live = WriteTranscript("live.jsonl", ageInDays: 0);

        var result = await _service.DeleteStaleTranscriptsAsync();

        Assert.Equal(0, result.FilesProcessed);
        Assert.True(File.Exists(live));
    }

    [Fact]
    public async Task DeleteStaleTranscriptsAsync_ReportsFreedBytes()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 7 });
        var content = new string('z', 4096);
        WriteTranscript("old.jsonl", ageInDays: 30, content: content);

        var result = await _service.DeleteStaleTranscriptsAsync();

        Assert.Equal(4096, result.BytesFreed);
    }

    [Fact]
    public async Task GetStaleTranscriptsAsync_ReturnsOnlyEligibleFilesWithProjectNameAndId()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { TranscriptRetentionDays = 30 });
        WriteTranscript("old-session.jsonl", ageInDays: 90);
        WriteTranscript("recent.jsonl", ageInDays: 1);

        var preview = await _service.GetStaleTranscriptsAsync();

        var item = Assert.Single(preview);
        Assert.Equal("old-session", item.SessionId);
        Assert.Equal("C--test-repo", item.WorkingDirectory);
    }

    [Fact]
    public void DeleteTranscript_FreesSpaceForAGenuinelyOldFile()
    {
        var content = new string('z', 2048);
        var path = WriteTranscript("old.jsonl", ageInDays: 30, content: content);

        var result = _service.DeleteTranscript(path);

        Assert.False(result.Skipped);
        Assert.Equal(1, result.FilesProcessed);
        Assert.Equal(2048, result.BytesFreed);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void DeleteTranscript_RefusesAFileInsideThe24HourGrace()
    {
        var path = WriteTranscript("recent.jsonl", ageInDays: 0);

        var result = _service.DeleteTranscript(path);

        Assert.True(result.Skipped);
        Assert.Equal(0, result.FilesProcessed);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DeleteTranscript_RefusesWhenTheFileNoLongerExists()
    {
        var result = _service.DeleteTranscript(Path.Combine(_transcriptsRoot, "C--test-repo", "gone.jsonl"));

        Assert.True(result.Skipped);
        Assert.Equal(0, result.FilesProcessed);
    }

    [Fact]
    public void DeleteTranscripts_DeletesEveryOldFileAndAggregatesBytesFreed()
    {
        var first = WriteTranscript("old1.jsonl", ageInDays: 30, content: new string('a', 1024));
        var second = WriteTranscript("old2.jsonl", ageInDays: 60, content: new string('b', 512));

        var result = _service.DeleteTranscripts(new[] { first, second });

        Assert.False(result.Skipped);
        Assert.Equal(2, result.FilesProcessed);
        Assert.Equal(1536, result.BytesFreed);
        Assert.False(File.Exists(first));
        Assert.False(File.Exists(second));
    }

    [Fact]
    public void DeleteTranscripts_StillProtectsFilesInsideThe24HourGraceEvenWhenMixedWithOldOnes()
    {
        var old = WriteTranscript("old.jsonl", ageInDays: 30);
        var recent = WriteTranscript("recent.jsonl", ageInDays: 0);

        var result = _service.DeleteTranscripts(new[] { old, recent });

        Assert.False(result.Skipped);
        Assert.Equal(1, result.FilesProcessed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void DeleteTranscripts_ReportsSkippedWhenEveryFileIsProtectedOrMissing()
    {
        var recent = WriteTranscript("recent.jsonl", ageInDays: 0);
        var missing = Path.Combine(_transcriptsRoot, "C--test-repo", "gone.jsonl");

        var result = _service.DeleteTranscripts(new[] { recent, missing });

        Assert.True(result.Skipped);
        Assert.Equal(0, result.FilesProcessed);
        Assert.True(File.Exists(recent));
    }

    [Fact]
    public void DeleteTranscripts_HandlesAnEmptyList()
    {
        var result = _service.DeleteTranscripts(Array.Empty<string>());

        Assert.True(result.Skipped);
        Assert.Equal(0, result.FilesProcessed);
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_FlipsTheFlagOnlyOnStaleUnarchivedSessions()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        var stale = WriteSession("stale.json", ageInDays: 30, archived: false);
        var expectedLastWrite = File.GetLastWriteTime(stale);
        var fresh = WriteSession("fresh.json", ageInDays: 1, archived: false);

        var result = await _service.ArchiveStaleSessionsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.Contains("\"isArchived\":true", File.ReadAllText(stale));
        Assert.Contains("\"isArchived\":false", File.ReadAllText(fresh));
        Assert.Equal(expectedLastWrite, File.GetLastWriteTime(stale));
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_LeavesAlreadyArchivedSessionsUntouched()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        WriteSession("already.json", ageInDays: 30, archived: true);

        var result = await _service.ArchiveStaleSessionsAsync();

        Assert.Equal(0, result.FilesProcessed);
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_RefusesWhileClaudeIsRunning()
    {
        // Guard prevents in-memory state overwrite collision
        var service = new ClaudeMaintenanceService(_transcriptsRoot, _sessionsRoot, () => true);
        service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        var stale = WriteSession("stale.json", ageInDays: 30, archived: false);

        var result = await service.ArchiveStaleSessionsAsync();

        Assert.True(result.Skipped);
        Assert.Equal(0, result.FilesProcessed);
        Assert.Contains("\"isArchived\":false", File.ReadAllText(stale));
    }

    [Fact]
    public async Task ArchiveStaleSessionsAsync_FlipsTheFlag_WhenJsonHasWhitespace()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        var path = Path.Combine(_sessionsRoot, "whitespace.json");
        File.WriteAllText(path, "{\"sessionId\": \"s99\", \"cwd\": \"C:\\\\code\", \"isArchived\": false, \"transcript\": \"details\"}");
        File.SetLastWriteTime(path, DateTime.Now.AddDays(-30));

        var result = await _service.ArchiveStaleSessionsAsync();

        Assert.Equal(1, result.FilesProcessed);
        Assert.Contains("\"isArchived\":true", File.ReadAllText(path));
    }

    [Fact]
    public async Task GetCliSessionsAsync_ListsTopLevelTranscriptsAndExcludesSubagents()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        var session = WriteTranscript("session-a.jsonl", ageInDays: 1);

        // A subagent transcript nested under a folder named after the session -- must not
        // be listed as its own session, the bug this test guards against would double-count it.
        var subagentsDir = Path.Combine(Path.GetDirectoryName(session)!, "session-a", "subagents");
        Directory.CreateDirectory(subagentsDir);
        File.WriteAllText(Path.Combine(subagentsDir, "agent-1.jsonl"), "{}");

        var result = await _service.GetCliSessionsAsync();

        var item = Assert.Single(result);
        Assert.Equal("session-a", item.SessionId);
        Assert.Equal("C--test-repo", item.WorkingDirectory);
        Assert.False(item.IsStale);
        Assert.False(item.IsArchived);
    }

    [Fact]
    public async Task GetCliSessionsAsync_MarksOldTranscriptsAsStale()
    {
        _service.UpdateSettings(new ClaudeMaintenanceSettings { SessionRetentionDays = 7 });
        WriteTranscript("old-session.jsonl", ageInDays: 30);

        var result = await _service.GetCliSessionsAsync();

        var item = Assert.Single(result);
        Assert.True(item.IsStale);
        Assert.Equal("Archivable", item.StatusBadge);
    }

    [Fact]
    public async Task GetCliSessionsAsync_ReturnsEmptyWhenTranscriptsRootMissing()
    {
        var service = new ClaudeMaintenanceService(Path.Combine(_testDir, "missing-transcripts"), _sessionsRoot, () => false);

        var result = await service.GetCliSessionsAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetCliSessionsAsync_MarksSessionActiveOnlyWhenRegistryPidIsAGenuinelyRunningProcess()
    {
        var dummy = StartDummyProcess();
        try
        {
            WriteTranscript("real-active.jsonl", ageInDays: 1);
            WriteLiveSessionRegistry("real-active", dummy.Id, dummy.StartTime, cwd: @"C:\real\cwd", name: "claude-desktop-tools-07");

            var result = await _service.GetCliSessionsAsync();

            var item = Assert.Single(result);
            Assert.True(item.IsActive);
            Assert.Equal(dummy.Id, item.ProcessId);
            Assert.Equal(@"C:\real\cwd", item.WorkingDirectory);
            Assert.Equal("Activa", item.StatusBadge);
            Assert.Equal("claude-desktop-tools-07", item.SessionName);
            Assert.True(item.HasSessionName);
        }
        finally
        {
            dummy.Kill();
            dummy.Dispose();
        }
    }

    [Fact]
    public async Task GetCliSessionsAsync_DoesNotTrustARegistryEntryForAProcessThatIsNotRunning()
    {
        WriteTranscript("stale-registry.jsonl", ageInDays: 1);
        // A PID that is essentially guaranteed not to correspond to any running process right now.
        WriteLiveSessionRegistry("stale-registry", 999999, DateTime.Now);

        var result = await _service.GetCliSessionsAsync();

        var item = Assert.Single(result);
        Assert.False(item.IsActive);
        Assert.Null(item.ProcessId);
        Assert.Equal("Inactiva", item.StatusBadge);
        Assert.False(item.HasSessionName);
    }

    [Fact]
    public async Task GetCliSessionsAsync_DoesNotTrustARecycledPidWithAMismatchedStartTime()
    {
        var dummy = StartDummyProcess();
        try
        {
            WriteTranscript("recycled-pid.jsonl", ageInDays: 1);
            // Same real, running PID -- but the recorded start time does not match, simulating a
            // stale registry entry whose original process already exited and Windows reused the PID.
            WriteLiveSessionRegistry("recycled-pid", dummy.Id, dummy.StartTime.AddHours(-1));

            var result = await _service.GetCliSessionsAsync();

            var item = Assert.Single(result);
            Assert.False(item.IsActive);
        }
        finally
        {
            dummy.Kill();
            dummy.Dispose();
        }
    }

    [Fact]
    public void CloseSession_KillsTheGenuinelyActiveProcess()
    {
        var dummy = StartDummyProcess();
        try
        {
            WriteLiveSessionRegistry("to-close", dummy.Id, dummy.StartTime);

            var result = _service.CloseSession(dummy.Id, "to-close");

            Assert.False(result.Skipped);
            Assert.Empty(result.Failures);
            dummy.WaitForExit(5000);
            Assert.True(dummy.HasExited);
        }
        finally
        {
            if (!dummy.HasExited) dummy.Kill();
            dummy.Dispose();
        }
    }

    [Fact]
    public void CloseSession_RefusesWhenTheSessionIsNoLongerActive()
    {
        // Nothing registered for this sessionId/pid pair -- must refuse, never call Process.Kill blindly.
        var result = _service.CloseSession(999999, "not-a-real-session");

        Assert.True(result.Skipped);
        Assert.Equal(0, result.FilesProcessed);
    }

    [Fact]
    public async Task GetCliSessionsAsync_ExcludesSubagentTranscriptsFromLiveRegistryMatching()
    {
        // A subagent transcript sharing an id that happens to collide with a registered pid's
        // sessionId must never be reported (GetCliSessionsAsync_ListsTopLevel... already covers
        // the exclusion itself; this guards the live-session cross-reference specifically).
        var dummy = StartDummyProcess();
        try
        {
            var session = WriteTranscript("parent-session.jsonl", ageInDays: 1);
            var subagentsDir = Path.Combine(Path.GetDirectoryName(session)!, "parent-session", "subagents");
            Directory.CreateDirectory(subagentsDir);
            File.WriteAllText(Path.Combine(subagentsDir, "parent-session.jsonl"), "{}");
            WriteLiveSessionRegistry("parent-session", dummy.Id, dummy.StartTime);

            var result = await _service.GetCliSessionsAsync();

            var item = Assert.Single(result);
            Assert.Equal("parent-session", item.SessionId);
            Assert.True(item.IsActive);
        }
        finally
        {
            dummy.Kill();
            dummy.Dispose();
        }
    }

    [Fact]
    public void ClaudeStoreReport_Summary_AdaptsForSessionsVsTranscripts()
    {
        var transcriptsStore = new ClaudeStoreReport
        {
            DisplayName = "Transcripts",
            Exists = true,
            TotalFiles = 10,
            TotalBytes = 1024 * 1024,
            StaleFiles = 3,
            StaleBytes = 512 * 1024,
            ReclaimsDiskSpace = true
        };

        var sessionsStore = new ClaudeStoreReport
        {
            DisplayName = "Sesiones",
            Exists = true,
            TotalFiles = 10,
            TotalBytes = 1024 * 1024,
            StaleFiles = 3,
            StaleBytes = 512 * 1024,
            ReclaimsDiskSpace = false
        };

        Assert.Contains("recuperables", transcriptsStore.Summary);
        Assert.DoesNotContain("recuperables", sessionsStore.Summary);
        Assert.Contains("fuera de retención", sessionsStore.Summary);
    }
}
