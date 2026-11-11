using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class Ui2324Tests
    {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static IEnumerable<Control> All(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in All(child))yield return nested;}}
        static FieldInfo Find(object obj,string name){for(Type t=obj.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(f!=null)return f;}throw new Exception(name);}
        static T Field<T>(object obj,string name){return (T)Find(obj,name).GetValue(obj);}
        static void Call(object obj,string name,params object[] args){obj.GetType().GetMethod(name,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(obj,args);}
        static void Show(Form f){f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=f is SoftwareRemoteDialog ? Screen.PrimaryScreen.WorkingArea.Location : new Point(-20000,-20000);f.Show();Application.DoEvents();f.PerformLayout();Application.DoEvents();}
        static void Capture(Form f,string name){using(var image=new Bitmap(f.Width,f.Height)){f.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png"));}}
        [STAThread] static int Main(){try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            foreach(bool dark in new[]{false,true})
            {
                foreach(string id in new[]{"anydesk","rudesktop"})using(var dialog=new SoftwareRemoteDialog(ThemePalette.Create(dark),null,id,false))
                {
                    Show(dialog);var confirmation=Field<BrandTextBox>(dialog,"confirmation");var languages=Field<List<Label>>(dialog,"languages");var language=languages.Single();
                    Check(languages.Count==1&&language.Font.Bold,"One bold keyboard indicator");dialog.UpdateKeyboardState("ru",false);Check(language.Text=="РУС","Russian state");dialog.UpdateKeyboardState("en",false);Check(language.Text=="ENG","English state");
                    Point end=confirmation.PointToScreen(new Point(confirmation.Width,confirmation.Height));Point label=language.PointToScreen(new Point(language.Width,0));Check(Math.Abs(end.X-label.X)<=2&&label.Y>=end.Y,"Indicator below confirmation aligned to input right edge end="+end+" label="+label);
                    foreach(var eye in All(dialog).OfType<PasswordEyeButton>())
                    {
                        var row=(TableLayoutPanel)eye.Parent;var box=(BrandTextBox)row.GetControlFromPosition(0,0);Check(eye.Left-box.Right>=8,"Eye has separate gap");
                    }
                    var notice=All(dialog).OfType<Label>().Single(l=>l.Name=="passwordNotice");Check(notice.TextAlign==ContentAlignment.MiddleLeft&&notice.ForeColor==language.ForeColor,"Notice left aligned with matching theme warning color");
                    Check(Field<BrandButton>(dialog,"ok").Height<=60,"Normal dialog button height");Capture(dialog,"remote2324-"+id+"-"+dark);
                }
                foreach(float scale in new[]{1F,1.25F,1.5F,2F})
                {
                    Size? menuSize=null;
                    foreach(Form form in new Form[]{new MainForm(null,true),new LibraryManagerForm(true),new SoftwareManagerForm(true),new ToolsDeploymentForm(true),new InitialSystemSettingsForm(true)})using(form)
                    {
                        var module=form as ManagerModuleForm;if(module!=null)module.SetTheme(dark,false);var service=form as MainForm;if(service!=null)service.SetTheme(dark,false);Show(form);if(scale!=1){form.Scale(new SizeF(scale,scale));Application.DoEvents();}
                        var menu=All(form).OfType<Button>().Single(b=>b.Text=="В меню");if(!menuSize.HasValue)menuSize=menu.Size;Check(form is LibraryManagerForm || Math.Abs(menu.Width-menuSize.Value.Width)<=1&&Math.Abs(menu.Height-menuSize.Value.Height)<=1,"Same menu size "+form.GetType().Name+" "+scale+" "+menu.Size+" expected "+menuSize);
                        foreach(var size in new[]{form.ClientSize,new Size(1100,1400),new Size(1800,900)}){form.ClientSize=size;Application.DoEvents();form.PerformLayout();Application.DoEvents();Check(menu.Parent.ClientRectangle.Contains(menu.Bounds),"Menu inside footer on orientation change");}
                        form.WindowState=FormWindowState.Maximized;Application.DoEvents();form.WindowState=FormWindowState.Normal;Application.DoEvents();Check(menu.Parent.ClientRectangle.Contains(menu.Bounds),"Menu stable after maximize/restore");
                        if(form is InitialSystemSettingsForm){var grid=Field<ServiceGrid>(form,"grid");Check(!(grid.Parent.Parent is Card)&&grid.TouchScrollingEnabled,"Flat settings table and touch enabled");}
                        if(scale==1)Capture(form,form.GetType().Name+"2324-"+dark);
                    }
                }
                using(var form=new UserGuideForm())
                {
                    form.SetTheme(dark,false);Show(form);var reader=Field<GuideReader>(form,"reader");var search=Field<BrandTextBox>(form,"search");var sections=Field<GuideSectionListBox>(form,"sections");
                    Check(search.Left==0&&search.Height<=search.PreferredHeight+1,"Thin left-aligned search");Check(All(form).OfType<Label>().All(l=>!l.Text.StartsWith("Выбери раздел")),"Old instruction prompt removed");
                    for(int i=0;i<sections.Items.Count;i++){sections.SelectedIndex=i;Application.DoEvents();Check(reader.TextLength>0&&reader.SelectionLength==0&&reader.Visible,"Section text visible without transient selection");}
                    reader.Text=String.Join("\n",Enumerable.Range(0,150).Select(i=>"Строка проверки прокрутки "+i));Application.DoEvents();reader.ScrollTo(reader.MaximumScroll);Application.DoEvents();Check(reader.ScrollPosition>0,"Custom reader scroll reaches lower text");Check(!Field<GuideReaderScrollBar>(form,"readerScroll").ThumbBounds.IsEmpty,"Custom scrollbar has thumb");reader.ScrollTo(0);Check(reader.ScrollPosition==0,"Scroll returns to top");
                    Check(reader.ScrollBars==RichTextBoxScrollBars.None,"No native white scrollbar");sections.SelectedIndex=0;Application.DoEvents();Capture(form,"guide2324-"+dark);
                }
                using(var form=new SoftwareManagerForm(true)){Show(form);Find(form,"checking").SetValue(form,true);Call(form,"Lock",true);var back=Field<BrandButton>(form,"back");Check(back.Enabled,"Software Back enabled during scan");back.PerformClick();Check(form.IsDisposed&&form.MenuRequested,"Software scan closes immediately without confirmation");}
                using(var form=new InitialSystemSettingsForm(true)){Show(form);Find(form,"Scanning").SetValue(form,true);Call(form,"Lock",true);var back=Field<BrandButton>(form,"Back");Check(back.Enabled,"Settings Back enabled during scan");back.PerformClick();Check(form.IsDisposed&&form.MenuRequested,"Settings scan closes immediately without confirmation");}
            }
            var watch=Stopwatch.StartNew();bool cancelled=false;
            try{LibraryCancellation.Current=()=>watch.ElapsedMilliseconds>300;WindowsComponents.Run("Start-Sleep -Seconds 30",delegate{},true);}catch(OperationCanceledException){cancelled=true;}finally{LibraryCancellation.Current=null;}
            Check(cancelled&&watch.Elapsed<TimeSpan.FromSeconds(6),"Read-only PowerShell child cancelled promptly");
            Console.WriteLine("Ui2324Tests: "+count+" passed; previews, scrolling, scan-close fixtures and cancellation of isolated sleep process only.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    }
}
