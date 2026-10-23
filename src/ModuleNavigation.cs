using System.Drawing;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class ModuleNavigation
    {
        internal static void Configure(Button button)
        {button.Dock=DockStyle.None;button.Anchor=AnchorStyles.Right|AnchorStyles.Bottom;button.Margin=Padding.Empty;button.Size=new Size(140,40);}
    }
}
