using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;

internal static class DialogRouteStateTests {
    static void Require(bool value, string step) {
        if (!value) throw new InvalidOperationException("dialog-route-" + step);
    }
    internal static bool Run() {
        var routes = new DialogRouteState();
        Require(routes.Advance("10:100", 1, true), "first-window");
        Require(routes.Advance("10:100", 2, true), "second-window-not-suppressed");
        Require(!routes.Advance("10:100", 2, false), "leave-second-window");
        Require(!routes.Advance("10:100", 1, true), "other-window-cannot-reset-first");
        Require(routes.Advance("10:100", 2, true), "second-window-reentry");
        Require(routes.Advance("11:100", 1, true), "same-window-id-other-process");
        Require(routes.Advance("10:200", 1, true), "reused-pid-new-lifetime");
        Require(!routes.Advance(null, 1, true) && !routes.Advance("10:100", 0, true), "invalid-key");
        routes.Retain(new HashSet<string> { "10:100" });
        Require(routes.Count == 2 && !routes.Advance("10:100", 1, true), "retire-process-preserve-live");
        routes.Retain(new HashSet<string>());
        Require(routes.Count == 0, "retire-all");
        routes.MarkDesktopStarted("10:100");
        Require(!routes.Advance("10:100", 1, true), "startup-page-not-new-chat");
        Require(routes.Advance("10:100", 2, true), "startup-seed-only-first-window");
        Require(routes.Advance("11:100", 1, true), "startup-seed-not-global");
        Require(!routes.Advance("10:100", 1, false) && routes.Advance("10:100", 1, true), "navigation-after-startup");
        routes.MarkDesktopStarted("10:100");
        Require(routes.Advance("10:100", 3, true), "late-startup-does-not-seed-another-window");
        routes.MarkDesktopStarted("12:100");
        Require(!routes.Advance("12:100", 1, false) && routes.Advance("12:100", 1, true), "startup-nonblank-then-new-chat");
        routes.MarkDesktopStarted("13:100");
        routes.Retain(new HashSet<string>());
        Require(routes.Advance("13:100", 1, true), "startup-seed-retired");
        routes.Retain(new HashSet<string>());
        for (int i = 1; i <= DialogRouteState.Capacity; i++) Require(routes.Advance("10:100", i, true), "capacity-fill");
        Require(!routes.Advance("10:100", DialogRouteState.Capacity + 1, true), "capacity-bound");
        Require(!routes.Advance("10:100", 1, true) && routes.Count == DialogRouteState.Capacity, "no-live-eviction");
        routes.Retain(new HashSet<string>());
        Require(routes.Advance("10:200", 1, true), "capacity-recovered");
        int window;
        Require(DialogRouteState.TryWindowId("event windowId=12\r", out window) && window == 12, "parse-window");
        foreach (string invalid in new[] { "event", "otherwindowId=1", "windowId=-1", "windowId=0", "windowId=+1", "windowId=2147483648", "windowId=1 windowId=2", "windowId=1abc" })
            Require(!DialogRouteState.TryWindowId(invalid, out window), "reject-window");
        TestProductionHandler();
        return true;
    }
    static void TestProductionHandler() {
        // Paused production handler advances routing but cannot launch a player.
        Type type = typeof(IntroWatcher);
        BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        object watcher = FormatterServices.GetUninitializedObject(type);
        var routes = new DialogRouteState();
        type.GetField("dialogRoutes", flags).SetValue(watcher, routes);
        type.GetField("dialogRouteLock", flags).SetValue(watcher, new object());
        type.GetField("paused", flags).SetValue(watcher, true);
        type.GetField("dialogIdentity", flags).SetValue(watcher, new Func<string, string, string>(delegate(string path, string line) {
            return path == "active.log" ? "10:100" : null;
        }));
        MethodInfo handle = type.GetMethod("HandleLogLine", flags);
        string prefix = "IAB_LIFECYCLE received browser sidebar owner sync conversationId=client-new-thread:fixture ownerRoutePath=";
        handle.Invoke(watcher, new object[] { "active.log", prefix + "/ windowId=1", "test", 0L });
        handle.Invoke(watcher, new object[] { "active.log", prefix + "/ windowId=2", "test", 0L });
        handle.Invoke(watcher, new object[] { "active.log", prefix + "/local/fixture windowId=2", "test", 0L });
        handle.Invoke(watcher, new object[] { "inactive.log", prefix + "/ windowId=3", "test", 0L });
        handle.Invoke(watcher, new object[] { "active.log", prefix + "/", "test", 0L });
        Require(routes.Count == 2 && !routes.Advance("10:100", 1, true) && routes.Advance("10:100", 2, true), "production-key-and-leave");
        // Timestamp validation in the production identity resolver uses this
        // same predicate; a reused PID cannot lend identity to older records.
        DateTime started = new DateTime(2026, 10, 29, 1, 0, 0, DateTimeKind.Utc);
        string file = "codex-desktop-aaaa-10-t0-i0-1-1.log";
        Require(!NewChatDiscovery.ReadyLine(file, "2026-10-29T00:59:59Z " + prefix + "/ windowId=1", 10, started), "old-process-record");
        Require(NewChatDiscovery.ReadyLine(file, "2026-10-29T01:00:01Z " + prefix + "/ windowId=1", 10, started), "current-process-record");
    }
}
