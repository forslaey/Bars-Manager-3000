using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
    // Compact controls at the top, a growing journal, and a pinned footer.
    // A short viewport scrolls only the controls; it never inserts gaps between them.
    internal sealed class CompactJournalLayout : TableLayoutPanel
    {
        private readonly TableLayoutPanel content;
        private readonly ServiceGrid grid;
        private readonly int tableRow;
        private readonly float footerBase,journalBase;
        private bool pending,arranging;
        internal CompactJournalLayout(TableLayoutPanel content,int tableRow,ServiceGrid grid,Control journal,Control footer,float journalMinimum,float footerHeight)
        {
            this.content=content;this.grid=grid;this.tableRow=tableRow;journalBase=journalMinimum;footerBase=footerHeight;
            DoubleBuffered=true;
            Dock=DockStyle.Fill;Margin=Padding.Empty;ColumnCount=1;RowCount=3;
            ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));
            RowStyles.Add(new RowStyle(SizeType.Absolute,1));RowStyles.Add(new RowStyle(SizeType.Percent,100));RowStyles.Add(new RowStyle(SizeType.Absolute,footerHeight));
            Controls.Add(new FillContentPanel(content){Margin=Padding.Empty,PreserveScrollOnFocus=true},0,0);Controls.Add(journal,0,1);Controls.Add(footer,0,2);
            grid.RowsAdded+=delegate{QueueArrange();};grid.RowsRemoved+=delegate{QueueArrange();};grid.ColumnWidthChanged+=delegate{QueueArrange();};grid.RowHeightChanged+=delegate{QueueArrange();};grid.CellValueChanged+=delegate{QueueArrange();};
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);var form=FindForm();if(form!=null)form.Shown+=delegate{QueueArrange();};}
        protected override void OnLayout(LayoutEventArgs e){base.OnLayout(e);QueueArrange();}
        private void QueueArrange()
        {
            if(pending||arranging||!IsHandleCreated||IsDisposed)return;
            var form=FindForm();if(form==null||!form.Visible)return;pending=true;
            BeginInvoke((MethodInvoker)delegate{pending=false;if(!IsDisposed)ArrangeSpace();});
        }
        private void ArrangeSpace()
        {
            if(arranging||ClientSize.Height<=0)return;arranging=true;
            try
            {
                GridViewportLayout.UniformRows(grid);
                int fixedHeight=(int)Math.Ceiling(content.Padding.Vertical+Enumerable.Range(0,content.RowCount).Where(i=>i!=tableRow).Sum(i=>content.RowStyles[i].Height));
                int header=grid.ColumnHeadersVisible?grid.ColumnHeadersHeight:0;
                int firstRow=grid.RowCount>0?grid.Rows[0].Height:Math.Max(1,grid.RowTemplate.Height);
                int minimum=fixedHeight+header+firstRow;
                if(content.MinimumSize.Height!=minimum)content.MinimumSize=new Size(content.MinimumSize.Width,minimum);
                float scale=RowStyles[2].Height/footerBase;
                int available=Math.Max(0,ClientSize.Height-(int)Math.Ceiling(RowStyles[2].Height+journalBase*scale));
                int tableLimit=Math.Max(header+firstRow,Math.Max(minimum,available)-fixedHeight);
                int tableHeight=GridViewportLayout.WholeRowsHeight(grid,tableLimit);
                SetHeight(content.RowStyles[tableRow],tableHeight);
                SetHeight(RowStyles[0],Math.Min(available,fixedHeight+tableHeight));
            }
            finally{arranging=false;}
        }
        private static void SetHeight(RowStyle row,int value)
        {if(row.SizeType!=SizeType.Absolute||Math.Abs(row.Height-value)>0.5F){row.SizeType=SizeType.Absolute;row.Height=value;}}
    }
}