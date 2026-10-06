using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

internal static class TrayIconResource {
    internal const string ResourceName = "CodexBootAnimation.TrayIcon";

    internal static Stream OpenEmbedded() {
        return Assembly.GetExecutingAssembly().GetManifestResourceStream(ResourceName);
    }

    // The watcher owns the returned icon. Clone before closing the resource
    // stream so neither NotifyIcon nor Windows depends on a live file/stream.
    internal static Icon Load(out bool embedded) {
        return Load(OpenEmbedded, SystemInformation.SmallIconSize, out embedded);
    }

    internal static Icon Load(Func<Stream> open, Size size, out bool embedded) {
        embedded = false;
        try {
            using (Stream stream = open()) {
                if (stream == null) throw new InvalidDataException("Missing embedded tray icon.");
                using (var icon = new Icon(stream, size)) {
                    var owned = (Icon)icon.Clone();
                    embedded = true;
                    return owned;
                }
            }
        } catch (Exception error) {
            // A cosmetic resource must not prevent the companion from starting.
            // No private paths or exception messages are written here.
            IntroLog.Write("tray-icon-fallback type=" + error.GetType().Name);
            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
