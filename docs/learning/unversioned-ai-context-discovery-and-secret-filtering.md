# Engineering Learning: High-Performance Unversioned AI Context Discovery & Infrastructure Secret Scanning

> **Date:** 2026-08-28 (Consolidated: 2026-09-04)  
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

---

## 2.3 Defense-in-Depth Secret & Privacy Filter
Before admitting any file into the discovery list, it must pass a two-layer inspection:
1. **Filename Keyword Check:** Files containing `secret`, `credential`, `password`, `token`, `private_key`, `id_rsa`, or `id_ed25519` are immediately rejected.
2. **Streaming Buffer Regex Inspection:** The file's first 64 KB are scanned for signatures matching:
   - Private Keys: `-----BEGIN (RSA|OPENSSH|DSA|EC|PGP) PRIVATE KEY-----`
   - AWS Keys: `(AKIA|AGPA|AIDA|AROA|AIPA|ANPA|ANVA|ASIA)[A-Z0-9]{16}`
   - GitHub PATs: `ghp_[a-zA-Z0-9]{36,255}`, `gho_[a-zA-Z0-9]{36,255}`, `github_pat_[a-zA-Z0-9]{22}_[a-zA-Z0-9]{59}`
   - Slack Tokens: `xox[baprs]-[0-9]{10,13}-[0-9]{10,13}[a-zA-Z0-9-]*`
