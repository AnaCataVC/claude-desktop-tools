> **Created:** 2026-09-05
> **Last Updated:** 2026-09-05
> **Status:** Active
> **Scope:** Process Resource Monitoring Subsystem (`ProcessMonitorService`, `ProcessMonitorViewModel`, `ProcessMonitorView`, `ProcessMonitorServiceTests`)

# Adversarial Stress-Test: Claude Process Resource Monitoring & Control

An adversarial red-team evaluation of the Claude Process Resource Monitoring subsystem in `ClaudeDesktopTools` (.NET 9, WinUI 3, xUnit).

---

## 1. Executive Summary & Threat Vectors

The Process Resource Monitor introduces live visibility (RAM/CPU%) and two per-process actions (`TrimWorkingSet` and `SetLowPriority`) targeting running Claude processes. While designed to be lossless and non-destructive, adversarial stress-testing across Windows NT kernel primitives, WinUI 3 threading, and testing harnesses reveals critical failure modes:

| ID | Vulnerability / Threat Vector | Severity | Component | Root Cause |
| :--- | :--- | :--- | :--- | :--- |
| **SEC-01** | Immediate UI Feedback Clobbering | **High (Functional Bug)** | `ProcessMonitorViewModel.cs` | Action feedback message overwritten by periodic count message within the same tick. |
| **KRN-01** | Corrupted CPU% Delta on OS PID Recycling | **High (State Invariant Violation)** | `ProcessMonitorService.cs` | Sample dictionary keys solely by `int Pid` without `process.StartTime` verification. |
| **UI-01** | Visual Flicker, Focus Destruction & Click Interception | **High (UI/UX & Accessibility)** | `ProcessMonitorViewModel.cs` | Periodic collection wipeout (`Processes.Clear()`) every 2 seconds destroys XAML visual tree containers. |
| **UI-02** | UI Thread Micro-Freezes via Synchronous Win32 Calls | **Medium (Desktop Stability)** | `ProcessMonitorView.xaml.cs` | `DispatcherTimer` invokes `Process.GetProcessesByName` and properties synchronously on the WinUI 3 UI thread. |
| **KRN-02** | Page Fault Storms & Thrashing from `EmptyWorkingSet` | **Medium (System Performance)** | `ProcessMonitorService.cs` | `EmptyWorkingSet` purges all physical pages to pagefile indiscriminately, causing I/O lag in V8 heaps. |
| **SEC-02** | Process Identification Blind Spots & Collisions | **Medium (Security & Reliability)** | `ProcessMonitorService.cs` | Only matches `"claude"`. Ignores CLI sessions under `node.exe`; blindly targets non-Anthropic executables named `claude.exe`. |
| **REL-01** | Flaky Unit Tests from Ambient OS Process Collisions | **Medium (QA / CI Integrity)** | `ProcessMonitorServiceTests.cs` | `Assert.Single(snapshot.Processes)` fails if any other ambient `ping.exe` process is active on the host machine. |
| **REL-02** | Unhandled Win32 Enumeration Exceptions Crashing Dispatcher | **Medium (Desktop Stability)** | `ProcessMonitorService.cs` | `Process.GetProcessesByName` call is outside the `try/catch` block. |

---

## 2. Detailed Technical Dissection

### Vector 1: Operational Failure Modes & Memory Semantics
- **The Fallacy of `EmptyWorkingSet`:**
  The `EmptyWorkingSet` Win32 API (`psapi.dll`, wrapping `SetProcessWorkingSetSize(-1, -1)`) does not perform smart garbage collection or free "idle" virtual memory. Instead, it strips all resident physical pages from the process's working set and flushes them to the Standby List or Windows `pagefile.sys`.
  Because Claude (both Electron Desktop and Node.js CLI) runs on the Google V8 engine with active generational garbage collection, accessing memory immediately triggers a severe storm of hard and soft page faults. This induces disk I/O latency, micro-stutters in Claude, and within seconds the working set expands back to its previous size. Presenting this action to the user as a benign "memory cleaner" creates false expectations and can degrade execution speed.

- **Unobserved Win32 Exceptions in Dispatcher:**
  `Process.GetProcessesByName(_processName)` in `ProcessMonitorService.GetClaudeProcesses()` is invoked outside any `try/catch` block. If the Windows process table query fails due to handle exhaustion or security sandbox restrictions, a `Win32Exception` escapes directly to the `DispatcherTimer.Tick` handler, terminating the desktop application with a stowed exception.

---

### Vector 2: Concurrency, Race Conditions & State Drift
- **PID Recycling & Arithmetic Corruption:**
  Windows NT assigns process IDs in multiples of 4 and aggressively reuses PIDs when processes exit.
  `ProcessMonitorService` maintains `_previousSamples = Dictionary<int, (TimeSpan CpuTime, DateTime SampledAt)>`.
  If a Claude process terminates and Windows reassigns its PID to a newly launched Claude process between 2-second scan intervals:
  1. If the previous process had 45s of CPU time and the new process has 0.2s, `cpuDelta` is negative. `Math.Max(0, ...)` masks this corruption and reports `0.0%`.
  2. If the previous process had 0.05s and the new process immediately spikes to 1.5s, the delta is taken across two distinct processes, generating a wildly inaccurate CPU spike.
  *Invariant Fix:* The sample cache must key on `(int Pid, DateTime StartTime)` to ensure deltas are calculated strictly against the same OS process lifecycle.

- **UI Collection Thrashing & Focus Invalidation:**
  `ProcessMonitorViewModel.Refresh()` clears the `ObservableCollection<ClaudeProcessInfo>` on every tick:
  ```csharp
  Processes.Clear();
  foreach (var item in snapshot.Processes) Processes.Add(item);
  ```
  In WinUI 3:
  1. `Processes.Clear()` sends a `CollectionReset` notification, causing the `ListView` to destroy all item visual containers.
  2. If a user moves their mouse to click "Limpiar RAM" or "Liberar CPU" and the tick fires concurrently, the container collapses under the pointer. The click is lost or, if the list is re-sorted by memory usage (`OrderByDescending`), the click hits a completely different process.
  3. Assistive technologies (Narrator, keyboard navigation) lose focus every 2 seconds, violating Windows desktop accessibility guidelines.

---

### Vector 3: Performance & Background Resource Leaks
- **Sustained Background Polling:**
  The `DispatcherTimer` in `ProcessMonitorView` only pauses on `Unloaded` (navigation away). If the user leaves the window open on "Monitoreo de Recursos" and minimizes the window or locks the workstation, the application executes 1,800 process enumerations per hour, allocating unnecessary snapshot objects and wasting CPU cycles.

---

### Vector 4: Security & Process Identification
- **Ambiguity Between Electron and CLI:**
  Anthropic's Claude Desktop runs as `Claude.exe` with multiple sub-processes (main, GPU, renderers). Claude Code CLI installed via npm often runs under `node.exe`.
  - The UI labels all entries as "Claude Code". Lowering the priority of an Electron renderer can freeze the Desktop GUI.
  - CLI sessions running directly under `node.exe` are completely invisible to `Process.GetProcessesByName("claude")`, producing false negatives.
  - The service performs no path or digital signature validation; an untrusted binary named `claude.exe` in any directory will be displayed and manipulated.

---

### Vector 5: Testing Fragility & Environment Collisions
- **Unconstrained `Assert.Single` Against OS State:**
  In `ProcessMonitorServiceTests.cs`:
  ```csharp
  var dummy = StartDummyPing();
  var snapshot = _service.GetClaudeProcesses();
  var item = Assert.Single(snapshot.Processes);
  ```
  `Assert.Single` presumes no other process named `ping` is running anywhere on the operating system. In CI build environments or developer machines running network health checks or previous test runs, this test suffers intermittent, unpredictable failures.

---

## 3. Recommended Hardening Mitigations

1. **Decouple Action Status from Periodic Metrics in MVVM:**
   Store action feedback in an independent property (`ActionStatusMessage` or WinUI 3 `InfoBar`) with a transient auto-dismiss timer, preventing `Refresh()` from clobbering execution feedback.
2. **Implement In-Place Collection Reconciliation:**
   Instead of `Processes.Clear()`, match new snapshots against existing `ObservableCollection` items by `(Pid, StartTime)`. Update existing items in place, append new processes, and remove terminated ones to eliminate layout thrashing and maintain keyboard focus.
3. **Compound Key with Process StartTime:**
   Index `_previousSamples` by `(int Pid, DateTime StartTime)`. Verify `StartTime` before executing `TrimWorkingSet` or `SetLowPriority` to prevent acting on recycled PIDs.
4. **Asynchronous Win32 Polling:**
   Move `Process.GetProcessesByName` and property resolution to `Task.Run()` on the threadpool, updating the UI collection on completion to keep the WinUI 3 dispatcher responsive.
5. **Scoped Assertions in Tests:**
   Replace `Assert.Single(snapshot.Processes)` with `snapshot.Processes.FirstOrDefault(p => p.Pid == dummy.Id)`.
6. **Accurate User Warnings on RAM Trimming:**
   Update UI tooltips and technical documentation to clarify that `EmptyWorkingSet` performs physical page trimming and may induce temporary page fault latency when memory is re-accessed.
