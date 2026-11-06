using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class Ui2330Tests
    {
        private static int count;
        private static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        private static IEnumerable<Control> All(Control c){foreach(Control x in c.Controls){yield return x;foreach(var item in All(x))yield return item;}}
        private static void Pump(){for(int i=0;i<5;i++)Application.DoEvents();}
        private static void Show(Form f,bool dark,float scale){var m=f as ManagerModuleForm;if(m!=null)m.SetTheme(dark,false);var s=f as MainForm;if(s!=null)s.SetTheme(dark,false);f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.ShowInTaskbar=false;f.Show();Pump();if(scale!=1F)f.Scale(new SizeF(scale,scale));Pump();}
        private static Rectangle Bounds(Control c){return new Rectangle(c.PointToScreen(Point.Empty),c.Size);}
        private static T Field<T>(object o,string n){return (T)o.GetType().GetField(n,BindingFlags.NonPublic|BindingFlags.Instance).GetValue(o);}
        private static void Capture(Form f,string n){string dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews2330");Directory.CreateDirectory(dir);using(var b=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(dir,n+".png"));}}
        private static Rectangle Glyph(Label label){var size=TextRenderer.MeasureText(label.Text,label.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine);var p=label.PointToScreen(Point.Empty);return new Rectangle(p.X,p.Y+(label.Height-size.Height)/2,size.Width,size.Height);}
        private static int Gap(Control caption,Control field){return Bounds(field).Top-Bounds(caption).Bottom;}
        [STAThread] private static int Main(){try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})
            {

                string[] names={"Менеджер служб","Менеджер системных библиотек Windows","Программы для рабочего места пользователя","Первичные системные настройки","Распаковка утилит","Компоненты Windows"};
                Form[] forms={new MainForm(null,true),new LibraryManagerForm(true),new SoftwareManagerForm(true),new InitialSystemSettingsForm(true),new ToolsDeploymentForm(true),new WindowsFeaturesForm(true,true)};
                int? fieldGap=null;
                for(int i=0;i<forms.Length;i++)using(var f=forms[i])
                {
                    Show(f,dark,scale);var title=All(f).OfType<Label>().Single(l=>l.Text==names[i]);

                    Check(title is FlushLabel,"Module title uses flush rendering: "+f.GetType().Name);
                    Check(title.Parent.ClientRectangle.Contains(title.Bounds),"Title fits its own module header: "+f.GetType().Name);
                    if(f is MainForm)
                    {
                        var hint=Field<FlushLabel>(f,"fileTitle");var frame=Field<PathFieldFrame>(f,"pathField");
                        int a=Glyph(hint).Top-Glyph(title).Bottom,b=Bounds(frame).Top-Glyph(hint).Bottom;
                        Check(a>0&&b>0&&Math.Abs(a-b)<=Math.Ceiling(3*scale*f.CurrentAutoScaleDimensions.Width/96F),"Equal visible service gaps: "+a+" / "+b+" / "+scale);
                        if(scale==1F)Capture(f,"services-"+dark);
                    }
                    if(f is SoftwareManagerForm)
                    {
                        var caption=All(f).OfType<InputCaptionLabel>().Single();var input=All(f).OfType<BrandTextBox>().Single();fieldGap=Gap(caption,input);
                    }
                    if(f is ToolsDeploymentForm)
                    {
                        var captions=All(f).OfType<InputCaptionLabel>().OrderBy(c=>c.Top).ToArray();var inputs=All(f).OfType<BrandTextBox>().OrderBy(c=>c.PointToScreen(Point.Empty).Y).ToArray();
                        for(int n=0;n<2;n++){Check(captions[n].PointToScreen(Point.Empty).X==inputs[n].PointToScreen(Point.Empty).X,"Tools caption left aligned");Check(captions[n].Height>=captions[n].Font.Height&&Math.Abs(Gap(captions[n],inputs[n])-fieldGap.Value)<=1,"Tools caption geometry matches software: "+Gap(captions[n],inputs[n])+" / "+fieldGap);}
                        if(scale==1F)Capture(f,"tools-"+dark);
                    }
                    if(f is InitialSystemSettingsForm)
                    {
                        var grid=Field<ServiceGrid>(f,"grid");var bar=Field<LibraryGridScrollBar>(f,"gridScroll");
                        foreach(DataGridViewRow row in grid.Rows)row.Cells[2].Value="Политики отличаются от профиля BARS\nПодробности в журнале";Pump();
                        foreach(var size in new[]{new Size(950,680),new Size(1080,840),new Size(1080,1920),new Size(1600,900)})
                        {
                            f.ClientSize=new Size((int)(size.Width*scale),(int)(size.Height*scale));f.PerformLayout();Pump();var bounds=grid.Bounds;
                            for(int n=0;n<12;n++)
                            {
                                grid.FirstDisplayedScrollingRowIndex=n%grid.RowCount;grid.TouchPan(new Point(70,150),true,false);grid.TouchPan(new Point(70,30),false,true);Pump();
                                Check(grid.Bounds==bounds,"Settings viewport shifts on scroll");Check(grid.DisplayedRowCount(true)==grid.DisplayedRowCount(false),"Settings cuts last row");
                                Check(bar.ClientRectangle.Contains(bar.ThumbBounds)&&bar.ThumbBounds.Top>=grid.ColumnHeadersHeight,"Settings thumb leaves row viewport");
                                var last=grid.GetRowDisplayRectangle(grid.RowCount-1,false);if(last.Height>0)Check(Math.Abs(last.Bottom-grid.ClientSize.Height)<=1,"Settings leaves blank final page");
                            }
                            if(scale==1F&&size.Width==1080&&size.Height==840)Capture(f,"settings-bottom-"+dark);
                        }
                    }
                }
            }
            Console.WriteLine("Ui2330Tests: "+count+" passed; module headings, equal service gaps, matching field captions and settings scrolling; both themes and simulated DPI. No system operations.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
    }
}
