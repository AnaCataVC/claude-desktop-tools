# Engineering Learning: High-Performance Unversioned AI Context Discovery & Infrastructure Secret Scanning

> **Date:** 2026-08-28 (Consolidated: 2026-09-05)  
> **Status:** Implemented in `ClaudeDesktopTools.Services.ClaudeConfigDiscoveryService` & `ContextDiscoveryView`  
> **Target Framework:** .NET 9 (`net9.0-windows10.0.26100.0`, Unpackaged)  
> **Origin:** Decoupled and elevated from `work-activity-panel`

---

## 1. Context & Architectural Challenge

When collaborating with AI coding agents (such as Claude Code, Antigravity, or Copilot Workspace) across numerous software projects, developers maintain local steering instructions (`CLAUDE.md`), operational manuals, team rosters, and domain reference documents (`.claude/references/*.md`, `references/*.md`).

### The Dual Failure Modes:
1. **Unversioned AI Context Loss:** Because these files often contain machine-specific overrides, testing tokens, or local workflows, they are frequently excluded from Git (`.gitignore`) or kept in untracked folders. When a developer's workstation is formatted or fails, this entire knowledge base is permanently lost.
2. **The "Fork Bomb" and Git Traversal Overhead:** An earlier design evaluated Git tracking directory-by-directory via `git rev-parse --is-inside-work-tree`. This introduced two critical flaws:
   - It treated any file inside a Git repository as "versioned", thereby skipping all untracked or gitignored context files (`.claude/references/`, `.env.local`).
   - Querying Git synchronously per file via `Process.Start("git", ...)` on Windows creates severe process creation latency (~20–50 ms per process), taking over a minute on large directories with thousands of files.
3. **Secret Leakage Vector:** Naive context gathering risks exposing private SSH keys (`id_rsa`), AWS root keys, or GitHub personal access tokens if they are accidentally referenced in markdown files.

---

## 2. Engineered Solution

### 2.1 Multi-Zone Breadth-First Search (BFS) Traversal
`ClaudeConfigDiscoveryService.cs` implements an iterative BFS walk up to a configurable depth (`maxDepth`, default 3 to 6):
- **Direct Instructions:** Detects `CLAUDE.md` in any scanned folder and `.claude/CLAUDE.md`.
- **Targeted Reference Folders:** Discovers all `*.md` files inside direct `references/` subdirectories (`references/**/*.md`) and within `.claude/references/**/*.md`.
- **Strict Isolation of Claude Internals:** Excludes Claude CLI and desktop internal state directories (`.claude/projects/**/memory`, `.claude/plans`, `.claude/security`, `.claude/cache`, `.claude/plugins`, `*.log`).
- **Strict Directory Exclusions:** Automatically skips build outputs, package caches, and backup trees (`node_modules`, `.git`, `.vs`, `bin`, `obj`, `venv`, `.venv`, `__pycache__`, `AppData`, `dist`, `build`, and directories matching `_backup_*` or `backup_*`).

```text
Project Root / User Profile
├── CLAUDE.md                     ──> Discovered
├── .claude/
│    ├── CLAUDE.md                ──> Discovered
│    ├── references/
│    │    ├── team-roster.md      ──> Discovered
│    │    └── architecture.md     ──> Discovered
│    ├── projects/                ──> Skipped (Internal session memory)
│    ├── plans/                   ──> Skipped (Internal session plans)
│    └── security/                ──> Skipped (Internal security logs)
├── geocoding/
│    ├── CLAUDE.md                ──> Discovered
│    └── references/
│         └── bq-gotchas.md       ──> Discovered
└── _backup_claudemd_20260828/    ──> Skipped (Anti-noise filter)
```

---

## 2.2 Batched Git Tracking Verification (`batchSize = 50`)
To eliminate process creation overhead while accurately identifying untracked files:
1. **Repository Root Discovery:** Discovers the repository root once per candidate directory via `git rev-parse --show-toplevel` and caches the result.
2. **Orphan Identification:** Candidates outside any Git repository are immediately marked as unversioned.
3. **Chunked `git ls-files` Execution:** Candidates inside a Git repository are grouped and queried in batches of up to 50 files:
   ```bash
   git -C <repoRoot> ls-files -- <file1> <file2> ... <file50>
   ```
4. **Normalized Tracking Check:** Files output by `ls-files` are converted into an in-memory `HashSet<string>` with normalized path separators for instant \(O(1)\) lookups.

### 2.2.1 Orphan Destination Path: Full Relative Path, Not Filename (fixed 2026-09-04)
Orphan candidates (step 2 above) originally resolved their Drive destination path via `Path.GetFileName(file)` -- just the bare filename. Because every stray `CLAUDE.md` (or `references/*.md`) living outside a Git repository shares the same filename, this collapsed multiple, unrelated files onto the same Drive destination (`_sin-repo/CLAUDE.md`) and each upload silently overwrote the previous one. Only the last-uploaded orphan file ever survived on Drive.

**Fix:** orphan candidates now resolve `RelativePath` via `Path.GetRelativePath(rootPath, file)` -- the full path relative to the scan root -- so `DriveSyncService.BuildDriveRelativePath` recreates the real folder tree under the `_sin-repo/` bucket (e.g. `_sin-repo/some-project/CLAUDE.md` and `_sin-repo/nested/other-project/CLAUDE.md` instead of two collisions on `_sin-repo/CLAUDE.md`). Same technique as `work-activity-panel`'s `ClaudeConfigDiscovery.FindUnversionedAsync`, which already kept the path under the profile root for exactly this reason. Candidates that *do* belong to a Git repository were never affected -- their `RelativePath` is `Path.GetRelativePath(repoRoot, file)`, already unique per repository.

---

## 2.3 Defense-in-Depth Secret & Privacy Filter
Before admitting any file into the discovery list, it must pass a two-layer inspection:
1. **Filename Keyword Check:** Files containing `secret`, `credential`, `password`, `token`, `private_key`, `id_rsa`, or `id_ed25519` are immediately rejected.
2. **Streaming Buffer Regex Inspection:** The file's first 64 KB are scanned for signatures matching:
   - Private Keys: `-----BEGIN (RSA|OPENSSH|DSA|EC|PGP) PRIVATE KEY-----`
   - AWS Keys: `(AKIA|AGPA|AIDA|AROA|AIPA|ANPA|ANVA|ASIA)[A-Z0-9]{16}`
   - GitHub PATs: `ghp_[a-zA-Z0-9]{36,255}`, `gho_[a-zA-Z0-9]{36,255}`, `github_pat_[a-zA-Z0-9]{22}_[a-zA-Z0-9]{59}`
   - Slack Tokens: `xox[baprs]-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*`

---

## 2.4 Grouping by Category and Per-File Selective Sync

Every `ClaudeDiscoveryCandidate` already carried a `Category` (`ClaudeDiscoveryCategory`: Context/CLAUDE.md, Skill, Agent, ScheduledTask, Hook), but the view only showed it as a badge on an otherwise flat, mixed list, and "Sincronizar a Drive" always sent every discovered candidate -- there was no way to keep CLAUDE.md files separate from skills/agents/hooks, or to exclude specific files from a sync.

- **`CandidateGroup`** (`ClaudeDiscoveryModels.cs`) is an `ObservableCollection<ClaudeDiscoveryCandidate>` tagged with its `Category`. `CandidateGroup.BuildFrom(candidates)` buckets candidates by category and returns one group per non-empty category, in a fixed display order (`ClaudeDiscoveryCategory.DisplayOrder`: Context, Skill, Agent, ScheduledTask, Hook) rather than discovery order.
- **`ClaudeDiscoveryCandidate.IsSelected`** (defaults `true`, so sync-everything remains the default behavior) is a plain `INotifyPropertyChanged` property -- not a full `ObservableObject` via the MVVM toolkit, since a model only needs to notify its own `CheckBox` binding, not participate in command/property-changed codegen.
- **`ContextDiscoveryViewModel.GroupedCandidates`** is rebuilt from `Candidates` right after each `DiscoverAsync` scan. `SyncToDriveAsync` filters `Candidates.Where(c => c.IsSelected)` before calling `DriveSyncService.SyncCandidatesAsync` -- an unchecked file is simply never in the list the sync service sees, so no changes were needed on the sync side.
- The view (`ContextDiscoveryView.xaml`) nests an `ItemsControl` (not a second `ListView` -- WinUI doesn't virtualize nested `ListView`s cleanly) inside each group's `ListView` item, with a per-group header offering "Seleccionar todo" / "Ninguno", plus the same two actions at the top of the page for all groups at once (`ContextDiscoveryViewModel.SelectAllCommand` / `DeselectAllCommand`).

---

## 2.5 Category Filtering, Collapsible Groups, and Bulk Deselect-by-Tracking-Status (2026-09-05)

Category grouping (2.4 above) only controlled what got *synced*, not what was *visible*: with hundreds of candidates the flat "one `ListView` item per category" layout still forced scrolling past every group to reach the one you cared about, and there was no fast way to isolate a single category or to bulk-drop already-versioned files from a sync.

- **`ContextDiscoveryViewModel._allGroups`** now holds the unfiltered `List<CandidateGroup>` built once per `DiscoverAsync` scan (via `RebuildGroups()`); the previous single-step `GroupedCandidates` rebuild was split into "rebuild groups" + "apply the category filter" so a filter toggle no longer needs to re-run `CandidateGroup.BuildFrom` (and, importantly, doesn't discard each `CandidateGroup`'s live `IsExpanded` state, since `_allGroups` keeps the same instances across filter changes).
- **`CategoryFilters`** (`ObservableCollection<CategoryFilterOption>`, one entry per `ClaudeDiscoveryCategory.DisplayOrder` value, all checked by default) is a view-only visibility toggle, independent of `IsSelected`/sync state. `ApplyCategoryFilter()` rebuilds `GroupedCandidates` from `_allGroups`, keeping only groups whose `CategoryFilterOption.IsChecked` is true; each option's `PropertyChanged` is wired in the constructor to re-run the filter live as checkboxes are toggled. `ShowAllCategoriesCommand` resets every filter back to checked.
- **`CandidateGroup.IsExpanded`** (default `true`) makes each category's group collapsible via a WinUI 3 `Expander` in the view -- no new dependency, `Expander` ships in the Windows App SDK already referenced by this project. The property lives on `CandidateGroup` itself (already `ObservableCollection<ClaudeDiscoveryCandidate>`, so it already implements `INotifyPropertyChanged`/`OnPropertyChanged` from the base class) rather than a wrapper view-model, since collapse state is inherently per-group UI state with no sync-time meaning.
- **`ViewModel.SelectOnlyCategory(string category)`** is a quick top-of-page action (one button per category, driven by `AvailableCategories => ClaudeDiscoveryCategory.DisplayOrder`) that sets `IsSelected` true for that category's candidates and false for every other candidate in one pass -- the existing per-group "Seleccionar todo"/"Ninguno" buttons already covered additive, category-by-category selection, but reaching "sync only Skills this time" still meant deselecting every other group by hand.
- **`DeselectGitTrackedCommand`** deselects every `IsTrackedByGit` candidate while leaving every other candidate's `IsSelected` untouched -- a direct shortcut for the sync's actual intended use case ("Sincronizar sin seguimiento a Drive"), since previously the only way to exclude tracked files was to uncheck them one by one or per whole category.
- **Test coverage:** `ContextDiscoveryViewModelTests.cs` is the first test file targeting a ViewModel rather than a Service, since `ContextDiscoveryViewModel` previously had none. The Tests project links main-project source files individually rather than via `ProjectReference` (see `ClaudeDesktopTools.Tests.csproj`), and deliberately includes single files rather than whole folders where a folder mixes WinUI-dependent code with plain code (`Helpers\LocalSettingsHelper.cs` alone, not all of `Helpers\`) -- `ContextDiscoveryViewModel.cs` was added the same way (`ViewModels\ContextDiscoveryViewModel.cs` alone, not the whole `ViewModels\` folder, since sibling ViewModels may depend on WinUI types the plain `net9.0` test TFM can't compile), alongside a `CommunityToolkit.Mvvm` package reference the ViewModel needs for `[ObservableProperty]`/`[RelayCommand]`. `IClaudeConfigDiscoveryService` and `IDriveSyncService` are faked inline (no mocking library is referenced in this project) since no existing test file already faked either interface.
