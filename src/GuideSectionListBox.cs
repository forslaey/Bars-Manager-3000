using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    internal sealed class GuideSectionListBox : ListBox
    {
        private bool measuring;
        private readonly TouchScrollSupport touchScroll;
        private int touchPixels;
        private void ScrollTouch(int delta)
        {
            touchPixels+=delta;
            int index=TopIndex;
            if(index<0||Items.Count==0){touchPixels=0;return;}
            while(touchPixels>0&&index<Items.Count-1){int height=Math.Max(1,GetItemHeight(index));if(touchPixels<height)break;touchPixels-=height;index++;}
            while(touchPixels<0&&index>0){int height=Math.Max(1,GetItemHeight(index-1));if(-touchPixels<height)break;touchPixels+=height;index--;}
            if(index==0||index==Items.Count-1)touchPixels=0;
            TopIndex=index;
            if(ViewportChanged!=null)ViewportChanged(this,EventArgs.Empty);
        }
        private bool hidingScroll;
        internal event EventHandler ViewportChanged;
        [DllImport("user32.dll")] private static extern bool ShowScrollBar(IntPtr handle,int bar,bool show);
        protected override void WndProc(ref Message message)
        {
            base.WndProc(ref message);
            if(message.Msg==0x02E0||message.Msg==0x02E3)RefreshHeights();
            if(IsHandleCreated&&!hidingScroll&&(message.Msg==0xF||message.Msg==5||message.Msg==0x20A||message.Msg==0x115||message.Msg==0x180||message.Msg==0x186))
            {hidingScroll=true;try{ShowScrollBar(Handle,1,false);}finally{hidingScroll=false;}if(ViewportChanged!=null)ViewportChanged(this,EventArgs.Empty);}
        }
        private const int SetItemHeight=0x01A0;
        internal const TextFormatFlags TitleFlags=TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix|TextFormatFlags.NoPadding;
        [DllImport("user32.dll",CharSet=CharSet.Auto)]
        private static extern IntPtr SendMessage(IntPtr window,int message,IntPtr index,IntPtr value);

        internal GuideSectionListBox(){DrawMode=DrawMode.OwnerDrawVariable;IntegralHeight=false;HorizontalScrollbar=false;touchScroll=new TouchScrollSupport(this,ScrollTouch);}
        private static int Pixels(float dpi,int value){return Math.Max(1,(int)Math.Ceiling(value*dpi/96F));}
        internal Rectangle TitleBounds(Graphics graphics,Rectangle item)
        {
            int horizontal=Pixels(graphics.DpiX,10),vertical=Pixels(graphics.DpiY,6);
            // Keep a scrollbar allowance even before it appears, so adding it cannot
            // reduce text width after the native list has measured its rows.
            int width=Math.Min(item.Width,Math.Max(1,ClientSize.Width-SystemInformation.VerticalScrollBarWidth));
            return new Rectangle(item.X+horizontal,item.Y+vertical,Math.Max(1,width-2*horizontal),Math.Max(1,item.Height-2*vertical));
        }
        internal int RequiredHeight(Graphics graphics,string title)
        {
            var bounds=TitleBounds(graphics,new Rectangle(0,0,ClientSize.Width,Int32.MaxValue/2));
            int textHeight=TextRenderer.MeasureText(graphics,title,Font,new Size(bounds.Width,Int32.MaxValue),TitleFlags).Height;
            return Math.Max(Pixels(graphics.DpiY,36),textHeight+2*Pixels(graphics.DpiY,6));
        }
        protected override void OnMeasureItem(MeasureItemEventArgs e)
        {
            base.OnMeasureItem(e);
            if(e.Index>=0&&e.Index<Items.Count)e.ItemHeight=RequiredHeight(e.Graphics,Items[e.Index].ToString());
        }
        internal void RefreshHeights()
        {
            if(measuring||!IsHandleCreated||IsDisposed||ClientSize.Width<=0)return;
            measuring=true;
            try
            {
                using(var graphics=CreateGraphics())for(int i=0;i<Items.Count;i++)SendMessage(Handle,SetItemHeight,new IntPtr(i),new IntPtr(RequiredHeight(graphics,Items[i].ToString())));
                Invalidate();
            }
            finally{measuring=false;}
        }
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);RefreshHeights();}
        protected override void OnFontChanged(EventArgs e){base.OnFontChanged(e);RefreshHeights();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);RefreshHeights();}
        protected override void ScaleControl(SizeF factor,BoundsSpecified specified){base.ScaleControl(factor,specified);RefreshHeights();}
    }
}
