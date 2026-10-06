# Security policy

Please do not post a suspected vulnerability or unredacted logs in a public
issue. After the GitHub repository is published, use its private vulnerability
reporting feature if enabled; otherwise contact the maintainer privately.

The companion reads Codex window metadata and selected local Codex log files to
detect new-chat navigation. Its own diagnostic log is written to the current
user's `%USERPROFILE%\.codex-boot-animation` directory, shared by packaged hooks
and the independent companion. Review and redact logs before sharing.
It uses a Windows low-level **mouse** hook only to detect release on the
identified New chat button; it does not install a keyboard hook or record
general mouse activity in its diagnostic log. The prebuilt executable is
unsigned; users can compile it from `src/*.cs` and inspect the source
before running it.

The resident named pipe is scoped to the current Windows user and session.
Its explicit protected ACL permits only that user, and command reads have a
1-second absolute deadline and a 256-byte limit. This protects optional animation
delivery from stalled clients; it is not a sandbox against other programs already
running as the same user. Media decoding remains the Windows media stack's job.
