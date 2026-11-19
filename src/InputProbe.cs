using System;
using System.Drawing;
using Forms = System.Windows.Forms;

// An owned diagnostic window for real mouse delivery; never drives Codex UI.
internal static class InputProbe {
    internal static int Run() {
        using (var form = new Forms.Form { Text="Codex Animation Input Test", Width=540, Height=220, StartPosition=Forms.FormStartPosition.CenterScreen })
        using (var label = new Forms.Label { Text="Raw mouse releases: 0", AutoSize=true, Left=30, Top=25 })
        using (var button = new Forms.Button { Text="Test mouse release", Left=30, Top=70, Width=200, Height=40 })
        using (var close = new Forms.Button { Text="Close test", Left=260, Top=70, Width=150, Height=40 })
        using (var deadline = new Forms.Timer { Interval=60000 }) {
            int count=0;
            form.Controls.Add(label); form.Controls.Add(button); form.Controls.Add(close);
            close.Click+=delegate { form.Close(); };
            deadline.Tick+=delegate { form.Close(); }; deadline.Start();
            using(var raw=new RawMouseInput(delegate(int x,int y) {
                if (form.IsDisposed || !form.Visible) return;
                var local=button.PointToClient(new Point(x,y));
                if(button.ClientRectangle.Contains(local)) {
                    count++; label.Text="Raw mouse releases: "+count;
                    IntroLog.Write("raw-input-owned-window-release count="+count);
                }
            })) Forms.Application.Run(form);
            return count>0?0:1;
        }
    }
}
