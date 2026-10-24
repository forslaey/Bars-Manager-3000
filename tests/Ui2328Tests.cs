using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class Ui2328Tests
    {
        private static int count;
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        private static IEnumerable<Control> All(Control parent){foreach(Control child in parent.Controls){yield return child;foreach(var item in All(child))yield return item;}}
        private static void Pump(){for(int i=0;i<4;i++)Application.DoEvents();}
        private static void Show(Form form){form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.ShowInTaskbar=false;form.Show();Pump();}
        private static Rectangle ScreenBounds(Control c){return new Rectangle(c.PointToScreen(Point.Empty),c.Size);}
        private static void Capture(Form form,string name){string path=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews2328");Directory.CreateDirectory(path);using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(path,name+".png"));}}
        [STAThread] private static int Main(){try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            foreach(bool dark in new[]{false,true})
            {
                using(var form=new UserGuideForm())
                {
                    form.SetTheme(dark,false);Show(form);
                    var search=All(form).OfType<BrandTextBox>().Single();
                    Check(search.BorderStyle==BorderStyle.FixedSingle,"Search uses flat border");
                    Check(search.Palette.InputBorder.GetBrightness()<0.5F||dark,"Light input has contrasting edge");
                    if(!dark)using(var bitmap=new Bitmap(search.Width,search.Height))
                    { search.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));Check(bitmap.GetPixel(0,0).ToArgb()==search.Palette.Accent.ToArgb(),"Light input border is painted brand green"); }
                    var caption=All(form).OfType<Label>().Single(l=>l.Text=="Поиск по всей инструкции:");
                    Check(caption.Font.Bold&&caption.ForeColor==(dark?Color.Yellow:Color.Black),"Search caption preserves requested style");
                    Capture(form,"guide-"+dark);
                }
                foreach(string id in new[]{"anydesk","rudesktop"})using(var form=new SoftwareRemoteDialog(ThemePalette.Create(dark),null,id,false))
                {
                    Show(form);var notice=All(form).OfType<Label>().Single(l=>l.Name=="passwordNotice");var cancel=All(form).OfType<BrandButton>().Single(b=>b.Text=="Отмена");
                    Check(notice.TextAlign==ContentAlignment.MiddleLeft,"Notice aligned left");
                    Check(ScreenBounds(notice).Left==ScreenBounds(cancel).Left,"Notice starts at Cancel left edge");
                    Check(All(form).OfType<BrandTextBox>().All(t=>t.BorderStyle==BorderStyle.FixedSingle),"Password fields use flat borders");
                    Capture(form,id+"-"+dark);
                }
                foreach(float scale in new[]{1F,1.25F,1.5F,2F})using(var form=new SoftwareManagerForm(true))
                {
                    form.SetTheme(dark,false);Show(form);if(scale!=1F)form.Scale(new SizeF(scale,scale));Pump();
                    var grid=All(form).OfType<ServiceGrid>().Single();var host=All(form).OfType<FillContentPanel>().Single();
                    var choices=All(form).OfType<BrandCheckBox>().OrderBy(c=>c.Top).ToArray();
                    Check(choices.Length==3&&choices.Select(c=>c.Height).Distinct().Count()==1,"Equal checkbox height");
                    float factor=choices[0].Height/36F;Check(choices.All(c=>c.Height>=36*scale-1),"Comfortable checkbox height");
                    Check(choices[1].Top-choices[0].Bottom==choices[2].Top-choices[1].Bottom,"Equal checkbox spacing");
                    var field=All(form).OfType<BrandTextBox>().Single();var caption=All(form).OfType<Label>().Single(l=>l.Text=="Папка установщиков:");
                    Check(ScreenBounds(field).Top-ScreenBounds(caption).Bottom>=6*factor-1,"Folder caption separated from input");
                    foreach(var size in new[]{new Size(1000,700),new Size(1120,780),new Size(1080,1920),new Size(1800,900)})
                    {
                        form.ClientSize=new Size((int)(size.Width*scale),(int)(size.Height*scale));form.PerformLayout();Pump();
                        var before=ScreenBounds(grid);var bodyBounds=host.Controls[0].Bounds;var widths=grid.Columns.Cast<DataGridViewColumn>().Select(c=>c.Width).ToArray();
                        for(int n=0;n<12;n++)
                        {
                            grid.Focus();grid.FirstDisplayedScrollingRowIndex=n%grid.RowCount;
                            typeof(Control).GetMethod("OnMouseWheel",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(grid,new object[]{new HandledMouseEventArgs(MouseButtons.None,0,20,60,n%2==0?-120:120)});
                            grid.TouchPan(new Point(60,120),true,false);grid.TouchPan(new Point(60,40),false,true);Pump();
                            Check(ScreenBounds(grid)==before,"Grid moves while scrolling: "+size+" / "+scale);
                            Check(host.Controls[0].Bounds==bodyBounds,"Focus scrolls outer content");
                            Check(grid.Columns.Cast<DataGridViewColumn>().Select(c=>c.Width).SequenceEqual(widths),"Scrolling changes column widths");
                        }
                        Check(All(form).OfType<BrandButton>().Single(b=>b.Text=="В меню").Visible,"Menu remains visible");
                        if(scale==1F&&size.Width==1120)Capture(form,"software-"+dark);
                    }
                }
            }
            Console.WriteLine("Ui2328Tests: "+count+" passed; both themes, portrait/landscape and simulated DPI, scrolling and focus. No system operations.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    }
}
