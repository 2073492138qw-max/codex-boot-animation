using System;
using System.Windows;

// Window scenes only. Monitor-wide intros and independent notices do not use this.
internal static class OwnedVideoViewport {
    internal const double TitleBarDip = 48;

    internal static Rect ContentBounds(Rect owner, double scale) {
        if (Double.IsNaN(scale) || Double.IsInfinity(scale) || scale <= 0) scale = 1;
        double header = Math.Ceiling(TitleBarDip * scale);
        // Do not cover the remaining title strip if the owner becomes too small.
        if (owner.IsEmpty || owner.Width <= 0 || owner.Height <= header) return Rect.Empty;
        return new Rect(owner.Left, owner.Top + header, owner.Width, owner.Height - header);
    }
}
