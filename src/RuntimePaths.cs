using System;
using System.IO;
using System.Threading;

// One user-profile root holds every companion state file. Tests must bind an
// isolated root before any state or log type is touched; the bound root is
// inherited by child processes so a nested fixture cannot fall back to the
// real profile. A rejected binding fails before the first write.
internal static class RuntimePaths {
 internal const string TestRootVariable = "CODEX_BOOT_ANIMATION_TEST_ROOT";
 internal const int RejectedExitCode = 46;
 static string boundRoot;

 internal static string DefaultRoot {
  get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex-boot-animation"); }
 }
 internal static string Root {
  get { string bound = Volatile.Read(ref boundRoot); return bound != null ? bound : DefaultRoot; }
 }
 internal static bool IsBound { get { return Volatile.Read(ref boundRoot) != null; } }

 static string Clean(string path) {
  if (String.IsNullOrWhiteSpace(path)) throw new ArgumentException("A runtime root path is required.");
  return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
 }
 static bool Inside(string candidate, string parent) {
  return (Clean(candidate) + Path.DirectorySeparatorChar).StartsWith(Clean(parent) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
 }
 internal static bool IsRealRoot(string path) {
  try { return String.Equals(Clean(path), Clean(DefaultRoot), StringComparison.OrdinalIgnoreCase); }
  catch { return false; }
 }

 // Rejects anything that could alias the real profile state: the real root
 // itself, a directory below it, or a parent that would contain it.
 internal static void Bind(string path) {
  string full = BootAnimationTestRoot.Validate(path, DefaultRoot);
  Directory.CreateDirectory(full);
  Volatile.Write(ref boundRoot, full);
  // Child fixtures inherit the binding without threading the argument again.
  Environment.SetEnvironmentVariable(TestRootVariable, full);
 }

 // Only a value that already passes Bind is adopted; anything else is refused
 // rather than silently writing to the real profile.
 internal static void AdoptInherited() {
  string inherited = Environment.GetEnvironmentVariable(TestRootVariable);
  if (String.IsNullOrWhiteSpace(inherited)) return;
  Bind(inherited);
 }

 // The optional leading "--test-root <path>" is consumed before any other
 // argument parsing, so no state type can initialize against the real root.
 internal static string[] Apply(string[] args) {
  AdoptInherited();
  if (args == null || args.Length == 0) return new string[0];
  if (!String.Equals(args[0], "--test-root", StringComparison.Ordinal)) return args;
  if (args.Length < 3) throw new ArgumentException("--test-root requires a directory and a mode.");
  Bind(args[1]);
  string[] rest = new string[args.Length - 2];
  Array.Copy(args, 2, rest, 0, rest.Length);
  return rest;
 }

 // Both self-tests and helper fixtures require an isolated root, including
 // diagnostic test modes whose names do not end in -test.
 internal static bool RequiresIsolatedRoot(string mode) {
  if (String.IsNullOrEmpty(mode)) return false;
  if (mode.EndsWith("-test", StringComparison.Ordinal)) return true;
  switch (mode) {
   case "--intro-media-decoder-probe":
   case "--repair-log-child":
   case "--lifetime-parent":
   case "--lifetime-probe":
   case "--test-fail":
   case "--test-hang": return true;
   default: return false;
  }
 }
}
