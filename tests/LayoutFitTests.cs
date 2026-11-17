using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class LayoutFitTests
    {
        private sealed class CaptionFixture:BrandWindow
        {
            internal CaptionFixture(FormBorderStyle style)
            {
                FormBorderStyle=style;
                ShowInTaskbar=false;
                StartPosition=FormStartPosition.Manual;
                Location=new Point(50,50);
            }
        }
        private static int count;
        private static IEnumerable<Control> Children(Control parent) {foreach(Control c in parent.Controls){yield return c;foreach(var item in Children(c))yield return item;}}
        private static void Check(bool ok,string message){count++;if(!ok)throw new Exception(message);}

        private static void WaitForUi(Func<bool> ready, string activity)
        {
            var elapsed = Stopwatch.StartNew();
            do
            {
                Application.DoEvents();
                if (ready()) return;
                Thread.Sleep(10);
            }
            while (elapsed.Elapsed < TimeSpan.FromSeconds(10));
            throw new TimeoutException(activity);
        }

        private static void ShowWindow(Form form, float scaleBeforeShown = 1F)
        {
            bool shown = false;
            form.Shown += delegate { shown = true; };
            form.Show();
            if (scaleBeforeShown != 1F)
                form.Scale(new SizeF(scaleBeforeShown, scaleBeforeShown));
            WaitForUi(() => shown && form.Visible && form.IsHandleCreated, "Waiting for Shown");

            // Finish startup work before measuring the controls or closing the window.
            string readyButton = form is ToolsDeploymentForm ? "Выбрать папку" :
                form is ApplicationLogsForm ? "Обновить список" : null;
            if (readyButton != null)
            {
                var button = Children(form).OfType<BrandButton>().Single(item => item.Text == readyButton);
                WaitForUi(() => button.Enabled, "Waiting for startup scan to finish");
            }
        }

        private static void RunWindow<T>(Func<T> create, bool dark, float scale, Action<T> inspect) where T : Form
        {
            string context = typeof(T).Name + " / dark=" + dark + " / " + scale;
            T window = null;
            Exception failure = null;
            string phase = "construct";
            try
            {
                window = create();
                phase = "show and inspect";
                inspect(window);
                phase = "close";
                window.Close();
            }
            catch (Exception error)
            {
                failure = new InvalidOperationException(context + " / " + phase, error);
            }
            try
            {
                if (window != null && !window.IsDisposed) window.Dispose();
            }
            catch (Exception error)
            {
                var cleanup = new InvalidOperationException(context + " / dispose", error);
                if (failure != null) throw new AggregateException(failure, cleanup);
                throw cleanup;
            }
            if (failure != null) throw failure;
        }

        private static Point CaptionInset(FormBorderStyle style, bool dark, float scale)
        {
            Point inset = Point.Empty;
            RunWindow(() => new CaptionFixture(style), dark, scale, window =>
            {
                window.ClientSize = new Size(420, 300);
                window.Show();
                NativeWindowTheme.Apply(window, dark);
                if (scale != 1F) window.Scale(new SizeF(scale, scale));
                Application.DoEvents();
                inset = window.PointToScreen(Point.Empty) - new Size(window.Location);
            });
            return inset;
        }

        private static ToolsDeploymentForm CreateToolsFixture()
        {
            string source = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "layout-fixture", "_tools");
            Directory.CreateDirectory(Path.Combine(source, "Fixture utility"));
            var form = new ToolsDeploymentForm(true);
            typeof(BundleDownloadForm).GetField("localTools", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(form, source);
            return form;
        }
        private static void CheckInputFrames(Form form)
        {
            foreach(var field in Children(form).OfType<BrandTextBox>())
            {
                var frame=field.Parent as PathFieldFrame;
                string context=form.GetType().Name+" / "+field.AccessibleName;
                Check(frame!=null&&field.BorderStyle==BorderStyle.None,"Single shared input frame: "+context);
                Check(Math.Abs(field.Top*2+field.Height-frame.ClientSize.Height)<=2,"Centered editor: "+context+" "+field.Bounds+" "+frame.ClientSize);
                using(var graphics=frame.CreateGraphics())
                    Check(field.Left==(int)Math.Round(8F*graphics.DpiX/96F),"Input inset: "+context);
            }
        }
        [STAThread] internal static int Main()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
            Control.CheckForIllegalCrossThreadCalls = true;
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // Keep the UI synchronization context active while nested DoEvents calls
            // process layout and asynchronous Shown handlers.
            using (var context = new ApplicationContext())
            {
                int result = 1;
                EventHandler run = null;
                run = delegate
                {
                    Application.Idle -= run;
                    try { result = RunChecks(); }
                    finally { context.ExitThread(); }
                };
                Application.Idle += run;
                try { Application.Run(context); }
                finally { Application.Idle -= run; }
                return result;
            }
        }

        private static int RunChecks()
        {
            try
            {
                foreach(bool dark in new[]{false,true}) foreach(float scale in new[]{1F,1.25F,1.5F,2F})
                {
                    Check(CaptionInset(FormBorderStyle.FixedDialog,dark,scale)==CaptionInset(FormBorderStyle.Sizable,dark,scale),
                        "Fixed and resizable windows use equal native caption insets");
                    var palette=ThemePalette.Create(dark);
                    Action<ManagerModuleForm> inspectExtra = extra =>
                    {
                        extra.SetTheme(dark,false);
                        extra.StartPosition=FormStartPosition.Manual;
                        extra.Location=new Point(-22000,-22000);
                        extra.ShowInTaskbar=false;
                        ShowWindow(extra,scale);
                        CheckInputFrames(extra);
                        if(extra is BundleDownloadForm&&scale==1F)
                        using(var image=new Bitmap(extra.Width,extra.Height))
                        {
                            extra.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                            image.Save(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundle-input-"+dark+".png"));
                        }
                    };
                    RunWindow<BundleDownloadForm>(() => new BundleDownloadForm(null), dark, scale, inspectExtra);
                    RunWindow<ApplicationLogsForm>(() => new ApplicationLogsForm(), dark, scale, inspectExtra);
                    RunWindow<ServiceProbeForm>(() => new ServiceProbeForm("Fixture",true), dark, scale, inspectExtra);
                    RunWindow<UserGuideForm>(() => new UserGuideForm(), dark, scale, inspectExtra);

                    Action<Form> inspectForm = form =>
                    {
                        var module=form as ManagerModuleForm;if(module!=null)module.SetTheme(dark,false);var services=form as MainForm;if(services!=null)services.SetTheme(dark,false);
                        form.StartPosition=FormStartPosition.Manual;form.Location=form is SoftwareRemoteDialog ? Screen.PrimaryScreen.WorkingArea.Location : new Point(-22000,-22000);form.ShowInTaskbar=false;ShowWindow(form);
                        if(form is SoftwareManagerForm)
                        {
                            var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                            var checks=(Dictionary<string,SoftwareCheck>)form.GetType().GetField("checks",flags).GetValue(form);
                            var packages=(Dictionary<string,SoftwarePackage>)form.GetType().GetField("packages",flags).GetValue(form);
                            foreach(string id in checks.Keys.ToArray()){checks[id].State=LibraryState.Installed;checks[id].Version=id=="anydesk"?"9.8.2":packages[id].Version;checks[id].Detail="Установлена · "+(id=="anydesk"?"x86":"x64");}
                            packages["anydesk"].Source="Релиз на официальном сайте AnyDesk";packages["anydesk"].Version="0.0";
                            ((BrandTextBox)form.GetType().GetField("sourceLabel",flags).GetValue(form)).Text=@"C:\Библиотеки\_soft";
                            form.GetType().GetMethod("UpdateRows",flags).Invoke(form,null);Application.DoEvents();
                        }
                        float metricsScale=scale*form.CurrentAutoScaleDimensions.Width/96F;
                        if(scale!=1F){form.Scale(new SizeF(scale,scale));Application.DoEvents();}
                        CheckInputFrames(form);
                        foreach(var button in Children(form).OfType<BrandButton>())
                        {
                            string context=form.GetType().Name+" / "+button.Text+" / "+scale;
                            Check(button.Parent.ClientRectangle.Contains(button.Bounds),"Button outside parent: "+context+" / "+button.Bounds+" / "+button.Parent.ClientRectangle);
                            var size=TextRenderer.MeasureText(button.Text,button.Font,new Size(Math.Max(1,button.Width-16),Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                            Check(size.Width<=button.Width-16 && size.Height<=button.Height-6,"Painted text does not fit "+context+" requires "+size+" available "+button.Size);
                            if(button.Text=="Перезагрузка" || button.Text=="Компоненты")Check(TextRenderer.MeasureText(button.Text,button.Font).Width<=button.Width-16,"Single-line footer action: "+context);
                        }
                        foreach(var toggle in Children(form).OfType<RefreshToggle>())using(var graphics=toggle.CreateGraphics())
                        {
                            int prefix=1+(int)Math.Round(42*graphics.DpiX/96F)+(int)(8*graphics.DpiX/96F);
                            int textWidth=TextRenderer.MeasureText(graphics,toggle.Text,toggle.Font,Size.Empty,TextFormatFlags.NoPadding|TextFormatFlags.SingleLine).Width;
                            Check(toggle.Width>=prefix+textWidth,"Refresh caption fits: "+form.GetType().Name+" required="+(prefix+textWidth)+" actual="+toggle.Width+" dpi="+graphics.DpiX+" font="+toggle.Font.Size);
                        }
                        foreach(var label in Children(form).OfType<Label>().Where(l=>l.Visible&&!l.AutoEllipsis&&!String.IsNullOrWhiteSpace(l.Text)))
                        {
                            int width=Math.Max(1,label.ClientSize.Width-label.Padding.Horizontal),height=Math.Max(1,label.ClientSize.Height-label.Padding.Vertical);
                            var measured=TextRenderer.MeasureText(label.Text,label.Font,new Size(width,Int32.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                            Check(measured.Width<=width+1&&measured.Height<=height+1,"Label does not fit "+form.GetType().Name+" / "+label.Text+" / "+scale+" measured "+measured+" available "+label.Size);
                        }
                        if(form is ToolsDeploymentForm)
                        {
                            var fields=Children(form).OfType<BrandTextBox>().ToArray();
                            Check(fields.Length==2&&fields.Count(field=>!field.ReadOnly)==1,"Separate source and editable destination");
                            var grid=Children(form).OfType<ServiceGrid>().Single();
                            Check(grid.Columns.Count==4&&grid.Columns[0].HeaderText==""&&grid.TouchScrollingEnabled,"Utility grid uses four columns and touch scrolling");
                            var actions=Children(form).OfType<BrandButton>().Where(b=>new[]{"Скачать","Распаковать","Проверить в сети","Перепроверить файлы","Пауза","Отменить","Удалить","В меню","Закрыть"}.Contains(b.Text)).ToArray();
                            Check(actions.Length==9&&actions.GroupBy(button=>button.Parent).All(group=>group.Select(button=>button.Size).Distinct().Count()==1),"Actions live in equal-size rows in the same window");
                        }
                        if(form is SoftwareManagerForm)
                        {
                            var grid=Children(form).OfType<ServiceGrid>().Single();
                            Check(grid.Columns[3].Width>grid.Columns[2].Width,"Source column wider than installed-state column");
                            int gridRequired=grid.ColumnHeadersHeight+grid.Rows.Cast<DataGridViewRow>().Sum(r=>r.Height)+2;
                            if(grid.ClientSize.Height>=gridRequired)Check(grid.DisplayedRowCount(false)==5&&grid.GetRowDisplayRectangle(4,false).Bottom<=grid.ClientRectangle.Bottom,"All five rows visible when viewport fits their measured height");
                            else Check(grid.DisplayedRowCount(false)>0&&Children(form).OfType<LibraryGridScrollBar>().Any(),"Small high-DPI screen scrolls only table while footer remains reachable");
                            Check(grid.ScrollBars==ScrollBars.None,"No native scrollbar breaking either theme");
                            foreach(DataGridViewRow row in grid.Rows)
                            {
                                var cell=row.Cells[3];var style=cell.InheritedStyle;int width=cell.Size.Width-style.Padding.Horizontal-3;
                                var required=TextRenderer.MeasureText(Convert.ToString(cell.Value),style.Font,new Size(width,int.MaxValue),TextFormatFlags.WordBreak|TextFormatFlags.NoPrefix);
                                Check(required.Height<=2*style.Font.Height+2,"Source occupies at most two lines, "+row.Tag+" / "+scale);
                            }
                            var field=Children(form).OfType<BrandTextBox>().Single();var picker=Children(form).OfType<Button>().Single(b=>b.Text=="Выбрать папку...");
                            var frame=(PathFieldFrame)field.Parent;
                            Check(field.BorderStyle==BorderStyle.None&&Math.Abs(field.Top*2+field.Height-frame.ClientSize.Height)<=2,"Picker edit is borderless and centered inside its frame");
                            Check(Math.Abs(picker.Parent.PointToScreen(new Point(picker.Left,picker.Bottom-2)).Y-frame.Parent.PointToScreen(new Point(frame.Left,frame.Bottom-1)).Y-4*metricsScale)<=1,"Software picker has same small downward offset as utilities");
                            Check(Math.Abs(picker.Parent.PointToScreen(new Point(picker.Right,0)).X-grid.Parent.Parent.PointToScreen(new Point(grid.Parent.Right,0)).X)<=1,"Folder block anchored to right edge of working area");
                            var heading=(TableLayoutPanel)frame.Parent.Parent;var title=heading.GetControlFromPosition(0,1);var caption=heading.GetControlFromPosition(1,1);
                            Check(title.Top==caption.Top&&title.Height==caption.Height,"Title and folder caption share one row");
                            var choices=Children(form).OfType<BrandCheckBox>().ToArray();var first=choices.OrderBy(c=>c.Top).First();var last=choices.OrderBy(c=>c.Top).Last();var install=Children(form).OfType<Button>().Single(b=>b.Text=="Установить");
                            int topGap=first.Parent.PointToScreen(first.Location).Y-grid.Parent.Parent.PointToScreen(new Point(grid.Parent.Left,grid.Parent.Bottom)).Y;
                            int bottomGap=install.Parent.PointToScreen(install.Location).Y-last.Parent.PointToScreen(new Point(last.Left,last.Bottom)).Y;
                            Check(topGap>=6*metricsScale-2&&Math.Abs(topGap-bottomGap)<=2,"Compact checkbox block has equal gaps and does not touch table or actions");
                        }
                        if(scale==1F&&(form is SoftwareManagerForm||form is ToolsDeploymentForm))
                        {
                            string directory=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews");System.IO.Directory.CreateDirectory(directory);
                            using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(System.IO.Path.Combine(directory,form.GetType().Name+"-"+(dark?"dark":"light")+".png"));}
                        }
                        if(form is ManagerHub)
                        {
                            var cards=Children(form).OfType<Card>().ToArray();Check(cards.Length==6,"Six launcher cards");
                            foreach(var card in cards){var b=Children(card).OfType<BrandButton>().Single();var at=card.PointToClient(b.PointToScreen(Point.Empty));Check(Math.Abs(at.X*2+b.Width-card.Width)<=2,"Launcher action horizontally centered");Check(card.Height-at.Y-b.Height>=12*metricsScale && card.Height-at.Y-b.Height<=16*metricsScale,"Launcher action near lower edge with consistent inset at "+scale+" card "+card.Size+" gap "+(card.Height-at.Y-b.Height));}
                        }
                        if(form is LibraryManagerForm)
                        {
                            var actions=Children(form).OfType<Button>().Where(b=>new[]{"Открыть журнал","Перезагрузка","Компоненты","В меню","Закрыть"}.Contains(b.Text)).OrderBy(b=>b.Left).ToArray();
                            Check(actions.Length==5&&actions.Select(b=>b.Size).Distinct().Count()==1,"Footer actions have equal dimensions");
                            int gap=actions[1].Left-actions[0].Right;
                            Check(gap>0&&actions.Skip(1).Where((b,i)=>b.Left-actions[i].Right!=gap).Count()==0,"Footer actions have equal gaps");
                        }
                        if(form is LibraryManagerForm && scale==1F)
                        {
                            Children(form).OfType<Button>().Single(b=>b.Text=="Компоненты").PerformClick();
                            WaitForUi(() => form.OwnedForms.OfType<WindowsFeaturesForm>().Any(), "Waiting for Windows components");
                            var child=form.OwnedForms.OfType<WindowsFeaturesForm>().Single();
                            Check(child.Owner==form&&!child.Modal&&child.Enabled&&!form.Enabled,"Windows components lock only the owning module");
                            Children(child).OfType<Button>().Single(b=>b.Text=="Назад").PerformClick();
                            WaitForUi(() => child.IsDisposed && form.Enabled, "Waiting for Windows components to close");
                            Check(form.Visible&&!form.IsDisposed&&form.Enabled,"Nested return keeps component manager open");
                        }
                    };
                    RunWindow<ManagerHub>(() => new ManagerHub(), dark, scale, inspectForm);
                    RunWindow<InitialSystemSettingsForm>(() => new InitialSystemSettingsForm(true), dark, scale, inspectForm);
                    RunWindow<LibraryManagerForm>(() => new LibraryManagerForm(true), dark, scale, inspectForm);
                    RunWindow<SoftwareManagerForm>(() => new SoftwareManagerForm(true), dark, scale, inspectForm);
                    RunWindow<ToolsDeploymentForm>(CreateToolsFixture, dark, scale, inspectForm);
                    RunWindow<WindowsFeaturesForm>(() => new WindowsFeaturesForm(true,true), dark, scale, inspectForm);
                    RunWindow<MainForm>(() => new MainForm(null,true), dark, scale, inspectForm);
                    RunWindow<AboutDialog>(() => new AboutDialog(palette,null), dark, scale, inspectForm);
                    RunWindow<ServiceAccountDialog>(() => new ServiceAccountDialog(palette,ServiceInstallProfile.Default(@"C:\fixture\HttpService.exe"),false), dark, scale, inspectForm);
                    RunWindow<SoftwareRemoteDialog>(() => new SoftwareRemoteDialog(palette,null,"anydesk"), dark, scale, inspectForm);
                    RunWindow<SoftwareRemoteDialog>(() => new SoftwareRemoteDialog(palette,null,"rudesktop"), dark, scale, inspectForm);
                    RunWindow<LibraryActionDialog>(() => new LibraryActionDialog(palette,null,new LibraryCheck[0]), dark, scale, inspectForm);
                    RunWindow<LogArchiveDialog>(() => new LogArchiveDialog(palette,"Служба BARS",new List<LogDateFolder>(),DateTime.Today), dark, scale, inspectForm);
                }
                Console.WriteLine("PASS: "+count+" actual button paint-area / centered layout checks in modules and dialogs; both themes, simulated 100/125/150/200%. No installer or system changes.");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
        }
    }
}
