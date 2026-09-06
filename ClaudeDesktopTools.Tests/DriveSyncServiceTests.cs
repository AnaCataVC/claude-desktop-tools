using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Helpers;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services;
using Xunit;

namespace ClaudeDesktopTools.Tests;

/// <summary>Reports synchronously so assertions can rely on ordering without a UI SynchronizationContext.</summary>
internal class SyncProgressCollector : IProgress<DriveSyncProgress>
{
    public List<DriveSyncProgress> Reports { get; } = new();
    public void Report(DriveSyncProgress value) => Reports.Add(value);
}

public class DriveSyncServiceTests : IDisposable
{
    private readonly string _testDir;

    public DriveSyncServiceTests()
    {
        // DriveSyncService persists settings through LocalSettingsHelper's static path -- without
        // redirecting it to a throwaway file, UpdateSettings() here overwrites the real
        // %LOCALAPPDATA%\ClaudeDesktopTools\LocalSettings.json on every test run.
        _testDir = Path.Combine(Path.GetTempPath(), "ClaudeDesktopTools_DriveSyncTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        LocalSettingsHelper.SettingsFilePath = Path.Combine(_testDir, "test_settings.json");
    }

    public void Dispose()
    {
        LocalSettingsHelper.ResetToDefaultPath();
        if (Directory.Exists(_testDir))
        {
            try { Directory.Delete(_testDir, true); } catch { }
        }
    }
    [Fact]
    public void BuildDriveRelativePath_UsesRepoFolderNameWhenTracked()
    {
        var candidate = new ClaudeDiscoveryCandidate
        {
            RepositoryRoot = @"C:\Users\someone\Repos\claude-desktop-tools",
            RelativePath = @".claude\references\architecture.md"
        };

        string result = DriveSyncService.BuildDriveRelativePath(candidate, "claude-md-unversioned");

        Assert.Equal("claude-md-unversioned/claude-desktop-tools/.claude/references/architecture.md", result);
    }

    [Fact]
    public void BuildDriveRelativePath_UsesFallbackSegmentWhenNoRepo()
    {
        var candidate = new ClaudeDiscoveryCandidate
        {
            RepositoryRoot = string.Empty,
            RelativePath = "CLAUDE.md"
        };

        string result = DriveSyncService.BuildDriveRelativePath(candidate, "claude-md-unversioned");

        Assert.Equal("claude-md-unversioned/_sin-repo/CLAUDE.md", result);
    }

    [Fact]
    public void BuildDriveRelativePath_UsesClaudeConfigSegmentForSkillsWithNoRepo()
    {
        var candidate = new ClaudeDiscoveryCandidate
        {
            RepositoryRoot = string.Empty,
            RelativePath = "skills/my-skill/SKILL.md",
            Category = ClaudeDiscoveryCategory.Skill
        };

        string result = DriveSyncService.BuildDriveRelativePath(candidate, "claude-md-unversioned");

        Assert.Equal("claude-md-unversioned/_claude-config/skills/my-skill/SKILL.md", result);
    }

    [Fact]
    public void BuildDriveRelativePath_FallsBackToDefaultPrefixWhenBlank()
    {
        var candidate = new ClaudeDiscoveryCandidate { RepositoryRoot = string.Empty, RelativePath = "CLAUDE.md" };

        string result = DriveSyncService.BuildDriveRelativePath(candidate, "   ");

        Assert.StartsWith("claude-md-unversioned/", result);
    }

    [Fact]
    public void BuildDriveRelativePath_UsesCustomBucketNamesWhenProvided()
    {
        var noRepoCandidate = new ClaudeDiscoveryCandidate { RepositoryRoot = string.Empty, RelativePath = "CLAUDE.md" };
        var skillCandidate = new ClaudeDiscoveryCandidate { RepositoryRoot = string.Empty, RelativePath = "skills/foo/SKILL.md", Category = ClaudeDiscoveryCategory.Skill };

        string noRepoResult = DriveSyncService.BuildDriveRelativePath(noRepoCandidate, "prefix", noRepoBucketName: "sueltos", claudeConfigBucketName: "config-global");
        string skillResult = DriveSyncService.BuildDriveRelativePath(skillCandidate, "prefix", noRepoBucketName: "sueltos", claudeConfigBucketName: "config-global");

        Assert.Equal("prefix/sueltos/CLAUDE.md", noRepoResult);
        Assert.Equal("prefix/config-global/skills/foo/SKILL.md", skillResult);
    }

    [Fact]
    public void BuildDriveRelativePath_FallsBackToDefaultBucketNamesWhenBlank()
    {
        var noRepoCandidate = new ClaudeDiscoveryCandidate { RepositoryRoot = string.Empty, RelativePath = "CLAUDE.md" };
        var skillCandidate = new ClaudeDiscoveryCandidate { RepositoryRoot = string.Empty, RelativePath = "skills/foo/SKILL.md", Category = ClaudeDiscoveryCategory.Skill };

        string noRepoResult = DriveSyncService.BuildDriveRelativePath(noRepoCandidate, "prefix", noRepoBucketName: "  ", claudeConfigBucketName: "");
        string skillResult = DriveSyncService.BuildDriveRelativePath(skillCandidate, "prefix", noRepoBucketName: "  ", claudeConfigBucketName: "");

        Assert.Equal("prefix/_sin-repo/CLAUDE.md", noRepoResult);
        Assert.Equal("prefix/_claude-config/skills/foo/SKILL.md", skillResult);
    }

    [Fact]
    public void ParseResponse_ReadsSuccessStatus()
    {
        var (success, message) = DriveSyncService.ParseResponse("{\"status\":\"success\",\"fileId\":\"abc\",\"url\":\"https://drive\"}");

        Assert.True(success);
        Assert.Equal("OK", message);
    }

    [Fact]
    public void ParseResponse_ReadsErrorMessage()
    {
        var (success, message) = DriveSyncService.ParseResponse("{\"status\":\"error\",\"message\":\"Unauthorized\"}");

        Assert.False(success);
        Assert.Equal("Unauthorized", message);
    }

    [Fact]
    public void ParseResponse_HandlesMalformedJson()
    {
        var (success, message) = DriveSyncService.ParseResponse("not json");

        Assert.False(success);
        Assert.Contains("inválida", message);
    }

    [Theory]
    [InlineData(0, 10, 0)]
    [InlineData(5, 10, 50)]
    [InlineData(10, 10, 100)]
    [InlineData(0, 0, 100)]
    public void DriveSyncProgress_CalculatesPercentageCorrectly(int current, int total, int expectedPercentage)
    {
        var progress = DriveSyncProgress.FileStep(current, total, "file.md", "file.md", DriveSyncStepStatus.Uploading, 0, 0);

        Assert.Equal(expectedPercentage, progress.Percentage);
    }

    private static DriveSyncService CreateServiceWithInvalidEndpoint()
    {
        var service = new DriveSyncService();
        service.UpdateSettings(new DriveSyncSettings { WebAppUrl = "http://127.0.0.1:1/exec" });
        return service;
    }

    private static ClaudeDiscoveryCandidate CreateUntrackedFileCandidate(string relativePath)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        File.WriteAllText(path, "contenido de prueba");
        return new ClaudeDiscoveryCandidate { FilePath = path, RelativePath = relativePath, IsTrackedByGit = false };
    }

    [Fact]
    public async Task SyncCandidatesAsync_WithUntrackedFiles_EmitsSequentialProgressReports()
    {
        var service = CreateServiceWithInvalidEndpoint();
        var candidates = new[]
        {
            CreateUntrackedFileCandidate("a.md"),
            CreateUntrackedFileCandidate("b.md")
        };
        var progress = new SyncProgressCollector();

        try
        {
            await service.SyncCandidatesAsync(candidates, progress);

            Assert.True(progress.Reports.Count >= 1 + candidates.Length * 2 + 1);
            Assert.Equal(DriveSyncStepStatus.Starting, progress.Reports[0].Status);
            Assert.Equal(DriveSyncStepStatus.Completed, progress.Reports[^1].Status);
            for (int i = 1; i < progress.Reports.Count - 1; i++)
            {
                Assert.InRange(progress.Reports[i].Percentage, 0, 100);
            }
        }
        finally
        {
            foreach (var candidate in candidates) File.Delete(candidate.FilePath);
        }
    }

    [Fact]
    public async Task SyncCandidatesAsync_WithOnlyTrackedFiles_CompletesWithZeroProgressGracefully()
    {
        var service = CreateServiceWithInvalidEndpoint();
        var candidates = new[]
        {
            new ClaudeDiscoveryCandidate { FilePath = "unused.md", RelativePath = "unused.md", IsTrackedByGit = true }
        };
        var progress = new SyncProgressCollector();

        var result = await service.SyncCandidatesAsync(candidates, progress);

        Assert.Equal(0, result.Uploaded);
        Assert.Equal(0, result.Failed);
        Assert.Equal(DriveSyncStepStatus.Starting, progress.Reports[0].Status);
        Assert.Equal(DriveSyncStepStatus.Completed, progress.Reports[^1].Status);
        Assert.Equal(100, progress.Reports[^1].Percentage);
    }

    [Fact]
    public async Task SyncCandidatesAsync_WhenCancellationTriggered_ThrowsOperationCanceled()
    {
        var service = CreateServiceWithInvalidEndpoint();
        var candidate = CreateUntrackedFileCandidate("a.md");
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        try
        {
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => service.SyncCandidatesAsync(new[] { candidate }, cancellationToken: cts.Token));
        }
        finally
        {
            File.Delete(candidate.FilePath);
        }
    }

    [Fact]
    public void DriveSyncSettings_RoundTripSerialization_PreservesLastSyncAtAndCount()
    {
        var service = new DriveSyncService();
        var syncTime = new DateTime(2026, 9, 5, 22, 15, 0);

        service.UpdateSettings(new DriveSyncSettings
        {
            WebAppUrl = "https://script.google.com/test",
            LastSyncAt = syncTime,
            LastSyncCount = 143
        });

        var reloadedService = new DriveSyncService();
        Assert.Equal(syncTime, reloadedService.Settings.LastSyncAt);
        Assert.Equal(143, reloadedService.Settings.LastSyncCount);
    }

    [Fact]
    public async Task SyncCandidatesAsync_WhenZeroUploaded_DoesNotOverwriteExistingLastSync()
    {
        var service = CreateServiceWithInvalidEndpoint();
        var syncTime = new DateTime(2026, 9, 5, 20, 0, 0);
        service.UpdateSettings(new DriveSyncSettings
        {
            WebAppUrl = "http://127.0.0.1:1/exec",
            LastSyncAt = syncTime,
            LastSyncCount = 10
        });

        var trackedCandidate = new ClaudeDiscoveryCandidate
        {
            FilePath = "unused.md",
            RelativePath = "unused.md",
            IsTrackedByGit = true
        };

        await service.SyncCandidatesAsync(new[] { trackedCandidate });

        Assert.Equal(syncTime, service.Settings.LastSyncAt);
        Assert.Equal(10, service.Settings.LastSyncCount);
    }
}
