using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Security;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class NativeWindowTheme
    {
        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);
        internal static void Bind(Form form, Func<bool> dark)
        {
            form.HandleCreated += delegate { Apply(form, dark()); };
            Apply(form, dark());
        }
        internal static void Apply(Form form, bool dark)
        {
            if (!form.IsHandleCreated) return;
            try
            {
                int value = dark ? 1 : 0;
                if (DwmSetWindowAttribute(form.Handle, 20, ref value, sizeof(int)) < 0)
                    DwmSetWindowAttribute(form.Handle, 19, ref value, sizeof(int));
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
        }
    }

    internal sealed class ThemePalette
    {
        public bool Dark;
        public Color Canvas, Surface, SurfaceAlt, Text, Muted, Border;
        public Color InputBorder { get { return Dark ? Border : Accent; } }
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
                Canvas = Hex("F7F9F8"), Surface = Color.White, SurfaceAlt = Hex("E9F2EC"),
                Text = Hex("37352F"), Muted = Hex("506359"), Border = Hex("83958E"),
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

    internal sealed partial class ServiceGrid : DataGridView
    {
        public ServiceGrid() { DoubleBuffered = true; SetStyle(ControlStyles.ResizeRedraw, true); }
    }

    internal sealed class InputCaptionLabel : Label
    {
        protected override void OnPaint(PaintEventArgs e)
        {
            var bounds=new Rectangle(Padding.Left,Padding.Top,Math.Max(1,ClientSize.Width-Padding.Horizontal),Math.Max(1,ClientSize.Height-Padding.Vertical));
            TextFormatFlags flags=TextFormatFlags.NoPadding|TextFormatFlags.NoPrefix|TextFormatFlags.SingleLine;
            flags|=TextAlign==ContentAlignment.BottomLeft?TextFormatFlags.Bottom:TextFormatFlags.VerticalCenter;
            if(AutoEllipsis)flags|=TextFormatFlags.EndEllipsis;
            TextRenderer.DrawText(e.Graphics,Text,Font,bounds,ForeColor,flags);
        }
    }

    // Measure the visible glyph edge, not a Label's logical box or GDI overhang.
    // Cached by text prefix, font and DPI; no per-paint bitmap allocation after warm-up.
    internal static class TextInkMetrics
    {
        private static readonly System.Collections.Generic.Dictionary<string,int> cache=new System.Collections.Generic.Dictionary<string,int>();
        internal static int Left(string text,Font font,Graphics target,bool noPadding)
        {
            if(String.IsNullOrEmpty(text))return 0;
            string sample=text.Substring(0,Math.Min(12,text.Length));
            string key=sample+"|"+font.Name+"|"+font.SizeInPoints.ToString(System.Globalization.CultureInfo.InvariantCulture)+"|"+font.Style+"|"+target.DpiX+"|"+target.DpiY+"|"+noPadding;
            int result;if(cache.TryGetValue(key,out result))return result;
            int height=Math.Max(32,(int)Math.Ceiling(font.GetHeight(target))*3);
            using(var bitmap=new Bitmap(Math.Max(256,height*8),height))
            {
                bitmap.SetResolution(target.DpiX,target.DpiY);
                using(var graphics=Graphics.FromImage(bitmap))
                {
                    graphics.Clear(Color.White);
                    TextFormatFlags flags=TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix;
                    if(noPadding)flags|=TextFormatFlags.NoPadding;
                    TextRenderer.DrawText(graphics,sample,font,new Rectangle(8,0,bitmap.Width-16,height),Color.Black,flags);
                }
                result=0;bool found=false;
                for(int x=0;x<bitmap.Width&&!found;x++)for(int y=0;y<height;y++)
                {Color c=bitmap.GetPixel(x,y);if(c.R<224||c.G<224||c.B<224){result=x-8;found=true;break;}}
            }
            cache[key]=result;return result;
        }
    }

    internal sealed class FlushLabel : Label
    {
        public bool SingleLine { get; set; }
        internal Func<Graphics,int> InkLeft { get; set; }
        internal bool WrapLines { get; set; }
        protected override void OnPaint(PaintEventArgs e)
        {
            TextFormatFlags flags = TextFormatFlags.PreserveGraphicsTranslateTransform | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix |
                (SingleLine ? TextFormatFlags.SingleLine : TextFormatFlags.WordBreak);
            if (TextAlign == ContentAlignment.MiddleLeft && !WrapLines) flags |= TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine;
            if (AutoEllipsis) flags |= TextFormatFlags.EndEllipsis;
            Rectangle bounds=ClientRectangle;
            if(InkLeft!=null&&!String.IsNullOrEmpty(Text))
            {
                int shift=InkLeft(e.Graphics)-TextInkMetrics.Left(Text,Font,e.Graphics,true);
                bounds.X+=shift;bounds.Width-=shift;
            }
            if(WrapLines)
            {
                int height=TextRenderer.MeasureText(e.Graphics,Text,Font,new Size(Math.Max(1,bounds.Width),Int32.MaxValue),flags).Height;
                bounds.Y+=Math.Max(0,(bounds.Height-height)/2);
            }
            TextRenderer.DrawText(e.Graphics, Text, Font, bounds, ForeColor, flags);
        }
    }

    internal sealed class BrandMotion : IDisposable
    {
        private readonly Control owner;
        private readonly Timer timer = new Timer { Interval = 15 };
        private float target;
        public float Value { get; private set; }
        public BrandMotion(Control control)
        {
            owner = control;
            timer.Tick += delegate {
                float delta = target - Value;
                Value = Math.Abs(delta) < .015F ? target : Value + delta * .28F;
                owner.Invalidate();
                if (Value == target) timer.Stop();
            };
        }
        public void Move(float value)
        {
            target = value;
            if (!owner.Visible || !owner.IsHandleCreated || !SystemInformation.IsMenuAnimationEnabled)
            { Value = target; timer.Stop(); owner.Invalidate(); }
            else timer.Start();
        }
        public void Dispose() { timer.Dispose(); }
        public static Color Mix(Color a, Color b, float t)
        {
            return Color.FromArgb((int)(a.R+(b.R-a.R)*t), (int)(a.G+(b.G-a.G)*t), (int)(a.B+(b.B-a.B)*t));
        }
    }

    internal class BrandCheckBox : CheckBox
    {
        public ThemePalette Palette;
        private bool hovered;

        public BrandCheckBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Opaque, true);
        }

        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); Invalidate(); }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hovered = false; Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96F;
            int size = Math.Min((int)(20 * scale), Math.Max(1, Height - 4));
            Rectangle box = new Rectangle(1, (Height - size) / 2, size, size);
            Color fill = !Enabled ? p.DisabledSurface : (Checked ? p.Accent : p.Surface);
            using (Brush brush = new SolidBrush(fill)) e.Graphics.FillEllipse(brush, box);
            using (Pen pen = new Pen(Enabled && (Checked || Focused || hovered) ? p.Accent : p.Border, Math.Max(1, scale)))
                e.Graphics.DrawEllipse(pen, box);
            if (Checked)
            {
                int inset = Math.Max(3, size / 3);
                using (Brush brush = new SolidBrush(Enabled ? p.AccentText : p.DisabledText))
                    e.Graphics.FillEllipse(brush, Rectangle.Inflate(box, -inset, -inset));
            }
            Rectangle label = new Rectangle(box.Right + (int)(9 * scale), 0, Math.Max(0, Width - box.Right - (int)(9 * scale)), Height);
            TextRenderer.DrawText(e.Graphics, Text, Font, label, Enabled ? p.Text : p.Muted,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix | TextFormatFlags.PreserveGraphicsTranslateTransform);
            if (Enabled && Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, label, p.Text, BackColor);
        }
    }

    internal sealed class RefreshToggle : CheckBox
    {
        public ThemePalette Palette;
        private readonly BrandMotion motion;
        public RefreshToggle()
        {
            motion = new BrandMotion(this);
            AutoSize = false;
            Cursor = Cursors.Hand;
            AccessibleRole = AccessibleRole.CheckButton;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }
        protected override void OnCheckedChanged(EventArgs e) { base.OnCheckedChanged(e); if (motion != null) motion.Move(Checked ? 1 : 0); Invalidate(); }
        protected override void Dispose(bool disposing) { if (disposing) motion.Dispose(); base.Dispose(disposing); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }
        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96F;
            int width = (int)Math.Round(42 * scale), height = (int)Math.Round(22 * scale);
            Rectangle track = new Rectangle(1, (Height - height) / 2, width, height);
            using (var shape = BrandButton.RoundRect(track, height / 2F))
            using (var fill = new SolidBrush(!Enabled ? p.DisabledSurface : BrandMotion.Mix(p.Surface, p.Accent, motion.Value)))
            using (var edge = new Pen(Enabled && (Checked || Focused) ? p.Accent : p.Border))
            { e.Graphics.FillPath(fill, shape); e.Graphics.DrawPath(edge, shape); }
            int inset = Math.Max(2, (int)(3 * scale)), diameter = height - 2 * inset;
            using (var thumb = new SolidBrush(!Enabled ? p.DisabledText : BrandMotion.Mix(p.Muted, Color.White, motion.Value)))
                e.Graphics.FillEllipse(thumb, track.Left + inset + (track.Width - inset * 2 - diameter) * motion.Value,
                    track.Top + inset, diameter, diameter);
            TextRenderer.DrawText(e.Graphics, Text, Font, new Rectangle(track.Right + (int)(8 * scale), 0,
                Math.Max(1, Width - track.Right - (int)(8 * scale)), Height), Enabled ? p.Text : p.DisabledText,
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, p.Text, BackColor);
        }
    }

    internal sealed class ThemeToggleButton : Button
    {
        public ThemePalette Palette;
        public bool Dark;
        private readonly BrandMotion hoverMotion, pressMotion;

        public ThemeToggleButton()
        {
            hoverMotion = new BrandMotion(this); pressMotion = new BrandMotion(this);
            Text = String.Empty;
            AccessibleRole = AccessibleRole.PushButton;
            Cursor = Cursors.Hand;
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { hoverMotion.Move(1); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverMotion.Move(0); pressMotion.Move(0); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void Dispose(bool disposing) { if (disposing) { hoverMotion.Dispose(); pressMotion.Dispose(); } base.Dispose(disposing); }
        protected override void OnMouseDown(MouseEventArgs e) { if (Enabled && e.Button == MouseButtons.Left) pressMotion.Move(1); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressMotion.Move(0); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) pressMotion.Move(0); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (Enabled && e.KeyCode == Keys.Space) pressMotion.Move(1); base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressMotion.Move(0); base.OnKeyUp(e); }
        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            e.Graphics.Clear(Parent == null ? p.Canvas : Parent.BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float scale = e.Graphics.DpiX / 96F;
            Rectangle bounds = new Rectangle(1, 1, Math.Max(2, Width - 3), Math.Max(2, Height - 3));
            Color track = BrandMotion.Mix(p.SurfaceAlt, p.Selection, hoverMotion.Value);
            if (Enabled) track = BrandMotion.Mix(track, Color.Black, .12F * pressMotion.Value);
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
        private readonly BrandMotion hoverMotion, pressMotion;
        private Font boldFont;
        private Bitmap surface;
        private Color surfaceFill, surfaceEdge, surfaceBackground;
        private float surfaceDpi;

        public BrandButton()
        {
            hoverMotion = new BrandMotion(this); pressMotion = new BrandMotion(this);
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
            if (disposing) { hoverMotion.Dispose(); pressMotion.Dispose(); }
            base.Dispose(disposing);
            if (disposing && boldFont != null) { boldFont.Dispose(); boldFont = null; }
            if (disposing && surface != null) { surface.Dispose(); surface = null; }
        }

        protected override void OnMouseEnter(EventArgs e) { hoverMotion.Move(1); Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hoverMotion.Move(0); pressMotion.Move(0); Invalidate(); base.OnMouseLeave(e); }
        protected override void OnEnabledChanged(EventArgs e) { if (!Enabled) { hoverMotion.Move(0); pressMotion.Move(0); } Invalidate(); base.OnEnabledChanged(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { pressMotion.Move(0); Invalidate(); base.OnLostFocus(e); }
        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left && Enabled) pressMotion.Move(1); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { pressMotion.Move(0); base.OnMouseUp(e); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) pressMotion.Move(0); base.OnMouseCaptureChanged(e); }
        protected override void OnKeyDown(KeyEventArgs e) { if (e.KeyCode == Keys.Space && Enabled) pressMotion.Move(1); base.OnKeyDown(e); }
        protected override void OnKeyUp(KeyEventArgs e) { if (e.KeyCode == Keys.Space) pressMotion.Move(0); base.OnKeyUp(e); }

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
            if (Enabled)
            {
                fill = Blend(fill, Color.White, .05F * hoverMotion.Value);
                fill = Blend(fill, Color.Black, .20F * pressMotion.Value);
                if (Primary || Selected || !p.Dark) edge = fill;
            }
            Rectangle bounds = new Rectangle(1, 1, Math.Max(1, Width - 2), Math.Max(1, Height - 2));
            Color background = Parent == null ? p.Canvas : Parent.BackColor;
            EnsureSurface(fill, edge, background, e.Graphics.DpiX);
            e.Graphics.DrawImageUnscaled(surface, 0, 0);
            int offset = (int)Math.Round(pressMotion.Value * 2 * e.Graphics.DpiX / 96F);
            Rectangle textBounds = new Rectangle(8, 3 + offset, Math.Max(1, Width - 16), Math.Max(1, Height - 6));
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
            float radius = Math.Min(6F * dpi / 96F, Math.Min(outer.Width, outer.Height) / 2F);
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
    // Retain the native edit behavior, selection and password masking; paint only its border.
    internal sealed class BrandTextBox : TextBox
    {
        private ThemePalette palette;
        public ThemePalette Palette { get { return palette; } set { palette=value; Invalidate(); PaintBorder(); } }
        public BrandTextBox() { BorderStyle=BorderStyle.FixedSingle; }
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr handle);
        [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr handle, IntPtr dc);
        protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); PaintBorder(); }
        protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); PaintBorder(); }
        protected override void OnEnabledChanged(EventArgs e) { base.OnEnabledChanged(e); Invalidate(); }
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if (message.Msg == 0x000F || message.Msg == 0x0085 || message.Msg == 0x0317 || message.Msg == 0x0318) PaintBorder(message.Msg==0x0317 ? message.WParam : IntPtr.Zero);
        }
        private void PaintBorder(IntPtr printDc = default(IntPtr))
        {
            if (!IsHandleCreated || IsDisposed || BorderStyle != BorderStyle.FixedSingle) return;
            ThemePalette p = Palette ?? ThemePalette.Create(false);
            IntPtr dc = printDc==IntPtr.Zero ? GetWindowDC(Handle) : printDc;
            if (dc == IntPtr.Zero) return;
            try
            {
                using (Graphics graphics = Graphics.FromHdc(dc))
                using (Pen edge = new Pen(Enabled && Focused ? p.Accent : p.InputBorder))
                    graphics.DrawRectangle(edge, 0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            }
            finally { if(printDc==IntPtr.Zero)ReleaseDC(Handle, dc); }
        }
    }

    // Keep the native popup and keyboard behavior with a palette-aware border.
    internal sealed class BrandComboBox : ComboBox
    {
        private readonly PopupFrame popupFrame;
        internal bool UseManagedPopup;
        private ToolStripDropDown managedPopup;
        private bool suppressOwnerClick;
        public ThemePalette Palette;
        public bool HoverHighlight = false;
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
        [System.Runtime.InteropServices.DllImport("uxtheme.dll",CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
        private static extern int SetWindowTheme(IntPtr window,string application,string identifiers);

        public BrandComboBox()
        {
            popupFrame=new PopupFrame(this);
            SetStyle(ControlStyles.ResizeRedraw|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer, true);FlatStyle=FlatStyle.Flat;
        }

        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e);SetWindowTheme(Handle,String.Empty,String.Empty);BindPopup(); }
        protected override void OnMouseEnter(EventArgs e) { hovered = true;if(HoverHighlight)Invalidate();base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false;if(HoverHighlight)Invalidate();base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
        private void BindPopup() {
            if (popupFrame == null) return;
            var info=new ComboBoxInformation();info.Size=System.Runtime.InteropServices.Marshal.SizeOf(typeof(ComboBoxInformation));
            if(GetComboBoxInfo(Handle,ref info)&&info.ListHandle!=IntPtr.Zero){popupFrame.Bind(info.ListHandle);}
        }
        protected override void OnDropDown(EventArgs e) {
            if (!UseManagedPopup) { BindPopup(); popupFrame.ApplyTheme(); }
            base.OnDropDown(e);
            Invalidate();
        }
        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (managedPopup != null) managedPopup.Close();
            if (popupFrame != null) popupFrame.Detach();
            base.OnHandleDestroyed(e);
        }

        private sealed class FlatPopupRenderer : ToolStripRenderer
        {
            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                using (var fill = new SolidBrush(e.ToolStrip.BackColor))
                    e.Graphics.FillRectangle(fill, e.AffectedBounds);
            }
        }

        private void ShowManagedPopup()
        {
            if (!Enabled || Items.Count == 0 || managedPopup != null || Palette == null) return;
            OnDropDown(EventArgs.Empty);
            int rowHeight = Math.Max(ItemHeight, Font.Height + 8);
            int width = Math.Max(Width, DropDownWidth);
            var list = new ListBox
            {
                Font = Font, BackColor = Palette.SurfaceAlt, ForeColor = Palette.Text,
                BorderStyle = BorderStyle.None, IntegralHeight = false,
                DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = rowHeight
            };
            foreach (object item in Items) list.Items.Add(GetItemText(item));
            list.SelectedIndex = SelectedIndex;
            list.DrawItem += delegate(object sender, DrawItemEventArgs e) { OnDrawItem(e); };

            var popup = new ToolStripDropDown
            {
                AutoSize = false, Padding = new Padding(1), BackColor = Palette.Border,
                DropShadowEnabled = false, Renderer = new FlatPopupRenderer(),
                LayoutStyle = ToolStripLayoutStyle.VerticalStackWithOverflow
            };
            int height = Math.Min(Items.Count, Math.Max(1, MaxDropDownItems)) * rowHeight;
            var host = new ToolStripControlHost(list)
            {
                AutoSize = false, Margin = Padding.Empty, Padding = Padding.Empty, Size = new Size(width - 2, height)
            };
            popup.Items.Add(host);
            popup.Size = new Size(width, height + 2);
            managedPopup = popup;

            Action commit = delegate
            {
                if (list.SelectedIndex < 0) return;
                SelectedIndex = list.SelectedIndex;
                OnSelectionChangeCommitted(EventArgs.Empty);
                popup.Close();
                if (CanFocus) Focus();
            };
            list.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                int index = list.IndexFromPoint(e.Location);
                if (e.Button == MouseButtons.Left && index >= 0) { list.SelectedIndex = index; commit(); }
            };
            list.KeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; commit(); }
                else if (e.KeyCode == Keys.Escape || e.KeyCode == Keys.F4 || (e.Alt && e.KeyCode == Keys.Up))
                {
                    e.SuppressKeyPress = true;
                    popup.Close();
                    if (CanFocus) Focus();
                }
            };
            popup.Closed += delegate(object sender, ToolStripDropDownClosedEventArgs e)
            {
                managedPopup = null;
                suppressOwnerClick = e.CloseReason == ToolStripDropDownCloseReason.AppClicked &&
                    RectangleToScreen(ClientRectangle).Contains(Cursor.Position);
                if (suppressOwnerClick && IsHandleCreated)
                    BeginInvoke((MethodInvoker)delegate { suppressOwnerClick = false; });
                OnDropDownClosed(EventArgs.Empty);
                if (IsHandleCreated) BeginInvoke((MethodInvoker)delegate { popup.Dispose(); });
                else popup.Dispose();
            };
            Point below = PointToScreen(new Point(0, Height));
            Rectangle work = Screen.FromControl(this).WorkingArea;
            popup.Show(this, new Point(0, below.Y + popup.Height > work.Bottom ? -popup.Height : Height));
            list.Focus();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (UseManagedPopup && (keyData == Keys.F4 || keyData == (Keys.Alt | Keys.Down) || keyData == Keys.Space))
            {
                if (managedPopup == null) ShowManagedPopup();
                else managedPopup.Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }
        protected override void OnDropDownClosed(EventArgs e) { Invalidate(); base.OnDropDownClosed(e); }

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct PaintInfo {public IntPtr Dc;public int Erase;public Rectangle Bounds;public int Restore,Update;[System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValArray,SizeConst=32)]public byte[] Reserved;}
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern IntPtr BeginPaint(IntPtr handle,out PaintInfo info);
        [System.Runtime.InteropServices.DllImport("user32.dll")]private static extern bool EndPaint(IntPtr handle,ref PaintInfo info);
        protected override void WndProc(ref Message message)
        {
            if (UseManagedPopup)
            {
                switch (message.Msg)
                {
                    case 0x014F: // CB_SHOWDROPDOWN
                        if (message.WParam == IntPtr.Zero) { if (managedPopup != null) managedPopup.Close(); }
                        else ShowManagedPopup();
                        message.Result = (IntPtr)1;
                        return;
                    case 0x0157: // CB_GETDROPPEDSTATE
                        message.Result = managedPopup != null ? (IntPtr)1 : IntPtr.Zero;
                        return;
                    case 0x0201: // WM_LBUTTONDOWN
                        if (CanFocus) Focus();
                        if (suppressOwnerClick) suppressOwnerClick = false;
                        else if (managedPopup == null) ShowManagedPopup();
                        else managedPopup.Close();
                        return;
                    case 0x0202: // WM_LBUTTONUP
                    case 0x0203: // WM_LBUTTONDBLCLK
                        return;
                }
            }
            if(Palette!=null&&DropDownStyle==ComboBoxStyle.DropDownList&&message.Msg==WM_PAINT){PaintInfo paint;IntPtr dc=BeginPaint(Handle,out paint);try{using(var graphics=Graphics.FromHdc(dc))PaintChrome(graphics);}finally{EndPaint(Handle,ref paint);}return;}
            if(Palette!=null&&DropDownStyle==ComboBoxStyle.DropDownList&&message.Msg==0x14){message.Result=(IntPtr)1;return;}
            if (Palette != null && popupFrame != null && message.Msg == 0x0134)
            {
                IntPtr brush = popupFrame.BackgroundBrush();
                if (brush != IntPtr.Zero) { message.Result = brush; return; }
            }
            if (Palette != null && DropDownStyle == ComboBoxStyle.DropDownList &&
                (message.Msg == WM_PRINT || message.Msg == WM_PRINTCLIENT) && message.WParam != IntPtr.Zero)
            {
                using (Graphics graphics = Graphics.FromHdc(message.WParam)) PaintChrome(graphics);
                return;
            }
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
              if(DropDownStyle==ComboBoxStyle.DropDownList)
              {
                  Color surface=Enabled?p.SurfaceAlt:p.DisabledSurface;
                  using(Brush brush=new SolidBrush(surface))graphics.FillRectangle(brush,ClientRectangle);
                  TextRenderer.DrawText(graphics,Text,Font,new Rectangle(7,1,Math.Max(1,button.Left-12),ClientSize.Height-2),Enabled?p.Text:p.DisabledText,
                      TextFormatFlags.VerticalCenter|TextFormatFlags.SingleLine|TextFormatFlags.NoPrefix|TextFormatFlags.EndEllipsis|TextFormatFlags.PreserveGraphicsTranslateTransform);
              }
            Rectangle inner = new Rectangle(1, 1, ClientSize.Width - 2, ClientSize.Height - 2);
            Rectangle fill = Rectangle.Intersect(inner, Rectangle.FromLTRB(button.Left - 1, 1, button.Right + 1, ClientSize.Height - 1));
            bool highlighted=HoverHighlight&&hovered;
            Color background = !Enabled ? p.DisabledSurface : (highlighted ? p.Selection : p.SurfaceAlt);
            Color foreground = !Enabled ? p.DisabledText : (highlighted ? p.SelectionText : p.Text);
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
            using (Pen pen = new Pen(p.Border))
                graphics.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);
        }

        // The list is a separate native window. Handle erase and frame before
        // Windows can draw the default light background or border.
        private sealed class PopupFrame : NativeWindow
        {
            private readonly BrandComboBox owner;
            private IntPtr brush;
            private Color brushColor;
            private bool? darkTheme;

            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetWindowDC(IntPtr window);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr window, IntPtr dc);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out NativeRectangle rect);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr window, out NativeRectangle rect);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr window, int index);
            [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int FillRect(IntPtr dc, ref NativeRectangle rect, IntPtr brush);
            [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateSolidBrush(int color);
            [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr handle);
            [System.Runtime.InteropServices.DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left, int top, int right, int bottom);

            internal PopupFrame(BrandComboBox owner) { this.owner = owner; }

            internal void Bind(IntPtr handle)
            {
                if (Handle == handle) return;
                Detach();
                AssignHandle(handle);
            }

            internal void ApplyTheme()
            {
                if (Handle == IntPtr.Zero || owner.Palette == null || darkTheme == owner.Palette.Dark) return;
                darkTheme = owner.Palette.Dark;
                SetWindowTheme(Handle, darkTheme.Value ? "DarkMode_Explorer" : "Explorer", null);
            }

            internal IntPtr BackgroundBrush()
            {
                if (owner.Palette == null) return IntPtr.Zero;
                Color color = owner.Palette.SurfaceAlt;
                if (brush != IntPtr.Zero && color == brushColor) return brush;
                if (brush != IntPtr.Zero) DeleteObject(brush);
                brushColor = color;
                brush = CreateSolidBrush(ColorTranslator.ToWin32(color));
                return brush;
            }

            internal void Detach()
            {
                if (Handle != IntPtr.Zero) ReleaseHandle();
                if (brush != IntPtr.Zero) { DeleteObject(brush); brush = IntPtr.Zero; }
                darkTheme = null;
            }

            protected override void WndProc(ref Message message)
            {
                if (owner.Palette != null)
                {
                    if (message.Msg == 0x0014 && message.WParam != IntPtr.Zero)
                    {
                        NativeRectangle client;
                        IntPtr fill = BackgroundBrush();
                        if (fill != IntPtr.Zero && GetClientRect(Handle, out client))
                        {
                            FillRect(message.WParam, ref client, fill);
                            message.Result = (IntPtr)1;
                            return;
                        }
                    }
                    if (message.Msg == 0x0086) { message.Result = (IntPtr)1; return; }
                    if (message.Msg == 0x0085 && PaintFrame(ref message)) return;
                }
                base.WndProc(ref message);
            }

            private bool PaintFrame(ref Message message)
            {
                NativeRectangle bounds;
                if (!GetWindowRect(Handle, out bounds)) return false;
                int width = bounds.Right - bounds.Left, height = bounds.Bottom - bounds.Top;
                if (width < 6 || height < 6) return false;
                if ((GetWindowLong(Handle, -16) & 0x00200000) != 0)
                {
                    // Preserve the native scrollbar while excluding the outer border.
                    IntPtr region = CreateRectRgn(bounds.Left + 2, bounds.Top + 2, bounds.Right - 2, bounds.Bottom - 2);
                    if (region == IntPtr.Zero) return false;
                    try
                    {
                        var scrollbarPaint = Message.Create(Handle, message.Msg, region, message.LParam);
                        base.WndProc(ref scrollbarPaint);
                    }
                    finally { DeleteObject(region); }
                }
                IntPtr dc = GetWindowDC(Handle);
                if (dc == IntPtr.Zero) return false;
                try
                {
                    using (var graphics = Graphics.FromHdc(dc))
                    using (var fill = new SolidBrush(owner.Palette.SurfaceAlt))
                    using (var pen = new Pen(owner.Palette.Border))
                    {
                        graphics.FillRectangle(fill, 0, 0, width, 2);
                        graphics.FillRectangle(fill, 0, height - 2, width, 2);
                        graphics.FillRectangle(fill, 0, 0, 2, height);
                        graphics.FillRectangle(fill, width - 2, 0, 2, height);
                        graphics.DrawRectangle(pen, 0, 0, width - 1, height - 1);
                    }
                    message.Result = IntPtr.Zero;
                    return true;
                }
                finally { ReleaseDC(Handle, dc); }
            }
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

namespace ServiceTool
{
    // Keep the picker slightly below the path frame in both modules.
    internal sealed class PathPickerRow : System.Windows.Forms.Panel
    {
        private readonly BrandTextBox field;
        private readonly BrandButton picker;
        private readonly int buttonWidth;
        private System.Drawing.Size pickerSize;
        private int pickerDrop=4;
        private bool arranging;
        internal bool FillAvailableWidth {get;set;}
        internal int PreferredRowHeight {get{return pickerSize.Height;}}
        internal int PreferredRowWidth {get{return PathFieldMetrics.Measure(field).Width+pickerSize.Width+(int)System.Math.Round(12F*pickerSize.Width/buttonWidth);}}
        internal PathPickerRow(BrandTextBox path,BrandButton button,int width)
        {
            field=path;picker=button;buttonWidth=width;
            SetStyle(System.Windows.Forms.ControlStyles.AllPaintingInWmPaint|System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer|System.Windows.Forms.ControlStyles.ResizeRedraw,true);
            Margin=System.Windows.Forms.Padding.Empty;
            field.Dock=picker.Dock=System.Windows.Forms.DockStyle.None;
            field.Anchor=picker.Anchor=System.Windows.Forms.AnchorStyles.Top|System.Windows.Forms.AnchorStyles.Left;
            field.Margin=picker.Margin=System.Windows.Forms.Padding.Empty;
            pickerSize=new System.Drawing.Size(buttonWidth,40);picker.Size=pickerSize;
            Controls.Add(field);Controls.Add(picker);
        }
        protected override void OnLayout(System.Windows.Forms.LayoutEventArgs e)
        {
            base.OnLayout(e);if(arranging||field==null||ClientSize.Width<1||ClientSize.Height<1)return;
            arranging=true;
            try
            {
                var measure=PathFieldMetrics.Measure(field);
                int width=pickerSize.Width,gap=(int)System.Math.Round(12F*width/buttonWidth);
                int height=System.Math.Min(ClientSize.Height,pickerSize.Height);
                int pathWidth=System.Math.Max(1,FillAvailableWidth?ClientSize.Width-gap-width:System.Math.Min(measure.Width,ClientSize.Width-gap-width));
                picker.SetBounds(pathWidth+gap,ClientSize.Height-height,width,height);
                field.SetBounds(0,ClientSize.Height-1-pickerDrop-measure.Height,pathWidth,measure.Height);
            }
            finally{arranging=false;}
        }
        protected override void ScaleControl(System.Drawing.SizeF factor,System.Windows.Forms.BoundsSpecified specified)
        {
            pickerSize=new System.Drawing.Size((int)System.Math.Round(pickerSize.Width*factor.Width),(int)System.Math.Round(pickerSize.Height*factor.Height));
            pickerDrop=(int)System.Math.Round(pickerDrop*factor.Height);
            base.ScaleControl(factor,specified);
        }
    }
    internal static class PathFieldMetrics
    {
        internal const int CaptionHeight=24, PickerHeight=36;
        internal const string ShortcutCaption="Создать ярлыки на рабочем столе";
        internal static System.Drawing.Size Measure(System.Windows.Forms.Control control)
        {
            using(var graphics=control.CreateGraphics())
            {
                var text=System.Windows.Forms.TextRenderer.MeasureText(graphics,ShortcutCaption,control.Font,new System.Drawing.Size(int.MaxValue,int.MaxValue),System.Windows.Forms.TextFormatFlags.SingleLine|System.Windows.Forms.TextFormatFlags.NoPrefix);
                int prefix=1+(int)System.Math.Round(29*graphics.DpiX/96F);
                int overhang=(int)System.Math.Ceiling(control.Font.SizeInPoints*graphics.DpiX/72F/6F);
                int height=System.Windows.Forms.TextRenderer.MeasureText(graphics,"Ag",control.Font).Height+(int)System.Math.Round(6*graphics.DpiX/96F);
                return new System.Drawing.Size(prefix+text.Width-overhang,height);
            }
        }
    }
}
