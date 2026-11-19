using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

// Per-file state retains partial UTF-8 lines without rereading them. Work and
// memory are bounded even for a producer writing a megabyte without a newline.
internal sealed class NavigationCursor {
    internal const int ReadBudget = 256 * 1024;
    internal const int LineLimit = 64 * 1024;
    internal long Position;
    readonly MemoryStream pending = new MemoryStream();
    bool discarding;
    const int AnchorLimit = 64;
    readonly byte[] anchor = new byte[AnchorLimit];
    readonly byte[] check = new byte[AnchorLimit];
    int anchorLength;
    bool bound;
    string fileIdentity;
    Stream nonFileStream;

    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation {
        internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh;
        internal uint WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation information);

    internal NavigationCursor(long position) { Position = position; }
    // Capture identity and the complete-line boundary while the startup handle
    // is still open. A later replacement must not inherit that file's seed.
    internal NavigationCursor(Stream stream, long position) : this(position) {
        Bind(stream);
        if (stream.Length < Position) { Reset(); return; }
        anchorLength = (int)Math.Min(Position, AnchorLimit);
        stream.Position = Position - anchorLength;
        int count = 0;
        while (count < anchorLength) {
            int read = stream.Read(anchor, count, anchorLength - count);
            if (read == 0) throw new EndOfStreamException("Navigation seed changed during initialization.");
            count += read;
        }
    }
    internal NavigationCursor(Stream stream, long position, bool discardPartial) : this(stream, position) {
        // A startup tail beyond the line limit has no retained prefix. Its
        // future suffix must be ignored through the first newline as well.
        discarding = discardPartial;
    }

    void Reset() {
        Position = 0;
        pending.SetLength(0);
        discarding = false;
        anchorLength = 0;
    }
    void Bind(Stream stream) {
        var file = stream as FileStream;
        string identity = null;
        if (file != null) {
            FileInformation info;
            if (!GetFileInformationByHandle(file.SafeFileHandle, out info))
                throw new IOException("Cannot identify navigation file.", new Win32Exception(Marshal.GetLastWin32Error()));
            identity = info.Volume + ":" + info.IndexHigh + ":" + info.IndexLow + ":" + info.CreationHigh + ":" + info.CreationLow;
        }
        if (bound && (identity != fileIdentity || (identity == null && !Object.ReferenceEquals(stream, nonFileStream)))) Reset();
        bound = true;
        fileIdentity = identity;
        nonFileStream = file == null ? stream : null;
    }
    bool AnchorMatches(Stream stream) {
        return AnchorMatches(stream, Position, anchor, anchorLength);
    }
    bool AnchorMatches(Stream stream, long position, byte[] expected, int length) {
        if (stream.Length < position) return false;
        if (length == 0) return true;
        stream.Position = position - length;
        int count = 0;
        while (count < length) {
            int read = stream.Read(check, count, length - count);
            if (read == 0) return false;
            count += read;
        }
        for (int i = 0; i < length; i++) if (check[i] != expected[i]) return false;
        return true;
    }
    void RetainAnchor(byte[] bytes, int count) {
        int added = Math.Min(count, AnchorLimit);
        int kept = Math.Min(anchorLength, AnchorLimit - added);
        if (kept > 0) Buffer.BlockCopy(anchor, anchorLength - kept, anchor, 0, kept);
        Buffer.BlockCopy(bytes, count - added, anchor, kept, added);
        anchorLength = kept + added;
    }

    internal string Read(Stream stream) {
        Bind(stream);
        // Length alone misses truncate-and-regrow between two notifications.
        // The bounded anchor detects changed bytes, not every possible rewrite.
        if (!AnchorMatches(stream)) Reset();
        stream.Position = Position;
        // Snapshot the bound; concurrent appends are read on the next notification.
        long remaining = Math.Min(ReadBudget, stream.Length - Position);
        if (remaining <= 0) return "";
        long savedPosition = Position;
        byte[] savedPending = pending.ToArray();
        byte[] savedAnchor = (byte[])anchor.Clone();
        int savedAnchorLength = anchorLength;
        bool savedDiscarding = discarding;
        byte[] bytes = new byte[4096];
        var complete = new StringBuilder();
        try {
        while (remaining > 0) {
            int count = stream.Read(bytes, 0, (int)Math.Min(bytes.Length, remaining));
            if (count == 0) break;
            Position += count; remaining -= count;
            RetainAnchor(bytes, count);
            for (int i = 0; i < count; i++) {
                byte value = bytes[i];
                if (value == (byte)'\n') {
                    if (!discarding) complete.Append(Encoding.UTF8.GetString(pending.ToArray()).TrimStart('\uFEFF')).Append('\n');
                    pending.SetLength(0); discarding = false;
                } else if (!discarding) {
                    if (pending.Length >= LineLimit) { pending.SetLength(0); discarding = true; }
                    else pending.WriteByte(value);
                }
            }
        }
        // Verify consumed bytes, rather than taking a fresh anchor from a file
        // that might already have changed. Retry a detected rewrite next time.
        if (!AnchorMatches(stream) || !AnchorMatches(stream, savedPosition, savedAnchor, savedAnchorLength)) { Reset(); return ""; }
        return complete.ToString();
        } catch {
            // A failed read must not consume complete events which were never
            // returned to the caller. Keep this retry state bounded as well.
            Position = savedPosition;
            pending.SetLength(0); pending.Write(savedPending, 0, savedPending.Length);
            Buffer.BlockCopy(savedAnchor, 0, anchor, 0, AnchorLimit);
            anchorLength = savedAnchorLength;
            discarding = savedDiscarding;
            throw;
        }
    }

    internal static bool SelfTest() {
        byte[] line = Encoding.UTF8.GetBytes("你好 ownerRoutePath=/ windowId=1\r\n");
        for (int split = 1; split < line.Length; split++) using (var stream = new MemoryStream()) {
            var cursor = new NavigationCursor(0);
            stream.Write(line, 0, split);
            if (cursor.Read(stream) != "" || cursor.Position != split) return false;
            stream.Position = stream.Length; stream.Write(line, split, line.Length - split);
            if (cursor.Read(stream) != Encoding.UTF8.GetString(line) || cursor.Read(stream) != "") return false;
        }
        using (var stream = new MemoryStream()) {
            byte[] huge = new byte[ReadBudget * 4]; for (int i = 0; i < huge.Length; i++) huge[i] = (byte)'x';
            stream.Write(huge, 0, huge.Length); var cursor = new NavigationCursor(0);
            for (int i = 1; i <= 4; i++) if (cursor.Read(stream) != "" || cursor.Position != ReadBudget * i) return false;
            if (cursor.pending.Length > LineLimit || cursor.Read(stream) != "") return false;
            stream.Position = stream.Length; byte[] tail = Encoding.UTF8.GetBytes("\nvalid\n"); stream.Write(tail, 0, tail.Length);
            if (cursor.Read(stream) != "valid\n") return false;
            stream.SetLength(0); stream.Position = 0; stream.Write(line, 0, line.Length);
            if (cursor.Read(stream) != Encoding.UTF8.GetString(line)) return false;
        }
        return true;
    }
}
