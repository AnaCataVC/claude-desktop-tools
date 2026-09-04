using System;
using System.Collections.Generic;

namespace ClaudeDesktopTools.Models;

/// <summary>
/// Retention thresholds for the local Claude stores. Nothing is ever removed automatically:
/// the app reports what is stale and the user triggers each action explicitly.
/// </summary>
public class ClaudeMaintenanceSettings
{
    public int TranscriptRetentionDays { get; set; } = 30;
    public int SessionRetentionDays { get; set; } = 7;
}

/// <summary>
/// Size and staleness of one on-disk store.
/// </summary>
public class ClaudeStoreReport
{
    public string DisplayName { get; set; } = string.Empty;
    public bool Exists { get; set; }
    public int TotalFiles { get; set; }
    public long TotalBytes { get; set; }
    public int StaleFiles { get; set; }
    public long StaleBytes { get; set; }

    public bool ReclaimsDiskSpace { get; set; } = true;

    public string TotalDisplay => FormatBytes(TotalBytes);
    public string StaleDisplay => FormatBytes(StaleBytes);
    public bool HasStaleFiles => StaleFiles > 0;

    public string Summary => Exists
        ? (ReclaimsDiskSpace
            ? $"{TotalFiles} archivos · {TotalDisplay} · {StaleFiles} recuperables ({StaleDisplay})"
            : $"{TotalFiles} sesiones · {TotalDisplay} ({StaleFiles} fuera de retención)")
        : "Directorio no encontrado";

    public static string FormatBytes(long bytes)
    {
        if (bytes >= 1024L * 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0 * 1024.0):N1} GB";
        if (bytes >= 1024L * 1024L) return $"{bytes / (1024.0 * 1024.0):N1} MB";
        if (bytes >= 1024L) return $"{bytes / 1024.0:N1} KB";
        return $"{bytes} B";
    }
}

public class ClaudeMaintenanceReport
{
    public ClaudeStoreReport Transcripts { get; set; } = new() { DisplayName = "Transcripts de sesiones (CLI)", ReclaimsDiskSpace = true };
    public ClaudeStoreReport Sessions { get; set; } = new() { DisplayName = "Índice de sesiones (Desktop)", ReclaimsDiskSpace = false };
    public bool ClaudeIsRunning { get; set; }
    public DateTime GeneratedAt { get; set; } = DateTime.Now;

    public long TotalReclaimableBytes => Transcripts.StaleBytes;
    public string TotalReclaimableDisplay => ClaudeStoreReport.FormatBytes(TotalReclaimableBytes);
}

/// <summary>
/// Outcome of a maintenance action. <see cref="Skipped"/> marks a refusal to act — a guard
/// tripped, not a failure — and <see cref="Message"/> always says why.
/// </summary>
public class ClaudeCleanupResult
{
    public int FilesProcessed { get; set; }
    public long BytesFreed { get; set; }
    public bool Skipped { get; set; }
    public string Message { get; set; } = string.Empty;
    public List<string> Failures { get; set; } = new();

    public string BytesFreedDisplay => ClaudeStoreReport.FormatBytes(BytesFreed);
}

/// <summary>
/// Metadata for an individual Claude Desktop session.
/// </summary>
public class ClaudeSessionItem
{
    public string SessionId { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string WorkingDirectory { get; set; } = string.Empty;
    public DateTime LastModified { get; set; }
    public long FileSizeBytes { get; set; }
    public bool IsArchived { get; set; }
    public bool IsStale { get; set; }

    public string StatusBadge => IsArchived ? "Archivada" : (IsStale ? "Archivable" : "Activa");
    public string FileSizeDisplay => ClaudeStoreReport.FormatBytes(FileSizeBytes);
}
