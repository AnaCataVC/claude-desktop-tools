using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ClaudeDesktopTools.Models;
using ClaudeDesktopTools.Services.Interfaces;

namespace ClaudeDesktopTools.Services;

public class ClaudeConfigDiscoveryService : IClaudeConfigDiscoveryService
{
    private static readonly HashSet<string> SkippedDirectories = new(StringComparer.OrdinalIgnoreCase)
    {
        ".git", ".svn", ".hg", "node_modules", "bin", "obj", ".vs", ".idea",
        ".vscode", "vendor", "packages", "dist", "build", "target", "out",
        "artifacts", "releases", ".next", ".nuxt", ".venv", "venv", "env",
        "__pycache__", ".pytest_cache", ".terraform", ".angular"
    };

    private static readonly HashSet<string> SensitiveNameKeywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "secret", "credential", "password", "token", "private_key", "id_rsa", "id_ed25519"
    };

    private static readonly List<Regex> InfrastructureSecretPatterns = new()
    {
        new Regex(@"-----BEGIN\s+(?:RSA|OPENSSH|DSA|EC|PGP)?\s*PRIVATE KEY-----", RegexOptions.Compiled),
        new Regex(@"(?:A3T[A-Z0-9]|AKIA|AGPA|AIDA|AROA|AIPA|ANPA|ANVA|ASIA)[A-Z0-9]{16}", RegexOptions.Compiled),
        new Regex(@"ghp_[a-zA-Z0-9]{36,255}", RegexOptions.Compiled),
        new Regex(@"gho_[a-zA-Z0-9]{36,255}", RegexOptions.Compiled),
        new Regex(@"github_pat_[a-zA-Z0-9]{22}_[a-zA-Z0-9]{59}", RegexOptions.Compiled),
        new Regex(@"xox[baprs]-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*", RegexOptions.Compiled)
    };

    private readonly string _gitExecutable;

    public ClaudeConfigDiscoveryService(string gitExecutable = "git")
    {
        _gitExecutable = gitExecutable;
    }

    public async Task<ClaudeDiscoveryReport> DiscoverAsync(string rootPath, int maxDepth = 4, CancellationToken cancellationToken = default)
    {
        var report = new ClaudeDiscoveryReport();
        if (!Directory.Exists(rootPath))
            return report;

        var repoCandidateMap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var directCandidates = new List<string>();

        // Enumerate candidates using BFS
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((rootPath, 0));

        while (queue.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (currentDir, depth) = queue.Dequeue();

            try
            {
                // Look for CLAUDE.md directly in current folder
                var claudeFile = Path.Combine(currentDir, "CLAUDE.md");
                if (File.Exists(claudeFile) && IsCandidateAllowed(claudeFile))
                {
                    directCandidates.Add(claudeFile);
                }

                // Look for references/ folder or .claude/references
                var refDir = Path.Combine(currentDir, "references");
                if (Directory.Exists(refDir))
                {
                    foreach (var f in SafeEnumerateFiles(refDir, "*.md"))
                    {
                        if (IsCandidateAllowed(f)) directCandidates.Add(f);
                    }
                }

                var dotClaudeDir = Path.Combine(currentDir, ".claude");
                if (Directory.Exists(dotClaudeDir))
                {
                    var dotClaudeFile = Path.Combine(dotClaudeDir, "CLAUDE.md");
                    if (File.Exists(dotClaudeFile) && IsCandidateAllowed(dotClaudeFile))
                    {
                        directCandidates.Add(dotClaudeFile);
                    }

                    var dotClaudeRefs = Path.Combine(dotClaudeDir, "references");
                    if (Directory.Exists(dotClaudeRefs))
                    {
                        foreach (var f in SafeEnumerateFilesRecursive(dotClaudeRefs, 3))
                        {
                            if (IsCandidateAllowed(f)) directCandidates.Add(f);
                        }
                    }
                }
            }
            catch { }

            if (depth >= maxDepth)
                continue;

            try
            {
                foreach (var sub in Directory.GetDirectories(currentDir))
                {
                    var name = Path.GetFileName(sub);
                    if (!IsDirectorySkipped(name))
                    {
                        queue.Enqueue((sub, depth + 1));
                    }
                }
            }
            catch { }
        }

        // Group discovered files by git repo
        foreach (var file in directCandidates.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var fileDir = Path.GetDirectoryName(file);
            if (string.IsNullOrEmpty(fileDir)) continue;

            string? repoRoot = await GetGitRepoRootAsync(fileDir, cancellationToken);
            if (!string.IsNullOrEmpty(repoRoot))
            {
                if (!repoCandidateMap.TryGetValue(repoRoot, out var list))
                {
                    list = new List<string>();
                    repoCandidateMap[repoRoot] = list;
                }
                list.Add(file);
            }
            else
            {
                var fi = new FileInfo(file);
                report.Candidates.Add(new ClaudeDiscoveryCandidate
                {
                    FilePath = file,
                    RelativePath = Path.GetFileName(file),
                    RepositoryRoot = string.Empty,
                    IsTrackedByGit = false,
                    FileSizeBytes = fi.Exists ? fi.Length : 0,
                    LastModified = fi.Exists ? fi.LastWriteTime : DateTime.Now
                });
            }
        }

        report.RepositoriesScanned = repoCandidateMap.Count;

        // Check git tracking in batches of 50
        foreach (var kvp in repoCandidateMap)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var repoRoot = kvp.Key;
            var files = kvp.Value;

            var relFiles = files.Select(f => Path.GetRelativePath(repoRoot, f)).ToList();
            var tracked = await GetTrackedFilesAsync(repoRoot, relFiles, cancellationToken);

            for (int i = 0; i < files.Count; i++)
            {
                var absPath = files[i];
                var relPath = relFiles[i];
                var isTracked = tracked.Contains(relPath.Replace('/', Path.DirectorySeparatorChar));
                var fi = new FileInfo(absPath);

                report.Candidates.Add(new ClaudeDiscoveryCandidate
                {
                    FilePath = absPath,
                    RelativePath = relPath,
                    RepositoryRoot = repoRoot,
                    IsTrackedByGit = isTracked,
                    FileSizeBytes = fi.Exists ? fi.Length : 0,
                    LastModified = fi.Exists ? fi.LastWriteTime : DateTime.Now
                });
            }
        }

        report.UntrackedCandidatesCount = report.Candidates.Count(c => !c.IsTrackedByGit);
        return report;
    }

    public static bool IsDirectorySkipped(string dirName)
    {
        if (SkippedDirectories.Contains(dirName))
            return true;

        if (dirName.StartsWith("_backup_", StringComparison.OrdinalIgnoreCase) ||
            dirName.StartsWith("backup_", StringComparison.OrdinalIgnoreCase) ||
            dirName.Equals("memory", StringComparison.OrdinalIgnoreCase) ||
            dirName.Equals("plans", StringComparison.OrdinalIgnoreCase) ||
            dirName.Equals("security", StringComparison.OrdinalIgnoreCase) ||
            dirName.Equals("cache", StringComparison.OrdinalIgnoreCase) ||
            dirName.Equals("plugins", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    public static bool IsCandidateAllowed(string filePath)
    {
        var ext = Path.GetExtension(filePath);
        if (!string.Equals(ext, ".md", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".txt", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var fileName = Path.GetFileName(filePath);
        foreach (var keyword in SensitiveNameKeywords)
        {
            if (fileName.Contains(keyword, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        if (HasInfrastructureSecret(filePath))
            return false;

        return true;
    }

    public static bool HasInfrastructureSecret(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return false;

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var buffer = new char[65536];
            int read = reader.Read(buffer, 0, buffer.Length);
            if (read <= 0) return false;

            var content = new string(buffer, 0, read);
            foreach (var pattern in InfrastructureSecretPatterns)
            {
                if (pattern.IsMatch(content))
                    return true;
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dirPath, string searchPattern)
    {
        try
        {
            return Directory.GetFiles(dirPath, searchPattern, SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return Enumerable.Empty<string>();
        }
    }

    private static IEnumerable<string> SafeEnumerateFilesRecursive(string rootDir, int maxDepth)
    {
        var results = new List<string>();
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((rootDir, 0));

        while (queue.Count > 0)
        {
            var (current, depth) = queue.Dequeue();
            try
            {
                foreach (var file in Directory.GetFiles(current))
                {
                    results.Add(file);
                }
            }
            catch { }

            if (depth >= maxDepth) continue;

            try
            {
                foreach (var sub in Directory.GetDirectories(current))
                {
                    var dirInfo = new DirectoryInfo(sub);
                    if (!dirInfo.Attributes.HasFlag(FileAttributes.ReparsePoint) && !IsDirectorySkipped(dirInfo.Name))
                    {
                        queue.Enqueue((sub, depth + 1));
                    }
                }
            }
            catch { }
        }

        return results;
    }

    public async Task<string?> GetGitRepoRootAsync(string directory, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _gitExecutable,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        startInfo.ArgumentList.Add("-C");
        startInfo.ArgumentList.Add(directory);
        startInfo.ArgumentList.Add("rev-parse");
        startInfo.ArgumentList.Add("--show-toplevel");

        try
        {
            using var process = Process.Start(startInfo);
            if (process == null) return null;

            var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var stdout = await stdoutTask;
            await stderrTask;

            if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
            {
                var repoRoot = stdout.Trim().Replace('/', Path.DirectorySeparatorChar);
                return Directory.Exists(repoRoot) ? repoRoot : Path.GetFullPath(repoRoot);
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    public async Task<HashSet<string>> GetTrackedFilesAsync(
        string repoRoot,
        IEnumerable<string> relativeFilePaths,
        CancellationToken cancellationToken)
    {
        var tracked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var fileList = relativeFilePaths.ToList();
        if (fileList.Count == 0) return tracked;

        const int batchSize = 50;
        for (int i = 0; i < fileList.Count; i += batchSize)
        {
            var batch = fileList.Skip(i).Take(batchSize).ToList();
            var startInfo = new ProcessStartInfo
            {
                FileName = _gitExecutable,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            startInfo.ArgumentList.Add("-C");
            startInfo.ArgumentList.Add(repoRoot);
            startInfo.ArgumentList.Add("ls-files");
            startInfo.ArgumentList.Add("--");
            foreach (var relPath in batch)
            {
                startInfo.ArgumentList.Add(relPath.Replace('\\', '/'));
            }

            try
            {
                using var process = Process.Start(startInfo);
                if (process == null) continue;

                var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
                var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
                await process.WaitForExitAsync(cancellationToken);
                var stdout = await stdoutTask;
                await stderrTask;

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(stdout))
                {
                    using var reader = new StringReader(stdout);
                    string? line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (!string.IsNullOrWhiteSpace(line))
                        {
                            var normalized = line.Trim().Replace('/', Path.DirectorySeparatorChar);
                            tracked.Add(normalized);
                        }
                    }
                }
            }
            catch
            {
                // Ignore failure and treat as untracked
            }
        }

        return tracked;
    }
}
