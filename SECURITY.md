# Security

## Scope of this project

WinDirCleaner is focused on **read-only** inspection of storage (drive and folder summaries). It does not delete files, run cleanup jobs, or modify disk contents for analysis.

## Current limitations (relevant to reporting)

- There is **no deletion feature** today.
- NTFS-related menus are **local diagnostics** (volume/USN/OpenFileById experiments). They do not upload data.
- The app does **not** perform network transmission or telemetry as part of its design.
- **Demo mode** (checkbox in the UI) shows static sample text and lists only; it does **not** open volumes or enumerate disks for that preview.

## Reporting a security concern

Please open a **public GitHub issue** with:

- A short description of the concern
- Steps to reproduce (if applicable)
- Windows version and app version or **git commit hash**
- Which feature was in use (e.g. normal folder analysis vs NTFS diagnostics)

**Redact** user names, full personal paths, and sensitive folder or file names in screenshots or logs. Use `C:\Users\user\...` or `X:\path\to\folder` style placeholders when possible.

Do **not** attach secrets (tokens, passwords, connection strings).

Private security contact email is not listed for this repository; use GitHub issues unless that changes in the future.
