# Roadmap

This file describes direction and scope. It is not a release promise.

## Current focus

- Read-only storage overview
- **Cleanup candidate preview** (risk labels + protected dangerous rows; no deletion)
- Stable analysis UX (drive top-level enumeration only)
- **Demo mode** for safe screenshots (no disk access, static sample data)
- NTFS diagnostics as **experimental** tools (not a replacement for normal enumeration)

## Near-term

- Candidate detection rules (read-only heuristics; still no destructive actions)
- Dry-run design (separate UX and safety review)
- Deletion safety review (if ever considered)
- Polish UI layout for preview vs analysis vs NTFS sections
- Improve NTFS diagnostic reporting (clarity, errors, progress wording)
- Compare fast-scan diagnostics against normal scan results where useful
- Add curated screenshots under `docs/screenshots/` (prefer demo mode captures)

## Research

- NTFS file-size strategy (USN, handles, MFT attributes)
- Raw MFT attribute parsing feasibility
- Accuracy vs normal directory enumeration
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
