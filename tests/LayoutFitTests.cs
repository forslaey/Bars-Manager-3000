using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class LayoutFitTests
    {
        private static int count;
        private static IEnumerable<Control> Children(Control parent) {foreach(Control c in parent.Controls){yield return c;foreach(var item in Children(c))yield return item;}}
        private static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}
        [STAThread] internal static int Main()
        {
            try
            {
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                foreach(bool dark in new[]{false,true}) foreach(float scale in new[]{1F,1.25F,1.5F,2F})
                {
                    var palette=ThemePalette.Create(dark);
                    var forms=new Form[]{new ManagerHub(),new InitialSystemSettingsForm(true),new LibraryManagerForm(true),new SoftwareManagerForm(true),new ToolsDeploymentForm(true),new WindowsFeaturesForm(true,true),new MainForm(null,true),new AboutDialog(palette,null),new ServiceAccountDialog(palette,ServiceInstallProfile.Default(@"C:\fixture\HttpService.exe"),false),new SoftwareRemoteDialog(palette,null,"anydesk",false),new SoftwareRemoteDialog(palette,null,"rudesktop",false),new LibraryActionDialog(palette,null,new LibraryCheck[0]),new LogArchiveDialog(palette,"Служба BARS",new List<LogDateFolder>(),DateTime.Today)};
                    foreach(var form in forms) using(form)
                    {
                        var module=form as ManagerModuleForm;if(module!=null)module.SetTheme(dark,false);var services=form as MainForm;if(services!=null)services.SetTheme(dark,false);
                        form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-22000,-22000);form.ShowInTaskbar=false;form.Show();Application.DoEvents();
                        if(form is SoftwareManagerForm)
                        {
                            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                            var checks=(Dictionary<string,SoftwareCheck>)form.GetType().GetField("checks",flags).GetValue(form);
                            var packages=(Dictionary<string,SoftwarePackage>)form.GetType().GetField("packages",flags).GetValue(form);
                            foreach(string id in checks.Keys.ToArray()){checks[id].State=LibraryState.Installed;checks[id].Version=id=="anydesk"?"9.8.2":packages[id].Version;checks[id].Detail="Установлена · "+(id=="anydesk"?"x86":"x64");}
                            packages["anydesk"].Source="Официальный AnyDesk · версия после получения";packages["anydesk"].Version="0.0";
                            ((BrandTextBox)form.GetType().GetField("sourceLabel",flags).GetValue(form)).Text=@"C:\Библиотеки\_soft";
                            form.GetType().GetMethod("UpdateRows",flags).Invoke(form,null);Application.DoEvents();
                        }
                        if(scale!=1F){form.Scale(new SizeF(scale,scale));Application.DoEvents();}
                        foreach(var button in Children(form).OfType<BrandButton>())
                        {
                            string context=form.GetType().Name+" / "+button.Text+" / "+scale;
                            Check(button.Parent.ClientRectangle.Contains(button.Bounds),"Button outside parent: "+context);
                            var size=TextRenderer.MeasureText(button.Text,button.Font,new Size(Math.Max(1,button.Width-16),Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                            Check(size.Width<=button.Width-16 && size.Height<=button.Height-6,"Painted text does not fit "+context+" requires "+size+" available "+button.Size);
                            if(button.Text=="Перезагрузка" || button.Text=="Компоненты")Check(TextRenderer.MeasureText(button.Text,button.Font).Width<=button.Width-16,"Single-line footer action: "+context);
                        }
                        foreach(var label in Children(form).OfType<Label>().Where(l=>l.Visible&&!l.AutoEllipsis&&!String.IsNullOrWhiteSpace(l.Text)))
                        {
                            int width=Math.Max(1,label.ClientSize.Width-label.Padding.Horizontal),height=Math.Max(1,label.ClientSize.Height-label.Padding.Vertical);
                            var measured=TextRenderer.MeasureText(label.Text,label.Font,new Size(width,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                            Check(measured.Width<=width+1&&measured.Height<=height+1,"Label does not fit "+form.GetType().Name+" / "+label.Text+" / "+scale+" measured "+measured+" available "+label.Size);
                        }
                        if(form is ToolsDeploymentForm)
                        {
                            var fields=Children(form).OfType<BrandTextBox>().ToArray();var field=fields.Single(b=>!b.ReadOnly);var picker=Children(form).OfType<Button>().Single(b=>b.Text=="Выбрать путь…");
                            Check(Math.Abs(picker.Parent.PointToScreen(new Point(picker.Left,picker.Bottom-2)).Y-field.Parent.PointToScreen(new Point(field.Left,field.Bottom-1)).Y-4*scale)<=1,"Utility picker sits four units below path frame at "+scale);
                            Check(fields.Length==2&&fields[0].Size==fields[1].Size,"Source and destination have matching thin frames");
                            Check(field.Height<picker.Height,"Path frame is thinner from the top, keeping bottom alignment");
                            Check(form.ClientSize.Width<=640*scale+1&&form.ClientSize.Height<=460*scale+1,"Utility window stays compact in both dimensions");
                            Check(form.Text=="Распаковка утилит","Short utility window title");
                            Check(Children(form).OfType<Label>().Any(l=>l.Text=="Будет взято из:")&&Children(form).OfType<Label>().Any(l=>l.Text=="Путь распаковки:"),"Clear independent path captions");
                        }
                        if(form is SoftwareManagerForm)
                        {
                            var grid=Children(form).OfType<ServiceGrid>().Single();
                            Check(grid.Columns[3].Width>grid.Columns[2].Width,"Source column wider than installed-state column");
                            Check(grid.DisplayedRowCount(false)==5&&grid.GetRowDisplayRectangle(4,false).Bottom<=grid.ClientRectangle.Bottom,"All five installed programs completely visible with longest official source");
                            Check(!Children(form).OfType<LibraryGridScrollBar>().Any(),"Four-program list has no scroll control");
                            foreach(DataGridViewRow row in grid.Rows)
                            {
                                var cell=row.Cells[3];var style=cell.InheritedStyle;int width=cell.Size.Width-style.Padding.Horizontal-3;
                                var required=TextRenderer.MeasureText(Convert.ToString(cell.Value),style.Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                                Check(required.Height<=2*style.Font.Height+2,"Source occupies at most two lines, "+row.Tag+" / "+scale);
                            }
                            var field=Children(form).OfType<BrandTextBox>().Single();var picker=Children(form).OfType<Button>().Single(b=>b.Text=="Выбрать папку...");
                            Check(Math.Abs(picker.Parent.PointToScreen(new Point(picker.Left,picker.Bottom-2)).Y-field.Parent.PointToScreen(new Point(field.Left,field.Bottom-1)).Y-4*scale)<=1,"Software picker has same small downward offset as utilities");
                            Check(Math.Abs(picker.Parent.PointToScreen(new Point(picker.Right,0)).X-grid.Parent.Parent.PointToScreen(new Point(grid.Parent.Right,0)).X)<=1,"Folder block anchored to right edge of working area");
                            var folder=field.Parent.Parent;var heading=(TableLayoutPanel)folder.Parent;var title=heading.GetControlFromPosition(0,0);
                            Check(folder.Top-title.Bottom>=20*scale-1,"Clear gap after module title");
                            var choices=Children(form).OfType<BrandCheckBox>().ToArray();var first=choices.OrderBy(c=>c.Top).First();var last=choices.OrderBy(c=>c.Top).Last();var install=Children(form).OfType<Button>().Single(b=>b.Text=="Установить");
                            int topGap=first.Parent.PointToScreen(first.Location).Y-grid.Parent.Parent.PointToScreen(new Point(grid.Parent.Left,grid.Parent.Bottom)).Y;
                            int bottomGap=install.Parent.PointToScreen(install.Location).Y-last.Parent.PointToScreen(new Point(last.Left,last.Bottom)).Y;
                            Check(topGap>=16*scale-1&&Math.Abs(topGap-bottomGap)<=1,"Checkbox block has equal comfortable gaps from table and actions");
                        }
                        if(scale==1F&&(form is SoftwareManagerForm||form is ToolsDeploymentForm))
                        {
                            string directory=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews");System.IO.Directory.CreateDirectory(directory);
                            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(System.IO.Path.Combine(directory,form.GetType().Name+"-"+(dark?"dark":"light")+".png"));}
                        }
                        if(form is ManagerHub)
                        {
                            var cards=Children(form).OfType<Card>().ToArray();Check(cards.Length==5,"Five launcher cards");
                            foreach(var card in cards){var b=Children(card).OfType<BrandButton>().Single();var at=card.PointToClient(b.PointToScreen(Point.Empty));Check(Math.Abs(at.X*2+b.Width-card.Width)<=2,"Launcher action horizontally centered");Check(card.Height-at.Y-b.Height>=12*scale && card.Height-at.Y-b.Height<=16*scale,"Launcher action near lower edge with consistent inset at "+scale+" card "+card.Size+" gap "+(card.Height-at.Y-b.Height));}
                        }
                        if(form is LibraryManagerForm)
                        {
                            var actions=Children(form).OfType<Button>().Where(b=>new[]{"Открыть журнал","Перезагрузка","Компоненты"}.Contains(b.Text)).ToArray();
                            Check(actions.Length==3&&actions.Select(b=>b.Size).Distinct().Count()==1,"All three footer actions have exactly equal dimensions");
                        }
                        if(form is LibraryManagerForm && scale==1F)
                        {
                            Exception failure=null; bool opened=false;
                            using(var timer=new Timer {Interval=25})
                            {
                                timer.Tick+=delegate {
                                    var child=Application.OpenForms.OfType<WindowsFeaturesForm>().FirstOrDefault();if(child==null)return;
                                    timer.Stop();opened=true;
                                    try{Check(child.Owner==form&&child.Modal,"Windows components owned by component manager");var back=Children(child).OfType<Button>().Single(b=>b.Text=="Назад");back.PerformClick();}
                                    catch(Exception ex){failure=ex;child.Close();}
                                };
                                timer.Start();Children(form).OfType<Button>().Single(b=>b.Text=="Компоненты").PerformClick();timer.Stop();
                            }
                            if(failure!=null)throw failure;Check(opened&&form.Visible&&!form.IsDisposed,"Nested return keeps component manager open");
                        }
                        form.Close();
                    }
                }
                Console.WriteLine("PASS: "+count+" actual button paint-area / centered layout checks in modules and dialogs; both themes, simulated 100/125/150/200%. No installer or system changes.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
