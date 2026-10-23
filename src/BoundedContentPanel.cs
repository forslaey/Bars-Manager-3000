using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace ServiceTool
{
    // DockStyle.Fill children do not contribute their overflowing MinimumSize
    // reliably to WinForms AutoScroll. Size the service content explicitly.
    internal sealed class FillContentPanel : Panel
    {
        private readonly Control content;
        private bool arranging,ready,pending,loadHooked;
        [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] private static extern int SetWindowTheme(IntPtr handle,string app,string identifiers);
        private void UpdateScrollTheme(){if(IsHandleCreated)SetWindowTheme(Handle,BackColor.GetBrightness()<0.5F?"DarkMode_Explorer":"Explorer",null);}
        protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);UpdateScrollTheme();HookFormLoad();}
        protected override void OnBackColorChanged(EventArgs e){base.OnBackColorChanged(e);UpdateScrollTheme();}
        internal FillContentPanel(Control content)
        {
            this.content=content;Dock=DockStyle.Fill;AutoScroll=true;DoubleBuffered=true;
            content.Dock=DockStyle.None;content.Anchor=AnchorStyles.Top|AnchorStyles.Left;Controls.Add(content);
        }
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);HookFormLoad();
        }
        private void HookFormLoad()
        {
            var form=FindForm();if(form==null||loadHooked)return;loadHooked=true;
            form.Load+=delegate{ready=true;PerformLayout();};
            if(form.Visible){ready=true;PerformLayout();}
        }
        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);if(content==null||arranging||!ready||pending||!IsHandleCreated)return;
            pending=true;BeginInvoke((MethodInvoker)delegate{pending=false;if(!IsDisposed)ArrangeContent();});
        }
        private void ArrangeContent()
        {
            arranging=true;
            try
            {
                AutoScrollMinSize=content.MinimumSize;
                for(int pass=0;pass<3;pass++)
                {
                    Size before=ClientSize;
                    content.Bounds=new Rectangle(AutoScrollPosition,new Size(Math.Max(ClientSize.Width,content.MinimumSize.Width),Math.Max(ClientSize.Height,content.MinimumSize.Height)));
                    AdjustFormScrollbars(true);
                    if(ClientSize==before)break;
                }
            }
            finally{arranging=false;}
        }
    }
    // Limit the working area, while allowing the containing window to be maximized.
    // ScaleControl follows both WinForms DPI scaling and explicit Scale calls.
    internal sealed class BoundedContentPanel : Panel
    {
        private readonly Control content;
        private SizeF limit;
        private bool arranging,ready,pending;

        internal BoundedContentPanel(Control content, Size maximum)
        {
            this.content = content;
            limit = maximum;
            Dock = DockStyle.Fill;
            Margin = Padding.Empty;
            AutoScroll = false;
            DoubleBuffered = true;
            content.Dock = DockStyle.None;
            content.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            Controls.Add(content);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);var form=FindForm();
            if(form!=null)form.Load+=delegate{ready=true;PerformLayout();};
        }
        protected override void ScaleControl(SizeF factor, BoundsSpecified specified)
        {
            limit = new SizeF(limit.Width * factor.Width, limit.Height * factor.Height);
            base.ScaleControl(factor, specified);
        }

        protected override void OnLayout(LayoutEventArgs e)
        {
            base.OnLayout(e);
            if(content==null||arranging||!ready||pending||!IsHandleCreated)return;
            pending=true;BeginInvoke((MethodInvoker)delegate{pending=false;if(!IsDisposed)ArrangeContent();});
        }
        private void ArrangeContent()
        {
            arranging = true;
            try
            {
                int width = Math.Max(content.MinimumSize.Width, Math.Min(ClientSize.Width, (int)Math.Round(limit.Width)));
                int height = Math.Max(content.MinimumSize.Height, Math.Min(ClientSize.Height, (int)Math.Round(limit.Height)));
                var bounds = new Rectangle(
                    Math.Max(0, (ClientSize.Width - width) / 2),
                    Math.Max(0, (ClientSize.Height - height) / 2), width, height);
                if (content.Bounds != bounds) content.Bounds = bounds;
            }
            finally { arranging = false; }
        }
    }
}
