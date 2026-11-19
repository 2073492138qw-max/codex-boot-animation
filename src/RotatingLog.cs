using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

// A cross-process lock covers both rotation and append. Keep at most two files.
internal static class RotatingLog {
    internal const int Limit = 2 * 1024 * 1024;

    internal static bool Append(string path, string text, int limit = Limit) {
        string key;
        using (var hash = SHA256.Create())
            key = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(Path.GetFullPath(path).ToUpperInvariant()))).Replace("-", "");
        try {
            using (var gate = new Mutex(false, "Local\\CodexAnimationLog-" + key)) {
                bool acquired = false;
                try {
                    try { acquired = gate.WaitOne(500); }
                    catch (AbandonedMutexException) { acquired = true; }
                    if (!acquired) return false;
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    // Bound a diagnostic record as well as the accumulated file.
                    if (text.Length > 8192) text = text.Substring(0, 8192) + " [truncated]\n";
                    byte[] bytes = new UTF8Encoding(false).GetBytes(text);
                    if (bytes.Length > limit) return false;
                    if (File.Exists(path) && new FileInfo(path).Length + bytes.Length > limit) {
                        string previous = path + ".1";
                        if (File.Exists(previous)) File.Delete(previous);
                        File.Move(path, previous);
                    }
                    using (var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.Read))
                        stream.Write(bytes, 0, bytes.Length);
                    return true;
                } finally { if (acquired) gate.ReleaseMutex(); }
            }
        } catch (IOException) { return false; }
        catch (UnauthorizedAccessException) { return false; }
    }
}
