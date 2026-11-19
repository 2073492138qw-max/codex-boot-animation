using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Security.Cryptography;
using System.Text;

// Application window IDs are not native HWNDs. Only navigation-log events use
// these keys; never associate a click with a log window by time or foreground.
internal sealed class DialogRouteState {
    internal const int Capacity = 512;
    sealed class Entry {
        internal string Process;
        internal bool Blank;
        internal string Token;
        internal DateTime LastRecord;
        internal readonly Queue<string> Recent = new Queue<string>();
    }
    readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.Ordinal);
    readonly HashSet<string> startupPending = new HashSet<string>(StringComparer.Ordinal);
    static readonly Regex WindowField = new Regex(@"(?:^|\s)windowId=([^\s]+)", RegexOptions.CultureInvariant);
    static readonly Regex RouteField = new Regex(@"(?:^|\s)ownerRoutePath=([^\s]+)", RegexOptions.CultureInvariant);
    static readonly Regex ConversationField = new Regex(@"(?:^|\s)conversationId=([^\s]+)", RegexOptions.CultureInvariant);

    internal static bool TryRoute(string line, out bool blank, out string token) {
        blank = false; token = null;
        if (String.IsNullOrEmpty(line) || line.IndexOf("IAB_LIFECYCLE received browser sidebar owner sync", StringComparison.Ordinal) < 0) return false;
        MatchCollection routes = RouteField.Matches(line), conversations = ConversationField.Matches(line);
        if (routes.Count != 1 || conversations.Count != 1) return false;
        blank = routes[0].Groups[1].Value == "/";
        string conversation = conversations[0].Groups[1].Value;
        const string prefix = "client-new-thread:";
        if (blank && (!conversation.StartsWith(prefix, StringComparison.Ordinal) || conversation.Length <= prefix.Length)) return false;
        // Keep only a bounded fingerprint in memory, never the conversation ID.
        using (var hash = SHA256.Create()) token = Convert.ToBase64String(hash.ComputeHash(Encoding.UTF8.GetBytes(conversation)));
        return true;
    }

    internal int Count { get { return entries.Count; } }

    internal void MarkDesktopStarted(string processIdentity) {
        if (String.IsNullOrEmpty(processIdentity) || startupPending.Count >= Capacity) return;
        foreach (Entry entry in entries.Values) if (entry.Process == processIdentity) return;
        startupPending.Add(processIdentity);
    }

    internal static bool TryWindowId(string line, out int windowId) {
        windowId = 0;
        if (String.IsNullOrEmpty(line)) return false;
        MatchCollection matches = WindowField.Matches(line);
        return matches.Count == 1 && Int32.TryParse(matches[0].Groups[1].Value,
            NumberStyles.None, CultureInfo.InvariantCulture, out windowId) && windowId > 0;
    }

    // Caller holds the watcher's route lock. Capacity exhaustion does not evict
    // live keys: eviction would make repeated blank events look new again.
    internal bool Advance(string processIdentity, int windowId, bool blank, string token = null, DateTime? recorded = null) {
        if (String.IsNullOrEmpty(processIdentity) || windowId <= 0) return false;
        string key = processIdentity + "/" + windowId.ToString(CultureInfo.InvariantCulture);
        Entry entry;
        if (!entries.TryGetValue(key, out entry)) {
            if (entries.Count >= Capacity) return false;
            // Preserve the startup blank-page seed, but consume it only for
            // this process's first observed log window, not every window.
            entry = new Entry { Process = processIdentity, Blank = startupPending.Remove(processIdentity) };
            entries.Add(key, entry);
        }
        if (recorded.HasValue) {
            if (recorded.Value < entry.LastRecord) return false;
            string eventKey = recorded.Value.Ticks.ToString(CultureInfo.InvariantCulture) + ":" + blank + ":" + token;
            if (entry.Recent.Contains(eventKey)) return false;
            if (entry.Recent.Count == 32) entry.Recent.Dequeue();
            entry.Recent.Enqueue(eventKey);
            entry.LastRecord = recorded.Value;
        }
        bool changedToken = blank && entry.Blank && entry.Token != null && token != null && entry.Token != token;
        bool entered = IntroWatcher.AdvanceDialogRoute(ref entry.Blank, blank);
        if (blank && token != null) entry.Token = token;
        return entered || changedToken;
    }

    internal void Retain(ISet<string> liveProcesses) {
        var expired = new List<string>();
        foreach (var pair in entries) if (!liveProcesses.Contains(pair.Value.Process)) expired.Add(pair.Key);
        foreach (string key in expired) entries.Remove(key);
        startupPending.IntersectWith(liveProcesses);
    }
}
