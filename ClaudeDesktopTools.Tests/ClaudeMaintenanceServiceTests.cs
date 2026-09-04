using System;
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
    private readonly ClaudeMaintenanceService _service;

    public ClaudeMaintenanceServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "ClaudeDesktopTools_MaintTests_" + Guid.NewGuid().ToString("N"));
        _transcriptsRoot = Path.Combine(_testDir, "projects");
        _sessionsRoot = Path.Combine(_testDir, "claude-code-sessions");
        Directory.CreateDirectory(_transcriptsRoot);
        Directory.CreateDirectory(_sessionsRoot);

        LocalSettingsHelper.SettingsFilePath = Path.Combine(_testDir, "test_settings.json");

        // By default, claude.exe is simulated as not running
        _service = new ClaudeMaintenanceService(_transcriptsRoot, _sessionsRoot, () => false);
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
