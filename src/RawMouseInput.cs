using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;

// Raw Input observes mouse releases without installing a synchronous global hook.
// Windows input delivery never waits for this process. Do not use RIDEV_NOLEGACY.
internal sealed class RawMouseInput : Forms.NativeWindow, IDisposable {
    [StructLayout(LayoutKind.Sequential)]
    struct Device { public ushort UsagePage, Usage; public uint Flags; public IntPtr Target; }
    [StructLayout(LayoutKind.Sequential)]
    struct Point { public int X, Y; }
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool RegisterRawInputDevices(Device[] devices, uint count, uint size);
    [DllImport("user32.dll", SetLastError = true)]
    static extern uint GetRawInputData(IntPtr input, uint command, IntPtr data, ref uint size, uint headerSize);
    [DllImport("user32.dll")] static extern bool GetCursorPos(out Point point);
    readonly Action<int, int> released;
    IntPtr buffer;
    readonly uint headerSize = (uint)(8 + 2 * IntPtr.Size);

    internal RawMouseInput(Action<int, int> onRelease) {
        released = onRelease;
        CreateHandle(new Forms.CreateParams { Caption = "Codex animation input", Parent = new IntPtr(-3) });
        var devices = new[] { new Device { UsagePage = 1, Usage = 2, Flags = 0x100, Target = Handle } };
        if (!RegisterRawInputDevices(devices, 1, (uint)Marshal.SizeOf(typeof(Device)))) {
            int error = Marshal.GetLastWin32Error();
            DestroyHandle();
            throw new Win32Exception(error);
        }
        buffer = Marshal.AllocHGlobal(64);
    }

    protected override void WndProc(ref Forms.Message message) {
        if (message.Msg == 0x00FF && buffer != IntPtr.Zero) {
            uint size = 64;
            uint read = GetRawInputData(message.LParam, 0x10000003, buffer, ref size, headerSize);
            if (read != UInt32.MaxValue && read >= headerSize + 24 && Marshal.ReadInt32(buffer) == 0) {
                // RAWMOUSE's button union is aligned at offset 4 after RAWINPUTHEADER.
                ushort buttons = (ushort)Marshal.ReadInt16(buffer, (int)headerSize + 4);
                if ((buttons & 0x0002) != 0) {
                    Point point;
                    if (GetCursorPos(out point)) {
                        try { released(point.X, point.Y); }
                        catch (Exception error) { IntroLog.Write("raw-input-callback-error type=" + error.GetType().Name); }
                    }
                }
            }
        }
        base.WndProc(ref message);
    }

    public void Dispose() {
        if (Handle != IntPtr.Zero) {
            RegisterRawInputDevices(new[] { new Device { UsagePage = 1, Usage = 2, Flags = 1 } }, 1, (uint)Marshal.SizeOf(typeof(Device)));
            DestroyHandle();
        }
        if (buffer != IntPtr.Zero) { Marshal.FreeHGlobal(buffer); buffer = IntPtr.Zero; }
    }
}
