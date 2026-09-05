using System.Collections.Generic;

namespace ClaudeDesktopTools.Models;

public class DriveSyncSettings
{
    public string WebAppUrl { get; set; } = string.Empty;
    public string AuthToken { get; set; } = string.Empty;
    public string DestinationPrefix { get; set; } = "claude-md-unversioned";

    /// <summary>Bucket for skills/agents/scheduled-tasks/hooks with no owning Git repo.</summary>
    public string ClaudeConfigBucketName { get; set; } = "_claude-config";

    /// <summary>Bucket for CLAUDE.md/references with no owning Git repo.</summary>
    public string NoRepoBucketName { get; set; } = "_sin-repo";
}

public class DriveSyncResult
{
    public int Uploaded { get; set; }
    public int Failed { get; set; }
    public List<string> Errors { get; set; } = new();
    public string Message { get; set; } = string.Empty;
}

public enum DriveSyncStepStatus
{
    Starting,
    Uploading,
    FileUploaded,
    FileFailed,
    Completed
}

public class DriveSyncProgress
{
    public int Current { get; set; }
    public int Total { get; set; }
    public int Percentage { get; set; }
    public string CurrentFileName { get; set; } = string.Empty;
    public string CurrentRelativePath { get; set; } = string.Empty;
    public DriveSyncStepStatus Status { get; set; }
    public string Detail { get; set; } = string.Empty;
    public int UploadedCount { get; set; }
    public int FailedCount { get; set; }

    public static DriveSyncProgress Initial(int total) => new()
    {
        Current = 0,
        Total = total,
        Percentage = 0,
        Status = DriveSyncStepStatus.Starting,
        Detail = total == 0 ? "No hay archivos sin seguimiento para sincronizar." : $"Preparando sincronización de {total} archivos..."
    };

    public static DriveSyncProgress FileStep(int current, int total, string fileName, string relativePath, DriveSyncStepStatus status, int uploadedCount, int failedCount)
    {
        int percentage = total == 0 ? 100 : (int)System.Math.Round(current * 100.0 / total);
        string detail = status == DriveSyncStepStatus.Uploading
            ? $"Subiendo {fileName} ({current} de {total})..."
            : status == DriveSyncStepStatus.FileFailed
                ? $"Falló {fileName} ({current} de {total})"
                : $"Subido {fileName} ({current} de {total})";

        return new DriveSyncProgress
        {
            Current = current,
            Total = total,
            Percentage = percentage,
            CurrentFileName = fileName,
            CurrentRelativePath = relativePath,
            Status = status,
            Detail = detail,
            UploadedCount = uploadedCount,
            FailedCount = failedCount
        };
    }

    public static DriveSyncProgress Finished(int total, int uploadedCount, int failedCount) => new()
    {
        Current = total,
        Total = total,
        Percentage = 100,
        Status = DriveSyncStepStatus.Completed,
        Detail = failedCount == 0 ? "Sincronización completada." : $"Completado con {failedCount} error(es).",
        UploadedCount = uploadedCount,
        FailedCount = failedCount
    };
}
