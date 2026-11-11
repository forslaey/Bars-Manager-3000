using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
namespace ServiceTool
{
    internal static class Ui2332Tests
    {
        static int count;
        static void Check(bool value,string message){if(!value)throw new Exception(message);count++;}
        static IEnumerable<Control> All(Control c){foreach(Control x in c.Controls){yield return x;foreach(var y in All(x))yield return y;}}
        static T Field<T>(object value,string name){for(Type t=value.GetType();t!=null;t=t.BaseType){var f=t.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic);if(f!=null)return (T)f.GetValue(value);}throw new Exception(name);}
        static void Pump(){for(int i=0;i<12;i++)Application.DoEvents();}
        static void Capture(Control root,string name){using(var b=new Bitmap(root.Width,root.Height)){root.DrawToBitmap(b,new Rectangle(Point.Empty,b.Size));b.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name+".png"));}}
        static void Show(Form f){f.ShowInTaskbar=false;f.StartPosition=FormStartPosition.Manual;f.Location=new Point(-20000,-20000);f.Show();Pump();}
        [STAThread] static int Main(){try{
            Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
            Check(!ManagerSystemActions.Known("software-defaults"),"Hidden defaults command rejected");
            foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})foreach(bool software in new[]{true,false})
            using(ManagerModuleForm form=software?(ManagerModuleForm)new SoftwareManagerForm(true):new InitialSystemSettingsForm(true))
            {
                form.SetTheme(dark,false);Show(form);if(scale!=1F){form.Scale(new SizeF(scale,scale));Pump();}
                var root=Field<TableLayoutPanel>(form,"Root");var outer=All(form).OfType<CompactJournalLayout>().Single();var viewport=outer.GetControlFromPosition(0,0);var content=(TableLayoutPanel)viewport.Controls[0];
                var grid=Field<ServiceGrid>(form,"grid");var journal=Field<JournalTextBox>(form,"journal");var footer=outer.GetControlFromPosition(0,2);
                var back=All(form).OfType<Button>().Single(b=>b.Text=="В меню");Check(back.Parent.ClientRectangle.Contains(back.Bounds),"Pinned menu in native window");
                form.WindowState=FormWindowState.Maximized;Pump();form.WindowState=FormWindowState.Normal;Pump();Check(back.Parent.ClientRectangle.Contains(back.Bounds),"Menu survives maximize/restore");
                if(software){var choices=All(form).OfType<BrandCheckBox>().ToArray();Check(choices.Length==1&&choices[0].Text.Contains("неконтролируемый доступ")&&!choices[0].Checked,"Only remote checkbox remains");Check(All(form).OfType<Button>().Any(b=>b.Text=="Приложения по умолчанию"),"Safe Windows defaults shortcut");Check(typeof(SoftwareManagerForm).GetMethod("DefaultsCore",BindingFlags.Instance|BindingFlags.NonPublic)==null,"No automatic defaults routine");}
                // Test layout surfaces independently of the host monitor's maximum window size.
                root.Dock=DockStyle.None;float factor=form.CurrentAutoScaleDimensions.Width/96F*scale;int previousJournal=0;
                foreach(Size logical in new[]{new Size(1366,768),new Size(1920,1080),new Size(1080,1920),new Size(1440,2560)})
                {
                    root.Bounds=new Rectangle(0,0,(int)(logical.Width*factor),(int)(logical.Height*factor));root.PerformLayout();Pump();
                    Check(outer.ClientRectangle.Contains(footer.Bounds),"Footer within layout surface");Check(!viewport.Bounds.IntersectsWith(outer.GetControlFromPosition(0,1).Bounds),"Controls and journal do not overlap");Check(outer.GetControlFromPosition(0,1).Bottom<=footer.Top,"Journal above footer");
                    Check(journal.Height>0,"Journal has useful viewport");
                    int fixedRows=(int)Math.Ceiling(content.Padding.Vertical+Enumerable.Range(0,content.RowCount).Where(i=>i!=1).Sum(i=>content.RowStyles[i].Height));
                    int fullTable=grid.ColumnHeadersHeight+grid.Rows.Cast<DataGridViewRow>().Sum(r=>r.Height);
                    Check(viewport.Height<=fixedRows+Math.Max(fullTable,grid.ColumnHeadersHeight+grid.RowTemplate.Height)+3,"Controls never stretch beyond actual contents: software="+software+", DPI="+scale+", size="+logical+", viewport="+viewport.Height+", rows="+grid.RowCount+", fixed="+fixedRows+", table="+fullTable);
                    if(logical.Height>=1920)
                    {
                        Check(grid.DisplayedRowCount(false)==grid.RowCount,"All short-list rows fit on portrait surface");
                        Check(journal.Height>root.Height/3,"Portrait free area belongs to journal");
                        Check(journal.Height>previousJournal,"Journal grows with additional height");
                        if(software){var remote=Field<BrandCheckBox>(form,"remote");var options=remote.Parent;var actions=content.GetControlFromPosition(0,3);var table=content.GetControlFromPosition(0,1);Check(options.Top-table.Bottom<=2,"Options immediately follow table");Check(actions.Top-options.Bottom<=2,"Actions immediately follow options");Check(options.Height<=90*factor,"No percentage spacer in options");}
                    }
                    previousJournal=journal.Height;
                    if(scale==1F&&(logical.Height==1920||logical.Width==1920))Capture(root,(software?"programs":"settings")+"-"+logical.Width+"x"+logical.Height+"-"+dark);
                }
                form.Close();
            }
            foreach(bool dark in new[]{false,true})
            {
                using(var services=new MainForm(null,true))
                {
                    services.SetTheme(dark,false);Show(services);var root=All(services).OfType<TableLayoutPanel>().First(p=>p.Parent is FillContentPanel);var host=root.Parent;host.Controls.Remove(root);services.Controls.Remove(host);host.Dispose();services.Controls.Add(root);root.Dock=DockStyle.None;root.Size=new Size(1350,1100);Pump();var list=All(services).OfType<ServiceGrid>().First();int before=list.Height;int journalHeight=Field<TextBox>(services,"log").Height;root.Size=new Size(1350,2400);Pump();Check(list.Height>before+900,"Service table receives portrait space");Check(Field<TextBox>(services,"log").Height==journalHeight,"Service journal keeps its height");services.Close();
                }
                using(var libraries=new LibraryManagerForm(true))
                {
                    libraries.SetTheme(dark,false);Show(libraries);var root=Field<TableLayoutPanel>(libraries,"Root");var host=root.Parent as FillContentPanel;if(host!=null){host.Controls.Remove(root);libraries.Controls.Remove(host);host.Dispose();libraries.Controls.Add(root);}root.Dock=DockStyle.None;root.Size=new Size(1350,1100);Pump();int before=Field<ServiceGrid>(libraries,"grid").Height;int journalHeight=Field<JournalTextBox>(libraries,"journal").Height;root.Size=new Size(1350,2400);Pump();Check(Field<ServiceGrid>(libraries,"grid").Height>before+900,"Libraries give portrait space to component list");Check(Field<JournalTextBox>(libraries,"journal").Height==journalHeight,"Library journal does not steal list space");libraries.Close();
                }
                using(var guide=new UserGuideForm())
                {
                    guide.SetTheme(dark,false);Show(guide);var root=Field<TableLayoutPanel>(guide,"Root");root.Dock=DockStyle.None;root.Size=new Size(1350,1100);Pump();int before=Field<GuideReader>(guide,"reader").Height;root.Size=new Size(1350,2400);Pump();Check(Field<GuideReader>(guide,"reader").Height>before+900,"Guide gives portrait space to reading text");guide.Close();
                }
            }
            foreach(bool dark in new[]{false,true})using(var components=new WindowsFeaturesForm(true,true))
            {
                components.SetTheme(dark,false);Show(components);components.WindowState=FormWindowState.Maximized;Pump();var root=Field<TableLayoutPanel>(components,"Root");Check(root.Parent is BoundedContentPanel,"Windows components remain compact");Check(root.Height<1000,"Small feature list never stretches vertically");components.Close();
            }
            Console.WriteLine("Ui2332Tests: "+count+" passed; compact controls, growing journals, both themes, layout surfaces 1366x768 through 1440x2560 and simulated DPI. No system settings, installations or services changed.");return 0;
        }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
    }
}