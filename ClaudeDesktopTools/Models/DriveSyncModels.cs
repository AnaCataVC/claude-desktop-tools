using System.Collections.Generic;

namespace ClaudeDesktopTools.Models;

public class DriveSyncSettings
{
    public string WebAppUrl { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string DestinationPrefix { get; set; } = "claude-md-unversioned";
}

public class DriveSyncResult
{
    public int Uploaded { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}
