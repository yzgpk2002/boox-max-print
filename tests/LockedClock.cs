using System;
using System.Drawing;
using System.Windows.Forms;

// Local fixture for an operator-driven Win+L test. No desktop input or network.
class LockedClock : Form {
    readonly Timer tick = new Timer { Interval = 1000 };
    readonly Label clock = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter };
    LockedClock() {
        Text = "BOOX 锁屏同步测试时钟";
        ClientSize = new Size(600, 240);
        clock.Font = new Font(FontFamily.GenericMonospace, 52);
        Controls.Add(clock);
        tick.Tick += delegate { clock.Text = DateTime.Now.ToString("HH:mm:ss"); };
        tick.Start();
    }
    protected override void Dispose(bool disposing) {
        if(disposing) tick.Dispose();
        base.Dispose(disposing);
    }
    [STAThread] static void Main() { Application.Run(new LockedClock()); }
}
