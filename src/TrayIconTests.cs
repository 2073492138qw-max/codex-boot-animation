using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

internal static class TrayIconTests {
    internal static bool Run() {
        try {
            int[] sizes = {16, 20, 24, 32, 40, 48, 64, 128, 256};
            using (Stream resource = TrayIconResource.OpenEmbedded()) {
                if (resource == null) return false;
                using (var reader = new BinaryReader(resource)) {
                    if (reader.ReadUInt16() != 0 || reader.ReadUInt16() != 1 || reader.ReadUInt16() != sizes.Length) return false;
                    foreach (int size in sizes) {
                        if (reader.ReadByte() != (size == 256 ? 0 : size) || reader.ReadByte() != (size == 256 ? 0 : size)) return false;
                        reader.ReadByte(); reader.ReadByte();
                        if (reader.ReadUInt16() != 1 || reader.ReadUInt16() != 32) return false;
                        uint length = reader.ReadUInt32(), offset = reader.ReadUInt32();
                        if (length == 0 || offset < 6 + 16 * sizes.Length || (long)offset + length > resource.Length) return false;
                        long nextEntry = resource.Position;
                        resource.Position = offset;
                        using (var frame = new MemoryStream(reader.ReadBytes(checked((int)length))))
                        using (Image image = Image.FromStream(frame)) {
                            if (image.Width != size || image.Height != size) return false;
                        }
                        resource.Position = nextEntry;
                    }
                }
            }
            foreach (int size in sizes) {
                bool embedded;
                using (Icon icon = TrayIconResource.Load(TrayIconResource.OpenEmbedded, new Size(size, size), out embedded)) {
                    // Framework 4 treats a zero-encoded 256px entry differently
                    // and can choose 128px. Its actual PNG frame was checked above;
                    // require exact selection for all practical tray sizes.
                    int selected = icon.Width;
                    if (!embedded || icon.Height != selected || (selected != size && !(size == 256 && selected == 128))) return false;
                    // Also tests that the clone is usable after its stream was closed.
                    using (Bitmap bitmap = icon.ToBitmap()) {
                        int visible = 0;
                        for (int y = 0; y < bitmap.Height; y++)
                            for (int x = 0; x < bitmap.Width; x++)
                                if (bitmap.GetPixel(x, y).A > 0) visible++;
                        if (visible < bitmap.Width * bitmap.Height / 4) return false;
                    }
                    using (var tray = new NotifyIcon {Icon = icon, Visible = false}) {
                        if (tray.Visible || tray.Icon != icon) return false;
                    }
                }
            }
            bool custom;
            using (Icon fallback = TrayIconResource.Load(delegate {return null;}, new Size(16, 16), out custom)) {
                if (custom || fallback.Width == 0) return false;
                using (Bitmap bitmap = fallback.ToBitmap()) {if (bitmap.Width == 0) return false;}
            }
            using (Icon fallback = TrayIconResource.Load(delegate {return new MemoryStream(new byte[] {1, 2, 3});}, new Size(16, 16), out custom)) {
                if (custom || fallback.Height == 0) return false;
            }
            using (Icon icon = TrayIconResource.Load(out custom)) {
                if (!custom) return false;
            }
            IntroLog.Write("tray-icon-test pass=True sizes=" + sizes.Length);
            return true;
        } catch (Exception error) {
            IntroLog.Write("tray-icon-test pass=False type=" + error.GetType().Name);
            return false;
        }
    }
}
