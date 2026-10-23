using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed class GuideReader : RichTextBox
    {
        [StructLayout(LayoutKind.Sequential)] private struct NativePoint {public int X,Y;}
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll")] private static extern IntPtr SendMessage(IntPtr handle,int message,IntPtr wParam,ref NativePoint point);
        private bool updating;
        internal event EventHandler ViewportChanged;
        internal int ScrollPosition {get{var p=new NativePoint();if(IsHandleCreated)SendMessage(Handle,0x4DD,IntPtr.Zero,ref p);return Math.Max(0,p.Y);}}
        internal int ContentHeight {get{if(!IsHandleCreated||TextLength==0)return ClientSize.Height;return Math.Max(ClientSize.Height,ScrollPosition+GetPositionFromCharIndex(TextLength-1).Y+Font.Height+8);}}
        internal int MaximumScroll {get{return Math.Max(0,ContentHeight-ClientSize.Height);}}
        internal void ScrollTo(int y){if(!IsHandleCreated)return;var p=new NativePoint{Y=Math.Max(0,Math.Min(MaximumScroll,y))};SendMessage(Handle,0x4DE,IntPtr.Zero,ref p);Notify();}
        internal void BeginFormatting(){updating=true;if(IsHandleCreated)SendMessage(Handle,11,IntPtr.Zero,IntPtr.Zero);}
        internal void EndFormatting(){updating=false;if(IsHandleCreated)SendMessage(Handle,11,new IntPtr(1),IntPtr.Zero);Invalidate();Update();Notify();}
        private void Notify(){if(!updating&&ViewportChanged!=null)ViewportChanged(this,EventArgs.Empty);}
        protected override void OnTextChanged(EventArgs e){base.OnTextChanged(e);Notify();}
        protected override void OnSizeChanged(EventArgs e){base.OnSizeChanged(e);Notify();}
        protected override void OnVScroll(EventArgs e){base.OnVScroll(e);Notify();}
        protected override void WndProc(ref Message m)
        {
            if(m.Msg==0x20A){int delta=(short)((m.WParam.ToInt64()>>16)&0xffff);ScrollTo(ScrollPosition-(int)Math.Round(delta/120.0*Font.Height*3));return;}
            base.WndProc(ref m);if(m.Msg==0x115||m.Msg==0x101)Notify();
        }
    }
    internal sealed class GuideReaderScrollBar : Control
    {
        private GuideReader reader;private bool dragging;private int offset;
        internal ThemePalette Palette;
        internal GuideReaderScrollBar(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);TabStop=false;Cursor=Cursors.Hand;AccessibleName="Прокрутка инструкции";}
        internal void Bind(GuideReader target){reader=target;reader.ViewportChanged+=delegate{Invalidate();};}
        internal Rectangle ThumbBounds
        {
            get{if(reader==null||reader.MaximumScroll==0||Height<=12)return Rectangle.Empty;int track=Height-12;int length=Math.Min(track,Math.Max(28,(int)((double)track*reader.ClientSize.Height/reader.ContentHeight)));int y=6+(int)((double)(track-length)*reader.ScrollPosition/reader.MaximumScroll);return new Rectangle(Width/2-3,y,6,length);}
        }
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.Clear(BackColor);Rectangle thumb=ThumbBounds;if(thumb.IsEmpty)return;var p=Palette??ThemePalette.Create(false);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=BrandButton.RoundRect(thumb,3))using(var brush=new SolidBrush(dragging?p.Accent:p.Border))e.Graphics.FillPath(brush,path);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||reader==null)return;var thumb=ThumbBounds;if(thumb.IsEmpty)return;if(thumb.Contains(e.Location)){dragging=true;offset=e.Y-thumb.Y;Capture=true;}else reader.ScrollTo(reader.ScrollPosition+(e.Y<thumb.Top?-1:1)*reader.ClientSize.Height);Invalidate();}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(!dragging||reader==null)return;int travel=Math.Max(1,Height-12-ThumbBounds.Height);reader.ScrollTo((int)((double)(e.Y-offset-6)*reader.MaximumScroll/travel));}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
        protected override void OnMouseCaptureChanged(EventArgs e){base.OnMouseCaptureChanged(e);if(!Capture){dragging=false;Invalidate();}}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(reader!=null)reader.ScrollTo(reader.ScrollPosition-(int)Math.Round(e.Delta/120.0*reader.Font.Height*3));}
    }
    internal sealed class GuideSectionScrollBar : Control
    {
        private GuideSectionListBox list;private bool dragging;private int offset;
        internal ThemePalette Palette;
        internal GuideSectionScrollBar(){SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint|ControlStyles.ResizeRedraw,true);Cursor=Cursors.Hand;TabStop=false;AccessibleName="Прокрутка разделов";}
        internal void Bind(GuideSectionListBox target){list=target;list.ViewportChanged+=delegate{Invalidate();};}
        private int VisibleItems {get{if(list==null)return 1;int count=0;for(int i=list.TopIndex;i<list.Items.Count;i++){if(list.GetItemRectangle(i).Bottom>list.ClientSize.Height)break;count++;}return Math.Max(1,count);}}
        private int Maximum {get{return list==null?0:Math.Max(0,list.Items.Count-VisibleItems);}}
        private Rectangle Thumb {get{if(Maximum==0||Height<=12)return Rectangle.Empty;int track=Height-12;int length=Math.Min(track,Math.Max(28,track*VisibleItems/list.Items.Count));int top=6+(track-length)*Math.Min(Maximum,list.TopIndex)/Maximum;return new Rectangle(Width/2-3,top,6,length);}}
        protected override void OnPaint(PaintEventArgs e){base.OnPaint(e);e.Graphics.Clear(BackColor);var thumb=Thumb;if(thumb.IsEmpty)return;var p=Palette??ThemePalette.Create(false);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;using(var path=BrandButton.RoundRect(thumb,3))using(var brush=new SolidBrush(dragging?p.Accent:p.Border))e.Graphics.FillPath(brush,path);}
        protected override void OnMouseDown(MouseEventArgs e){base.OnMouseDown(e);if(e.Button!=MouseButtons.Left||list==null||Thumb.IsEmpty)return;var thumb=Thumb;if(thumb.Contains(e.Location)){dragging=true;offset=e.Y-thumb.Top;Capture=true;}else list.TopIndex=Math.Max(0,Math.Min(list.Items.Count-1,list.TopIndex+(e.Y<thumb.Top?-1:1)*VisibleItems));Invalidate();}
        protected override void OnMouseMove(MouseEventArgs e){base.OnMouseMove(e);if(dragging&&list!=null){int travel=Math.Max(1,Height-12-Thumb.Height);list.TopIndex=Math.Max(0,Math.Min(Maximum,(e.Y-offset-6)*Maximum/travel));Invalidate();}}
        protected override void OnMouseUp(MouseEventArgs e){base.OnMouseUp(e);dragging=false;Capture=false;Invalidate();}
        protected override void OnMouseWheel(MouseEventArgs e){base.OnMouseWheel(e);if(list!=null){list.TopIndex=Math.Max(0,Math.Min(Maximum,list.TopIndex-Math.Sign(e.Delta)*3));Invalidate();}}
    }
}
