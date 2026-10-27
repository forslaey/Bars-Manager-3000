using System;
using System.Drawing;
using System.Windows.Forms;
namespace ServiceTool
{
    // Session-only placement. Never persist a minimized or off-screen window.
    internal sealed class WindowPlacement
    {
        private Rectangle bounds;
        private FormWindowState state;
        internal void Capture(Form form)
        {
            bounds = form.WindowState == FormWindowState.Normal ? form.Bounds : form.RestoreBounds;
            state = form.WindowState == FormWindowState.Maximized ? FormWindowState.Maximized : FormWindowState.Normal;
        }
        internal Rectangle WorkingArea { get { return Screen.FromRectangle(bounds).WorkingArea; } }
        internal void Restore(Form form)
        {
            form.StartPosition = FormStartPosition.Manual;
            form.WindowState = FormWindowState.Normal;
            form.Bounds = Fit(bounds, Screen.FromRectangle(bounds).WorkingArea);
            form.WindowState = state;
        }
        internal static Rectangle Fit(Rectangle bounds, Rectangle area)
        {
            int width = Math.Min(Math.Max(1, bounds.Width), area.Width);
            int height = Math.Min(Math.Max(1, bounds.Height), area.Height);
            return new Rectangle(Math.Max(area.Left, Math.Min(bounds.Left, area.Right-width)),
                Math.Max(area.Top, Math.Min(bounds.Top, area.Bottom-height)), width, height);
        }
        internal static void Center(Form form, Rectangle area)
        {
            form.StartPosition=FormStartPosition.Manual;
            form.MinimumSize=new Size(Math.Min(form.MinimumSize.Width,area.Width),Math.Min(form.MinimumSize.Height,area.Height));
            Rectangle fitted=Fit(form.Bounds,area);
            form.Bounds=new Rectangle(area.Left+(area.Width-fitted.Width)/2,area.Top+(area.Height-fitted.Height)/2,fitted.Width,fitted.Height);
        }
        internal static void Constrain(Form form)
        {
            Rectangle area = Screen.FromControl(form).WorkingArea;
            form.MinimumSize = new Size(Math.Min(form.MinimumSize.Width, area.Width), Math.Min(form.MinimumSize.Height, area.Height));
            if (form.WindowState == FormWindowState.Normal) form.Bounds = Fit(form.Bounds, area);
        }
    }
}
