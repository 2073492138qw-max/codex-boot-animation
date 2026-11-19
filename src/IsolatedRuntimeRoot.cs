using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Shared by the executable and installation fixtures. This is a guard against
// accidental test writes, not a boundary against concurrent filesystem attacks.
public static class BootAnimationTestRoot {
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern uint GetLongPathName(string path, StringBuilder result, uint size);

    static string Absolute(string path) {
        // Path.IsPathRooted also accepts C:relative and \relative on Windows.
        if (String.IsNullOrWhiteSpace(path) || path.Length < 3 ||
            !Char.IsLetter(path[0]) || path[1] != ':' || (path[2] != '\\' && path[2] != '/'))
            throw new ArgumentException("A test root must be an absolute local drive path.");
        foreach (string part in path.Substring(3).Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
            if (part.EndsWith(".", StringComparison.Ordinal) || part.EndsWith(" ", StringComparison.Ordinal) ||
                part.IndexOfAny(new[] { ':', '*', '?', '\"', '<', '>', '|' }) >= 0)
                throw new ArgumentException("Ambiguous test root path component.");
        return Path.GetFullPath(path).TrimEnd('\\', '/');
    }

    static bool Attributes(string path, out FileAttributes attributes) {
        try { attributes = File.GetAttributes(path); return true; }
        catch (FileNotFoundException) { attributes = 0; return false; }
        catch (DirectoryNotFoundException) { attributes = 0; return false; }
    }

    static bool Related(string left, string right) {
        left += "\\"; right += "\\";
        return left.StartsWith(right, StringComparison.OrdinalIgnoreCase) ||
            right.StartsWith(left, StringComparison.OrdinalIgnoreCase);
    }

    static string ExpandExisting(string path) {
        string existing = path, suffix = "";
        FileAttributes attributes;
        while (!Attributes(existing, out attributes)) {
            suffix = Path.Combine(Path.GetFileName(existing), suffix);
            existing = Path.GetDirectoryName(existing);
            if (String.IsNullOrEmpty(existing)) throw new IOException("Test root has no accessible ancestor.");
        }
        var expanded = new StringBuilder(32768);
        uint length = GetLongPathName(existing, expanded, (uint)expanded.Capacity);
        if (length == 0) throw new Win32Exception(Marshal.GetLastWin32Error());
        if (length >= expanded.Capacity) throw new PathTooLongException();
        return Path.Combine(expanded.ToString(), suffix).TrimEnd('\\', '/');
    }

    static void RejectLinks(string path, bool children, ref int budget) {
        if (--budget < 0) throw new IOException("Test root validation exceeded its entry budget.");
        FileAttributes attributes;
        if (!Attributes(path, out attributes)) return;
        if ((attributes & FileAttributes.ReparsePoint) != 0)
            throw new IOException("A test root cannot contain or traverse a reparse point.");
        if (children && (attributes & FileAttributes.Directory) != 0)
            foreach (string child in Directory.EnumerateFileSystemEntries(path)) RejectLinks(child, true, ref budget);
    }

    public static string Validate(string path, string realRoot) {
        string full = Absolute(path), real = Absolute(realRoot);
        if (full.Length <= 2 || Related(full, real))
            throw new InvalidOperationException("Test root overlaps the real runtime root or a drive root.");
        // Inspect every ancestor before expanding 8.3 aliases or creating anything.
        int budget = 4096;
        for (string parent = full; !String.IsNullOrEmpty(parent); parent = Path.GetDirectoryName(parent))
            RejectLinks(parent, false, ref budget);
        full = ExpandExisting(full);
        if (Related(full, ExpandExisting(real)))
            throw new InvalidOperationException("Test root aliases the real runtime root.");
        FileAttributes attributes;
        if (Attributes(full, out attributes) && (attributes & FileAttributes.Directory) == 0)
            throw new IOException("Test root must be a directory.");
        // A safe root with a linked turns/disabled/settings child is unsafe too.
        RejectLinks(full, true, ref budget);
        return full;
    }
}
