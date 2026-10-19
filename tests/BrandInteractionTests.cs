using System;
using System.Drawing;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class BrandInteractionTests
    {
        private static int checks;
        private static void Assert(bool value, string message) { checks++; if (!value) throw new Exception(message); }
        private static void Pump() { DateTime end = DateTime.UtcNow.AddMilliseconds(350); while (DateTime.UtcNow < end) { Application.DoEvents(); Thread.Sleep(5); } }
        private static void AwaitColor(Control control, Color expected)
        {
            // A busy machine can defer WinForms timer ticks. Wait for completion instead
            // of assuming all animation frames were delivered within a fixed 350 ms.
            DateTime deadline = DateTime.UtcNow.AddSeconds(3);
            while (Sample(control).ToArgb() != expected.ToArgb() && DateTime.UtcNow < deadline) { Application.DoEvents(); Thread.Sleep(10); }
        }
        private static void Event(Control c, string name, object args) { c.GetType().GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance).Invoke(c, new object[] { args }); }
        private static Color Sample(Control c) { using (Bitmap b = new Bitmap(c.Width,c.Height)) { c.DrawToBitmap(b,c.ClientRectangle); return b.GetPixel(c.Width / 2, 5); } }
        [STAThread] public static int Main(string[] args)
        {
            try
            {
                Application.EnableVisualStyles();
                foreach (bool dark in new[] { false, true })
                using (Form f = new Form { ClientSize = new Size(620,240), ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20000,-20000) })
                {
                    ThemePalette palette = ThemePalette.Create(dark); f.BackColor = palette.Canvas;
                    using (BrandButton button = new BrandButton { Palette = palette, Primary = true, Text = "Проверить подключение", Bounds = new Rectangle(24,24,260,42) })
                    using (RefreshToggle toggle = new RefreshToggle { Palette = palette, Text = "Автоматическое обновление", Bounds = new Rectangle(24,86,500,36), BackColor = palette.Canvas })
                    using (LogDateCheckBox date = new LogDateCheckBox { Palette = palette, Text = "19.10.2026", Bounds = new Rectangle(24,146,260,36), BackColor = palette.Canvas })
                    {
                        f.Controls.AddRange(new Control[] { button, toggle, date }); f.Show(); Pump();
                        Color normal = Sample(button);
                        Event(button,"OnMouseEnter",EventArgs.Empty); Pump(); Color hover = Sample(button);
                        Assert(hover.GetBrightness() > normal.GetBrightness(), "Hover must lighten button in both themes");
                        Event(button,"OnMouseDown",new MouseEventArgs(MouseButtons.Left,1,10,10,0)); Pump();
                        Assert(Sample(button).GetBrightness() < hover.GetBrightness(),"Press must darken button");
                        Event(button,"OnMouseUp",new MouseEventArgs(MouseButtons.Left,1,10,10,0)); Pump();
                        AwaitColor(button,hover); Assert(Sample(button).ToArgb() == hover.ToArgb(),"Mouse release restores hover");
                        Event(button,"OnMouseLeave",EventArgs.Empty); Pump();
                        AwaitColor(button,normal); Assert(Sample(button).ToArgb() == normal.ToArgb(),"Leaving restores normal");
                        Event(button,"OnKeyDown",new KeyEventArgs(Keys.Space)); Pump();
                        Assert(Sample(button).GetBrightness() < normal.GetBrightness(),"Keyboard press is visible");
                        Event(button,"OnKeyUp",new KeyEventArgs(Keys.Space)); Pump();
                        AwaitColor(button,normal); Assert(Sample(button).ToArgb() == normal.ToArgb(),"Keyboard release restores normal");
                        toggle.Checked = true; Pump();
                        Assert(toggle.Checked,"Toggle checked");
                        toggle.Checked = false; Pump(); Assert(!toggle.Checked,"Toggle unchecked");
                        date.Checked=true; date.Enabled=false; Event(date,"OnClick",EventArgs.Empty);
                        Assert(date.Checked,"Disabled date must not change checked state");
                        date.Enabled=true;
                        using (Bitmap bitmap = new Bitmap(f.ClientSize.Width, f.ClientSize.Height))
                        { f.DrawToBitmap(bitmap, f.ClientRectangle); if(args.Length>0) bitmap.Save(System.IO.Path.Combine(args[0],dark ? "controls-dark.png" : "controls-light.png")); }
                        f.Close();
                    }
                }
                Console.WriteLine("PASS: " + checks + " brand interaction checks, light and dark; no service actions."); return 0;
            }
            catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        }
    }
}
