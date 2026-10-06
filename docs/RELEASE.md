# Release readiness

Current version: **0.7.0-alpha.5**, Windows-only community preview.
It can be published to GitHub as an **alpha/prerelease**, not advertised as a
stable Codex extension on every platform. No remote repository has been created
or pushed by the local packaging workflow.

Alpha.5 packages the accepted completion-delivery fix (`f57bafb`) and its
frozen executable without rebuilding or changing the live installation.
Earlier Alpha.1–Alpha.4 ZIPs are retained. Local archive/extracted/isolated-install
verification is tracked in [the Alpha.5 check record](ALPHA5-PACKAGE-CHECK-2026-10-06.md);
read that record for the actual result instead of assuming all checks passed.

Alpha.5 local verification passed on 2026-10-06: both PowerShell versions each
passed 40 extracted-package groups, both non-mutating installer preflights
passed, and isolated plugin installation/cache/three untrusted hooks passed.
The immutable ZIP comes from `ead63ed`; the `v0.7.0-alpha.5` tag pins that
commit. Later report-only commits are not silently repacked into the same ZIP.
Distribute the ZIP with its `.sha256` and `.verify.md` sidecars. The current
installation and prior ZIPs were preserved. No upload has been performed.

## Maintainer workflow

1. Review source, license/catalog additions and ignored personal files.
2. Build with `scripts/Build.ps1`; run `scripts/Test.ps1` using Windows
   PowerShell 5.1 as well as any newer development shell.
3. Update the native plugin manifest, portable documentation example and changelog together. Commit the
   reviewed executable, source and licensed media; keep the Git tree clean.
4. Run `scripts/Package-Release.ps1`. It archives the immutable Git commit, not
   arbitrary working-directory files. ZIP and SHA-256 are generated under `dist/`.
5. Extract that ZIP to a permanent-looking path with spaces and non-ASCII
   characters. Run the extracted `scripts/Test.ps1` and `Install.ps1 -CheckOnly`.
   The latter must not alter registration, autostart or trust.
   Separately verify a genuine task-completion record with foreground Codex
   using `scripts/Verify-Completion.ps1`; unit tests or a tray preview alone
   are not evidence that task-end detection and delivery work.
   Run `scripts/Verify-CleanInstall.ps1` from the extracted package when a
   desktop CLI is available: it installs into a new, isolated Codex home in the
   Windows temporary folder and verifies the cached package without changing the real
   user config, startup or hook trust. This remains a same-PC integration check.
6. Before a stable release, complete `INSTALLATION.md`'s manual acceptance
   checklist on an independent Windows account/PC, including sign-in/reboot,
   hook trust, two new-chat clicks, tray exit, each video scene, and uninstallation.
7. Create a GitHub repository, upload code and an alpha GitHub Release only
   after checking rights/privacy. Retain media credits and mark the release
   prerelease. Do not upload runtime logs, old binaries or private extra clips.

## Known boundaries

- Earlier visual acceptance: packaged Windows Codex 26.924.6891.0; earlier
  package checks: desktop 26.928.2636.0 and CLI 0.159.2. Alpha.5 local checks
  use desktop 26.930.3930.0 / CLI 0.160.0 (results in the Alpha.5 record). Other app packaging
  and macOS/Linux are not supported yet.
- New chat UI names and navigation/session logs are internal app details, not
  stable extension events. Re-test after Codex updates.
- Completion and emotion filters are conservative heuristics and can miss
  valid events. They do not determine task success or sentiment perfectly.
- The helper is a separate owned/native window, not embedded Codex UI. Cold
  startup speed depends on Windows process startup/video decoding; literal
  zero latency is not guaranteed.
- Prebuilt EXE is unsigned. Windows may ask users to review it; instructions
  never disable system security or silently approve Codex hooks.
- Companion-only updates preserve personal videos and plugin trust; changing
  hooks or moving directories requires an explicit install/migration.
- Current-PC tests and extracted-package checks are not proof of a real
  second-PC or post-reboot run. These remain release acceptance work.
