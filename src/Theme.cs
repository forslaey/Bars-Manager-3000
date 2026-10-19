using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class ThemePalette
    {
        public bool Dark;
        public Color Canvas, Surface, SurfaceAlt, Text, Muted, Border;
        public Color Accent, AccentText, Good, Warning, Danger;
        public Color Selection, SelectionText, DisabledSurface, DisabledText;

        public static ThemePalette Create(bool dark)
        {
            return dark ? new ThemePalette
            {
                Dark = true,
                Canvas = Hex("121714"), Surface = Hex("1B241E"), SurfaceAlt = Hex("25362C"),
                Text = Hex("F3F7F4"), Muted = Hex("B7C7BC"), Border = Hex("4B6153"),
                Accent = Hex("00824C"), AccentText = Color.White, Good = Hex("65DEA6"),
                Warning = Hex("F4CC73"), Danger = Hex("FFB1AE"),
                Selection = Hex("245B40"), SelectionText = Hex("F5FFF4"),
                DisabledSurface = Hex("243029"), DisabledText = Hex("9CAFA2")
            } : new ThemePalette
            {
                Dark = false,
                Canvas = Hex("F3F6F2"), Surface = Color.White, SurfaceAlt = Hex("E9F2EC"),
                Text = Hex("37352F"), Muted = Hex("506359"), Border = Hex("B2C4B8"),
                Accent = Hex("00824C"), AccentText = Color.White, Good = Hex("006B3F"),
                Warning = Hex("775011"), Danger = Hex("A52B2B"),
                Selection = Hex("CEE9D9"), SelectionText = Hex("243B2D"),
                DisabledSurface = Hex("E4EBE1"), DisabledText = Hex("556451")
            };
        }

        private static Color Hex(string value) { return ColorTranslator.FromHtml("#" + value); }
    }

    internal static class ThemePreference
    {
        private static string PreferencePath
        {
            get { return ApplicationStorage.FilePath("theme.txt"); }
        }

        public static bool LoadDark()
        {
            try { return File.Exists(PreferencePath) && File.ReadAllText(PreferencePath).Trim() == "dark"; }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }

        public static bool TrySave(bool dark)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(PreferencePath));
                File.WriteAllText(PreferencePath, dark ? "dark" : "light");
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
            catch (SecurityException) { return false; }
        }
    }

    internal sealed class BufferedPanel : Panel
    {
        public BufferedPanel() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); }
    }

    internal sealed class BufferedLayoutPanel : TableLayoutPanel
    {
        public BufferedLayoutPanel() { SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint | ControlStyles.ResizeRedraw, true); }
    }

    internal sealed class ServiceGrid : DataGridView
    {
        public ServiceGrid() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    }

    internal sealed class FlushLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            TextFormatFlags flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.WordBreak;
            if (TextAlign == ContentAlignment.MiddleLeft) flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, ForeColor, flags);
        }
    }

    internal sealed class BrandCheckBox : CheckBox
    {
        public ThemePalette Palette;

        public BrandCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            // ButtonBase can skip background painting for disabled themed checkboxes.
            // Always paint the whole surface so pixels from the owner cannot show through.
            e.Graphics.Clear(BackColor);
            int size = Math.Max(13, (int)Math.Round(13 * e.Graphics.DpiX / 96F));
            var box = new Rectangle(0, (Height - size) / 2, size, size);
            using (var fill = new SolidBrush(Enabled ? p.SurfaceAlt : p.DisabledSurface)) e.Graphics.FillRectangle(fill, box);
            using (var border = new Pen(p.Border)) e.Graphics.DrawRectangle(border, box);
            if (!Enabled)
                using (var pen = new Pen(p.Danger, Math.Max(1.5F, size / 8F)))
                {
                    int inset = Math.Max(3, size / 4);
                    e.Graphics.DrawLine(pen, box.Left + inset, box.Top + inset, box.Right - inset, box.Bottom - inset);
                    e.Graphics.DrawLine(pen, box.Left + inset, box.Bottom - inset, box.Right - inset, box.Top + inset);
                }
            else if (Checked)
                using (var pen = new Pen(Enabled ? p.Text : p.DisabledText, 2))
                    e.Graphics.DrawLines(pen, new Point[] {
                        new Point(box.Left + size / 5, box.Top + size / 2),
                        new Point(box.Left + size * 2 / 5, box.Top + size * 3 / 4),
                        new Point(box.Left + size * 4 / 5, box.Top + size / 4) });
            var text = new Rectangle(size + 6, 0, Math.Max(0, Width - size - 6), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, text, Enabled ? p.Text : p.DisabledText,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, text, p.Text, BackColor);
        }
    }

    internal sealed class RefreshToggle : CheckBox
    {
        public ThemePalette Palette;
        public RefreshToggle()
        {
            AutoSize = false;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96F;
            int width = (int)Math.Round(32 * scale), height = (int)Math.Round(18 * scale);
            Rectangle track = new Rectangle(1, (Height - height) / 2, width, height);
            using (var shape = BrandButton.RoundRect(track, height / 2F))
            using (var fill = new SolidBrush(Checked ? p.Accent : p.SurfaceAlt))
            using (var edge = new Pen(p.Border))
            { e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(edge, shape); }
            int inset = Math.Max(2, (int)(3 * scale)), diameter = height - 2 * inset;
            using (var thumb = new SolidBrush(Checked ? p.AccentText : p.Muted))
                e.Graphics.FillEllipse(thumb, Checked ? track.Right - inset - diameter : track.Left + inset,
                    track.Top + inset, diameter, diameter);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(track.Right + (int)(8 * scale), 0,
                Math.Max(1, Width - track.Right - (int)(8 * scale)), Height), p.Text,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, p.Text, BackColor);
        }
    }

    internal sealed class ThemeToggleButton : Button
    {
        public ThemePalette Palette;
        public bool Dark;
        private bool hovered;

        public ThemeToggleButton()
        {
            Text = String.Empty;
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            e.Graphics.Clear(Parent == null ? p.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96F;
            Rectangle bounds = new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3));
            Color track = hovered ? p.Selection : p.SurfaceAlt;
            using (GraphicsPath path = BrandButton.RoundRect(bounds, bounds.Height / 2F))
            using (Brush fill = new SolidBrush(track))
            using (Pen edge = new Pen(p.Border))
            {
                e.Graphics.FillPath(fill, path);
                e.Graphics.DrawPath(edge, path);
            }
            float radius = Math.Min(bounds.Height - 6F * scale, bounds.Width / 2F - 4F * scale) / 2F;
            float left = bounds.Left + bounds.Width * 0.25F;
            float right = bounds.Left + bounds.Width * 0.75F;
            float centerY = bounds.Top + bounds.Height / 2F;
            float active = Dark ? right : left;
            using (Brush fill = new SolidBrush(p.Accent))
                e.Graphics.FillEllipse(fill, active - radius, centerY - radius, radius * 2, radius * 2);

            Color sunColor = Dark ? p.Muted : p.AccentText;
            using (Pen pen = new Pen(sunColor, Math.Max(1.3F, 1.5F * scale)))
            {
                e.Graphics.DrawEllipse(pen, left - 3.8F * scale, centerY - 3.8F * scale, 7.6F * scale, 7.6F * scale);
                for (int i = 0; i < 8; i++)
                {
                    double angle = i * Math.PI / 4F;
                    e.Graphics.DrawLine(pen,
                        left + (float)Math.Cos(angle) * 6F * scale, centerY + (float)Math.Sin(angle) * 6F * scale,
                        left + (float)Math.Cos(angle) * 8.1F * scale, centerY + (float)Math.Sin(angle) * 8.1F * scale);
                }
            }
            Color moonColor = Dark ? p.AccentText : p.Muted;
            Color moonBackground = Dark ? p.Accent : track;
            using (Brush moon = new SolidBrush(moonColor))
            using (Brush cutout = new SolidBrush(moonBackground))
            {
                e.Graphics.FillEllipse(moon, right - 7F * scale, centerY - 7F * scale, 14F * scale, 14F * scale);
                e.Graphics.FillEllipse(cutout, right - 2F * scale, centerY - 9F * scale, 12F * scale, 12F * scale);
            }
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -4, -4), p.Text, track);
        }
    }

    internal sealed class BrandButton : Button
    {
        public ThemePalette Palette;
        public bool Primary, Destructive;
        public bool Selected { get; set; }
        private bool hovered;
        private Font boldFont;
        private Bitmap surface;
        private Color surfaceFill, surfaceEdge, surfaceBackground;
        private float surfaceDpi;

        public BrandButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            Cursor = Cursors.Hand;
            UseVisualStyleBackColor = false;
        }

        protected override void OnFontChanged(EventArgs e)
        {
            if (!Font.Bold)
            {
                Font previous = boldFont;
                boldFont = new Font(Font, Font.Style | FontStyle.Bold);
                Font = boldFont;
                if (previous != null) previous.Dispose();
            }
            base.OnFontChanged(e);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && boldFont != null) { boldFont.Dispose(); boldFont = null; }
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            Color fill = !Enabled ? p.DisabledSurface : ((Primary || Selected) ? p.Accent : p.Surface);
            Color text = !Enabled ? p.DisabledText : ((Primary || Selected) ? p.AccentText : (Destructive ? p.Danger : p.Text));
            Color edge = !Enabled ? p.Border : ((Primary || Selected) ? p.Accent : (Destructive ? p.Danger : p.Border));
            if (!p.Dark)
            {
                fill = Enabled ? p.Accent : Blend(p.Accent, p.Canvas, 0.40F);
                text = p.AccentText;
                edge = fill;
            }
            if (hovered && Enabled) fill = Blend(fill, p.Text, 0.07F);
            Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            Color background = Parent == null ? p.Canvas : Parent.BackColor;
            EnsureSurface(fill, edge, background, e.Graphics.DpiX);
            e.Graphics.DrawImageUnscaled(surface, 0, 0);
            Rectangle textBounds = new Rectangle(8, 3, Math.Max(1, Width - 16), Math.Max(1, Height - 6));
            TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            if (Focused && ShowFocusCues)
                ControlPaint.DrawFocusRectangle(e.Graphics, Rectangle.Inflate(bounds, -5, -5), text, fill);
        }

        private void EnsureSurface(Color fill, Color edge, Color background, float dpi)
        {
            if (surface != null && surface.Size == ClientSize && surfaceFill == fill &&
                surfaceEdge == edge && surfaceBackground == background && surfaceDpi == dpi) return;
            if (surface != null) { surface.Dispose(); surface = null; }
            int width = Math.Max(1, Width), height = Math.Max(1, Height);
            var outer = new Rectangle(1, 1, Math.Max(0, width - 2), Math.Max(0, height - 2));
            var inner = Rectangle.Inflate(outer, -1, -1);
            float radius = Math.Min(10F * dpi / 96F, Math.Min(outer.Width, outer.Height) / 2F);
            int[] pixels = new int[width * height];
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    // Area coverage at 8x8 subpixel precision, without a blur/resampling filter.
                    // Only corner pixels need sampling; straight edges remain exact whole pixels.
                    float outside = Coverage(x, y, outer, radius);
                    float inside = edge == fill ? outside : Coverage(x, y, inner, Math.Max(0, radius - 1));
                    float border = outside - inside, canvas = 1F - outside;
                    pixels[y * width + x] = Color.FromArgb(
                        (int)Math.Round(fill.R * inside + edge.R * border + background.R * canvas),
                        (int)Math.Round(fill.G * inside + edge.G * border + background.G * canvas),
                        (int)Math.Round(fill.B * inside + edge.B * border + background.B * canvas)).ToArgb();
                }
            surface = new Bitmap(width, height, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            var data = surface.LockBits(new Rectangle(0, 0, width, height), System.Drawing.Imaging.ImageLockMode.WriteOnly,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            try { System.Runtime.InteropServices.Marshal.Copy(pixels, 0, data.Scan0, pixels.Length); }
            finally { surface.UnlockBits(data); }
            surfaceFill = fill; surfaceEdge = edge; surfaceBackground = background; surfaceDpi = dpi;
        }

        private static float Coverage(int x, int y, Rectangle box, float radius)
        {
            if (box.Width <= 0 || box.Height <= 0 || x < box.Left || x >= box.Right || y < box.Top || y >= box.Bottom) return 0;
            if (radius <= 0 || (x >= box.Left + radius && x + 1 <= box.Right - radius) ||
                (y >= box.Top + radius && y + 1 <= box.Bottom - radius)) return 1;
            int count = 0;
            for (int sy = 0; sy < 8; sy++)
                for (int sx = 0; sx < 8; sx++)
                {
                    float px = x + (sx + 0.5F) / 8F, py = y + (sy + 0.5F) / 8F;
                    float cx = Math.Max(box.Left + radius, Math.Min(px, box.Right - radius));
                    float cy = Math.Max(box.Top + radius, Math.Min(py, box.Bottom - radius));
                    if ((px - cx) * (px - cx) + (py - cy) * (py - cy) <= radius * radius) count++;
                }
            return count / 64F;
        }

        internal static GraphicsPath RoundRect(Rectangle bounds, float radius)
        {
            float diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));
            GraphicsPath path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        private static Color Blend(Color a, Color b, float weight)
        {
            return Color.FromArgb((int)(a.R * (1 - weight) + b.R * weight), (int)(a.G * (1 - weight) + b.G * weight), (int)(a.B * (1 - weight) + b.B * weight));
        }
    }
}

namespace ServiceTool
{
    // Keeps the native ComboBox keyboard and popup behavior while painting its
    // closed-state border and arrow in the same palette as owner-drawn items.
    internal sealed class BrandComboBox : ComboBox
    {
        public ThemePalette Palette;
        private bool hovered;
        private const int WM_PAINT = 0x000F;
        private const int WM_PRINT = 0x0317;
        private const int WM_PRINTCLIENT = 0x0318;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            public int Left, Top, Right, Bottom;
            public Rectangle ToRectangle() { return Rectangle.FromLTRB(Left, Top, Right, Bottom); }
        }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct ComboBoxInformation
        {
            public int Size;
            public NativeRectangle Item, Button;
            public int ButtonState;
            public IntPtr ComboHandle, ItemHandle, ListHandle;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool GetComboBoxInfo(IntPtr comboHandle, ref ComboBoxInformation information);

        public BrandComboBox()
        {
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnDropDown(EventArgs e) { Invalidate(); base.OnDropDown(e); }
        protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }

        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (Palette == null || DropDownStyle == ComboBoxStyle.Simple || IsDisposed) return;
            if ((message.Msg == WM_PRINT || message.Msg == WM_PRINTCLIENT) && message.WParam != IntPtr.Zero)
            {
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) PaintChrome(graphics);
            }
            else if (message.Msg == WM_PAINT && IsHandleCreated)
            {
                using (Graphics graphics = Graphics.FromHwnd(Handle)) PaintChrome(graphics);
            }
        }

        private void PaintChrome(Graphics graphics)
        {
            if (ClientSize.Width < 4 || ClientSize.Height < 4) return;
            ThemePalette p = Palette;
            Rectangle button = DropDownButtonRectangle();
            Rectangle inner = new Rectangle(1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
            Rectangle fill = Rectangle.Intersect(inner, Rectangle.FromLTRB(button.Left - 1, 1, button.Right + 1, ClientSize.Height - 1));
            Color background = !Enabled ? p.DisabledSurface : (hovered || DroppedDown ? p.Selection : p.SurfaceAlt);
            Color foreground = !Enabled ? p.DisabledText : (hovered || DroppedDown ? p.SelectionText : p.Text);
            using (Brush brush = new SolidBrush(background)) graphics.FillRectangle(brush, fill);
            float scale = graphics.DpiX / 96F;
            float centerX = button.Left + button.Width / 2F;
            float centerY = ClientSize.Height / 2F + scale;
            float halfWidth = Math.Max(3F, 3.5F * scale);
            PointF[] arrow =
            {
                new PointF(centerX - halfWidth, centerY - 1.5F * scale),
                new PointF(centerX + halfWidth, centerY - 1.5F * scale),
                new PointF(centerX, centerY + 2F * scale)
            };
            System.Drawing.Drawing2D.SmoothingMode previous = graphics.SmoothingMode;
            graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (Brush brush = new SolidBrush(foreground)) graphics.FillPolygon(brush, arrow);
            graphics.SmoothingMode = previous;
            using (Pen pen = new Pen(Enabled && Focused ? p.Accent : p.Border))
                graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        private Rectangle DropDownButtonRectangle()
        {
            ComboBoxInformation information = new ComboBoxInformation();
            information.Size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(ComboBoxInformation));
            if (GetComboBoxInfo(Handle, ref information))
            {
                Rectangle candidate = information.Button.ToRectangle();
                if (ClientRectangle.Contains(candidate) && candidate.Width > 0 && candidate.Height > 0) return candidate;
                candidate.Location = PointToClient(candidate.Location);
                if (ClientRectangle.Contains(candidate) && candidate.Width > 0 && candidate.Height > 0) return candidate;
            }
            int width = Math.Min(ClientSize.Width - 2, Math.Max(16, SystemInformation.VerticalScrollBarWidth));
            return new Rectangle(RightToLeft == RightToLeft.Yes ? 1 : ClientSize.Width - width - 1,
                1, width, ClientSize.Height - 2);
        }
    }
}
