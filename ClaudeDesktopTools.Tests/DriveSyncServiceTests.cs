using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services;
using Xunit;

namespace ClaudeDesktopTools.Tests;

public class DriveSyncServiceTests
{
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
}
