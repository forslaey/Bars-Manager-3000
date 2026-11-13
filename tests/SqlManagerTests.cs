using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class SqlManagerTests
    {
        static int checks;
        static void Check(bool result, string detail) { if (!result) throw new Exception(detail); checks++; }
        static IEnumerable<Control> ControlsUnder(Control root)
        {
            foreach (Control child in root.Controls)
            {
                yield return child;
                foreach (var nested in ControlsUnder(child)) yield return nested;
            }
        }

        [STAThread]
        static int Main()
        {
            string fixture = Path.Combine(Path.GetTempPath(), "bars-sql-test-" + Guid.NewGuid().ToString("N"));
            try
            {
                Application.EnableVisualStyles();
                string software = Path.Combine(fixture, "_soft");
                string express = Path.Combine(software, "_SQL", "SQL Server Express");
                Directory.CreateDirectory(express);
                File.WriteAllText(Path.Combine(express, "SQLEXPRESS_x64_RUS.exe"), "inert");
                File.WriteAllText(Path.Combine(express, "SQLServer2019_Update-KB5063757-x64.exe"), "inert");
                string knownName=BundleSqlFolders.Names(BundleCloudSource.Baseline()).Single(name=>name.Contains("2019 Express"));
                string partial=Path.Combine(software,"_SQL",knownName);
                Directory.CreateDirectory(partial);
                File.WriteAllText(Path.Combine(partial,"SQLServer2019_Update-KB5063757-x64.exe"),"inert");
                var partialSteps=SqlInstallPackages.Read(partial);
                Check(partialSteps.Length==2&&partialSteps[0].file=="SQLEXPRESS_x64_RUS.exe"&&!File.Exists(Path.Combine(partial,partialSteps[0].file)),"Known partial Express set shows missing main installer");
                File.Delete(Path.Combine(partial,"SQLServer2019_Update-KB5063757-x64.exe"));
                Directory.Delete(partial);
                var steps = SqlInstallPackages.Read(express);
                Check(steps.Length == 2 && steps[0].file == "SQLEXPRESS_x64_RUS.exe", "Express installer precedes update");
                SqlInstallPackages.Save(express, steps);
                Check(SqlInstallPackages.Read(express).Select(step => step.file).SequenceEqual(steps.Select(step => step.file)), "Manifest order round-trips");
                bool rejected = false;
                try { SqlInstallPackages.Resolve(express, "../other.exe"); } catch (InvalidDataException) { rejected = true; }
                Check(rejected, "Manifest cannot escape the package");
                string zip = Path.Combine(express, "SSMS_21.zip");
                using (var stream = File.Create(zip))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
                using (var entry = new StreamWriter(archive.CreateEntry("SSMS_21/nested/readme.txt").Open())) entry.Write("fixture");
                using(var cancel=new System.Threading.CancellationTokenSource()){
                    cancel.Cancel();
                    bool cancelled=false;
                    try{PackageZipExtraction.Extract(zip,cancel.Token);}catch(OperationCanceledException){cancelled=true;}
                    Check(cancelled&&!Directory.GetDirectories(express,".s????????").Any()&&!Directory.Exists(Path.Combine(express,"SSMS_21")),"Cancelled ZIP leaves no staging or published folder");
                }
                TransferProgress progress=null;
                string unpacked = PackageZipExtraction.Extract(zip,report: value=>progress=value);
                Check(progress!=null&&progress.Bytes==7&&progress.Total==7&&!progress.Complete,"Extraction reports actual bytes before publication");
                Check(File.Exists(zip) && File.ReadAllText(Path.Combine(unpacked, "nested", "readme.txt")) == "fixture", "ZIP extraction preserves archive and nested files");

                File.WriteAllText(Path.Combine(unpacked,"obsolete.txt"),"old");
                using(var cancelReplace=new System.Threading.CancellationTokenSource())
                {
                    bool interrupted=false;
                    try{PackageZipExtraction.Extract(zip,cancelReplace.Token,_=>cancelReplace.Cancel(),replace:true);}
                    catch(OperationCanceledException){interrupted=true;}
                    Check(interrupted&&File.Exists(Path.Combine(unpacked,"obsolete.txt"))&&
                        !Directory.GetDirectories(express,".s????????").Any(),"Cancellation before replacement preserves the prior complete folder");
                }
                PackageZipExtraction.Extract(zip,replace:true);
                Check(!File.Exists(Path.Combine(unpacked,"obsolete.txt"))&&File.Exists(Path.Combine(unpacked,"nested","readme.txt")),"Repeat SQL extraction fully replaces existing folder");
                Check(BundleFolders.PickerStart(Path.Combine(unpacked,"missing","nested"))==unpacked,"Folder picker starts at nearest existing ancestor");
                Check(SoftwarePackageStorage.Folder(Path.Combine(fixture,"BarsManager3000"))==Path.Combine(fixture,"BarsManager3000","_soft"),"Software default stays directly under bundle root");
                string toolsSource=Path.Combine(fixture,"source","_tools"),toolsTarget=Path.Combine(fixture,"target","_tools");
                Directory.CreateDirectory(Path.Combine(toolsSource,"WinSCP v6.6.2"));
                Directory.CreateDirectory(Path.Combine(toolsSource,"Piglet_V0.12"));
                File.WriteAllText(Path.Combine(toolsSource,"WinSCP v6.6.2","WinSCP.exe"),"inert");
                File.WriteAllText(Path.Combine(toolsSource,"Piglet_V0.12","DK-QStart.exe"),"inert");
                var selectedTools=new HashSet<string>(StringComparer.OrdinalIgnoreCase){"WinSCP v6.6.2"};
                ToolsDeployment.CopyToTarget(toolsSource,toolsTarget,delegate{},()=>false,selectedTools);
                Check(ToolsDeployment.Launchers(toolsSource,selectedTools).Single().Name=="WinSCP"&&
                    File.Exists(Path.Combine(toolsTarget,"WinSCP v6.6.2","WinSCP.exe"))&&!Directory.Exists(Path.Combine(toolsTarget,"Piglet_V0.12")),
                    "Only the selected tool is deployed and gets launcher metadata");
                string newTool=Path.Combine(toolsSource,"Termite");
                Directory.CreateDirectory(Path.Combine(newTool,"bin"));
                File.WriteAllText(Path.Combine(newTool,"bin","Termite.exe"),"inert");
                File.WriteAllText(Path.Combine(newTool,"_shortcut.txt"),@"bin\Termite.exe");
                var termite=new HashSet<string>{"Termite"};
                Check(ToolsDeployment.Launchers(toolsSource,termite).Single().Executable=="bin/Termite.exe","Nested declared shortcut");
                Check(ToolsDeployment.Launchers(toolsSource,termite,includeDeclared:false).Count==0,"Declared shortcut does not request administrator mode");
                File.WriteAllText(Path.Combine(newTool,"_shortcut.txt"),@"..\outside.exe");
                bool escaped=false;
                try{ToolsDeployment.Launchers(toolsSource,termite);}catch(InvalidDataException){escaped=true;}
                Check(escaped,"Shortcut cannot escape its own package");
                Check(ToolsDeployment.ShortcutExecutable("TurboWriter V9.zip")=="TurboWriter.exe"&&
                    ToolsDeployment.ShortcutExecutable("R-Tool's.zip")=="R-Tool.exe"&&
                    ToolsDeployment.ShortcutExecutable("STU-430.zip")==null,"Shortcut identities ignore archive version and exclude STU");
                Check(ToolsDeployment.ShortcutCandidates(newTool,"termite.exe").Single()==Path.Combine("bin","Termite.exe"),"Shortcut finds nested executable regardless of case");
                using(var picker=new ToolsPickerForm(new[]{"Termite.zip","STU-430.zip"}))
                {
                    var pickerGrid=(ServiceGrid)typeof(BundleDifferencesForm).GetField("grid",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(picker);
                    var click=typeof(DataGridView).GetMethod("OnCellMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(DataGridViewCellMouseEventArgs)},null);
                    Action<int,int> toggle=(column,row)=>click.Invoke(pickerGrid,new object[]{new DataGridViewCellMouseEventArgs(column,row,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});
                    Check(picker.Packages.Length==2&&picker.Shortcuts.Count==0,"Per-package shortcuts start clear");
                    toggle(2,1);toggle(2,0);
                    Check(picker.Shortcuts.SetEquals(new[]{"Termite.zip"}),"Only eligible package receives shortcut");
                    toggle(0,0);
                    Check(picker.Shortcuts.Count==0&&picker.Packages.SequenceEqual(new[]{"STU-430.zip"}),"Removing a package clears its shortcut");
                }
                var toolFiles=new Dictionary<string,string>
                {
                    {"TurboWriter V9","TurboWriter.exe"},{"Termite","termite-3.4.exe"},
                    {"R-Tool's","R-Tool.exe"},{"mfgtools","MfgTool2.exe"}
                };
                foreach(var item in toolFiles)
                {
                    string nested=Path.Combine(toolsSource,item.Key,"version");
                    Directory.CreateDirectory(nested);
                    File.WriteAllText(Path.Combine(nested,item.Value),"inert");
                }
                var allTools=new HashSet<string>(toolFiles.Keys.Concat(new[]{"WinSCP v6.6.2","Piglet_V0.12"}),StringComparer.OrdinalIgnoreCase);
                Directory.CreateDirectory(Path.Combine(toolsTarget,"Unrelated"));
                ToolsDeployment.CopyToTarget(toolsSource,toolsTarget,delegate{},()=>false,allTools);
                Check(ToolsDeployment.Installed(toolsTarget).Count==6&&Directory.Exists(Path.Combine(toolsTarget,"Unrelated")),"All six tools are removable while unrelated folders are preserved");
                Check(toolFiles.All(item=>File.ReadAllText(Path.Combine(toolsTarget,item.Key,"desktop.ini")).Contains(item.Value)),"Nested main executables provide folder icons");
                Check(Directory.GetDirectories(Path.GetDirectoryName(toolsTarget)).Length==1,"Deployment does not leave a backup folder");
                string variant=Path.Combine(toolsSource,"R-Tool's","variant 2");
                Directory.CreateDirectory(variant);
                File.WriteAllText(Path.Combine(variant,"R-Tool.exe"),"inert");
                var operation=new System.Threading.Tasks.TaskCompletionSource<int>();
                bool cancelledWorkspace=false;
                string selectiveTarget=Path.Combine(fixture,"selective","_tools");
                ToolsDeployment.CopyToTarget(toolsSource,selectiveTarget,delegate{},()=>false,
                    new HashSet<string>{"R-Tool's"},new[]{"R-Tool's/variant 2"});
                Check(File.Exists(Path.Combine(selectiveTarget,"R-Tool's","version","R-Tool.exe"))&&
                    !Directory.Exists(Path.Combine(selectiveTarget,"R-Tool's","variant 2")),"Filtered deployment copies only the selected version");
                foreach(bool dark in new[]{false,true})
                using(var picker=new BrandPathDialog("Выбрать папку",fixture,null,null,null,true,false,true))
                {
                    picker.SetTheme(dark,false);picker.ShowInTaskbar=false;picker.Show();Application.DoEvents();
                    var pathField=ControlsUnder(picker).OfType<BrandTextBox>().Single(field=>field.AccessibleName=="Путь к папке");
                    using(var bitmap=new Bitmap(picker.Width,picker.Height))
                    {
                        picker.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bundle-picker-"+dark+".png"));
                    }
                    pathField.Text=toolsSource;
                    ControlsUnder(picker).OfType<BrandButton>().Single(button=>button.Text=="Выбрать папку").PerformClick();
                    Check(picker.DialogResult==DialogResult.OK&&picker.SelectedPath==toolsSource,"Branded folder picker accepts typed absolute paths in both themes");
                }
                using(var workspace=new BundleDownloadForm(Path.GetDirectoryName(toolsSource),toolsOnly:true,localSource:toolsSource,
                    deploy:(path,packages,links,bar)=>operation.Task,cancelDeployment:()=>{cancelledWorkspace=true;operation.TrySetResult(0);}))
                {
                    workspace.SetTheme(true,false);
                    workspace.ShowInTaskbar=false;
                    workspace.StartPosition=FormStartPosition.Manual;
                    workspace.Location=new Point(-15000,-15000);
                    workspace.Show();Application.DoEvents();
                    var workspaceGrid=(ToolsWorkspaceGrid)typeof(BundleDownloadForm).GetField("toolGrid",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(workspace);
                    var table=workspaceGrid.Controls.OfType<ServiceGrid>().Single();
                    var mainRow=table.Rows.Cast<DataGridViewRow>().Single(row=>Convert.ToString(row.Cells[1].Value).EndsWith("R-Tool's",StringComparison.Ordinal));
                    var variants=table.Rows.Cast<DataGridViewRow>().Where(row=>Convert.ToString(row.Cells[1].Value).Trim().EndsWith("/R-Tool.exe")).ToArray();
                    Check(table.Columns[0].HeaderText==""&&variants.Length==2,"Workspace has unlabeled checkbox column and shows nested R-Tool versions");
                    var mouse=typeof(DataGridView).GetMethod("OnCellMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(DataGridViewCellMouseEventArgs)},null);
                    Action<int,int> click=(column,row)=>mouse.Invoke(table,new object[]{new DataGridViewCellMouseEventArgs(column,row,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});
                    Check(variants.All(row=>!row.Visible)&&table.Columns.Count==4&&table.Columns[2].HeaderText=="Источник","Variants start collapsed in four-column table");
                    click(0,mainRow.Index);click(0,mainRow.Index);
                    Check(workspaceGrid.Deployment.Length==0&&variants.All(row=>!row.Visible),"Two immediate checks toggle back without expanding");
                    click(0,mainRow.Index);click(1,mainRow.Index);click(1,variants[0].Index);click(3,variants[1].Index);
                    Check(variants.All(row=>row.Visible)&&workspaceGrid.Variants["R-Tool's"].SetEquals(new[]{"version/R-Tool.exe"}),"Name click reveals versions and child checkbox controls extraction");
                    click(1,mainRow.Index);
                    Check(variants.All(row=>!row.Visible),"Second name click collapses versions");
                    click(1,mainRow.Index);
                    Check(workspaceGrid.Deployment.SequenceEqual(new[]{"R-Tool's"})&&workspaceGrid.Shortcuts["R-Tool's"].SetEquals(new[]{"version/R-Tool.exe"}),
                        "Workspace deploys one package and selects only the chosen version shortcut");
                    var workspaceButtons=ControlsUnder(workspace).OfType<BrandButton>().ToArray();
                    var deployButton=workspaceButtons.Single(button=>button.Text=="Распаковать");
                    var stopButton=workspaceButtons.Single(button=>button.Text=="Отменить");
                    var destinationField=ControlsUnder(workspace).OfType<BrandTextBox>().Single(field=>!field.ReadOnly);
                    deployButton.PerformClick();Application.DoEvents();
                    Check(stopButton.Enabled&&!deployButton.Enabled&&!destinationField.Enabled,"Active extraction locks conflicting actions and destination");
                    stopButton.PerformClick();Application.DoEvents();
                    Check(cancelledWorkspace&&deployButton.Enabled&&destinationField.Enabled,"Cancellation restores actions without discarding selected tools");
                    foreach(bool dark in new[]{true,false})
                    using(var bitmap=new Bitmap(workspace.Width,workspace.Height))
                    {
                        workspace.SetTheme(dark,false);Application.DoEvents();
                        workspace.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));
                        bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"tools-workspace-"+dark+".png"));
                    }
                }
                var toolReference=new[]{
                    new BundleRemoteFile{Path="/_tools/Piglet_V0.12/a.exe"},
                    new BundleRemoteFile{Path="/_tools/Piglet_V0.12/b.ini"},
                    new BundleRemoteFile{Path="/_tools/WinSCP.zip"}
                };
                int missing;
                var confirmed=BundleDownloadForm.ConfirmedToolPackages(toolReference,file=>!file.Path.EndsWith(".ini"),out missing);
                Check(missing==1&&confirmed.Count==1&&confirmed[0]==toolReference[2],"A partially mirrored tool is not available from Google");
                Check(BundleDownloadForm.ConfirmedToolPackages(toolReference,file=>false,out missing).Count==0&&missing==2,"Empty Google mirror does not present reference packages as downloadable");
                Check(BundleDownloadForm.ToolPackage(new BundleRemoteFile{Path="/_tools/Piglet_V0.12/nested/test.txt"})=="Piglet_V0.12"&&
                    BundleDownloadForm.ToolPackage(new BundleRemoteFile{Path="/_tools/WinSCP.zip"})=="WinSCP.zip","Cloud recognizes nested packages and ZIP files");
                foreach (bool dark in new[] {false,true})
                foreach (float scale in new[] {1F,1.25F,1.5F,2F})
                using (var form = new Form {ClientSize=new Size(1070,730), ShowInTaskbar=false, StartPosition=FormStartPosition.Manual, Location=new Point(-15000,-15000)})
                using (var page = new SqlManagerPage(form,()=>software,delegate{},ThemePalette.Create(dark)))
                {
                    form.Controls.Add(page);
                    form.Scale(new SizeF(scale,scale));
                    form.Show();
                    var load=page.RefreshLocal(software);
                    while(!load.IsCompleted){Application.DoEvents();System.Threading.Thread.Sleep(2);}
                    load.GetAwaiter().GetResult();
                    Application.DoEvents();
                    var bar=ControlsUnder(page).OfType<DownloadProgressView>().Single();
                    Check(!bar.Visible,"SQL progress stays hidden before extraction");
                    var journalArea=(TableLayoutPanel)bar.Parent;
                    journalArea.RowStyles[0].Height=112*scale;
                    bar.Visible=true;page.PerformLayout();Application.DoEvents();
                    Check(bar.Height>0&&journalArea.ClientRectangle.Contains(bar.Bounds),"Extraction progress fits above SQL journal");
                    var grid=ControlsUnder(page).OfType<ServiceGrid>().Single();
                    Check(grid.Rows.Count==2 && grid.TouchScrollingEnabled, "SQL rows and touch input");
                    var buttons=ControlsUnder(page).OfType<BrandButton>().ToArray();
                    Check(buttons.Length==6 && buttons.All(button=>button.Height>0 && button.Parent.ClientRectangle.Contains(button.Bounds)), "SQL buttons stay within footer panels");
                    Check(buttons.Where(button=>button.Text!="Программы"&&button.Text!="Закрыть").Select(button=>button.Width).Distinct().Count()==1, "SQL action widths match");
                    Check(grid.Bottom<=page.GetControlFromPosition(0,2).Top,"Table stays above the actions");
                    if(scale==1F)using(var image=new Bitmap(page.Width,page.Height)){
                        page.DrawToBitmap(image,new Rectangle(Point.Empty,image.Size));
                        image.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"sql-page-"+dark+".png"));
                    }
                }
                using(var form=new SoftwareManagerForm(true))
                {
                    form.Show();Application.DoEvents();
                    var original=ControlsUnder(form).OfType<CompactJournalLayout>().Single();
                    var buttons=ControlsUnder(form).OfType<BrandButton>().ToArray();
                    var sqlButton=buttons.Single(button=>button.Text=="SQL Server и SQL Server Management Studio");
                    var access=buttons.Single(button=>button.Text=="Настроить удалённый доступ");
                    Check(sqlButton.Width==access.Width,"SQL entry has remote-access button width");
                    var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
                    typeof(SoftwareManagerForm).GetField("checking",flags).SetValue(form,true);
                    typeof(SoftwareManagerForm).GetMethod("Lock",flags).Invoke(form,new object[]{true});
                    Check(sqlButton.Enabled,"SQL entry remains enabled during program scan");
                    sqlButton.PerformClick();
                    typeof(SoftwareManagerForm).GetField("checking",flags).SetValue(form,false);
                    typeof(SoftwareManagerForm).GetMethod("Lock",flags).Invoke(form,new object[]{false});
                    var page=ControlsUnder(form).OfType<SqlManagerPage>().Single();
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    while(page.Busy&&timer.ElapsedMilliseconds<10000){Application.DoEvents();System.Threading.Thread.Sleep(2);}
                    Check(!page.Busy,"SQL page loaded from software navigation");
                    Check(!ControlsUnder(form).Contains(original),"Software content is hidden while SQL page is active");
                    Application.DoEvents();
                    var back=ControlsUnder(page).OfType<BrandButton>().Single(button=>button.Text=="Программы");
                    bool fired=false;
                    back.Click+=delegate{fired=true;};
                    back.Focus();
                    back.PerformClick();
                    Application.DoEvents();
                    Check(fired,"SQL return button responds: enabled="+back.Enabled+", visible="+back.Visible+", selectable="+back.CanSelect+", bounds="+back.Bounds);
                    Check(ControlsUnder(form).Contains(original)&&!ControlsUnder(form).Contains(page),"Return restores the same software page");
                    form.Close();
                }
                foreach(bool dark in new[]{false,true})
                using(var tools=new ToolsDeploymentForm(true))
                {
                    tools.SetTheme(dark,false);tools.Show();Application.DoEvents();
                    var labels=new[]{"Распаковать","Удалить","Отменить","В меню","Закрыть"};
                    var buttons=ControlsUnder(tools).OfType<BrandButton>().Where(button=>labels.Contains(button.Text)).ToArray();
                    Check(buttons.Length==5&&buttons.GroupBy(button=>button.Parent).All(group=>group.Select(button=>button.Size).Distinct().Count()==1),"Tools buttons match within each action row");
                    tools.Close();
                }
                Console.WriteLine("PASS: "+checks+" SQL manifest, archive and layout checks. No setup, cloud download, or Windows changes.");
                return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally
            {
                if(Directory.Exists(fixture))
                {
                    foreach(string entry in Directory.EnumerateFileSystemEntries(fixture,"*",SearchOption.AllDirectories))
                        File.SetAttributes(entry,FileAttributes.Normal);
                    Directory.Delete(fixture,true);
                }
            }
        }
    }
}
