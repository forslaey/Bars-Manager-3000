using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed partial class ServiceGrid
    {
        private bool touchEnabled,touchDown,touchMoved,panActive;
        private Point touchStart,touchLast;
        private double touchRemainder;
        private int touchRow=-1,touchColumn=-1;
        internal event EventHandler<DataGridViewCellEventArgs> TouchCellTapped;
        internal event Action TouchTapInterrupted;
        internal bool TouchScrollingEnabled {get{return touchEnabled;}set{touchEnabled=value;if(IsHandleCreated)ConfigureTouch();ResetTouch();}}
        [StructLayout(LayoutKind.Sequential)] private struct GestureConfig {public uint Id,Want,Block;}
        [StructLayout(LayoutKind.Sequential)] private struct GestureInfo {public uint Size,Flags,Id;public IntPtr Target;public short X,Y;public uint Instance,Sequence;public ulong Arguments;public uint ExtraSize;}
        [DllImport("user32.dll",SetLastError=true)] private static extern bool SetGestureConfig(IntPtr window,uint reserved,uint count,[In] GestureConfig[] config,uint size);
        [DllImport("user32.dll",SetLastError=true)] private static extern bool GetGestureInfo(IntPtr handle,ref GestureInfo info);
        [DllImport("user32.dll")] private static extern bool CloseGestureInfoHandle(IntPtr handle);
        [DllImport("user32.dll")] private static extern IntPtr GetMessageExtraInfo();
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);if(touchEnabled)ConfigureTouch();}
        protected override void OnHandleDestroyed(EventArgs e){ResetTouch();base.OnHandleDestroyed(e);}
        protected override void OnSizeChanged(EventArgs e){ResetTouch();base.OnSizeChanged(e);}
        protected override void OnRowsRemoved(DataGridViewRowsRemovedEventArgs e){ResetTouch();base.OnRowsRemoved(e);}
        protected override void OnEnabledChanged(EventArgs e){if(!Enabled)ResetTouch();base.OnEnabledChanged(e);}
        private void ConfigureTouch()
        {
            // Native Windows recognizes the gesture and supplies inertia. Do not
            // register WM_TOUCH or enable mouse-in-pointer globally: mouse stays native.
            var config=new[]{new GestureConfig{Id=4,Want=touchEnabled?0x13U:0U,Block=touchEnabled?0x4U:1U}};
            SetGestureConfig(Handle,0,1,config,(uint)Marshal.SizeOf(typeof(GestureConfig)));
        }
        internal void ResetTouch(){touchDown=touchMoved=panActive=false;touchRemainder=0;touchRow=touchColumn=-1;}
        internal static bool IsTouchMouse(long extra){return ((ulong)extra&0xFFFFFF00UL)==0xFF515700UL&&((ulong)extra&0x80UL)!=0;}
        private int TouchThreshold {get{return Math.Max(6,Font.Height/2);}}
        // Kept separate from native marshaling so tap/swipe routing can be tested
        // without injecting input into the operator's desktop.
        internal bool TouchMouse(int message,Point point,bool fromTouch)
        {
            if(!touchEnabled)return false;
            if(!fromTouch){if(message==0x201||message==0x203){ResetTouch();InterruptTouchTap();}return false;}
            if(message==0x201||message==0x203)
            {
                ResetTouch();touchDown=true;touchStart=touchLast=point;
                var hit=HitTest(point.X,point.Y);touchRow=hit.RowIndex;touchColumn=hit.ColumnIndex;return true;
            }
            if(message==0x200&&touchDown)
            {
                if(Math.Abs(point.Y-touchStart.Y)>TouchThreshold||Math.Abs(point.X-touchStart.X)>TouchThreshold)touchMoved=true;
                return true;
            }
            if(message==0x202)
            {
                bool tap=touchDown&&!touchMoved&&!panActive&&Math.Abs(point.Y-touchStart.Y)<=TouchThreshold&&Math.Abs(point.X-touchStart.X)<=TouchThreshold;
                touchDown=false;
                var hit=HitTest(point.X,point.Y);
                if(tap&&touchRow>=0&&touchColumn>=0&&hit.RowIndex==touchRow&&hit.ColumnIndex==touchColumn)
                {
                    // A touch tap is committed on release; a normal mouse click
                    // continues to be committed immediately by the original grid.
                    base.OnMouseDown(new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));
                    base.OnMouseUp(new MouseEventArgs(MouseButtons.Left,1,point.X,point.Y,0));
                    var tapped=TouchCellTapped;
                    if(tapped!=null)tapped(this,new DataGridViewCellEventArgs(hit.ColumnIndex,hit.RowIndex));
                }
                else InterruptTouchTap();
                return true;
            }
            return false;
        }
        private void InterruptTouchTap()
        {
            var interrupted=TouchTapInterrupted;
            if(interrupted!=null)interrupted();
        }
        internal void TouchPan(Point point,bool begin,bool end)
        {
            if(!touchEnabled||!Enabled)return;
            if(begin){InterruptTouchTap();panActive=true;touchMoved=true;touchLast=point;touchRemainder=0;}
            else if(panActive){touchRemainder+=touchLast.Y-point.Y;touchLast=point;ScrollTouchRows();}
            if(end){panActive=false;touchRemainder=0;}
        }
        private void ScrollTouchRows()
        {
            if(RowCount==0||FirstDisplayedScrollingRowIndex<0){touchRemainder=0;return;}
            int first=FirstDisplayedScrollingRowIndex,target=first;
            int remaining=Math.Max(1,ClientSize.Height-(ColumnHeadersVisible?ColumnHeadersHeight:0)-2),last=Rows.GetLastRow(DataGridViewElementStates.Visible),maximum=last;
            for(int row=last;row>=0;row=Rows.GetPreviousRow(row,DataGridViewElementStates.Visible)){if(Rows[row].Height>remaining&&row!=last)break;maximum=row;remaining-=Rows[row].Height;if(remaining<=0)break;}
            // DataGridView uses row-aligned vertical positions. Accumulate native
            // pixel deltas, including inertia, rather than multiplying wheel notches.
            while(touchRemainder>0)
            {
                int next=Rows.GetNextRow(target,DataGridViewElementStates.Visible);if(next<0){touchRemainder=0;break;}
                int h=Rows[target].Height;if(touchRemainder<h)break;touchRemainder-=h;target=next;
            }
            while(touchRemainder<0)
            {
                int previous=Rows.GetPreviousRow(target,DataGridViewElementStates.Visible);if(previous<0){touchRemainder=0;break;}
                int h=Rows[previous].Height;if(-touchRemainder<h)break;touchRemainder+=h;target=previous;
            }
            target=Math.Min(target,maximum);
            if((target==0&&touchRemainder<0)||(target==maximum&&touchRemainder>0))touchRemainder=0;
            if(target!=first)FirstDisplayedScrollingRowIndex=target;
        }
        protected override void WndProc(ref Message message)
        {
            if(touchEnabled)
            {
                if(message.Msg==0x20A)ResetTouch();
                if(message.Msg==0x201||message.Msg==0x203||message.Msg==0x202||message.Msg==0x200)
                {
                    long packed=message.LParam.ToInt64();var point=new Point((short)(packed&0xFFFF),(short)((packed>>16)&0xFFFF));
                    if(TouchMouse(message.Msg,point,IsTouchMouse(GetMessageExtraInfo().ToInt64()))){message.Result=IntPtr.Zero;return;}
                }
                if(message.Msg==0x119)
                {
                    var info=new GestureInfo{Size=(uint)Marshal.SizeOf(typeof(GestureInfo))};
                    if(GetGestureInfo(message.LParam,ref info)&&(info.Id==1||info.Id==2||info.Id==4))
                    {
                        try
                        {
                            if(info.Id==4)TouchPan(PointToClient(new Point(info.X,info.Y)),(info.Flags&1)!=0,(info.Flags&4)!=0);
                            if(info.Id==2){panActive=false;touchRemainder=0;}
                        }
                        finally{CloseGestureInfoHandle(message.LParam);}
                        message.Result=IntPtr.Zero;return;
                    }
                }
                if(message.Msg==0x1F||message.Msg==0x8)ResetTouch(); // cancel mode / focus lost
            }
            base.WndProc(ref message);
        }
    }
}
