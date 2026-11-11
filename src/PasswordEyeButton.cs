using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace ServiceTool
{
    internal sealed class PasswordEyeButton : Button
    {
        private bool mouseFocus;

        protected override void OnMouseDown(MouseEventArgs e)
        {
            mouseFocus = true;
            base.OnMouseDown(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            mouseFocus = false;
            base.OnLostFocus(e);
        }

        internal bool Revealed; internal ThemePalette Palette;
        internal PasswordEyeButton(){FlatStyle=FlatStyle.Flat;FlatAppearance.BorderSize=0;Cursor=Cursors.Hand;AccessibleName="Показать пароль";SetStyle(ControlStyles.UserPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.AllPaintingInWmPaint,true);}
        protected override void OnPaint(PaintEventArgs e){var p=Palette??ThemePalette.Create(false);e.Graphics.Clear(p.SurfaceAlt);e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;float w=Width,h=Height;using(var pen=new Pen(Enabled?p.Text:p.Muted,1.6F))using(var path=new GraphicsPath()){path.AddBezier(w*.2F,h*.5F,w*.38F,h*.17F,w*.62F,h*.17F,w*.8F,h*.5F);path.AddBezier(w*.8F,h*.5F,w*.62F,h*.83F,w*.38F,h*.83F,w*.2F,h*.5F);e.Graphics.DrawPath(pen,path);e.Graphics.DrawEllipse(pen,w*.43F,h*.4F,w*.14F,h*.2F);if(!Revealed)e.Graphics.DrawLine(pen,w*.23F,h*.8F,w*.77F,h*.2F);if(Focused && ShowFocusCues && !mouseFocus)e.Graphics.DrawRectangle(pen, 1, 1, Width-3, Height-3);}}
    }
}
