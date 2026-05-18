# Roadmap

This file describes direction and scope. It is not a release promise.

## Current focus

- Read-only cleanup candidate detection (safe paths only; refresh button)
- Candidate size estimation for allowed folders (no deletion)
- **Cleanup candidate preview** (risk labels + protected dangerous rows; no deletion)
- **Read-only cleanup preview** (counts + estimated size + sample paths; no deletion)
- Keep destructive actions disabled (no cleanup run, no delete execution)
- Stable analysis UX (drive top-level enumeration only)
- **Demo mode** for safe screenshots (no disk access for candidate refresh; sample cleanup preview only)
- NTFS diagnostics as **experimental** tools (not a replacement for normal enumeration)
- **NTFS path mapping probe** for selected cleanup rows (mapping check only; not used for sizes or dry-run)

## Near-term

- Keep cleanup preview Directory-based (stable default)
- Add diagnostics before NTFS integration (path mapping PoC, accuracy notes)
- Cleanup preview result refinement (wording, caps, overlap handling)
- Deletion safety design (if ever considered)
- Confirmation and logging design (if deletion is ever considered)
- Polish UI layout for preview vs analysis vs NTFS sections
- Improve NTFS diagnostic reporting (clarity, errors, progress wording)
- Compare fast-scan diagnostics against normal scan results where useful
- Add curated screenshots under `docs/screenshots/` (prefer demo mode captures)

## Research

- NTFS path mapping for cleanup candidates (USN/FRN vs user-visible paths)
- Candidate size aggregation strategy (OpenFileById vs MFT vs Directory)
- Accuracy comparison vs normal directory enumeration and Directory-based cleanup preview
- Fallback design when NTFS mapping or size reads fail
- NTFS fast scan size strategy (USN, handles, MFT attributes)
- Raw MFT attribute parsing feasibility
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
