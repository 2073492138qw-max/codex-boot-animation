# Contributing

Thanks for helping improve the project. Open an issue describing the behavior
and affected Codex/Windows versions before large changes. Keep changes focused,
include a reproducible test where possible, and run:

```powershell
.\scripts\Build.ps1
.\scripts\Test.ps1
```

For playback or click-detection changes, also manually test a cold Codex start,
two consecutive new-chat clicks, Escape/Skip, and a complete Codex exit and
restart. Do not submit personal logs, machine-specific paths, credentials, or
unlicensed media. The code and media have different licenses; see `LICENSE` and
`plugins/codex-boot-animation/videos/LICENSE.md`.
