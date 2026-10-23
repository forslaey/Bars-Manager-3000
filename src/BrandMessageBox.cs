using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class BrandMessageBox
    {
        internal static DialogResult Show(string text,string caption="BARS Manager 3000",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None,MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1)
        {return Show(Form.ActiveForm,text,caption,buttons,icon,defaultButton);}
        internal static DialogResult Show(IWin32Window owner,string text,string caption="BARS Manager 3000",MessageBoxButtons buttons=MessageBoxButtons.OK,MessageBoxIcon icon=MessageBoxIcon.None,MessageBoxDefaultButton defaultButton=MessageBoxDefaultButton.Button1)
        {
            var control=owner as Control;
            bool dark=control==null?ThemePreference.LoadDark():control.BackColor.GetBrightness()<0.5F;
            using(var dialog=new BrandMessageDialog(text,caption,buttons,icon,defaultButton,ThemePalette.Create(dark)))
                return owner==null?dialog.ShowDialog():dialog.ShowDialog(owner);
        }
    }
    internal sealed class BrandMessageDialog : Form
    {
        internal BrandMessageDialog(string text,string caption,MessageBoxButtons buttons,MessageBoxIcon icon,MessageBoxDefaultButton defaultButton,ThemePalette colors)
        {
            SuspendLayout();AutoScaleMode=AutoScaleMode.None;AutoScaleDimensions=new SizeF(96,96);
            Font=new Font("Segoe UI",10F);Text=caption;Icon=BrandAssets.LoadIcon();
            BackColor=colors.Canvas;ForeColor=colors.Text;FormBorderStyle=FormBorderStyle.FixedDialog;
            MaximizeBox=MinimizeBox=false;ShowInTaskbar=false;StartPosition=FormStartPosition.CenterParent;
            NativeWindowTheme.Bind(this,()=>colors.Dark);
            Size measured;
            using(var bitmap=new Bitmap(1,1))using(var graphics=Graphics.FromImage(bitmap))
                measured=TextRenderer.MeasureText(graphics,text,Font,new Size(550,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
            ClientSize=new Size(640,Math.Max(208,Math.Min(560,measured.Height+126)));
            var layout=new BufferedLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(22),ColumnCount=1,RowCount=2,BackColor=colors.Canvas};
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,48));
            var content=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=2,RowCount=1,Margin=new Padding(0,0,0,18)};
            content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,44));content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));content.RowStyles.Add(new RowStyle(SizeType.Percent,100));
            var symbol=new Label{Text=icon==MessageBoxIcon.Warning?"!":icon==MessageBoxIcon.Error?"×":icon==MessageBoxIcon.Question?"?":"i",Font=new Font("Segoe UI",20F,FontStyle.Bold),ForeColor=icon==MessageBoxIcon.Warning?Color.FromArgb(210,153,15):colors.Accent,Dock=DockStyle.Fill,TextAlign=ContentAlignment.TopCenter,Margin=Padding.Empty,AccessibleName=icon.ToString()};
            var message=new JournalTextBox{Text=text,Dock=DockStyle.Fill,BackColor=colors.Canvas,ForeColor=colors.Text,Font=Font,TabStop=false};
            var viewport=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var scroll=new JournalScrollBar{Dock=DockStyle.Right,Width=14,Palette=colors};scroll.Bind(message);viewport.Controls.Add(message);viewport.Controls.Add(scroll);
            content.Controls.Add(symbol,0,0);content.Controls.Add(viewport,1,0);layout.Controls.Add(content,0,0);
            var row=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft,WrapContents=false,Margin=Padding.Empty};
            DialogResult[] results=buttons==MessageBoxButtons.YesNo?new[]{DialogResult.Yes,DialogResult.No}:buttons==MessageBoxButtons.YesNoCancel?new[]{DialogResult.Yes,DialogResult.No,DialogResult.Cancel}:buttons==MessageBoxButtons.OKCancel?new[]{DialogResult.OK,DialogResult.Cancel}:buttons==MessageBoxButtons.RetryCancel?new[]{DialogResult.Retry,DialogResult.Cancel}:new[]{DialogResult.OK};
            Button selected=null;
            for(int i=results.Length-1;i>=0;i--)
            {
                var result=results[i];string label=result==DialogResult.Yes?"Да":result==DialogResult.No?"Нет":result==DialogResult.Cancel?"Отмена":result==DialogResult.Retry?"Повторить":"Хорошо";
                var button=new BrandButton{Text=label,DialogResult=result,Palette=colors,Primary=i==0,Size=new Size(134,44),Margin=new Padding(10,0,0,0),Font=Font};
                row.Controls.Add(button);if(i==(int)defaultButton/256)selected=button;
                if(result==DialogResult.Cancel||result==DialogResult.No)CancelButton=button;
            }
            if(CancelButton==null){ControlBox=buttons==MessageBoxButtons.OK;CancelButton=row.Controls.OfType<Button>().First();}
            AcceptButton=selected;layout.Controls.Add(row,0,1);Controls.Add(layout);
            Shown+=delegate{if(selected!=null)selected.Focus();};
            AutoScaleMode=AutoScaleMode.Dpi;PerformAutoScale();ResumeLayout(true);
        }
    }
}
