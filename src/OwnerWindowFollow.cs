using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

// UI-thread owned, out-of-process location notifications; no UIA or injected DLL.
internal sealed class OwnerLocationSubscription : IDisposable {
    internal const uint LocationChanged = 0x800B;
    delegate void WinEventProc(IntPtr hook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint min, uint max, IntPtr module, WinEventProc callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
    readonly IntPtr owner;
    readonly Dispatcher dispatcher;
    readonly Action refresh;
    readonly WinEventProc callback;
    GCHandle callbackRoot;
    IntPtr hook;
    DispatcherOperation pending;
    bool disposed;
    internal bool Active { get { return hook != IntPtr.Zero && !disposed; } }

    internal OwnerLocationSubscription(IntPtr owner, Dispatcher dispatcher, Action refresh) {
        dispatcher.VerifyAccess();
        this.owner = owner; this.dispatcher = dispatcher; this.refresh = refresh;
        callback = OnEvent;
        uint process;
        uint thread = GetWindowThreadProcessId(owner, out process);
        if (owner == IntPtr.Zero || process == 0 || thread == 0) return;
        callbackRoot = GCHandle.Alloc(callback);
        hook = SetWinEventHook(LocationChanged, LocationChanged, IntPtr.Zero, callback, process, thread, 0);
        if (hook == IntPtr.Zero) callbackRoot.Free();
    }

    internal static bool Matches(IntPtr owner, uint kind, IntPtr window, int objectId, int childId) {
        return owner != IntPtr.Zero && window == owner && kind == LocationChanged && objectId == 0 && childId == 0;
    }

    void OnEvent(IntPtr nativeHook, uint kind, IntPtr window, int objectId, int childId, uint thread, uint time) {
        // Never let a managed exception cross the native callback boundary.
        try { if (Matches(owner, kind, window, objectId, childId)) RequestRefresh(); }
        catch (Exception error) { IntroLog.Write("owner-follow-callback-error type=" + error.GetType().Name); }
    }

    internal void RequestRefresh() {
        dispatcher.VerifyAccess();
        if (disposed || dispatcher.HasShutdownStarted || (pending != null && pending.Status == DispatcherOperationStatus.Pending)) return;
        pending = dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(delegate {
            pending = null;
            if (!disposed) refresh();
        }));
    }

    public void Dispose() {
        dispatcher.VerifyAccess();
        if (disposed) return;
        disposed = true;
        if (pending != null) { pending.Abort(); pending = null; }
        if (hook != IntPtr.Zero && !UnhookWinEvent(hook)) {
            // Keep the callback rooted if Windows failed to unregister it.
            IntroLog.Write("owner-follow-unhook-failed"); return;
        }
        hook = IntPtr.Zero;
        if (callbackRoot.IsAllocated) callbackRoot.Free();
    }
}

internal sealed class OwnedWindowPlacement {
    Rect last;
    bool placed;
    internal bool Apply(Rect bounds, Func<Rect, uint, bool> position) {
        if (placed && bounds == last) return false;
        uint flags = 0x0010 | 0x0004; // NOACTIVATE | NOZORDER
        if (placed && bounds.Size == last.Size) flags |= 0x0001; // NOSIZE on pure movement
        if (placed && bounds.Location == last.Location) flags |= 0x0002; // NOMOVE on pure sizing
        if (!position(bounds, flags)) return false; // Failed placement must be retryable.
        last = bounds; placed = true; return true;
    }
}
