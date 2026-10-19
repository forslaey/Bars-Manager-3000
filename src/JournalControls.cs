using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    // Keep the native edit control for its reliable selection and copy behavior.
    // Only its scrolling chrome is replaced; scrolling never changes selection.
    internal sealed class JournalTextBox : TextBox
    {
        private const int EM_GETRECT = 0x00B2;
        private const int EM_GETLINECOUNT = 0x00BA;
        private const int EM_LINEINDEX = 0x00BB;
        private const int EM_LINESCROLL = 0x00B6;
        private const int EM_GETFIRSTVISIBLELINE = 0x00CE;
        private const int WM_MOUSEWHEEL = 0x020A;
        private int wheelRemainder;
        private bool notifying;
        private int observedFirst = -1, observedVisible = -1, observedTotal = -1;

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle { public int Left, Top, Right, Bottom; }

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr handle, int message, IntPtr wParam, ref NativeRectangle rectangle);

        public event EventHandler ViewportChanged;

        public JournalTextBox()
        {
            Multiline = true;
            ReadOnly = true;
            WordWrap = true;
            ScrollBars = ScrollBars.None;
            BorderStyle = BorderStyle.None;
            HideSelection = false;
        }

        public int FirstVisibleLine
        {
            get { return IsHandleCreated ? Math.Max(0, SendMessage(Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32()) : 0; }
        }

        public int TotalLineCount
        {
            get
            {
                if (IsHandleCreated)
                    return Math.Max(1, SendMessage(Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32());
                int lines = 1;
                for (int i = 0; i < Text.Length; i++) if (Text[i] == '\n') lines++;
                return lines;
            }
        }

        public int VisibleLineCount
        {
            get
            {
                if (!IsHandleCreated) return Math.Max(1, ClientSize.Height / Math.Max(1, Font.Height));
                NativeRectangle native = new NativeRectangle();
                SendMessage(Handle, EM_GETRECT, IntPtr.Zero, ref native);
                Rectangle format = Rectangle.FromLTRB(native.Left, native.Top, native.Right, native.Bottom);
                if (format.Width <= 0 || format.Height <= 0) format = ClientRectangle;
                int first = FirstVisibleLine;
                int total = TotalLineCount;
                int firstIndex = LineIndex(first);
                Point firstPosition = firstIndex >= 0 ? GetPositionFromCharIndex(firstIndex) : new Point(format.Left, format.Top);
                int lineHeight = Math.Max(1, Font.Height);
                if (first + 1 < total)
                {
                    int nextIndex = LineIndex(first + 1);
                    if (nextIndex >= 0)
                    {
                        int measuredHeight = GetPositionFromCharIndex(nextIndex).Y - firstPosition.Y;
                        if (measuredHeight > 0 && measuredHeight <= Math.Max(Font.Height * 2, 4)) lineHeight = measuredHeight;
                    }
                }
                int availableHeight = Math.Max(0, format.Bottom - Math.Max(format.Top, firstPosition.Y));
                int capacity = Math.Max(1, availableHeight / lineHeight);

                // EM_CHARFROMPOS (used by GetCharIndexFromPosition) accounts for
                // the edit control's actual wrapping and its formatting inset.
                // At the end of the text it returns the last character, so use
                // the measured line height for the remaining empty viewport.
                if (TextLength > 0 && format.Height > 0)
                {
                    // Use the left edge. At the right edge of a soft-wrapped
                    // line the native edit can return the *next* line's first
                    // character, even when that line starts below the viewport.
                    int character = GetCharIndexFromPosition(new Point(format.Left, format.Bottom - 1));
                    int bottomLine = GetLineFromCharIndex(character);
                    if (bottomLine >= first && bottomLine < total - 1)
                    {
                        int bottomIndex = LineIndex(bottomLine);
                        if (bottomIndex >= 0)
                        {
                            int bottomY = GetPositionFromCharIndex(bottomIndex).Y;
                            int measuredCapacity = bottomLine - first + (bottomY + lineHeight <= format.Bottom ? 1 : 0);
                            if (measuredCapacity > 0) capacity = measuredCapacity;
                        }
                    }
                }
                return capacity;
            }
        }

        public int MaximumFirstLine { get { return Math.Max(0, TotalLineCount - VisibleLineCount); } }

        private int LineIndex(int line)
        {
            return SendMessage(Handle, EM_LINEINDEX, new IntPtr(line), IntPtr.Zero).ToInt32();
        }

        public void ScrollToLine(int line)
        {
            if (!IsHandleCreated) return;
            int target = Math.Max(0, Math.Min(line, MaximumFirstLine));
            int difference = target - FirstVisibleLine;
            if (difference != 0) SendMessage(Handle, EM_LINESCROLL, IntPtr.Zero, new IntPtr(difference));
            NotifyViewportChanged(false);
        }

        public void ScrollByLines(int lines)
        {
            long target = (long)FirstVisibleLine + lines;
            ScrollToLine((int)Math.Max(0L, Math.Min((long)MaximumFirstLine, target)));
        }

        internal void ScrollWheel(int delta)
        {
            wheelRemainder += delta;
            int steps = wheelRemainder / 120;
            wheelRemainder %= 120;
            int configuredLines = SystemInformation.MouseWheelScrollLines;
            if (steps == 0 || configuredLines == 0) return;
            int lines = configuredLines < 0 ? Math.Max(1, VisibleLineCount - 1) : configuredLines;
            long offset = -(long)steps * lines;
            ScrollByLines((int)Math.Max(Int32.MinValue, Math.Min(Int32.MaxValue, offset)));
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            ScrollWheel(e.Delta);
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            base.OnMouseWheel(e);
        }

        protected override void OnTextChanged(EventArgs e) { base.OnTextChanged(e); NotifyViewportChanged(true); }
        protected override void OnResize(EventArgs e) { base.OnResize(e); NotifyViewportChanged(true); }
        protected override void OnFontChanged(EventArgs e) { base.OnFontChanged(e); NotifyViewportChanged(true); }
        protected override void OnHandleCreated(EventArgs e) { base.OnHandleCreated(e); NotifyViewportChanged(true); }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg == WM_MOUSEWHEEL)
            {
                int delta = unchecked((short)((long)message.WParam >> 16));
                int packed = unchecked((int)message.LParam.ToInt64());
                Point point = PointToClient(new Point(unchecked((short)packed), unchecked((short)(packed >> 16))));
                OnMouseWheel(new HandledMouseEventArgs(MouseButtons.None, 0, point.X, point.Y, delta));
                message.Result = IntPtr.Zero;
                return;
            }
            int nativeMessage = message.Msg;
            base.WndProc(ref message);
            if (nativeMessage == EM_LINESCROLL || nativeMessage == 0x00B7 || // EM_SCROLLCARET
                nativeMessage == 0x0115 || nativeMessage == 0x0100 || nativeMessage == 0x0101 || // scroll / keys
                nativeMessage == 0x0200 || nativeMessage == 0x0202 || nativeMessage == 0x0113 || // selection drag / native autoscroll
                nativeMessage == 0x00B1 || nativeMessage == 0x00C2 || nativeMessage == 0x000C) // selection / replace / set text
                NotifyViewportChanged(false);
        }

        private void NotifyViewportChanged(bool force)
        {
            if (notifying || IsDisposed || !IsHandleCreated) return;
            notifying = true;
            try
            {
                int first = FirstVisibleLine, visible = VisibleLineCount, total = TotalLineCount;
                if (!force && observedFirst == first && observedVisible == visible && observedTotal == total) return;
                observedFirst = first; observedVisible = visible; observedTotal = total;
                EventHandler handler = ViewportChanged;
                if (handler != null) handler(this, EventArgs.Empty);
            }
            finally { notifying = false; }
        }
    }

    internal sealed class JournalScrollBar : Control
    {
        private JournalTextBox textBox;
        private ThemePalette palette;
        private bool hovered, dragging, updating, updatePending;
        private int dragOffset;

        public JournalScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            Width = 18;
            TabStop = true;
            AccessibleRole = AccessibleRole.ScrollBar;
            AccessibleName = "Прокрутка журнала сеанса";
            AccessibleDescription = "Стрелки прокручивают журнал. Page Up и Page Down — на страницу, Home и End — в начало и конец.";
        }

        public ThemePalette Palette
        {
            get { return palette; }
            set { palette = value; BackColor = CurrentPalette.Surface; Invalidate(); }
        }

        private ThemePalette CurrentPalette { get { return palette ?? ThemePalette.Create(false); } }
        private float DpiScale { get { using (Graphics graphics = CreateGraphics()) return graphics.DpiX / 96F; } }

        internal Rectangle TrackBounds
        {
            get
            {
                int inset = Math.Max(1, (int)Math.Round(3F * DpiScale));
                return new Rectangle(0, Math.Min(inset, ClientSize.Height / 2), ClientSize.Width,
                    Math.Max(0, ClientSize.Height - Math.Min(inset, ClientSize.Height / 2) * 2));
            }
        }

        internal Rectangle ThumbBounds
        {
            get
            {
                Rectangle track = TrackBounds;
                if (textBox == null || textBox.IsDisposed || textBox.MaximumFirstLine <= 0 || track.Height <= 0 || track.Width <= 0)
                    return Rectangle.Empty;
                // Keep enough travel even in the compact two-line journal.
                int minimum = Math.Min((int)Math.Round(24F * DpiScale),
                    Math.Max((int)Math.Round(10F * DpiScale), track.Height / 2));
                int height = Math.Max(minimum, (int)Math.Round(track.Height * Math.Min(1D, (double)textBox.VisibleLineCount / textBox.TotalLineCount)));
                height = Math.Min(track.Height, height);
                int travel = track.Height - height;
                int top = track.Top + (int)Math.Round(travel * Math.Min(1D, (double)textBox.FirstVisibleLine / textBox.MaximumFirstLine));
                int width = Math.Min(track.Width, Math.Max(2, (int)Math.Round((hovered || dragging ? 8F : 6F) * DpiScale)));
                return new Rectangle(track.Left + (track.Width - width) / 2, top, width, height);
            }
        }

        public void Bind(JournalTextBox target)
        {
            if (textBox == target) { UpdateViewport(); return; }
            if (textBox != null)
            {
                textBox.ViewportChanged -= BoundViewportChanged;
                textBox.Disposed -= BoundDisposed;
            }
            textBox = target;
            if (textBox != null)
            {
                textBox.ViewportChanged += BoundViewportChanged;
                textBox.Disposed += BoundDisposed;
            }
            UpdateViewport();
        }

        private void BoundViewportChanged(object sender, EventArgs e) { UpdateViewport(); }
        private void BoundDisposed(object sender, EventArgs e) { Bind(null); }

        private void UpdateViewport()
        {
            if (IsDisposed) return;
            if (updating) { updatePending = true; return; }
            updating = true;
            try
            {
                do
                {
                    updatePending = false;
                    bool needed = textBox != null && !textBox.IsDisposed && textBox.MaximumFirstLine > 0;
                    if (Visible != needed) Visible = needed;
                    if (!needed && dragging) { dragging = false; Capture = false; }
                    Invalidate();
                    if (IsHandleCreated) AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
                } while (updatePending);
            }
            finally { updating = false; }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = CurrentPalette;
            e.Graphics.Clear(p.Surface);
            Rectangle thumb = ThumbBounds;
            if (thumb.IsEmpty) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color fill = dragging ? p.Accent : Blend(p.Surface, p.Muted, hovered || Focused ? 0.72F : 0.48F);
            using (GraphicsPath path = BrandButton.RoundRect(thumb, thumb.Width / 2F))
            using (Brush brush = new SolidBrush(fill)) e.Graphics.FillPath(brush, path);
            if (Focused && ShowFocusCues)
            {
                Rectangle focus = Rectangle.Inflate(thumb, 2, 1);
                focus.Intersect(ClientRectangle);
                ControlPaint.DrawFocusRectangle(e.Graphics, focus, p.Text, p.Surface);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { hovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovered = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
        protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || textBox == null || !Enabled) return;
            Focus();
            Rectangle thumb = ThumbBounds;
            if (thumb.IsEmpty) return;
            // The entire column is a hit target even though the painted thumb is slim.
            if (e.Y >= thumb.Top && e.Y < thumb.Bottom)
            {
                dragOffset = e.Y - thumb.Top;
                dragging = true;
                Capture = true;
                Invalidate();
            }
            else textBox.ScrollByLines((e.Y < thumb.Top ? -1 : 1) * Math.Max(1, textBox.VisibleLineCount - 1));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging || textBox == null) return;
            Rectangle track = TrackBounds, thumb = ThumbBounds;
            int travel = track.Height - thumb.Height;
            if (travel <= 0) return;
            int offset = Math.Max(0, Math.Min(travel, e.Y - dragOffset - track.Top));
            textBox.ScrollToLine((int)Math.Round((double)offset * textBox.MaximumFirstLine / travel));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && dragging) { dragging = false; Capture = false; Invalidate(); }
            base.OnMouseUp(e);
        }

        protected override void OnMouseCaptureChanged(EventArgs e)
        {
            if (!Capture && dragging) { dragging = false; Invalidate(); }
            base.OnMouseCaptureChanged(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (textBox != null && !textBox.IsDisposed) textBox.ScrollWheel(e.Delta);
            HandledMouseEventArgs handled = e as HandledMouseEventArgs;
            if (handled != null) handled.Handled = true;
            base.OnMouseWheel(e);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            Keys key = keyData & Keys.KeyCode;
            return key == Keys.Up || key == Keys.Down || key == Keys.PageUp || key == Keys.PageDown ||
                key == Keys.Home || key == Keys.End || base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (textBox != null && !textBox.IsDisposed)
            {
                int page = Math.Max(1, textBox.VisibleLineCount - 1);
                switch (e.KeyCode)
                {
                    case Keys.Up: textBox.ScrollByLines(-1); break;
                    case Keys.Down: textBox.ScrollByLines(1); break;
                    case Keys.PageUp: textBox.ScrollByLines(-page); break;
                    case Keys.PageDown: textBox.ScrollByLines(page); break;
                    case Keys.Home: textBox.ScrollToLine(0); break;
                    case Keys.End: textBox.ScrollToLine(textBox.MaximumFirstLine); break;
                    default: base.OnKeyDown(e); return;
                }
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            base.OnKeyDown(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && textBox != null)
            {
                textBox.ViewportChanged -= BoundViewportChanged;
                textBox.Disposed -= BoundDisposed;
                textBox = null;
            }
            base.Dispose(disposing);
        }

        private static Color Blend(Color background, Color foreground, float opacity)
        {
            return Color.FromArgb((int)(background.R + (foreground.R - background.R) * opacity),
                (int)(background.G + (foreground.G - background.G) * opacity),
                (int)(background.B + (foreground.B - background.B) * opacity));
        }
    }
}
