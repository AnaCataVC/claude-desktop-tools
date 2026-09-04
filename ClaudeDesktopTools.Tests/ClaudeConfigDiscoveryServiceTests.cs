using System;
using System.IO;
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
}
