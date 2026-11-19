using System;
using System.IO;
using System.Text;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

// Scan incomplete lines once, retaining offsets and a small boundary anchor only.
// A completed candidate is read once more for decoding, bounded by the existing
// JSON character contract. No journal text is retained between calls.
internal sealed class CompletionCursor {
    internal const int ScanBudget = 256 * 1024;
    internal const int MaxChars = 16777216;
    internal const int MaxLineBytes = MaxChars * 3 + 4; // UTF-8 plus optional BOM and CR
    static readonly byte[][] Markers = {
        Encoding.ASCII.GetBytes("\"task_complete\""),
        Encoding.ASCII.GetBytes("\"item_completed\""),
        Encoding.ASCII.GetBytes("\"UserMessage\""),
        Encoding.ASCII.GetBytes("\"user_message\"")
    };
    internal long Position { get; private set; }
    long lineStart;
    bool ignoreLine, candidate;
    readonly int[] markerMatched = new int[Markers.Length];
    string identity;
    bool bound;
    Stream memoryIdentity;
    byte[] anchor = new byte[0];

    [StructLayout(LayoutKind.Sequential)]
    struct FileInformation {
        internal uint Attributes, CreationLow, CreationHigh, AccessLow, AccessHigh;
        internal uint WriteLow, WriteHigh, Volume, SizeHigh, SizeLow, Links, IndexHigh, IndexLow;
    }
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool GetFileInformationByHandle(SafeFileHandle handle, out FileInformation info);

    internal CompletionCursor(Stream stream, bool skipHistory) {
        Bind(stream);
        if (skipHistory) {
            Position = lineStart = stream.Length;
            anchor = ReadAnchor(stream, Position);
            ignoreLine = anchor.Length > 0 && anchor[anchor.Length - 1] != (byte)'\n';
        }
    }
    void Reset() {
        Position = lineStart = 0;
        candidate = ignoreLine = false;
        Array.Clear(markerMatched, 0, markerMatched.Length);
        anchor = new byte[0];
    }
    void Bind(Stream stream) {
        var file = stream as FileStream;
        string next = null;
        if (file != null) {
            FileInformation info;
            if (!GetFileInformationByHandle(file.SafeFileHandle, out info))
                throw new IOException("Cannot identify completion journal.", new Win32Exception(Marshal.GetLastWin32Error()));
            next = info.Volume + ":" + info.IndexHigh + ":" + info.IndexLow + ":" + info.CreationHigh + ":" + info.CreationLow;
        }
        if (bound && (identity != next || (next == null && !Object.ReferenceEquals(memoryIdentity, stream)))) Reset();
        bound = true; identity = next; memoryIdentity = file == null ? stream : null;
    }
    static byte[] ReadAnchor(Stream stream, long position) {
        byte[] value = new byte[(int)Math.Min(64, position)];
        stream.Position = position - value.Length;
        int read = 0;
        while (read < value.Length) {
            int count = stream.Read(value, read, value.Length - read);
            if (count == 0) throw new EndOfStreamException();
            read += count;
        }
        return value;
    }
    static bool Matches(Stream stream, long position, byte[] expected) {
        if (stream.Length < position) return false;
        byte[] actual = ReadAnchor(stream, position);
        if (actual.Length != expected.Length) return false;
        for (int i = 0; i < actual.Length; i++) if (actual[i] != expected[i]) return false;
        return true;
    }
    void Retain(byte[] bytes, int count) {
        int added = Math.Min(count, 64), kept = Math.Min(anchor.Length, 64 - added);
        byte[] next = new byte[kept + added];
        Buffer.BlockCopy(anchor, anchor.Length - kept, next, 0, kept);
        Buffer.BlockCopy(bytes, count - added, next, kept, added);
        anchor = next;
    }
    internal string Read(Stream stream, out bool more, out string skipped) {
        more = false; skipped = null;
        Bind(stream);
        if (!Matches(stream, Position, anchor)) Reset();
        long savedPosition = Position, savedStart = lineStart;
        bool savedIgnore = ignoreLine, savedCandidate = candidate;
        int[] savedMatched = (int[])markerMatched.Clone();
        byte[] savedAnchor = anchor;
        try {
            stream.Position = Position;
            int remaining = (int)Math.Min(ScanBudget, Math.Max(0, stream.Length - Position));
            byte[] bytes = new byte[4096];
            bool ended = false;
            while (remaining > 0 && !ended) {
                int count = stream.Read(bytes, 0, Math.Min(bytes.Length, remaining));
                if (count == 0) break;
                remaining -= count;
                int used = count;
                for (int i = 0; i < count; i++) {
                    if (bytes[i] == (byte)'\n') { used = i + 1; ended = true; break; }
                    if (!candidate && !ignoreLine) {
                        byte current = bytes[i];
                        for (int m = 0; m < Markers.Length; m++) {
                            byte[] marker = Markers[m];
                            int matched = markerMatched[m];
                            if (current == marker[matched]) matched++;
                            else matched = current == marker[0] ? 1 : 0;
                            if (matched == marker.Length) { candidate = true; Array.Clear(markerMatched, 0, markerMatched.Length); break; }
                            markerMatched[m] = matched;
                        }
                    }
                }
                Position += used;
                Retain(bytes, used);
            }
            string line = null;
            if (ended) {
                long length = Position - lineStart - 1;
                if (!ignoreLine && length > MaxLineBytes) skipped = "byte-limit";
                else if (!ignoreLine && candidate) line = Decode(stream, lineStart, length, out skipped);
                lineStart = Position; candidate = ignoreLine = false; Array.Clear(markerMatched, 0, markerMatched.Length);
            }
            // Short anchors detect common truncate/regrow races, not arbitrary
            // rewrites which deliberately preserve the checked boundaries.
            if (!Matches(stream, Position, anchor) || !Matches(stream, savedPosition, savedAnchor)) {
                Reset(); line = null; skipped = null;
            }
            more = Position < stream.Length;
            return line;
        } catch {
            Position = savedPosition; lineStart = savedStart;
            ignoreLine = savedIgnore; candidate = savedCandidate; Array.Copy(savedMatched, markerMatched, markerMatched.Length); anchor = savedAnchor;
            throw;
        }
    }
    static string Decode(Stream stream, long start, long length, out string skipped) {
        skipped = null;
        stream.Position = start;
        var text = new StringBuilder();
        var decoder = Encoding.UTF8.GetDecoder();
        byte[] bytes = new byte[4096]; char[] chars = new char[4097];
        while (length > 0) {
            int count = stream.Read(bytes, 0, (int)Math.Min(bytes.Length, length));
            if (count == 0) throw new EndOfStreamException();
            length -= count;
            int decoded = decoder.GetChars(bytes, 0, count, chars, 0, length == 0);
            if (text.Length > MaxChars + 2 - decoded) { skipped = "character-limit"; return null; }
            text.Append(chars, 0, decoded);
        }
        string line = text.ToString().TrimStart('\uFEFF');
        if (line.EndsWith("\r", StringComparison.Ordinal)) line = line.Substring(0, line.Length - 1);
        if (line.Length > MaxChars) { skipped = "character-limit"; return null; }
        return line;
    }
}
