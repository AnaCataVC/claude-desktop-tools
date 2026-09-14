> **Created:** 2026-09-14
> **Last Updated:** 2026-09-14

# Claude Code & Subagent Memory Architecture

This document consolidates external references and empirical findings on how Claude Code and agent frameworks persist memory, learnings, and project context across sessions.

---

## 1. Executive Summary

Claude Code and associated agentic frameworks use two primary mechanisms to maintain state, context, and learned lessons across developer sessions:

1. **Auto Memory (Project-Level / CLI Scope):**
   - **Path:** `~/.claude/projects/<project-slug>/memory/`
   - **Entry Point:** `MEMORY.md` (clickable table of contents / index).
   - **Topic Files:** Markdown files with YAML frontmatter linked from `MEMORY.md` (e.g. `feedback-docker-cleanup.md`, `project_architecture.md`, `contratos-stack-*.md`).
2. **Subagent Long-Term Memory (Agent-Scope):**
   - **Path:** `.claude/agent-memory/<agent-role>/` (both in project root and global `~/.claude/agent-memory/`).
   - **Entry Point:** `MEMORY.md` per agent role (e.g., `qa-browser-tester/MEMORY.md`, `tech-lead/MEMORY.md`).
   - **Topic Files:** Project-specific insights and regression prevention notes (e.g. `hotspot_*.md`, `project_*.md`).

Neither of these directories is typically committed to Git (they are considered local user state or unversioned configuration). Consequently, if a developer works across multiple machines or re-installs, all accumulated memory and architectural learnings are lost unless backed up by tools like **Claude Desktop Tools**.

---

## 2. Deep Dive: Auto Memory (`~/.claude/projects/<slug>/memory/`)

### 2.1 Storage & Naming Scheme
- Located under:
  ```text
  %USERPROFILE%\.claude\projects\<slug>\memory\
  ```
- `<slug>` is generated from the absolute path where Claude Code was launched, with non-alphanumeric characters replaced by hyphens (e.g., `C:\Users\anaca\Archivos Trabajo\Repositories\simplit\optimizer` becomes `C--Users-anaca-Archivos-Trabajo-Repositories-simplit-optimizer`).

### 2.2 Role of `MEMORY.md` vs. Topic Files
- **`MEMORY.md`:** Serves as the primary index file loaded by Claude Code at session start. Claude Code reads up to a bounded context window (~200 lines / ~25KB) from `MEMORY.md`.
- **Linked Files:** Detailed findings, feedback notes, and debugging logs are broken down into separate `.md` files linked from `MEMORY.md`:
  ```markdown
  # Memory Index
  - [Docker Cleanup Guidelines](feedback-docker-cleanup.md) — rules to prevent disk bloat
  - [Plugin Cleanup](claude-ai-plugins-connectors-cleanup-sept.md) — connectors status
  ```
- **Metadata Frontmatter:** Most detailed topic files contain metadata headers:
  ```markdown
  ---
  name: claude-ai-plugins-connectors-cleanup-sept
  description: "Status of claude.ai plugins..."
  metadata:
    node_type: memory
    type: reference
    originSessionId: 18c0f339-b395-472e-b153-4f3423345820
    modified: 2026-09-06T14:47:49.090Z
  ---
  ```

### 2.3 Difference Between `CLAUDE.md` and `MEMORY.md`
| Feature | `CLAUDE.md` | `MEMORY.md` |
| :--- | :--- | :--- |
| **Author** | Developer (explicit instructions) | Claude Code / Agent (autonomous capture) |
| **Version Control** | Frequently tracked in Git | Almost always untracked / local |
| **Purpose** | Rigid architectural & execution rules | Dynamic project learnings, workarounds, context |
| **Location** | Repo root or `.claude/CLAUDE.md` | `~/.claude/projects/<slug>/memory/` |

---

## 3. Deep Dive: Subagent Memory (`.claude/agent-memory/`)

When multi-agent orchestration is enabled, subagents maintain independent knowledge bases partitioned by agent role to prevent cognitive pollution across domains (e.g., QA tester vs. Security reviewer vs. Tech lead).

### 3.1 Directory Structure
```text
.claude/
└── agent-memory/
    ├── qa-browser-tester/
    │   ├── MEMORY.md
    │   ├── ada666-fix-builtin-tools.md
    │   └── credenciales-ausentes.md
    ├── security-reviewer/
    │   ├── MEMORY.md
    │   └── project_ada641_egress_guard.md
    └── tech-lead/
        ├── MEMORY.md
        └── feedback_estimacion_dias.md
```

### 3.2 Discovery & Backup Requirements
- Subagent memory files are stored either in workspace repos (`<repo>/.claude/agent-memory/`) or globally (`~/.claude/agent-memory/`).
- Must be scanned up to 3 levels deep (`agent-memory/<agent-role>/<file>.md`).
- Must be sanitized using infrastructure secret filters (regex pattern checks) before cloud synchronization.

---

## 4. Claude Desktop Tools Integration Strategy

### 4.1 Discovery Engine
1. **Adjust Directory Skipping:** Allow `memory` folders located inside `.claude/projects/` and `.claude/agent-memory/`.
2. **Collect Categories:**
   - `.claude/agent-memory/` -> Categorized as `ClaudeDiscoveryCategory.AgentMemory` ("Memoria de Agente").
   - `~/.claude/projects/<slug>/memory/` -> Categorized as `ClaudeDiscoveryCategory.ProjectMemory` ("Memoria de Proyecto").
3. **Category Filters & Display Order:**
   - Extend `ClaudeDiscoveryCategory.DisplayOrder` with `AgentMemory` and `ProjectMemory`.
   - UI views automatically render checkboxes and "Select Only" buttons for these new categories.

### 4.2 Drive Sync Mapping Invariants
- **Repo-scoped `agent-memory`:**
  - Destination: `<destinationPrefix>/<repo-name>/agent-memory/<agent-role>/<filename>.md`
- **Global `agent-memory`:**
  - Destination: `<destinationPrefix>/_claude-config/agent-memory/<agent-role>/<filename>.md`
- **Project-scoped memory (`projects/<slug>/memory/`):**
  - Destination: `<destinationPrefix>/_claude-projects/<slug>/memory/<filename>.md`
  - Guarantees zero collisions between different projects with similarly named memory notes (`MEMORY.md`, `feedback_*.md`).

---

## 5. References & Documentation
- Anthropic Claude Code Documentation: https://claude.ai
- Auto Memory System & Dreaming Specs: https://claude-dev.tools
- Local Path Specification: Anthropic CLI filesystem layout (`~/.claude/projects/`)
