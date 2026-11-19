using System;
using System.IO;
using System.Text;
using System.Threading;

internal static class NavigationLogStartup {
    internal const int HeadBudget = 512 * 1024, TailBudget = 64 * 1024;

    internal static NavigationCursor Seed(Stream stream) {
        bool discard;
        long offset = NavigationLogReader.InitialOffset(stream, out discard);
        return new NavigationCursor(stream, offset, discard);
    }

    // Window sizes are byte budgets, not character counts checked after ReadLine.
    // Never present a clipped prefix/suffix or over-limit line to the predicate.
    internal static bool HasReadyLine(Stream stream, Func<string, bool> matches) {
        long length = stream.Length;
        if (Scan(stream, 0, Math.Min(length, HeadBudget), matches)) return true;
        if (length <= HeadBudget) return false;
        long start = Math.Max(0, length - TailBudget);
        return Scan(stream, start, length, matches);
    }
    static bool Scan(Stream stream, long start, long end, Func<string, bool> matches) {
        bool discard = false;
        if (start > 0) {
            stream.Position = start - 1;
            int previous = stream.ReadByte();
            if (previous < 0) throw new EndOfStreamException();
            discard = previous != (byte)'\n';
        }
        stream.Position = start;
        byte[] bytes = new byte[4096];
        using (var line = new MemoryStream()) {
            long remaining = end - start;
            while (remaining > 0) {
                int count = stream.Read(bytes, 0, (int)Math.Min(remaining, bytes.Length));
                if (count == 0) throw new EndOfStreamException();
                remaining -= count;
                for (int i = 0; i < count; i++) {
                    if (bytes[i] == (byte)'\n') {
                        if (!discard && matches(Encoding.UTF8.GetString(line.GetBuffer(), 0, (int)line.Length).TrimStart('\uFEFF').TrimEnd('\r'))) return true;
                        line.SetLength(0); discard = false;
                    } else if (!discard) {
                        if (line.Length >= NavigationCursor.LineLimit) { line.SetLength(0); discard = true; }
                        else line.WriteByte(bytes[i]);
                    }
                }
            }
        }
        return false;
    }

    // Install notifications first. During historical seeding, notifications are
    // coalesced into one catch-up pass; no unbounded startup event queue is kept.
    internal static FileSystemWatcher Start(string root, Action<string> seed,
        Action<string> read, Action<string, Exception> report) {
        var watcher = new FileSystemWatcher(root, "*.log") {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size
        };
        int ready = 0;
        FileSystemEventHandler changed = delegate(object sender, FileSystemEventArgs args) {
            if (Thread.VolatileRead(ref ready) != 0) TryFile(args.FullPath, read, "read", report);
        };
        watcher.Created += changed; watcher.Changed += changed;
        try {
            watcher.EnableRaisingEvents = true;
            Visit(root, seed, "seed", report);
            Thread.VolatileWrite(ref ready, 1);
            Visit(root, read, "catch-up", report);
            return watcher;
        } catch { watcher.Dispose(); throw; }
    }
    static void Visit(string root, Action<string> action, string stage, Action<string, Exception> report) {
        try {
            foreach (string path in Directory.GetFiles(root, "*.log", SearchOption.AllDirectories))
                TryFile(path, action, stage, report);
        } catch (Exception error) { report("enumerate-" + stage, error); }
    }
    static void TryFile(string path, Action<string> action, string stage, Action<string, Exception> report) {
        try { action(path); } catch (Exception error) { report(stage, error); }
    }
}
