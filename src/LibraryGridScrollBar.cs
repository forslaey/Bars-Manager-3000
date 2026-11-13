using System;
using System.Drawing;
using System.Collections.Generic;
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
            foreach(DataGridViewRow row in grid.Rows){if(!row.Visible)continue;if(height+row.Height>limit)break;height+=row.Height;}
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
            grid.RowStateChanged+=delegate(object sender,DataGridViewRowStateChangedEventArgs e){if((e.StateChanged&(DataGridViewElementStates.Visible|DataGridViewElementStates.Frozen))!=0)QueueViewport();};
            grid.Scroll += delegate { CorrectPosition(); Invalidate(); };
            grid.MouseWheel += delegate(object sender, MouseEventArgs e) { MoveBy(-Math.Sign(e.Delta) * 3); var handled = e as HandledMouseEventArgs; if (handled != null) handled.Handled = true; };
        }
        private List<int> ScrollableRows()
        {
            var result=new List<int>();
            if(grid!=null)
                for(int index=grid.Rows.GetFirstRow(DataGridViewElementStates.Visible);index>=0;
                    index=grid.Rows.GetNextRow(index,DataGridViewElementStates.Visible))
                    if(!grid.Rows[index].Frozen)result.Add(index);
            return result;
        }
        private int MaximumPosition(List<int> rows)
        {
            if(rows.Count==0)return 0;
            int space=Math.Max(1,grid.ClientSize.Height-(grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0));
            int first=rows.Count-1,used=grid.Rows[rows[first]].Height;
            while(first>0&&used+grid.Rows[rows[first-1]].Height<=space)
            {
                first--;
                used+=grid.Rows[rows[first]].Height;
            }
            return first;
        }
        private int Maximum {get{return MaximumPosition(ScrollableRows());}}
        private int FirstPosition(List<int> rows)
        {
            return Math.Min(MaximumPosition(rows),Math.Max(0,rows.IndexOf(grid.FirstDisplayedScrollingRowIndex)));
        }
        private void SetPosition(List<int> rows,int position)
        {
            if(rows.Count==0||grid.Columns.GetColumnCount(DataGridViewElementStates.Visible)==0)return;
            int index=rows[Math.Max(0,Math.Min(MaximumPosition(rows),position))];
            if(index!=grid.FirstDisplayedScrollingRowIndex)grid.FirstDisplayedScrollingRowIndex=index;
        }
        private void CorrectPosition()
        {
            if(correcting||grid==null)return;
            var rows=ScrollableRows();
            if(rows.Count==0||grid.FirstDisplayedScrollingRowIndex<=rows[MaximumPosition(rows)])return;
            correcting=true;
            try{SetPosition(rows,MaximumPosition(rows));}
            finally{correcting=false;}
        }
        private Rectangle Track
        {
            get
            {
                int top=AlignToRows&&grid!=null&&grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0;
                int bottom=grid==null?Height:Math.Min(Height,grid.Bottom);
                if(LimitTrackToWholeRows && grid!=null && grid.RowCount>0) { int used=grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0; var rows=ScrollableRows();for(int row=MaximumPosition(rows);row<rows.Count;row++)used+=grid.Rows[rows[row]].Height; bottom=Math.Min(bottom,grid.Top+used); }
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
                var rows=ScrollableRows();int maximum=MaximumPosition(rows);
                int y=bounds.Top+(maximum==0?0:(int)((double)(track-length)*FirstPosition(rows)/maximum));
                return new Rectangle(Width / 2 - 3, y, 6, length);
            }
        }
        private void MoveBy(int rows)
        {
            if (grid == null || grid.RowCount == 0) return;
            var visible=ScrollableRows();if(visible.Count!=0)SetPosition(visible,FirstPosition(visible)+rows);Invalidate();
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
            var rows=ScrollableRows();SetPosition(rows,(int)Math.Round((double)y*MaximumPosition(rows)/travel));Invalidate();
        }
        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Capture = false; base.OnMouseUp(e); Invalidate(); }
        protected override void OnMouseCaptureChanged(EventArgs e) { if (!Capture) dragging = false; base.OnMouseCaptureChanged(e); }
        protected override bool IsInputKey(Keys key) { return key == Keys.Up || key == Keys.Down || key == Keys.PageUp || key == Keys.PageDown || base.IsInputKey(key); }
        protected override void OnKeyDown(KeyEventArgs e)
        { if (e.KeyCode == Keys.Up) MoveBy(-1); else if (e.KeyCode == Keys.Down) MoveBy(1); else if (e.KeyCode == Keys.PageUp) MoveBy(-Math.Max(1, grid.DisplayedRowCount(false))); else if (e.KeyCode == Keys.PageDown) MoveBy(Math.Max(1, grid.DisplayedRowCount(false))); base.OnKeyDown(e); }
        protected override void OnMouseWheel(MouseEventArgs e) { MoveBy(-Math.Sign(e.Delta) * 3); base.OnMouseWheel(e); }
    }
}
