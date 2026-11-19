using System;
using System.IO;
using System.Text;

internal static class NavigationCursorTests {
    static void Require(bool condition, string name) {
        if (!condition) throw new InvalidOperationException("navigation-generation-" + name);
    }
    static void Write(Stream stream, string text, bool replace) {
        if (replace) { stream.SetLength(0); stream.Position = 0; }
        else stream.Position = stream.Length;
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        stream.Write(bytes, 0, bytes.Length);
        stream.Flush();
    }
    static string ReadFile(NavigationCursor cursor, string path) {
        using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            return cursor.Read(stream);
    }
    sealed class ObservedStream : MemoryStream {
        internal long BytesRead;
        internal long FailAt = -1;
        internal Action BeforeRead;
        public override int Read(byte[] bytes, int offset, int count) {
            if (BeforeRead != null) BeforeRead();
            if (FailAt >= 0 && Position >= FailAt) throw new IOException("synthetic read failure");
            int read = base.Read(bytes, offset, count);
            BytesRead += read;
            return read;
        }
    }
    internal static bool Run() {
        // This failed on the previous implementation: it returned old/EVENT.
        using (var stream = new MemoryStream()) {
            var cursor = new NavigationCursor(0);
            Write(stream, "old/", false);
            Require(cursor.Read(stream) == "", "initial-partial");
            Write(stream, "NEW-EVENT\n", true);
            Require(cursor.Read(stream) == "NEW-EVENT\n", "truncate-regrow-partial");
            Require(cursor.Read(stream) == "", "regrow-duplicate");
        }
        foreach (string fresh in new[] { "x\n", "new\n", "long-new-event\n" }) using (var stream = new MemoryStream()) {
            var cursor = new NavigationCursor(0);
            Write(stream, "old\n", false);
            Require(cursor.Read(stream) == "old\n", "baseline-complete");
            Write(stream, fresh, true);
            Require(cursor.Read(stream) == fresh, "complete-short-equal-long");
        }
        using (var stream = new MemoryStream()) {
            var cursor = new NavigationCursor(0);
            Write(stream, new string('x', NavigationCursor.LineLimit + 1), false);
            Require(cursor.Read(stream) == "", "overlong-baseline");
            Write(stream, "valid\n" + new string('z', NavigationCursor.LineLimit), true);
            Require(cursor.Read(stream) == "valid\n", "reset-discarding");
        }
        using (var stream = new ObservedStream()) {
            var cursor = new NavigationCursor(0);
            Write(stream, "old/", false);
            Require(cursor.Read(stream) == "", "race-baseline");
            Write(stream, "tail\n", false);
            bool changed = false;
            stream.BeforeRead = delegate {
                if (!changed && stream.Position == 4) {
                    changed = true;
                    Write(stream, "NEW-LINE\n", true);
                    stream.Position = 4;
                }
            };
            Require(cursor.Read(stream) == "" && changed, "rewrite-during-read-rejected");
            stream.BeforeRead = null;
            Require(cursor.Read(stream) == "NEW-LINE\n", "rewrite-during-read-retry");
        }
        using (var stream = new ObservedStream()) {
            var cursor = new NavigationCursor(0);
            string text = "first\n" + new string('x', 5000) + "\n";
            Write(stream, text, false);
            stream.FailAt = 4096;
            bool failed = false;
            try { cursor.Read(stream); } catch (IOException) { failed = true; }
            Require(failed && cursor.Position == 0, "failed-read-consumed-events");
            stream.FailAt = -1;
            Require(cursor.Read(stream) == text && cursor.Read(stream) == "", "failed-read-retry");
        }
        using (var stream = new ObservedStream()) {
            var cursor = new NavigationCursor(0);
            Write(stream, new string('x', NavigationCursor.ReadBudget * 2), false);
            Require(cursor.Read(stream) == "" && cursor.Position == NavigationCursor.ReadBudget, "budget-first");
            Require(stream.BytesRead <= NavigationCursor.ReadBudget + 192, "budget-first-io");
            stream.BytesRead = 0;
            Require(cursor.Read(stream) == "" && cursor.Position == NavigationCursor.ReadBudget * 2, "budget-next");
            Require(stream.BytesRead <= NavigationCursor.ReadBudget + 192, "budget-next-io");
            stream.BytesRead = 0;
            Require(cursor.Read(stream) == "" && stream.BytesRead <= 64, "idle-io");
        }
        // Real reopened files: identity must survive a normal close/open, while
        // replacement must be noticed even when the previous byte anchor matches.
        string folder = Path.Combine(Path.GetTempPath(), "codex-navigation-generation-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "active.log");
        try {
            string prefix = new string('p', 100);
            File.WriteAllText(path, prefix, new UTF8Encoding(false));
            var cursor = new NavigationCursor(0);
            Require(ReadFile(cursor, path) == "", "file-partial");
            File.AppendAllText(path, "end\n", new UTF8Encoding(false));
            Require(ReadFile(cursor, path) == prefix + "end\n", "normal-reopen");
            File.AppendAllText(path, "old-tail", new UTF8Encoding(false));
            Require(ReadFile(cursor, path) == "", "file-old-tail");
            File.Move(path, Path.Combine(folder, "previous.log"));
            string replacement = new string('n', 20) + prefix.Substring(20) + "end\nold-tailNEW\n";
            File.WriteAllText(path, replacement, new UTF8Encoding(false));
            Require(ReadFile(cursor, path) == replacement, "replace-with-matching-anchor");
            Require(ReadFile(cursor, path) == "", "replace-duplicate");
            File.AppendAllText(path, "partial", new UTF8Encoding(false));
            Require(ReadFile(cursor, path) == "", "before-delete");
            File.Delete(path);
            File.WriteAllText(path, "recreated\n", new UTF8Encoding(false));
            Require(ReadFile(cursor, path) == "recreated\n", "delete-recreate");
            using (var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete))
                Write(writer, "same-file-rewritten-longer\n", true);
            Require(ReadFile(cursor, path) == "same-file-rewritten-longer\n", "real-truncate-regrow");
            File.WriteAllText(path, "history\npending", new UTF8Encoding(false));
            using (var seedStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                cursor = new NavigationCursor(seedStream, 8);
            Require(ReadFile(cursor, path) == "", "seed-keeps-partial");
            File.AppendAllText(path, "-end\n", new UTF8Encoding(false));
            Require(ReadFile(cursor, path) == "pending-end\n", "seed-skips-history");
            using (var seedStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                cursor = new NavigationCursor(seedStream, seedStream.Length);
            File.Move(path, Path.Combine(folder, "seed-old.log"));
            string sameBoundary = "history\npending-end\n";
            File.WriteAllText(path, sameBoundary, new UTF8Encoding(false));
            Require(ReadFile(cursor, path) == sameBoundary, "replacement-between-seed-and-first-read");
        } finally {
            foreach (string file in Directory.GetFiles(folder)) File.Delete(file);
            Directory.Delete(folder);
        }
        Require(NavigationCursor.SelfTest(), "existing-utf8-and-budget");
        return true;
    }
}
