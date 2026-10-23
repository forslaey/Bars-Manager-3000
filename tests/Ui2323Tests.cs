using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class Ui2323Tests
    {
        static int count;
        static void Check(bool ok,string message){if(!ok)throw new Exception(message);count++;}
        static IEnumerable<Control> All(Control parent){foreach(Control c in parent.Controls){yield return c;foreach(var child in All(c))yield return child;}}
        static T Field<T>(object form,string name){return (T)form.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);}
        static void Show(Form form){form.ShowInTaskbar=false;form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-22000,-22000);form.Show();Application.DoEvents();}
        static void Capture(Form form,string name){using(var image=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png"));}}
        [STAThread] static int Main(){try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            var state=new Dictionary<string,string>{{"admin.EnableLUA","0"},{"firewall.Domain","True"},{"firewall.Private","False"},{"firewall.Public","True"},{"defender.RealTimeProtection","False"},{"folders.Count","1"},{"folders.Checked","7"},{"folders.Restricted","0"},{"folders.Errors","0"},{"folders.Skipped","0"}};
            Check(SystemSettingsPresentation.Status("admin",state)=="Отключено","Exact UAC state");Check(SystemSettingsPresentation.Status("firewall",state).Contains("Частный: отключено"),"Russian firewall state");Check(SystemSettingsPresentation.Status("defender",state).EndsWith("отключена"),"Russian defender state");Check(SystemSettingsPresentation.Status("folders",state).StartsWith("Полные права на папки выданы\n"),"Verified recursive permissions status");state["folders.Restricted"]="1";Check(!SystemSettingsPresentation.Status("folders",state).StartsWith("Полные права на папки выданы\n"),"Partial grants never claim full access");state.Remove("folders.Errors");Check(!SystemSettingsPresentation.Status("folders",state).StartsWith("Полные права на папки выданы\n"),"Missing audit field never claims full access");
            foreach(var p in InitialSystemSettings.PasswordPolicies)state["passwords."+p.Name]=InitialSystemSettings.Value(p.Bars);
            Check(SystemSettingsPresentation.Status("passwords",state).StartsWith("Политики BARS применены"),"Password profile compared with actual values");state["passwords.MinimumPasswordLength"]="8";Check(SystemSettingsPresentation.Status("passwords",state).Contains("отличаются"),"Different password policy detected");
            Check(MainForm.ServiceActionMessage(new ServiceEntry{Name="Fixture",DisplayName="Тестовая служба"},"startup","auto-delayed").Contains("с задержкой"),"Russian service action");
            foreach(bool dark in new[]{false,true})
            {
                using(var form=new SoftwareManagerForm(true))
                {
                    form.SetTheme(dark,false);Show(form);
                    Check(Field<BrandTextBox>(form,"sourceLabel").Width>=200,"Source path remains readable after initial nested layout");
                    var grid=Field<ServiceGrid>(form,"grid");var back=Field<BrandButton>(form,"back");var log=Field<JournalTextBox>(form,"journal");
                    foreach(var size in new[]{form.ClientSize,new Size(1360,950),new Size(1280,920)})
                    {
                        form.ClientSize=size;Application.DoEvents();
                        Check(form.RectangleToScreen(form.ClientRectangle).Contains(back.RectangleToScreen(back.ClientRectangle)),"Back immediately reachable without outer scroll");
                        Check(back.PointToScreen(Point.Empty).Y>=log.PointToScreen(new Point(0,log.Height)).Y,"Journal above footer");
                        Check(!All(form).OfType<FillContentPanel>().Any(p=>p.Contains(back)||p.Contains(log)),"Navigation and journal never scroll with working area");
                        if(size==form.ClientSize&&size.Width>=1280&&size.Height>=920)Check(grid.DisplayedRowCount(false)==5,"All five rows fit laptop viewport grid="+grid.Size+" form="+form.ClientSize+" client="+grid.ClientRectangle+" shown="+grid.DisplayedRowCount(false)+" first="+grid.FirstDisplayedScrollingRowIndex+" lastRect="+grid.GetRowDisplayRectangle(4,false)+" header="+grid.ColumnHeadersHeight+" rows="+String.Join(",",grid.Rows.Cast<DataGridViewRow>().Select(r=>r.Height)));
                    }
                    Capture(form,"software2323-"+dark);
                }
                using(var form=new MainForm(null,true)){
                    form.SetTheme(dark,false);Show(form);var log=Field<TextBox>(form,"log");int initial=log.TextLength;
                    for(int i=0;i<30;i++)form.GetType().GetMethod("AppendLog",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[]{"Проверка действия "+i});
                    Check(log.TextLength>initial&&((JournalTextBox)log).FirstVisibleLine>0,"Service action log automatically follows newest entry");
                    Check(All(form).OfType<Label>().Any(c=>c.Text=="Журнал текущего сеанса"),"Session journal heading retained");Capture(form,"services2323-"+dark);
                }
                using(var hub=new ManagerHub()){hub.SetTheme(dark,false);Show(hub);Capture(hub,"hub2323-"+dark);}
                foreach(float scale in new[]{1.25F,1.5F,2F})using(var form=new SoftwareManagerForm(true))
                {
                    form.SetTheme(dark,false);Show(form);form.Scale(new SizeF(scale,scale));Application.DoEvents();
                    form.WindowState=FormWindowState.Maximized;Application.DoEvents();form.PerformLayout();Application.DoEvents();
                    var back=Field<BrandButton>(form,"back");var log=Field<JournalTextBox>(form,"journal");
                    Check(form.RectangleToScreen(form.ClientRectangle).Contains(back.RectangleToScreen(back.ClientRectangle)),"Back remains on screen at large text scale");
                    Check(form.RectangleToScreen(form.ClientRectangle).Contains(log.RectangleToScreen(log.ClientRectangle)),"Journal remains on screen at large text scale");
                    var work=All(form).OfType<FillContentPanel>().Single();work.AutoScrollPosition=new Point(0,Int32.MaxValue);Application.DoEvents();
                    Check(form.RectangleToScreen(form.ClientRectangle).Contains(back.RectangleToScreen(back.ClientRectangle)),"Scrolling work area never moves navigation");
                }
                foreach(string text in new[]{"Перезагрузить компьютер сейчас?\r\nСохрани открытые документы.",LibraryManagerForm.CancelWarning})
                using(var dialog=new BrandMessageDialog(text,"Подтверждение",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2,ThemePalette.Create(dark)))
                {
                    Show(dialog);Check(((Button)dialog.AcceptButton).DialogResult==DialogResult.No,"Safe default preserved");Check(((Button)dialog.CancelButton).DialogResult==DialogResult.No,"Escape cancels action");
                    foreach(var button in All(dialog).OfType<BrandButton>())Check(button.Parent.ClientRectangle.Contains(button.Bounds),"Dialog button fits");
                    Check(All(dialog).OfType<JournalTextBox>().Single().BackColor==dialog.BackColor,"Message theme matches dialog");Capture(dialog,"dialog2323-"+dark+"-"+text.Length);
                    ((Button)dialog.CancelButton).PerformClick();Check(dialog.DialogResult==DialogResult.No,"No button returns standard result");
                }
            }
            Console.WriteLine("Ui2323Tests: "+count+" passed; preview, status fixtures and dialog result checks only.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    }
}
