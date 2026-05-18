# Roadmap

This file describes direction and scope. It is not a release promise.

## Current focus

- Read-only cleanup candidate detection (safe paths only; refresh button)
- Candidate size estimation for allowed folders (no deletion)
- **Cleanup candidate preview** (risk labels + protected dangerous rows; no deletion)
- Keep destructive actions disabled (no dry-run, no cleanup run)
- Stable analysis UX (drive top-level enumeration only)
- **Demo mode** for safe screenshots (no disk access for candidate refresh; static sample data)
- NTFS diagnostics as **experimental** tools (not a replacement for normal enumeration)

## Near-term

- Detection rule refinement (read-only heuristics; still no destructive actions)
- Dry-run design (separate UX and safety review)
- Deletion safety model (if ever considered)
- Polish UI layout for preview vs analysis vs NTFS sections
- Improve NTFS diagnostic reporting (clarity, errors, progress wording)
- Compare fast-scan diagnostics against normal scan results where useful
- Add curated screenshots under `docs/screenshots/` (prefer demo mode captures)

## Research

- NTFS fast scan size strategy (USN, handles, MFT attributes)
- Raw MFT attribute parsing feasibility
- Accuracy comparison vs normal directory enumeration
- Hard links, reparse points, and special-file edge cases

## Not planned for now

- Automatic cleanup or “cleanup run” automation
- Registry cleaning
- Memory cleaning
- DriverStore cleanup
- WinSxS cleanup
- Background resident service
- Startup registration
- Network telemetry

Deletion or automated cleanup would need a separate safety design, UX, and review before appearing on any roadmap section above.
