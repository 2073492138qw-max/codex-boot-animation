using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

internal static class OwnedVideoViewportTests {
    [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left, Top, Right, Bottom;}
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);
    [DllImport("user32.dll")] static extern void NotifyWinEvent(uint kind, IntPtr window, int objectId, int childId);
    static void Require(bool value) {if (!value) throw new InvalidOperationException("Owned viewport regression.");}

    internal static bool Run() {
        try {
            foreach (double scale in new[] {1.0, 1.25, 1.5, 2.0, 3.0, Double.NaN, 0, Double.PositiveInfinity}) {
                double effective = Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0 ? 1 : scale;
                foreach (Rect owner in new[] {new Rect(20, 30, 900, 700), new Rect(-1920, 0, 1200, 800), new Rect(0, -1080, 1920, 1080)}) {
                    Rect content = OwnedVideoViewport.ContentBounds(owner, scale);
                    double header = Math.Ceiling(OwnedVideoViewport.TitleBarDip * effective);
                    Require(owner.Contains(content) && content.Left == owner.Left && content.Width == owner.Width);
                    Require(content.Top == owner.Top + header && content.Bottom == owner.Bottom && content.Height > 0);
                }
            }
            Require(OwnedVideoViewport.ContentBounds(new Rect(0, 0, 100, 48), 1).IsEmpty);
            Require(OwnedVideoViewport.ContentBounds(new Rect(0, 0, 0, 100), 1).IsEmpty);
            string[] paths = MediaLibrary.Prepared(); Require(paths.Length > 0);
            // Hidden synthetic owner, never the real Codex window or its UI.
            using (var owner = new Forms.Form {Text = "Owned viewport hidden fixture", ShowInTaskbar = false}) {
                owner.Bounds = new System.Drawing.Rectangle(40, 60, 900, 700);
                IntPtr handle = owner.Handle;
                var video = new CodexVideoWindow(handle, paths[0], VideoScene.Anger);
                try {
                    IntPtr child = new WindowInteropHelper(video).EnsureHandle();
                    int positionMessages = 0, sizeMessages = 0;
                    HwndSource source = HwndSource.FromHwnd(child);
                    source.AddHook(delegate(IntPtr window, int message, IntPtr wParam, IntPtr lParam, ref bool handled) {
                        if (message == 0x0046) positionMessages++;
                        if (message == 0x0005) sizeMessages++;
                        return IntPtr.Zero;
                    });
                    foreach (var rectangle in new[] {new System.Drawing.Rectangle(40, 60, 900, 700), new System.Drawing.Rectangle(100, 90, 1100, 800)}) {
                        owner.Bounds = rectangle;
                        video.PositionInOwner(child);
                        NativeRect parentRect = new NativeRect(), childRect = new NativeRect();
                        Require(GetWindowRect(handle, out parentRect) && GetWindowRect(child, out childRect));
                        Require(childRect.Left == parentRect.Left && childRect.Right == parentRect.Right);
                        Require(childRect.Top >= parentRect.Top + 48 && childRect.Bottom == parentRect.Bottom);
                        Require(!owner.Visible && !video.IsVisible);
                        int before = positionMessages;
                        for (int repeat = 0; repeat < 20; repeat++) video.PositionInOwner(child);
                        Require(positionMessages == before);
                    }
                    int beforeMoveSizes = sizeMessages;
                    owner.Left += 30;
                    video.PositionInOwner(child);
                    Require(sizeMessages == beforeMoveSizes);
                    // Exercise the actual SourceInitialized subscription, not just manual positioning.
                    owner.Left += 40;
                    NotifyWinEvent(0x800B, handle, 0, 0);
                    var frame = new DispatcherFrame();
                    var pump = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
                    pump.Tick += delegate { pump.Stop(); frame.Continue = false; };
                    pump.Start(); Dispatcher.PushFrame(frame);
                    NativeRect eventOwner, eventChild;
                    Require(GetWindowRect(handle, out eventOwner));
                    Require(GetWindowRect(child, out eventChild));
                    Require(eventChild.Left == eventOwner.Left && eventChild.Top >= eventOwner.Top + 48);
                    Require(sizeMessages == beforeMoveSizes && !owner.Visible && !video.IsVisible);
                } finally {video.Close();}
            }
            IntroLog.Write("owned-viewport-test pass=True"); return true;
        } catch (Exception error) {
            IntroLog.Write("owned-viewport-test pass=False type=" + error.GetType().Name); return false;
        }
    }
}
