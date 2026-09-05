# Technical Research: Vanilla Developer Product Landing Page Architecture

> **Created:** 2026-09-04  
> **Last Updated:** 2026-09-04  
> **Topic:** High-Performance Vanilla HTML/CSS/JS Product Landing Page for Claude Desktop Tools  
> **Status:** Completed  

---

## 1. Executive Summary

This research benchmarks modern developer tool landing pages (e.g., Raycast, Warp, Zed, Tailwind, Supabase) and establishes the architectural and visual standards for creating a zero-dependency, ultra-fast `index.htm` product page for **Claude Desktop Tools**.

The landing page must showcase:
- **Product Proposition:** Autonomous desktop workstation hub for Claude Code (CLI) and Claude Desktop on Windows 11.
- **Safety Invariants:** 24h grace window, process collision lock (`claude.exe`), surgical in-place header mutation, secret scanning, SHA-256 cloud deduplication.
- **Developer Experience:** Direct installer download (`.exe`), source code clone commands, interactive savings calculator, interactive UI preview mockup (Fluent/Mica aesthetic), and collapsible technical FAQ.

---

## 2. Technical Pillars & Design Patterns

### 2.1 Color Palette & Theme Engine
- **Dark Mode Default with System Preference Fallback:**
  - Deep dark background: `#0d1117` / `#161b22` (GitHub / Windows Terminal inspired dark tones) to eliminate eye strain.
  - Accent colors: Electric Claude Coral (`#D97706` / `#E06C75` / `#F97316` blend) and Microsoft Fluent Blue (`#0078D4` / `#60A5FA`).
  - Text: High contrast readable typography (`#F0F6FC` primary, `#8B949E` muted) exceeding WCAG AA standards (4.5:1 ratio).
- **CSS Custom Properties (Zero-Dependency):**
  - All styling variables defined under `:root` with `[data-theme="light"]` overrides.
  - Inline head script checks `localStorage` or `window.matchMedia('(prefers-color-scheme: dark)')` to prevent Flash of Unstyled Theme (FOUT).

### 2.2 Interactive Components (Vanilla JS)
1. **Live UI Simulator / Feature Tabs:**
   - Visual replica of the WinUI 3 Fluent app window with simulated Mica backdrop and titlebar controls.
   - Tab switching between:
     - *Overview Dashboard* (Disk reclaimed gauge, active sessions count).
     - *Storage Reclamation* (Unbounded transcripts audit & 24h safe pruning).
     - *Session Index Pruning* (Atomic header mutation without file rewrites).
     - *Context Discovery & Secret Scanning* (Multi-zone BFS, SSH/API token protection).
     - *Google Drive Sync* (SHA-256 integrity verification).
2. **Interactive Disk Space & Session Savings Estimator:**
   - Range slider for weekly prompt activity / number of repos.
   - Dynamic real-time calculation of estimated disk space reclaimed (GB) and sessions organized.
3. **One-Click Terminal Command Copy:**
   - Copy snippets for git clone, dotnet build, and winget / direct installer download with visual checkmark feedback.
4. **Accessible Collapsible FAQ Accordion:**
   - Pure vanilla event delegation with keyboard accessibility (`Enter` / `Space` toggle) and smooth height transitions.

### 2.3 Performance & Zero External Asset Fallbacks
- **Zero Heavy CDNs:** Inline SVG icons for Windows, GitHub, Terminal, Shield, Cloud, Lock, and Arrow indicators.
- **Single File Self-Contained:** Can be opened directly via `file:///` protocol without needing a local Node/Vite server.
- **SEO & Social Meta Tags:** OpenGraph, Twitter Cards, and schema markup for developer portfolio indexing.

---

## 3. References & Benchmarks
- [GitHub Primer Design System](https://primer.style/)
- [Microsoft Fluent 2 Web Specifications](https://fluent2.microsoft.design/)
- [Web Accessibility Guidelines (WCAG 2.1)](https://www.w3.org/WAI/standards-guidelines/wcag/)
