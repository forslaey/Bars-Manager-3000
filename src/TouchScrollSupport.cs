using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    // Opt-in for read-only text and lists; never installed on editable inputs.
    // Native WM_GESTURE supplies pan/inertia. Touch-generated mouse input is
    // delayed until release so swiping cannot select a list item or drag text.
    internal sealed class TouchScrollSupport : NativeWindow
    {
        readonly Control target;
        readonly Action<int> scroll;
        readonly bool panInScreenCoordinates;
        bool down, moved, panning, replaying;
        Point start, last;
        IntPtr downPosition;
        [StructLayout(LayoutKind.Sequential)] struct Config { public uint Id, Want, Block; }
        [StructLayout(LayoutKind.Sequential)] struct Info
        {
            public uint Size, Flags, Id; public IntPtr Target; public short X, Y;
            public uint Instance, Sequence; public ulong Arguments; public uint ExtraSize;
        }
        [DllImport("user32.dll")] static extern bool SetGestureConfig(IntPtr window,uint reserved,uint count,[In] Config[] config,uint size);
        [DllImport("user32.dll")] static extern bool GetGestureInfo(IntPtr handle,ref Info info);
        [DllImport("user32.dll")] static extern bool CloseGestureInfoHandle(IntPtr handle);
        [DllImport("user32.dll")] static extern IntPtr GetMessageExtraInfo();
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr window,int message,IntPtr wParam,IntPtr lParam);

        internal TouchScrollSupport(Control target, Action<int> scroll,bool panInScreenCoordinates=false)
        {
            this.target=target;this.scroll=scroll;this.panInScreenCoordinates=panInScreenCoordinates;
            target.HandleCreated+=delegate { Attach(); };
            target.HandleDestroyed+=delegate { Reset(); if(Handle!=IntPtr.Zero)ReleaseHandle(); };
            target.SizeChanged+=delegate { Reset(); };
            target.EnabledChanged+=delegate { if(!target.Enabled)Reset(); };
            if(target.IsHandleCreated)Attach();
        }
        void Attach()
        {
            AssignHandle(target.Handle);
            SetGestureConfig(Handle,0,1,new[]{new Config {Id=4,Want=0x13,Block=4}},(uint)Marshal.SizeOf(typeof(Config)));
        }
        internal void Reset() { down=moved=panning=false; }
        internal void Pan(Point point,bool begin,bool end)
        {
            if(!target.Enabled)return;
            if(begin){panning=true;moved=true;last=point;}
            else if(panning){int delta=last.Y-point.Y;last=point;if(delta!=0)scroll(delta);}
            if(end)panning=false;
        }
        internal bool TouchMouse(int message,Point point,bool touch)
        {
            if(!touch){if(message==0x201||message==0x203)Reset();return false;}
            if(message==0x201||message==0x203){Reset();down=true;start=point;return true;}
            if(message==0x200&&down){if(Math.Abs(point.Y-start.Y)>Math.Max(6,target.Font.Height/2)||Math.Abs(point.X-start.X)>Math.Max(6,target.Font.Height/2))moved=true;return true;}
            if(message==0x202){bool tap=down&&!moved&&!panning&&Math.Abs(point.Y-start.Y)<=Math.Max(6,target.Font.Height/2)&&Math.Abs(point.X-start.X)<=Math.Max(6,target.Font.Height/2);down=false;return !tap;}
            return false;
        }
        protected override void WndProc(ref Message message)
        {
            if(!replaying)
            {
                if(message.Msg==0x1F||message.Msg==8||message.Msg==0x20A)Reset();
                if(message.Msg==0x201||message.Msg==0x203||message.Msg==0x200||message.Msg==0x202)
                {
                    long extra=GetMessageExtraInfo().ToInt64();
                    bool touch=((ulong)extra&0xFFFFFF00UL)==0xFF515700UL&&((ulong)extra&0x80UL)!=0;
                    long packed=message.LParam.ToInt64();var point=new Point((short)packed,(short)(packed>>16));
                    bool wasDown=down;
                    if(touch&&(message.Msg==0x201||message.Msg==0x203))downPosition=message.LParam;
                    bool consumed=TouchMouse(message.Msg,point,touch);
                    if(touch&&message.Msg==0x202&&!consumed&&wasDown)
                    {
                        replaying=true;
                        try { SendMessage(Handle,0x201,new IntPtr(1),downPosition);SendMessage(Handle,0x202,IntPtr.Zero,message.LParam); }
                        finally { replaying=false; }
                        message.Result=IntPtr.Zero;return;
                    }
                    if(consumed){message.Result=IntPtr.Zero;return;}
                }
                if(message.Msg==0x119)
                {
                    var info=new Info {Size=(uint)Marshal.SizeOf(typeof(Info))};
                    if(GetGestureInfo(message.LParam,ref info)&&(info.Id==1||info.Id==2||info.Id==4))
                    {
                        try { if(info.Id==4)Pan(panInScreenCoordinates?new Point(info.X,info.Y):target.PointToClient(new Point(info.X,info.Y)),(info.Flags&1)!=0,(info.Flags&4)!=0);if(info.Id==2)panning=false; }
                        finally { CloseGestureInfoHandle(message.LParam); }
                        message.Result=IntPtr.Zero;return;
                    }
                }
            }
            base.WndProc(ref message);
        }
    }
}
