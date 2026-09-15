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

    // The service always additionally scans "<homeDirectory>/.claude" -- tests inject an empty,
    // isolated fake home so assertions aren't polluted by whatever is really under the machine's
    // own ~/.claude (real skills/agents/hooks would break exact-count assertions below).
    private readonly string _fakeHomeDir;

    public ClaudeConfigDiscoveryServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "ClaudeDiscoveryTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _fakeHomeDir = Path.Combine(Path.GetTempPath(), "ClaudeDiscoveryTests_FakeHome_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_fakeHomeDir);
    }

    private ClaudeConfigDiscoveryService CreateService() => new(homeDirectory: _fakeHomeDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }
        if (Directory.Exists(_fakeHomeDir))
        {
            try { Directory.Delete(_fakeHomeDir, true); } catch { }
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

        var service = CreateService();
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

        Directory.CreateDirectory(Path.Combine(dotClaudeDir, "agent-memory", "qa-tester"));
        File.WriteAllText(Path.Combine(dotClaudeDir, "agent-memory", "qa-tester", "MEMORY.md"), "# QA Tester Memory");

        Directory.CreateDirectory(Path.Combine(dotClaudeDir, "projects", "my-project", "memory"));
        File.WriteAllText(Path.Combine(dotClaudeDir, "projects", "my-project", "memory", "MEMORY.md"), "# Project Memory");

        var service = CreateService();
        var report = await service.DiscoverAsync(_tempDir, maxDepth: 3);

        var skill = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.Skill);
        Assert.Equal("skills/my-skill/SKILL.md", skill.RelativePath);

        var agent = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.Agent);
        Assert.Equal("agents/my-agent.md", agent.RelativePath);

        var scheduledTask = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.ScheduledTask);
        Assert.Equal("scheduled-tasks/my-task/SKILL.md", scheduledTask.RelativePath);

        var hook = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.Hook);
        Assert.Equal("hooks/my-hook.ps1", hook.RelativePath);

        var agentMemory = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.AgentMemory);
        Assert.Equal("agent-memory/qa-tester/MEMORY.md", agentMemory.RelativePath);

        var projectMemory = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.ProjectMemory);
        Assert.Equal("projects/my-project/memory/MEMORY.md", projectMemory.RelativePath);

        Assert.DoesNotContain(report.Candidates, c => c.FilePath.EndsWith("state.json"));
    }

    [Fact]
    public async System.Threading.Tasks.Task DiscoverAsync_MultipleOrphanClaudeFilesKeepDistinctRelativePaths()
    {
        // Two unrelated folders, neither tracked by git, each with their own CLAUDE.md.
        // Before the fix, both collapsed to RelativePath "CLAUDE.md" and would overwrite
        // each other once uploaded to the same Drive destination.
        string projectA = Path.Combine(_tempDir, "project-a");
        string projectB = Path.Combine(_tempDir, "nested", "project-b");
        Directory.CreateDirectory(projectA);
        Directory.CreateDirectory(projectB);
        File.WriteAllText(Path.Combine(projectA, "CLAUDE.md"), "# Project A context");
        File.WriteAllText(Path.Combine(projectB, "CLAUDE.md"), "# Project B context");

        var service = CreateService();
        var report = await service.DiscoverAsync(_tempDir, maxDepth: 4);

        var orphanClaudeFiles = report.Candidates
            .Where(c => c.FilePath.EndsWith("CLAUDE.md") && string.IsNullOrEmpty(c.RepositoryRoot))
            .ToList();

        Assert.Equal(2, orphanClaudeFiles.Count);
        Assert.Equal(2, orphanClaudeFiles.Select(c => c.RelativePath).Distinct().Count());
        Assert.Contains(orphanClaudeFiles, c => c.RelativePath == "project-a/CLAUDE.md");
        Assert.Contains(orphanClaudeFiles, c => c.RelativePath == "nested/project-b/CLAUDE.md");

        var drivePaths = orphanClaudeFiles
            .Select(c => DriveSyncService.BuildDriveRelativePath(c, "claude-md-unversioned"))
            .Distinct()
            .ToList();
        Assert.Equal(2, drivePaths.Count);
    }

    [Theory]
    [InlineData("settings.json", true)]
    [InlineData("mcp-secret.json", false)] // sensitive filename keyword
    [InlineData("notes.md", false)] // wrong extension
    public void IsJsonConfigAllowed_FiltersByExtensionAndName(string fileName, bool expected)
    {
        string filePath = Path.Combine(_tempDir, fileName);
        File.WriteAllText(filePath, "{}");

        Assert.Equal(expected, ClaudeConfigDiscoveryService.IsJsonConfigAllowed(filePath));
    }

    [Fact]
    public void IsJsonConfigAllowed_RejectsFileWithInfrastructureSecret()
    {
        string filePath = Path.Combine(_tempDir, "settings.json");
        File.WriteAllText(filePath, "{\"token\": \"ghp_123456789012345678901234567890123456\"}");

        Assert.False(ClaudeConfigDiscoveryService.IsJsonConfigAllowed(filePath));
    }

    [Fact]
    public async System.Threading.Tasks.Task DiscoverAsync_FindsGlobalSettingsAndKeybindingsAsLooseFiles()
    {
        string dotClaudeDir = Path.Combine(_tempDir, ".claude");
        Directory.CreateDirectory(dotClaudeDir);
        File.WriteAllText(Path.Combine(dotClaudeDir, "settings.json"), "{\"model\": \"sonnet\"}");
        File.WriteAllText(Path.Combine(dotClaudeDir, "settings.local.json"), "{\"env\": {}}");
        File.WriteAllText(Path.Combine(dotClaudeDir, "keybindings.json"), "{\"submit\": \"enter\"}");

        var service = CreateService();
        var report = await service.DiscoverAsync(_tempDir, maxDepth: 3);

        var settings = report.Candidates.Where(c => c.Category == ClaudeDiscoveryCategory.GlobalSetting).ToList();
        Assert.Equal(2, settings.Count);
        Assert.Contains(settings, c => c.RelativePath == "settings.json");
        Assert.Contains(settings, c => c.RelativePath == "settings.local.json");

        var keybinding = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.Keybinding);
        Assert.Equal("keybindings.json", keybinding.RelativePath);
    }

    [Fact]
    public async System.Threading.Tasks.Task DiscoverAsync_AlwaysScansHomeDotClaudeEvenWhenRootPathIsElsewhere()
    {
        // rootPath (_tempDir) has nothing Claude-related in it; the global CLAUDE.md and
        // settings.json only exist under the injected fake home directory.
        string homeDotClaudeDir = Path.Combine(_fakeHomeDir, ".claude");
        Directory.CreateDirectory(homeDotClaudeDir);
        File.WriteAllText(Path.Combine(homeDotClaudeDir, "CLAUDE.md"), "# Global user context");
        File.WriteAllText(Path.Combine(homeDotClaudeDir, "settings.json"), "{\"model\": \"sonnet\"}");

        var service = CreateService();
        var report = await service.DiscoverAsync(_tempDir, maxDepth: 2);

        Assert.Contains(report.Candidates, c => c.FilePath.EndsWith("CLAUDE.md") && c.Category == ClaudeDiscoveryCategory.Context);
        Assert.Contains(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.GlobalSetting);
    }

    [Fact]
    public void ExtractSanitizedMcpConfig_RedactsSecretShapedValuesAndKeepsSafeOnes()
    {
        string sourcePath = Path.Combine(_tempDir, ".claude.json");
        string outputPath = Path.Combine(_tempDir, "mcp-config.sanitized.json");
        File.WriteAllText(sourcePath, """
        {
          "oauthAccount": { "id": "should-not-appear" },
          "mcpServers": {
            "my-server": {
              "command": "npx",
              "args": ["my-mcp-server"],
              "env": { "API_KEY": "sk-live-should-be-redacted" }
            }
          }
        }
        """);

        bool result = ClaudeConfigDiscoveryService.ExtractSanitizedMcpConfig(sourcePath, outputPath);

        Assert.True(result);
        string sanitized = File.ReadAllText(outputPath);
        Assert.Contains("\"command\": \"npx\"", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
        Assert.DoesNotContain("sk-live-should-be-redacted", sanitized);
        Assert.DoesNotContain("should-not-appear", sanitized);
    }

    [Fact]
    public void ExtractSanitizedMcpConfig_RedactsBearerTokenInsideArgsArray()
    {
        // Found by adversarial review: a stdio MCP server commonly passes a bearer token as an
        // "args" array element (mcp-remote's --header flag), not under a suspicious-looking key.
        string sourcePath = Path.Combine(_tempDir, ".claude.json");
        string outputPath = Path.Combine(_tempDir, "mcp-config.sanitized.json");
        File.WriteAllText(sourcePath, """
        {
          "mcpServers": {
            "devops": {
              "command": "npx",
              "args": ["-y", "mcp-remote", "https://devops.internal/mcp/",
                       "--header", "Authorization: Bearer sk-liveTokenShouldNotSurvive1234567890"]
            }
          }
        }
        """);

        bool result = ClaudeConfigDiscoveryService.ExtractSanitizedMcpConfig(sourcePath, outputPath);

        Assert.True(result);
        string sanitized = File.ReadAllText(outputPath);
        Assert.DoesNotContain("sk-liveTokenShouldNotSurvive1234567890", sanitized);
        Assert.Contains("[REDACTED]", sanitized);
        Assert.Contains("mcp-remote", sanitized); // non-secret args entries survive
    }

    [Fact]
    public void ExtractSanitizedMcpConfig_RedactsCredentialsEmbeddedInUrl()
    {
        string sourcePath = Path.Combine(_tempDir, ".claude.json");
        string outputPath = Path.Combine(_tempDir, "mcp-config.sanitized.json");
        File.WriteAllText(sourcePath, """
        {
          "mcpServers": {
            "postgres": {
              "command": "npx",
              "url": "postgresql://dbuser:sup3rSecretPw@db.internal:5432/mydb"
            }
          }
        }
        """);

        bool result = ClaudeConfigDiscoveryService.ExtractSanitizedMcpConfig(sourcePath, outputPath);

        Assert.True(result);
        string sanitized = File.ReadAllText(outputPath);
        Assert.DoesNotContain("sup3rSecretPw", sanitized);
    }

    [Fact]
    public void ExtractSanitizedMcpConfig_RedactsLiteralGitHubTokenInsideNonSensitiveKey()
    {
        string sourcePath = Path.Combine(_tempDir, ".claude.json");
        string outputPath = Path.Combine(_tempDir, "mcp-config.sanitized.json");
        File.WriteAllText(sourcePath, """
        {
          "mcpServers": {
            "github": {
              "command": "npx",
              "args": ["-y", "@modelcontextprotocol/server-github", "--token", "ghp_123456789012345678901234567890123456"]
            }
          }
        }
        """);

        bool result = ClaudeConfigDiscoveryService.ExtractSanitizedMcpConfig(sourcePath, outputPath);

        Assert.True(result);
        string sanitized = File.ReadAllText(outputPath);
        Assert.DoesNotContain("ghp_123456789012345678901234567890123456", sanitized);
    }

    [Fact]
    public async System.Threading.Tasks.Task DiscoverAsync_McpConfigCandidateNeverContainsRawSecret()
    {
        // End-to-end: DiscoverAsync wires ExtractSanitizedMcpConfig's output into a real candidate.
        // This locks in that the wiring never bypasses the sanitizer (e.g. by falling back to the raw file).
        string homeDotClaudeDir = Path.Combine(_fakeHomeDir, ".claude");
        Directory.CreateDirectory(homeDotClaudeDir);
        File.WriteAllText(Path.Combine(_fakeHomeDir, ".claude.json"), """
        {
          "mcpServers": {
            "devops": {
              "command": "npx",
              "args": ["--header", "Authorization: Bearer sk-liveTokenShouldNotSurvive1234567890"]
            }
          }
        }
        """);

        var service = CreateService();
        var report = await service.DiscoverAsync(_tempDir, maxDepth: 2);

        var mcpCandidate = Assert.Single(report.Candidates, c => c.Category == ClaudeDiscoveryCategory.McpConfig);
        string uploadedContent = await File.ReadAllTextAsync(mcpCandidate.FilePath);
        Assert.DoesNotContain("sk-liveTokenShouldNotSurvive1234567890", uploadedContent);
    }

    [Fact]
    public void ExtractSanitizedMcpConfig_ReturnsFalseWhenNoMcpServersPresent()
    {
        string sourcePath = Path.Combine(_tempDir, ".claude.json");
        string outputPath = Path.Combine(_tempDir, "mcp-config.sanitized.json");
        File.WriteAllText(sourcePath, "{\"oauthAccount\": {\"id\": \"x\"}}");

        bool result = ClaudeConfigDiscoveryService.ExtractSanitizedMcpConfig(sourcePath, outputPath);

        Assert.False(result);
        Assert.False(File.Exists(outputPath));
    }

    [Fact]
    public void ExtractSanitizedMcpConfig_ReturnsFalseWhenSourceMissing()
    {
        string sourcePath = Path.Combine(_tempDir, "does-not-exist.json");
        string outputPath = Path.Combine(_tempDir, "mcp-config.sanitized.json");

        bool result = ClaudeConfigDiscoveryService.ExtractSanitizedMcpConfig(sourcePath, outputPath);

        Assert.False(result);
    }
}
