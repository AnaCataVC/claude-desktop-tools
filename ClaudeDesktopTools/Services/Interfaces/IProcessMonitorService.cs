using System;
using ClaudeDesktopTools.Models;

namespace ClaudeDesktopTools.Services.Interfaces;

public interface IProcessMonitorService
{
    /// <summary>Snapshots every running Claude Code CLI process (excludes the packaged desktop app and its Electron helper subprocesses, which also name-match "claude") with RAM/CPU% and, when resolvable, its working directory.</summary>
    ClaudeProcessSnapshot GetClaudeProcesses();

    /// <summary>Trims the process working set (returns idle physical RAM pages to the OS). Only acts if the pid still belongs to a "claude" process matching expectedStartTime if provided.</summary>
    bool TrimWorkingSet(int pid, DateTime? expectedStartTime = null);

    /// <summary>Sets CPU scheduling priority (Normal or BelowNormal). Only acts if the pid still belongs to a "claude" process matching expectedStartTime if provided.</summary>
    bool SetLowPriority(int pid, bool lowPriority, DateTime? expectedStartTime = null);
}
