# Engineering Learning: Claude Process Resource Monitoring & Control

> **Date:** 2026-09-05
> **Status:** Implemented in `ClaudeDesktopTools.Views.ProcessMonitorView`
> **Target Framework:** .NET 9 (`net9.0-windows10.0.26100.0`, Unpackaged)
> **Scope:** RAM/CPU visibility and two reversible, non-destructive resource actions, strictly scoped to `claude.exe` processes

---

## 1. Context

The Dashboard and Sessions views already probed `Process.GetProcessesByName("claude")` for liveness checks (collision guard, "Cerrar sesión"). The new **"Monitoreo de Recursos"** tab extends that same primitive into a live view: per-process RAM (`WorkingSet64`) and CPU% for every running Claude process, refreshed on a 2-second `DispatcherTimer`, with two per-process actions:

- **Limpiar RAM** — trims the process working set (`EmptyWorkingSet`, `psapi.dll`), returning idle physical pages to the OS without killing the process.
- **Liberar/Restaurar CPU** — toggles `Process.PriorityClass` between `Normal` and `BelowNormal`.

No new NuGet dependency was needed — everything is `System.Diagnostics.Process` plus one native P/Invoke.

---

## 2. CPU% Requires a Delta, Not a Single Read

`Process.TotalProcessorTime` is a cumulative counter (total CPU time consumed since process start), not an instantaneous percentage. Task-Manager-style CPU% is derived from two samples:

```csharp
public static double ComputeCpuPercent(TimeSpan cpuDelta, TimeSpan wallDelta, int processorCount)
{
    if (wallDelta.TotalMilliseconds <= 0) return 0;
    return Math.Max(0, 100.0 * cpuDelta.TotalMilliseconds / (wallDelta.TotalMilliseconds * processorCount));
}
```

`ProcessMonitorService` keeps a `Dictionary<(int Pid, DateTime StartTime), (TimeSpan CpuTime, DateTime SampledAt)>` of the previous scan. Keying on the composite `(Pid, StartTime)` tuple ensures that if Windows recycles a PID onto a newly started process within the 2-second window, the old sample is not matched, avoiding negative or absurdly large CPU deltas across unrelated processes. The first scan for any process has no prior sample, so it reports `0%` rather than guessing.

---

## 3. Re-Checking the Guard at Action Time, Not Just at List Time

The scan already filters to `Process.GetProcessesByName("claude")`, but `TrimWorkingSet(pid, expectedStartTime)` and `SetLowPriority(pid, ..., expectedStartTime)` receive the target process's `StartTime` from the view row. Both re-resolve the process and re-verify both its name and its `StartTime` before doing anything:

```csharp
private bool IsTargetProcess(Process process, DateTime? expectedStartTime)
{
    if (!process.ProcessName.Equals(_processName, StringComparison.OrdinalIgnoreCase))
        return false;

    if (expectedStartTime.HasValue)
        return (process.StartTime - expectedStartTime.Value).Duration() < ProcessStartTimeTolerance;

    return true;
}
```

This mirrors the PID-reuse defense in `ClaudeMaintenanceService.CloseSession` (README invariant #8): Windows can recycle a pid onto an unrelated process between the moment a row is rendered and the moment its button is clicked, so the safety check belongs at the action, not just at the scan.

---

## 4. Testability Seam: Inject the Process Name

Unlike the file-system services (which take injectable roots), there was no existing seam for "which process name to watch." Rather than mocking `Process` (not mockable — it is a sealed BCL class with static factory methods), `ProcessMonitorService` takes the target name as a constructor parameter, defaulting to `"claude"`:

```csharp
public ProcessMonitorService(string processName = "claude") { _processName = processName; }
```

Tests instantiate `new ProcessMonitorService("ping")` and spawn a real `ping.exe -n 60 127.0.0.1` child process (the same disposable-dummy-process pattern already used in `ClaudeMaintenanceServiceTests`). This exercises the real Windows `Process` APIs — `GetProcessesByName`, `WorkingSet64`, `EmptyWorkingSet`, `PriorityClass` — end to end, including the negative case: a default-named service (`"claude"`) must refuse to act on a genuinely running `ping` process, proving the name guard against a real OS process rather than a mock.

The CPU delta math (`ComputeCpuPercent`) is pulled out as a pure `public static` function precisely so it can be asserted exactly, without depending on real (and therefore flaky) process timing.

---

## 5. Why No Confirmation Dialog

Every other mutating action in this app (`CloseSession_Click`, `DeleteTranscript_Click`, bulk deletes) goes through the `ContentDialog` + `_dialogLock` semaphore pattern documented in `winui3-contentdialog-concurrency-and-dialog-lock.md`, because those actions are destructive and irreversible. Trimming a working set and lowering scheduling priority are neither: the OS repopulates trimmed pages on demand, and priority is a toggle. The two new buttons act immediately, no dialog — confirmation guards are reserved for actions that lose data or kill a process.

---

## 6. Page Faulting & Memory Semantics of EmptyWorkingSet

`EmptyWorkingSet` (`psapi.dll`, calling `SetProcessWorkingSetSize(-1, -1)`) does not delete virtual memory or alter the process's commit charge; it strips resident physical pages from the working set into the Standby List or `pagefile.sys`.
Because Claude runs on Google V8 (Electron for Desktop, Node for CLI) with active generational garbage collection, any resumption of thread activity immediately triggers hard or soft page faults to page memory back in. Thus, RAM trimming is a transient compaction, not a zero-cost memory freeing mechanism. The UI tooltip and documentation reflect this to set accurate expectations.

---

## 7. In-Place Collection Reconciliation & Decoupled Feedback in WinUI 3

Wiping the bound collection via `Processes.Clear()` on every 2-second timer tick causes visual layout thrashing, breaks keyboard/Narrator focus navigation, and risks misdirected clicks if item sorting shifts.
`ProcessMonitorViewModel` instead uses `SyncProcesses`:
- Existing processes update their metrics in place (`ClaudeProcessInfo.UpdateMetrics`) raising BCL `INotifyPropertyChanged` events.
- Terminated processes are pruned from the collection.
- Newly spawned processes are inserted smoothly.
Action feedback (`ActionFeedbackMessage`) is kept in an independent observable property with an auto-dismissing 4-second timeout on the main dispatcher, ensuring that periodic process count refreshes never clobber user feedback.
