# Claude Desktop Tools Documentation Hub

Welcome to the internal engineering documentation and architectural reference for **Claude Desktop Tools**.

---

## 📑 Documentation Index

### 1. Architecture & External References
- [`claude-desktop-tools-architecture.md`](external-references/claude-desktop-tools-architecture.md): Architectural benchmarking, UI stack comparisons (WinUI 3 vs. WPF vs. Avalonia), MVVM design decisions, and safety invariants.
- [`claude-process-monitor-stress-test.md`](external-references/claude-process-monitor-stress-test.md): Adversarial stress test report covering P/Invoke `EmptyWorkingSet`, CPU delta calculation, PID recycling defenses, and WinUI 3 UI synchronization.
- [`vanilla-product-landing-page-patterns.md`](external-references/vanilla-product-landing-page-patterns.md): Lightweight zero-dependency product landing page design patterns with client-side bilingual i18n and dynamic ROI calculation.

### 2. Engineering Learnings & Design Decisions
- [`claude-local-session-and-transcript-maintenance.md`](learning/claude-local-session-and-transcript-maintenance.md): Comprehensive analysis of the 24-hour grace window, in-memory process overwrite prevention (`claude.exe`), and surgical regex header swaps with timestamp preservation.
- [`claude-process-resource-monitoring-and-control.md`](learning/claude-process-resource-monitoring-and-control.md): Architecture of real-time Claude process monitoring, memory working set trimming, process priority adjustment, and high-frequency UI throttling.
- [`unversioned-ai-context-discovery-and-secret-filtering.md`](learning/unversioned-ai-context-discovery-and-secret-filtering.md): Multi-zone BFS context discovery (skills, agents, hooks, references), chunked Git tracking via `git ls-files` (batch size 50), and defense-in-depth regex secret scanning.
- [`winui3-contentdialog-concurrency-and-dialog-lock.md`](learning/winui3-contentdialog-concurrency-and-dialog-lock.md): Resolving WinUI 3 `ContentDialog` collision crashes (`0x80000018`) using zero-wait semaphore locks (`SemaphoreSlim(1, 1)`).

### 3. Packaging & Distribution
- [`installer/ClaudeDesktopTools.iss`](../installer/ClaudeDesktopTools.iss): Modern Inno Setup configuration for producing standalone, multi-language Windows desktop installer packages.
