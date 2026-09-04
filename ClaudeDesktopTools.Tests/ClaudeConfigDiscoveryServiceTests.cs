using System;
using System.IO;
using System.Linq;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services;
using Xunit;

namespace ClaudeDesktopTools.Tests;

public class ClaudeConfigDiscoveryServiceTests : IDisposable
{
    private readonly string _tempDir;

    public ClaudeConfigDiscoveryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ClaudeDiscoveryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData(".git", true)]
    [InlineData("node_modules", true)]
    [InlineData("memory", true)]
    [InlineData("plans", true)]
    [InlineData("security", true)]
    [InlineData("cache", true)]
    [InlineData("plugins", true)]
    [InlineData("_backup_2026", true)]
    [InlineData("backup_old", true)]
    [InlineData("src", false)]
    [InlineData("references", false)]
    public void IsDirectorySkipped_IdentifiesExcludedDirectories(string dirName, bool expected)
    {
        bool result = ClaudeConfigDiscoveryService.IsDirectorySkipped(dirName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void HasInfrastructureSecret_DetectsPrivateKey()
    {
        string filePath = Path.Combine(_tempDir, "key.md");
        File.WriteAllText(filePath, "# Config\n-----BEGIN RSA PRIVATE KEY-----\nMIIEowIBAAKCAQEA...\n-----END RSA PRIVATE KEY-----");

        Assert.True(ClaudeConfigDiscoveryService.HasInfrastructureSecret(filePath));
        Assert.False(ClaudeConfigDiscoveryService.IsCandidateAllowed(filePath));
    }

    [Fact]
    public void HasInfrastructureSecret_DetectsGitHubPAT()
    {
        string filePath = Path.Combine(_tempDir, "pat.md");
        File.WriteAllText(filePath, "token: ghp_123456789012345678901234567890123456");

        Assert.True(ClaudeConfigDiscoveryService.HasInfrastructureSecret(filePath));
        Assert.False(ClaudeConfigDiscoveryService.IsCandidateAllowed(filePath));
    }

    [Fact]
    public void IsCandidateAllowed_AllowsCleanMarkdownFile()
    {
        string filePath = Path.Combine(_tempDir, "CLAUDE.md");
        File.WriteAllText(filePath, "# System Prompt\nFollow coding rules.");

        Assert.False(ClaudeConfigDiscoveryService.HasInfrastructureSecret(filePath));
        Assert.True(ClaudeConfigDiscoveryService.IsCandidateAllowed(filePath));
    }

    [Fact]
    public async System.Threading.Tasks.Task DiscoverAsync_IgnoresReferencesFolderWithoutClaudeMarker()
    {
        // A top-level references/ folder with no CLAUDE.md nearby belongs to another
        // tool (e.g. Gemini), not Claude, and must not be reported as a candidate.
        string otherToolDir = Path.Combine(_tempDir, "gemini-project");
        Directory.CreateDirectory(Path.Combine(otherToolDir, "references"));
        File.WriteAllText(Path.Combine(otherToolDir, "references", "gemini-notes.md"), "# Gemini notes");

        string claudeDir = Path.Combine(_tempDir, "claude-project");
        Directory.CreateDirectory(Path.Combine(claudeDir, "references"));
        File.WriteAllText(Path.Combine(claudeDir, "CLAUDE.md"), "# Claude context");
        File.WriteAllText(Path.Combine(claudeDir, "references", "claude-notes.md"), "# Claude notes");

        var service = new ClaudeConfigDiscoveryService();
        var report = await service.DiscoverAsync(_tempDir, maxDepth: 3);

        Assert.Contains(report.Candidates, c => c.FilePath.EndsWith("claude-notes.md"));
        Assert.DoesNotContain(report.Candidates, c => c.FilePath.EndsWith("gemini-notes.md"));
    }

    [Fact]
    public async System.Threading.Tasks.Task DiscoverAsync_FindsSkillsAgentsScheduledTasksAndHooks()
    {
        string dotClaudeDir = Path.Combine(_tempDir, ".claude");

        Directory.CreateDirectory(Path.Combine(dotClaudeDir, "skills", "my-skill"));
        File.WriteAllText(Path.Combine(dotClaudeDir, "skills", "my-skill", "SKILL.md"), "# My Skill");

        Directory.CreateDirectory(Path.Combine(dotClaudeDir, "agents"));
        File.WriteAllText(Path.Combine(dotClaudeDir, "agents", "my-agent.md"), "# My Agent");

        Directory.CreateDirectory(Path.Combine(dotClaudeDir, "scheduled-tasks", "my-task"));
        File.WriteAllText(Path.Combine(dotClaudeDir, "scheduled-tasks", "my-task", "SKILL.md"), "# My Task");

        Directory.CreateDirectory(Path.Combine(dotClaudeDir, "hooks"));
        File.WriteAllText(Path.Combine(dotClaudeDir, "hooks", "my-hook.ps1"), "Write-Host 'hi'");
        File.WriteAllText(Path.Combine(dotClaudeDir, "hooks", "state.json"), "{}"); // non-script, must be ignored

        var service = new ClaudeConfigDiscoveryService();
        var report = await service.DiscoverAsync(_tempDir, maxDepth: 3);

        var skill = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.Skill);
        Assert.Equal("skills/my-skill/SKILL.md", skill.RelativePath);

        var agent = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.Agent);
        Assert.Equal("agents/my-agent.md", agent.RelativePath);

        var scheduledTask = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.ScheduledTask);
        Assert.Equal("scheduled-tasks/my-task/SKILL.md", scheduledTask.RelativePath);

        var hook = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.Hook);
        Assert.Equal("hooks/my-hook.ps1", hook.RelativePath);

        Assert.DoesNotContain(report.Candidates, c => c.FilePath.EndsWith("state.json"));
    }
}
