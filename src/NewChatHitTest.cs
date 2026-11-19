using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Automation;

// Resolve the element at the actual click, not an old rectangle. All provider
// calls live in a killable child; no recursive conversation-tree enumeration.
internal static class NewChatHitTest {
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    static readonly string[] DefaultNames = {
        "新聊天", "新对话", "新任务", "New chat", "New conversation", "New thread",
        "新しいチャット", "새 채팅", "Neuer Chat", "Nouvelle discussion", "Nuevo chat"
    };

    internal static bool Matches(string name, string id, IEnumerable names, IEnumerable ids) {
        string normalized = Regex.Replace(name ?? "", @"\s*[（(].*[）)]\s*$", "").Trim();
        foreach (object item in names)
            if (String.Equals(normalized, item as string, StringComparison.OrdinalIgnoreCase)) return true;
        foreach (object item in ids)
            if (!String.IsNullOrEmpty(id) && String.Equals(id, item as string, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    internal static bool Check(int pid, IntPtr window, int x, int y) {
        using (var child = Process.Start(new ProcessStartInfo {
            FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BootPlayer.exe"),
            Arguments = "--new-chat-hit " + pid + " " + window.ToInt64() + " " + x + " " + y,
            UseShellExecute = false, CreateNoWindow = true
        })) {
            return NewChatDiscovery.WaitForChild(child, 1200) && child.ExitCode == 0;
        }
    }

    internal static int Probe(string[] args) {
        try {
            int pid, x, y; long handle;
            if (args.Length != 5 || !Int32.TryParse(args[1], out pid) || !Int64.TryParse(args[2], out handle) ||
                !Int32.TryParse(args[3], out x) || !Int32.TryParse(args[4], out y)) return 2;
            IntPtr window = new IntPtr(handle); uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner != pid) return 2;
            using (var process = Process.GetProcessById(pid)) if (!DesktopInstancePolicy.IsCodex(process)) return 2;
            var names = new List<string>(DefaultNames);
            var ids = new List<string>();
            string selectors = Path.Combine(IntroLog.Data, "new-chat-selectors.json");
            if (File.Exists(selectors) && new FileInfo(selectors).Length <= 16384) {
                var settings = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(File.ReadAllText(selectors));
                foreach (string key in new[] { "names", "automationIds" }) {
                    object value;
                    if (settings.TryGetValue(key, out value) && value is IEnumerable && !(value is string))
                        foreach (object item in (IEnumerable)value) if (item is string)
                            (key == "names" ? names : ids).Add((string)item);
                }
            }
            using (var watchdog = new Timer(delegate { Environment.Exit(124); }, null, 1000, Timeout.Infinite)) {
                AutomationElement element = AutomationElement.FromPoint(new Point(x, y));
                for (int depth = 0; element != null && depth < 8; depth++) {
                    var current = element.Current;
                    if (current.ProcessId != pid) return 1;
                    if (current.ControlType == ControlType.Button &&
                        Matches(current.Name, current.AutomationId, names, ids) &&
                        !current.IsOffscreen && current.IsEnabled && current.BoundingRectangle.Contains(x, y)) {
                        // Verify the clicked provider still belongs to the captured top-level window.
                        AutomationElement root = element;
                        for (int i = 0; root != null && i < 16; i++, root = TreeWalker.ControlViewWalker.GetParent(root)) {
                            IntPtr native = new IntPtr(root.Current.NativeWindowHandle);
                            if (native != IntPtr.Zero && GetAncestor(native, 2) == window) return 0;
                        }
                        return 1;
                    }
                    element = TreeWalker.ControlViewWalker.GetParent(element);
                }
            }
            return 1;
        } catch (Exception error) { IntroLog.Write("new-chat-hit-error type=" + error.GetType().Name); return 2; }
    }
}
