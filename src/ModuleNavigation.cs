using System.Drawing;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class ModuleHeadingStyle
    {
        internal const int Left=12, Top=8;
        internal static Label Create(string text,bool alignToEdge=false)
        { return new FlushLabel {InkLeft=alignToEdge?(System.Func<Graphics,int>)(g=>0):null,Text=text,Font=new Font("Segoe UI",15F,FontStyle.Bold),Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true}; }
    }
    internal static class ModuleNavigation
    {
        internal static bool ReturnToHub()
        {
            var context = ManagerApplicationContext.ActiveContext;
            return context != null && context.ShowHub();
        }
        internal static T ShowDialog<T>(Form owner, System.Func<T> factory) where T : Form
        {
            var context=ManagerApplicationContext.ActiveContext;
            T form=context==null?factory():context.Dialog(factory);
            if(form.IsHandleCreated)form.StartPosition=FormStartPosition.Manual;
            var module=form as ManagerModuleForm; if(module!=null)module.SetTheme(ThemePreference.LoadDark(),false);
            try { WindowPlacement.Constrain(form); form.ShowDialog(owner); return form; }
            finally { if(context==null)form.Dispose(); }
        }
        internal static void EqualButtons(TableLayoutPanel row,Control scroll)
        {
            bool arranging=false;
            row.Layout+=delegate{if(arranging||row.Width<1)return;arranging=true;try{
                int n=row.ColumnCount,gap=System.Math.Max(1,(int)System.Math.Round(10F*row.Font.Height/16F)),inset=scroll.Visible?scroll.Width:0;
                int available=System.Math.Max(n,row.ClientSize.Width-inset),width=System.Math.Max(1,(available-gap*(n-1))/n),extra=available-width*n-gap*(n-1);
                row.Padding=new Padding(0,0,inset,0);
                for(int i=0;i<n;i++){int spacing=i==n-1?0:gap+extra/(n-1)+(i<extra%(n-1)?1:0);var control=row.GetControlFromPosition(i,0);if(control!=null)control.Margin=new Padding(0,0,spacing,0);float wanted=width+spacing;if(row.ColumnStyles[i].SizeType!=SizeType.Absolute||System.Math.Abs(row.ColumnStyles[i].Width-wanted)>.1){row.ColumnStyles[i].SizeType=SizeType.Absolute;row.ColumnStyles[i].Width=wanted;}}
            }finally{arranging=false;}};
        }
        internal static void Configure(Button button)
        {button.Dock=DockStyle.None;button.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;button.Margin=Padding.Empty;button.Size=new Size(140,40);}
    }
}
