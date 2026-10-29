using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class GridViewportLayout
    {
        internal static int FixedThumbLength(Control control,int track) {using(var graphics=control.CreateGraphics())return Math.Min(track,Math.Max(1,(int)Math.Round(28F*graphics.DpiY/96F)));}
        internal static void UniformRows(DataGridView grid)
        {
            int height=0;
            foreach(DataGridViewRow row in grid.Rows)height=Math.Max(height,row.GetPreferredHeight(row.Index,DataGridViewAutoSizeRowMode.AllCells,true));
            foreach(DataGridViewRow row in grid.Rows)if(row.Height!=height)row.Height=height;
        }
        internal static int WholeRowsHeight(DataGridView grid,int limit)
        {
            int height=grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0;
            int header=height;
            foreach(DataGridViewRow row in grid.Rows){if(height+row.Height>limit)break;height+=row.Height;}
            return height==header?Math.Max(1,limit):height;
        }
    }

    internal sealed class LibraryGridScrollBar : Control
    {
        private DataGridView grid;
        private bool dragging; private int dragOffset;
        private bool correcting;
        private bool viewportPending,updatingViewport;
        internal bool UseCardBackground {get;set;}
        internal int MaximumThumbLength {get;set;}
        internal bool LimitTrackToWholeRows {get;set;}
        internal Color TrackBackground {get{return Palette==null?BackColor:UseCardBackground?Palette.Surface:Palette.Canvas;}}
        internal bool AutoHideWhenUnused {get;set;}
        private void QueueViewport()
        {
            if(viewportPending||!IsHandleCreated||IsDisposed)return;viewportPending=true;
            BeginInvoke((MethodInvoker)delegate{viewportPending=false;if(!IsDisposed)UpdateViewport();});
        }
        internal void UpdateViewport()
        {
            if(grid==null||grid.Parent==null||updatingViewport)return;updatingViewport=true;
            try
            {
                BackColor=TrackBackground;grid.Parent.BackColor=BackColor;
                if(AutoHideWhenUnused)
                {
                    int height=grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0;
                    foreach(DataGridViewRow row in grid.Rows)if(row.Visible)height+=row.Height;
                    Visible=height>grid.ClientSize.Height;
                }
                CorrectPosition();Invalidate();
            }
            finally{updatingViewport=false;}
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);QueueViewport();}
        internal bool AlignToRows { get; set; }
        public ThemePalette Palette;
        public LibraryGridScrollBar()
        {
            AutoHideWhenUnused=true;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand; AccessibleRole = AccessibleRole.ScrollBar; AccessibleName = "Прокрутка компонентов"; TabStop = true;
        }
        public void Bind(DataGridView target)
        {
            grid = target;AlignToRows=true;
            grid.RowsAdded+=delegate{QueueViewport();};grid.RowsRemoved+=delegate{QueueViewport();};grid.SizeChanged+=delegate{QueueViewport();};grid.RowHeightChanged+=delegate{QueueViewport();};grid.ColumnWidthChanged+=delegate{QueueViewport();};grid.DataBindingComplete+=delegate{QueueViewport();};grid.VisibleChanged+=delegate{QueueViewport();};grid.BackgroundColorChanged+=delegate{QueueViewport();};
            grid = target; grid.Scroll += delegate { CorrectPosition(); Invalidate(); }; grid.RowsAdded += delegate { Invalidate(); }; grid.RowsRemoved += delegate { Invalidate(); }; grid.SizeChanged += delegate { CorrectPosition(); Invalidate(); };
            grid.MouseWheel += delegate(object sender, MouseEventArgs e) { MoveBy(-Math.Sign(e.Delta) * 3); var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; };
        }
        private int Maximum
        {
            get
            {
                if(grid==null||grid.RowCount==0)return 0;
                int space=Math.Max(1,grid.ClientSize.Height-(grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0));
                int first=grid.RowCount-1,used=grid.Rows[first].Height;
                while(first>0&&used+grid.Rows[first-1].Height<=space){first--;used+=grid.Rows[first].Height;}
                return first;
            }
        }
        private void CorrectPosition()
        {
            if(correcting||grid==null||grid.RowCount==0)return;
            int maximum=Maximum;
            if(grid.FirstDisplayedScrollingRowIndex<=maximum)return;
            correcting=true;try{grid.FirstDisplayedScrollingRowIndex=maximum;}finally{correcting=false;}
        }
        private int First { get { return grid == null ? 0 : Math.Min(Maximum,Math.Max(0, grid.FirstDisplayedScrollingRowIndex)); } }
        private Rectangle Track
        {
            get
            {
                int top=AlignToRows&&grid!=null&&grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0;
                int bottom=grid==null?Height:Math.Min(Height,grid.Bottom);
                if(LimitTrackToWholeRows && grid!=null && grid.RowCount>0) { int used=grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0; for(int row=Maximum;row<grid.RowCount;row++) if(grid.Rows[row].Visible) used+=grid.Rows[row].Height; bottom=Math.Min(bottom,grid.Top+used); }
                int inset=Math.Min(6,Math.Max(0,(bottom-top)/4));
                return new Rectangle(0,top+inset,Width,Math.Max(1,bottom-top-2*inset));
            }
        }
        internal DataGridView BoundGrid {get{return grid;}}
        internal Rectangle ThumbBounds { get { return Thumb; } }
        private Rectangle Thumb
        {
            get
            {
                Rectangle bounds=Track;
                float scale;using(var graphics=CreateGraphics())scale=graphics.DpiY/96F;
                int track=bounds.Height,length=Math.Min(track,Math.Max(1,(int)Math.Round(28F*scale)));
                int y = bounds.Top + (Maximum == 0 ? 0 : (int)((double)(track - length) * First / Maximum));
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
            ThemePalette p = Palette ?? ThemePalette.Create(false); e.Graphics.Clear(TrackBackground);
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
            int travel = Math.Max(1, Track.Height - Thumb.Height), y = Math.Max(0, Math.Min(travel, e.Y - dragOffset - Track.Top));
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
