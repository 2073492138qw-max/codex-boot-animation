using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

internal static class OwnerWindowFollowTests {
    [DllImport("user32.dll")] static extern void NotifyWinEvent(uint kind, IntPtr window, int objectId, int childId);
    static void Require(bool value) { if (!value) throw new InvalidOperationException("Owner follow regression."); }
    static void Pump() {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(150) };
        timer.Tick += delegate { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }
    internal static bool Run() {
        try {
            IntPtr ownerId = new IntPtr(10);
            Require(OwnerLocationSubscription.Matches(ownerId, 0x800B, ownerId, 0, 0));
            Require(!OwnerLocationSubscription.Matches(ownerId, 0x800B, new IntPtr(11), 0, 0));
            Require(!OwnerLocationSubscription.Matches(ownerId, 0x800B, ownerId, -4, 0));
            Require(!OwnerLocationSubscription.Matches(ownerId, 0x800B, ownerId, 0, 1));
            Require(!OwnerLocationSubscription.Matches(ownerId, 0x800C, ownerId, 0, 0));
            Require(!OwnerLocationSubscription.Matches(IntPtr.Zero, 0x800B, IntPtr.Zero, 0, 0));
            var placement = new OwnedWindowPlacement();
            int writes = 0; uint lastFlags = 0;
            Func<Rect, uint, bool> write = delegate(Rect bounds, uint flags) { writes++; lastFlags = flags; return true; };
            Rect initial = new Rect(10, 50, 900, 650);
            Require(placement.Apply(initial, write) && lastFlags == 0x14);
            for (int n = 0; n < 100; n++) Require(!placement.Apply(initial, write));
            Require(writes == 1);
            Require(placement.Apply(new Rect(40, 80, 900, 650), write) && lastFlags == 0x15);
            Require(placement.Apply(new Rect(40, 80, 1100, 750), write) && lastFlags == 0x16);
            Require(placement.Apply(new Rect(-1900, 0, 1200, 800), write) && lastFlags == 0x14);
            Rect failed = new Rect(0, 0, 1200, 800);
            Require(!placement.Apply(failed, delegate { return false; }));
            Require(placement.Apply(failed, write));

            // Our hidden fixture only. No Codex window discovery or input automation.
            using (var owner = new Forms.Form { ShowInTaskbar = false }) {
                int refreshes = 0;
                var subscription = new OwnerLocationSubscription(owner.Handle, Dispatcher.CurrentDispatcher, delegate { refreshes++; });
                try {
                    Require(subscription.Active);
                    for (int n = 0; n < 100; n++) subscription.RequestRefresh();
                    Pump(); Require(refreshes == 1);
                    GC.Collect(); GC.WaitForPendingFinalizers();
                    NotifyWinEvent(0x800B, owner.Handle, 0, 0);
                    Pump(); Require(refreshes == 2);
                    NotifyWinEvent(0x800B, owner.Handle, -4, 0);
                    Pump(); Require(refreshes == 2);
                    subscription.RequestRefresh();
                    subscription.Dispose(); subscription.Dispose();
                    Require(!subscription.Active);
                    NotifyWinEvent(0x800B, owner.Handle, 0, 0);
                    Pump(); Require(refreshes == 2 && !owner.Visible);
                } finally { subscription.Dispose(); }
            }
            IntroLog.Write("owner-follow-test pass=True"); return true;
        } catch (Exception error) {
            IntroLog.Write("owner-follow-test pass=False type=" + error.GetType().Name); return false;
        }
    }
}
