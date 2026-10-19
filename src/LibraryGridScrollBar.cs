using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed class LibraryGridScrollBar : Control
    {
        private DataGridView grid;
        private bool dragging; private int dragOffset;
        public ThemePalette Palette;
        public LibraryGridScrollBar()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "Прокрутка компонентов"; TabStop = true;
        }
        public void Bind(DataGridView target)
        {
            grid = target; grid.Scroll += delegate { Invalidate(); }; grid.RowsAdded += delegate { Invalidate(); }; grid.RowsRemoved += delegate { Invalidate(); }; grid.SizeChanged += delegate { Invalidate(); };
            grid.MouseWheel += delegate(object sender, MouseEventArgs e) { MoveBy(-Math.Sign(e.Delta) * 3); var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; };
        }
        private int Maximum { get { return grid == null ? 0 : Math.Max(0, grid.RowCount - Math.Max(1, grid.DisplayedRowCount(false))); } }
        private int First { get { return grid == null ? 0 : Math.Max(0, grid.FirstDisplayedScrollingRowIndex); } }
        private Rectangle Thumb
        {
            get
            {
                int track = Math.Max(1, Height - 12), visible = grid == null ? 1 : Math.Max(1, grid.DisplayedRowCount(false));
                int length = grid == null ? track : Math.Min(track, Math.Max(28, (int)((double)track * visible / Math.Max(1, grid.RowCount))));
                int y = 6 + (Maximum == 0 ? 0 : (int)((double)(track - length) * First / Maximum));
                return new Rectangle(Width / 2 - 3, y, 6, length);
            }
        }
        private void MoveBy(int rows)
        {
            if (grid == null || grid.RowCount == 0) return;
            int next = Math.Max(0, Math.Min(Maximum, First + rows)); if (next != First) grid.FirstDisplayedScrollingRowIndex = next; Invalidate();
        }
        protected override void OnPaint(PaintEventArgs e)
        {
            ThemePalette p = Palette ?? ThemePalette.Create(false); e.Graphics.Clear(BackColor);
            if (Maximum == 0) return; e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (var path = BrandButton.RoundRect(Thumb, 3)) using (var fill = new SolidBrush(dragging ? p.Accent : p.Border)) e.Graphics.FillPath(fill, path);
            if (Focused && ShowFocusCues) ControlPaint.DrawFocusRectangle(e.Graphics, ClientRectangle, p.Text, BackColor);
        }
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); if (e.Button != MouseButtons.Left || Maximum == 0) return;
            Focus(); Rectangle thumb = Thumb;
            if (thumb.Contains(e.Location)) { dragging = true; dragOffset = e.Y - thumb.Top; Capture = true; }
            else MoveBy(e.Y < thumb.Top ? -Math.Max(1, grid.DisplayedRowCount(false) - 1) : Math.Max(1, grid.DisplayedRowCount(false) - 1));
            Invalidate();
        }
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e); if (!dragging || grid == null || grid.RowCount == 0) return;
            int travel = Math.Max(1, Height - 12 - Thumb.Height), y = Math.Max(0, Math.Min(travel, e.Y - dragOffset - 6));
            int next = Math.Max(0, Math.Min(Maximum, (int)Math.Round((double)y * Maximum / travel))); if (next != First) grid.FirstDisplayedScrollingRowIndex = next; Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (e.KeyCode == Keys.Up) MoveBy(-1); else if (e.KeyCode == Keys.Down) MoveBy(1); else if (e.KeyCode == Keys.PageUp) MoveBy(-Math.Max(1, grid.DisplayedRowCount(false))); else if (e.KeyCode == Keys.PageDown) MoveBy(Math.Max(1, grid.DisplayedRowCount(false))); base.OnKeyDown(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { MoveBy(-Math.Sign(e.Delta) * 3); base.OnMouseWheel(e); }
    }
}
