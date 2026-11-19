using System;
using System.IO;

// Prunes expired transient state files so runtime directories do not grow without bound.
// Never touches videos, user settings, startup policy, logs, or manual backups.
internal static class RuntimeCleanup {
    internal const int KeepSettingsBackups = 10;

    internal static int Prune(string root) {
        return Prune(root, DateTime.UtcNow);
    }

    internal static int Prune(string root, DateTime utcNow) {
        if (String.IsNullOrEmpty(root) || !Directory.Exists(root)) return 0;
        int removed = 0;
        removed += PruneOlderThan(Path.Combine(root, "desktop-instances"), "*.claim", utcNow.AddDays(-14));
        removed += PruneOlderThan(Path.Combine(root, "turns"), "*", utcNow.AddDays(-7));
        removed += PruneOlderThan(Path.Combine(root, "completion-deliveries"), "*", utcNow.AddDays(-7));
        removed += PruneNewest(Path.Combine(root, "settings-backups"), "scene-settings-*.json", KeepSettingsBackups);
        if (removed > 0) IntroLog.Write("runtime-state-pruned count=" + removed);
        return removed;
    }

    static int PruneOlderThan(string folder, string pattern, DateTime cutoffUtc) {
        if (!Directory.Exists(folder)) return 0;
        int removed = 0;
        try {
            foreach (string file in Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly)) {
                try {
                    if (File.GetLastWriteTimeUtc(file) < cutoffUtc) {
                        File.Delete(file);
                        removed++;
                    }
                } catch { }
            }
        } catch { }
        return removed;
    }

    static int PruneNewest(string folder, string pattern, int keep) {
        if (!Directory.Exists(folder)) return 0;
        int removed = 0;
        try {
            string[] files = Directory.GetFiles(folder, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length <= keep) return 0;
            Array.Sort(files, delegate(string a, string b) {
                return File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a));
            });
            for (int i = keep; i < files.Length; i++) {
                try { File.Delete(files[i]); removed++; } catch { }
            }
        } catch { }
        return removed;
    }

    internal static bool SelfTest() {
        string root = Path.Combine(Path.GetTempPath(), "codex-runtime-cleanup-" + Guid.NewGuid().ToString("N"));
        try {
            DateTime now = new DateTime(2026, 11, 19, 12, 0, 0, DateTimeKind.Utc);
            string claims = Path.Combine(root, "desktop-instances");
            string turns = Path.Combine(root, "turns");
            string deliveries = Path.Combine(root, "completion-deliveries");
            string settingsBackups = Path.Combine(root, "settings-backups");
            Directory.CreateDirectory(claims);
            Directory.CreateDirectory(turns);
            Directory.CreateDirectory(deliveries);
            Directory.CreateDirectory(settingsBackups);

            string oldClaim = Path.Combine(claims, "old.claim");
            string freshClaim = Path.Combine(claims, "fresh.claim");
            string oldTurn = Path.Combine(turns, "old.candidate");
            string freshTurn = Path.Combine(turns, "fresh.candidate");
            string oldDelivery = Path.Combine(deliveries, "old.done");
            string userSettings = Path.Combine(root, "scene-settings.json");

            File.WriteAllText(oldClaim, ""); File.SetLastWriteTimeUtc(oldClaim, now.AddDays(-15));
            File.WriteAllText(freshClaim, ""); File.SetLastWriteTimeUtc(freshClaim, now.AddDays(-5));
            File.WriteAllText(oldTurn, "1"); File.SetLastWriteTimeUtc(oldTurn, now.AddDays(-8));
            File.WriteAllText(freshTurn, "1"); File.SetLastWriteTimeUtc(freshTurn, now.AddDays(-2));
            File.WriteAllText(oldDelivery, "ok"); File.SetLastWriteTimeUtc(oldDelivery, now.AddDays(-9));
            File.WriteAllText(userSettings, "{}"); File.SetLastWriteTimeUtc(userSettings, now.AddDays(-90));

            for (int i = 0; i < 12; i++) {
                string backup = Path.Combine(settingsBackups, "scene-settings-" + i.ToString("D2") + ".json");
                File.WriteAllText(backup, "{}");
                File.SetLastWriteTimeUtc(backup, now.AddMinutes(-i));
            }

            int removed = Prune(root, now);
            if (removed != 5) return false; // 1 claim + 1 turn + 1 delivery + 2 oldest settings backups
            if (File.Exists(oldClaim) || !File.Exists(freshClaim)) return false;
            if (File.Exists(oldTurn) || !File.Exists(freshTurn)) return false;
            if (File.Exists(oldDelivery) || !File.Exists(userSettings)) return false;
            if (Directory.GetFiles(settingsBackups, "*.json").Length != KeepSettingsBackups) return false;
            return true;
        } finally {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
}
