using System.Drawing;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class ModuleHeadingStyle
    {
        internal const int Left=12, Top=8;
        internal static Label Create(string text)
        { return new FlushLabel {Text=text,Font=new Font("Segoe UI",15F,FontStyle.Bold),Dock=DockStyle.Fill,Margin=Padding.Empty,TextAlign=ContentAlignment.MiddleLeft,AutoEllipsis=true}; }
    }
    internal static class ModuleNavigation
    {
        internal static T ShowDialog<T>(Form owner, System.Func<T> factory) where T : Form
        {
            var context=ManagerApplicationContext.ActiveContext;
            T form=context==null?factory():context.Dialog(factory);
            if(form.IsHandleCreated)form.StartPosition=FormStartPosition.Manual;
            var module=form as ManagerModuleForm; if(module!=null)module.SetTheme(ThemePreference.LoadDark(),false);
            try { WindowPlacement.Constrain(form); form.ShowDialog(owner); return form; }
            finally { if(context==null)form.Dispose(); }
        }
        internal static void Configure(Button button)
        {button.Dock=DockStyle.None;button.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;button.Margin=Padding.Empty;button.Size=new Size(140,40);}
    }
}
