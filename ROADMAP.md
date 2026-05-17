# Roadmap

This file describes direction and scope. It is not a release promise.

## Current focus

- Read-only storage overview
- Stable analysis UX
- **Demo mode** for safe screenshots (no disk access, static sample data)
- NTFS diagnostics as **experimental** tools (not a replacement for normal enumeration)

## Near-term

- 공개용 스크린샷 정리 및 데모 모드 안내 보강
- 실험 NTFS 진단과 일반 상세 분석 기능 구분 유지
- Polish UI layout
- Improve NTFS diagnostic reporting (clarity, errors, progress wording)
- Compare fast-scan diagnostics against normal scan results where useful
- Add curated screenshots under `docs/screenshots/` (prefer demo mode captures)

## Research

- NTFS file-size strategy (USN, handles, MFT attributes)
- Raw MFT attribute parsing feasibility
- Accuracy vs normal directory enumeration
- Hard links, reparse points, and special-file edge cases

## Not planned for now

- Automatic deletion or “cleanup run” automation
- Registry cleaning
- Memory cleaning
- DriverStore cleanup
- WinSxS cleanup
- Background resident service
- Startup registration
- Network telemetry

Deletion or automated cleanup would need a separate safety design, UX, and review before appearing on any roadmap section above.
