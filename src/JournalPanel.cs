using System.Drawing;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class JournalPanel
    {
        internal static Card Create(JournalTextBox journal,string title,ThemePalette colors)
        {
            var card=new Card{Dock=DockStyle.Fill,Padding=new Padding(12,8,12,8),Margin=Padding.Empty,BorderColor=colors.Border};
            var heading=new Label{Text=title,Tag="muted",Dock=DockStyle.Top,Height=24,Margin=Padding.Empty,Font=new Font("Segoe UI",9F),ForeColor=colors.Muted,TextAlign=ContentAlignment.MiddleLeft};
            journal.Dock=DockStyle.Fill;journal.Font=new Font("Consolas",9F);
            var viewport=new Panel{Dock=DockStyle.Fill,Margin=Padding.Empty};var scroll=new JournalScrollBar{Dock=DockStyle.Right,Width=14};scroll.Bind(journal);
            viewport.Controls.Add(journal);viewport.Controls.Add(scroll);card.Controls.Add(viewport);card.Controls.Add(heading);return card;
        }
    }
}
