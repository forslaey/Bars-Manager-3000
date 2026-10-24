using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class Ui2329Tests
    {
        private static int count;
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        private static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var item in All(child))yield return item;}}
        private static void Pump(){for(int i=0;i<5;i++)Application.DoEvents();}
        private static void Show(Form f){f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.ShowInTaskbar=false;f.Show();Pump();}
        private static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
        private static void Capture(Form f,string n){string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews2329");Directory.CreateDirectory(dir);using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(dir,n+".png"));}}
        [STAThread] private static int Main(){try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            foreach(bool dark in new[]{false,true})
            {
                foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var f=new SoftwareManagerForm(true))
                {
                    f.SetTheme(dark,false);Show(f);if(scale!=1F)f.Scale(new SizeF(scale,scale));Pump();
                    var grid=Field<ServiceGrid>(f,"grid");var bar=Field<LibraryGridScrollBar>(f,"gridScroll");
                    var field=Field<BrandTextBox>(f,"sourceLabel");var caption=All(f).OfType<InputCaptionLabel>().Single();
                    Check(caption.Text=="Папка установщиков:","Colon and flush caption");
                    Check(caption.PointToScreen(Point.Empty).X==field.PointToScreen(Point.Empty).X,"Folder caption starts exactly at field edge");
                    foreach(bool offline in new[]{false,true})
                    {
                        if(offline){Field<Dictionary<string,SoftwarePackage>>(f,"packages").Clear();foreach(var check in Field<Dictionary<string,SoftwareCheck>>(f,"checks").Values){check.State=LibraryState.Installed;check.Version="155.0.8059.85";check.Detail="Установлена, x64";}typeof(SoftwareManagerForm).GetMethod("UpdateRows",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(f,null);}
                        foreach(var size in new[]{new Size(1000,700),new Size(1120,780),new Size(1080,1920),new Size(1800,900)})
                        {
                            f.ClientSize=new Size((int)(size.Width*scale),(int)(size.Height*scale));f.PerformLayout();Pump();
                            var bounds=grid.Bounds;var barBounds=bar.Bounds;var columns=grid.Columns.Cast<DataGridViewColumn>().Select(c=>c.Width).ToArray();
                            for(int n=0;n<10;n++)
                            {
                                grid.FirstDisplayedScrollingRowIndex=n%grid.RowCount;Pump();
                                string context=dark+" / "+scale+" / "+size+" / "+offline+" / "+n;
                                Check(grid.Bounds==bounds&&bar.Bounds==barBounds,"Viewport changes while scrolling: "+context);
                                Check(grid.Columns.Cast<DataGridViewColumn>().Select(c=>c.Width).SequenceEqual(columns),"Column widths change while scrolling");
                                Check(bar.ClientRectangle.Contains(bar.ThumbBounds),"Thumb outside scrollbar: "+context+" "+bar.ThumbBounds+" / "+bar.ClientRectangle);
                                if(grid.ColumnHeadersHeight<grid.Height)Check(bar.ThumbBounds.Top>=grid.ColumnHeadersHeight,"Thumb overlaps header");
                                Check(grid.DisplayedRowCount(true)==grid.DisplayedRowCount(false),"Partially cut row: "+context+" first="+grid.FirstDisplayedScrollingRowIndex+" h="+grid.Height+" rows="+String.Join(",",grid.Rows.Cast<DataGridViewRow>().Select(r=>r.Height)));
                                var last=grid.GetRowDisplayRectangle(grid.RowCount-1,false);
                                if(last.Height>0)Check(Math.Abs(last.Bottom-grid.ClientSize.Height)<=1,"Blank space after final row: "+context+" last="+last+" size="+grid.ClientSize);
                            }
                            grid.FirstDisplayedScrollingRowIndex=0;Pump();
                            if(scale==1F&&size.Width==1120){Capture(f,"software-top-"+dark+"-"+offline);grid.FirstDisplayedScrollingRowIndex=grid.RowCount-1;Pump();Capture(f,"software-bottom-"+dark+"-"+offline);}
                        }
                    }
                }
                foreach(Form f in new Form[]{new UserGuideForm(),new ToolsDeploymentForm(true)})using(f)
                {
                    ((ManagerModuleForm)f).SetTheme(dark,false);Show(f);
                    var captions=All(f).OfType<InputCaptionLabel>().ToArray();Check(captions.Length>0&&captions.All(c=>c.Text.EndsWith(":")),"Shared caption rendering and colons");
                }
                foreach(string id in new[]{"anydesk","rudesktop"})using(var f=new SoftwareRemoteDialog(ThemePalette.Create(dark),null,id,false))
                {Show(f);Check(All(f).OfType<InputCaptionLabel>().Count(c=>c.Text.EndsWith(":"))==2,"Password labels use common caption style");}
            }
            Console.WriteLine("Ui2329Tests: "+count+" passed; both themes, offline/preview rows, portrait and simulated DPI; scrollbar bounds and final page. No system operations.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
