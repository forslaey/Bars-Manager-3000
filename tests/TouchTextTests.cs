using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class TouchTextTests
 {
  static int count;
  static void Check(bool ok,string text){count++;if(!ok)throw new Exception(text);}
  [STAThread] static int Main()
  {
   Application.EnableVisualStyles();
   try {
    using(var form=new Form {ClientSize=new Size(450,240)})
    using(var journal=new JournalTextBox {Dock=DockStyle.Fill}) {
     form.Controls.Add(journal);journal.Text=String.Join("\r\n",Enumerable.Range(0,200).Select(i=>"Строка "+i));form.Show();Application.DoEvents();
     journal.Select(4,8);int start=journal.SelectionStart,length=journal.SelectionLength;
     var support=new TouchScrollSupport(journal,delta=>journal.ScrollByLines(delta/Math.Max(1,journal.Font.Height)));
     support.Pan(new Point(20,180),true,false);support.Pan(new Point(20,20),false,true);
     Check(journal.FirstVisibleLine>0,"Journal pan");Check(journal.SelectionStart==start&&journal.SelectionLength==length,"Selection preserved");
     support.Pan(new Point(20,20),true,false);support.Pan(new Point(20,10000),false,true);Check(journal.FirstVisibleLine==0,"Top clamp");
     Check(!support.TouchMouse(0x201,new Point(2,2),false),"Mouse unchanged");
     Check(support.TouchMouse(0x201,new Point(2,2),true),"Touch down delayed");Check(!support.TouchMouse(0x202,new Point(2,2),true),"Tap preserved");
     support.TouchMouse(0x201,new Point(2,100),true);support.TouchMouse(0x200,new Point(2,20),true);Check(support.TouchMouse(0x202,new Point(2,20),true),"Swipe suppresses click");
     support.Reset();journal.Enabled=false;support.Pan(new Point(0,100),true,false);support.Pan(Point.Empty,false,true);Check(journal.FirstVisibleLine==0,"Disabled no pan");
    }
    using(var form=new Form {ClientSize=new Size(450,240)})using(var reader=new GuideReader {Dock=DockStyle.Fill}) {
     form.Controls.Add(reader);reader.Text=String.Join("\n",Enumerable.Range(0,200).Select(i=>"Описание "+i));form.Show();Application.DoEvents();
     var field=typeof(GuideReader).GetField("touchScroll",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var support=(TouchScrollSupport)field.GetValue(reader);
     support.Pan(new Point(0,180),true,false);support.Pan(Point.Empty,false,true);Check(reader.ScrollPosition>0,"Guide pan");
    }
    using(var form=new Form {ClientSize=new Size(450,240)})using(var list=new GuideSectionListBox {Dock=DockStyle.Fill}) {
     form.Controls.Add(list);for(int i=0;i<100;i++)list.Items.Add("Раздел "+i);form.Show();Application.DoEvents();list.SelectedIndex=0;
     var field=typeof(GuideSectionListBox).GetField("touchScroll",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);var support=(TouchScrollSupport)field.GetValue(list);
     support.Pan(new Point(0,200),true,false);support.Pan(Point.Empty,false,true);Check(list.TopIndex>0,"Sections pan");Check(list.SelectedIndex==0,"Swipe does not select section");
    }
    Console.WriteLine("PASS: "+count+" touch text/list checks; no input injection or system changes.");return 0;
   }catch(Exception e){Console.Error.WriteLine(e);return 1;}
  }
 }
}
