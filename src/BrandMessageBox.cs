using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class BrandMessageBox
    {
        internal static DialogResult ConfirmStop(Control owner,string text)
        {using(var dialog=new BrandMessageDialog(text,"Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2,ThemePalette.Create(owner.BackColor.GetBrightness()<.5F),true))return ScopedDialogs.Show(owner.FindForm(), dialog);}
        internal static DialogResult Show(string text,string caption="BARS Manager 3000",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None,MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1)
        {return Show(Form.ActiveForm,text,caption,buttons,icon,defaultButton);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption="BARS Manager 3000",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None,MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1)
        {
            var control=owner as Control;
            bool dark=control==null?ThemePreference.LoadDark():control.BackColor.GetBrightness()<0.5F;
            using(var dialog=new BrandMessageDialog(text,caption,buttons,icon,defaultButton,ThemePalette.Create(dark)))
            {
                if (owner == null)
                {
                    dialog.StartPosition = FormStartPosition.CenterScreen;
                    return dialog.ShowDialog();
                }
                return control == null ? dialog.ShowDialog(owner) : ScopedDialogs.Show(control.FindForm(), dialog);
            }
        }
    }
    internal sealed class BrandMessageDialog : BrandWindow
    {
        internal void SetButtonText(DialogResult result,string text)
        {
            foreach(Control control in Controls)
                if(RenameButton(control,result,text))return;
        }
        private static bool RenameButton(Control control,DialogResult result,string text)
        {
            var button=control as Button;
            if(button!=null&&button.DialogResult==result){button.Text=text;return true;}
            foreach(Control child in control.Controls)
                if(RenameButton(child,result,text))return true;
            return false;
        }
        internal BrandMessageDialog(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton,ThemePalette colors,bool compact=false)
        {
            SuspendLayout();AutoScaleMode=AutoScaleMode.None;AutoScaleDimensions=new SizeF(96,96);
            Font=new Font("Segoe UI",10F);Text=caption;Icon=BrandAssets.LoadIcon();
            BackColor=colors.Canvas;ForeColor=colors.Text;FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            Size measured;
            using(var bitmap=new Bitmap(1,1))using(var graphics=Graphics.FromImage(bitmap))
                measured=TextRenderer.MeasureText(graphics,text,Font,new Size(compact?390:550,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            ClientSize=compact?new Size(520,Math.Max(164,Math.Min(440,measured.Height+122))):new Size(640,Math.Max(208,Math.Min(560,measured.Height+126)));
            var layout=new BufferedLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=2,BackColor=colors.Canvas};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,54));
            var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,10)};
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var symbol=new Label{Text=icon==MessageBoxIcon.Warning?"!":icon==MessageBoxIcon.Error?"×":icon==MessageBoxIcon.Question?"?":"i",Font=new Font("Segoe UI",20F,FontStyle.Bold),ForeColor=icon==MessageBoxIcon.Warning?Color.FromArgb(210,153,15):colors.Accent,Dock=DockStyle.Fill,TextAlign=compact?ContentAlignment.MiddleCenter:ContentAlignment.TopCenter,Margin=Padding.Empty,AccessibleName=icon.ToString()};
            var message=new JournalTextBox{Text=text,Dock=DockStyle.Fill,BackColor=colors.Canvas,ForeColor=colors.Text,Font=Font,TabStop=false};
            var viewport=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var scroll=new JournalScrollBar{AlwaysVisible=false,Dock=DockStyle.Right,Width=14,Palette=colors};scroll.Bind(message);viewport.Controls.Add(message);viewport.Controls.Add(scroll);
            if(compact){symbol.Font=new Font("Segoe UI",16F,FontStyle.Bold);message.Multiline=true;message.ScrollBars=ScrollBars.None;message.Dock=DockStyle.None;message.Anchor=AnchorStyles.Left|AnchorStyles.Right;message.Height=measured.Height+6;viewport.SizeChanged+=delegate{message.Width=Math.Max(1,viewport.ClientSize.Width-scroll.Width);message.Top=Math.Max(0,(viewport.ClientSize.Height-message.Height)/2);};}
            content.Controls.Add(symbol,0,0);content.Controls.Add(viewport,1,0);layout.Controls.Add(content,0,0);
            var row=new TableLayoutPanel{Dock=DockStyle.Fill,Margin=Padding.Empty};
            var footerButtons = new System.Collections.Generic.List<Button>();
            DialogResult[] results=buttons==MessageBoxButtons.YesNo?new[]{DialogResult.Yes,DialogResult.No}:buttons==MessageBoxButtons.YesNoCancel?new[]{DialogResult.Yes,DialogResult.No,DialogResult.Cancel}:buttons==MessageBoxButtons.OKCancel?new[]{DialogResult.OK,DialogResult.Cancel}:buttons==MessageBoxButtons.RetryCancel?new[]{DialogResult.Retry,DialogResult.Cancel}:new[]{DialogResult.OK};
            Button selected=null;
            for(int i=0;i<results.Length;i++)
            {
                var result=results[i];string label=result==DialogResult.Yes?"Да":result==DialogResult.No?"Нет":result==DialogResult.Cancel?"Отмена":result==DialogResult.Retry?"Повторить":"Хорошо";
                var button=new BrandButton{Text=label,DialogResult=result,Palette=colors,Primary=i==0,Destructive=result==DialogResult.No,Size=new Size(134,44),Margin=new Padding(10,0,0,0),Font=Font};
                footerButtons.Add(button);if(i==(int)defaultButton/256)selected=button;
                if(result==DialogResult.Cancel||result==DialogResult.No)CancelButton=button;
            }
            if(CancelButton==null){ControlBox=buttons==MessageBoxButtons.OK;CancelButton=footerButtons.First();}
            ModuleNavigation.StandardFooter(row, footerButtons.ToArray());
            AcceptButton=selected;layout.Controls.Add(row,0,1);Controls.Add(layout);
            Shown+=delegate{if(selected!=null)selected.Focus();};
            AutoScaleMode=AutoScaleMode.Dpi;PerformAutoScale();ResumeLayout(true);
        }
    }
}
