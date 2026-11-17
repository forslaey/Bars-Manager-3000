using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace ServiceTool
{
    internal static class FullAuditFindingsTests
    {
        private static int failures;

        private static IEnumerable<Control> ControlsUnder(Control parent)
        {
            foreach(Control child in parent.Controls)
            {
                yield return child;
                foreach(Control descendant in ControlsUnder(child))yield return descendant;
            }
        }

        private static void Probe(string name, Action action)
        {
            try { action(); Console.WriteLine("PASS "+name); }
            catch(Exception ex) { failures++; Console.WriteLine("FAIL "+name+": "+ex.Message); }
        }

        private static void Require(bool condition,string message)
        {
            if(!condition)throw new InvalidOperationException(message);
        }

        private static void CreateZip(string path,string name,string value)
        {
            using(var archive=new System.IO.Compression.ZipArchive(File.Create(path),System.IO.Compression.ZipArchiveMode.Create))
            using(var writer=new StreamWriter(archive.CreateEntry(name,System.IO.Compression.CompressionLevel.NoCompression).Open()))
                writer.Write(value);
        }

        private static void WaitFor(Func<bool> condition)
        {
            var timer=System.Diagnostics.Stopwatch.StartNew();
            while(!condition()&&timer.ElapsedMilliseconds<5000){Application.DoEvents();Thread.Sleep(10);}
            Require(condition(),"Timed out waiting for tool workspace.");
        }

        [STAThread]
        internal static int Main()
        {
            Application.EnableVisualStyles();
            string root=Path.Combine(Path.GetTempPath(),"bars-audit-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                Probe("archive inspection separates corruption, access and cancellation",delegate
                {
                    var token=System.Threading.CancellationToken.None;
                    string path=Path.Combine(root,"broken.7z");
                    File.WriteAllText(path,"broken");
                    string[] entries;
                    Require(ToolArchives.Inspect(path,token,out entries)==ToolArchives.ReadState.Damaged,"Broken 7z was not classified per file.");
                    using(var locked=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None))
                        Require(ToolArchives.Inspect(path,token,out entries)==ToolArchives.ReadState.Unavailable,"Locked archive was classified as damaged.");
                    using(var cancel=new System.Threading.CancellationTokenSource())
                    {
                        cancel.Cancel();
                        bool cancelled=false;
                        try{ToolsWorkspaceGrid.ReadPackages(root,new string[0],cancel.Token);}
                        catch(OperationCanceledException){cancelled=true;}
                        Require(cancelled,"Cancelled package scan completed.");
                    }
                    using(var form=new ToolsDeploymentForm(true))
                    {
                        var recheck=ControlsUnder(form).OfType<Button>().Single(button=>button.Text=="Перепроверить файлы");
                        Require(!recheck.Enabled,"Recheck was enabled without a remote catalog.");
                    }
                });
                Probe("archive failure dialog lists names without repeated errors",delegate
                {
                    var names=new[]{"Termite.zip","WinSCP 6.6.2.zip"};
                    string message=BundleDownloadForm.FailedArchiveMessage(names,true);
                    Require(message=="Выбранные архивы скачаны не будут — они повреждены.\r\n\r\nTermite.zip\r\nWinSCP 6.6.2.zip\r\n\r\nПосмотреть наборы в другом облаке?","Damaged archive dialog text differs.");
                    Require(!BundleDownloadForm.FailedArchiveMessage(names,false).Contains("повреждены"),"Download failure was reported as corruption.");
                });
                Probe("archive payload validation and wrapper paths",delegate
                {
                    var token=System.Threading.CancellationToken.None;
                    string path=Path.Combine(root,"sample.zip");
                    using(var archive=new System.IO.Compression.ZipArchive(File.Create(path),System.IO.Compression.ZipArchiveMode.Create))
                    using(var writer=new StreamWriter(archive.CreateEntry("sample/file.txt",System.IO.Compression.CompressionLevel.NoCompression).Open()))
                        writer.Write("unique-archive-payload");
                    ToolArchives.ValidateDownload(path,path,token);
                    Require(ToolArchives.NormalizeEntries(path,ToolArchives.Entries(path,token)).SequenceEqual(new[]{"file.txt"}),"Wrapper paths do not agree with extraction.");
                    byte[] bytes=File.ReadAllBytes(path);
                    byte[] payload=System.Text.Encoding.ASCII.GetBytes("unique-archive-payload");
                    int offset=Enumerable.Range(0,bytes.Length-payload.Length).First(index=>bytes.Skip(index).Take(payload.Length).SequenceEqual(payload));
                    bytes[offset]^=1;
                    File.WriteAllBytes(path,bytes);
                    Require(PackageZipExtraction.HasReadableDirectory(path),"Fixture no longer has a readable ZIP directory.");
                    bool rejected=false;
                    try{ToolArchives.ValidateDownload(path,path,token);}catch(InvalidDataException){rejected=true;}
                    Require(rejected,"Damaged ZIP payload with valid directory was accepted.");
                    string[] contents;
                    Require(ToolArchives.Inspect(path,token,out contents)==ToolArchives.ReadState.Damaged,"Local ZIP with bad CRC was marked ready.");
                    string prior=Directory.CreateDirectory(Path.Combine(root,"sample")).FullName;
                    File.WriteAllText(Path.Combine(prior,"old.txt"),"previous");
                    rejected=false;
                    try{PackageZipExtraction.Extract(path,token,null,true);}catch(InvalidDataException){rejected=true;}
                    Require(rejected&&File.Exists(Path.Combine(prior,"old.txt"))&&!File.Exists(Path.Combine(prior,"file.txt")),
                        "Corrupt ZIP replaced the existing destination.");
                    Require(!Directory.GetDirectories(root,".s*").Any(),"Partial extraction was left behind.");
                });
                Probe("ZIP64 central directory",delegate
                {
                    string path=Path.Combine(root,"zip64.zip");
                    using(var zip=new System.IO.Compression.ZipArchive(File.Create(path),System.IO.Compression.ZipArchiveMode.Create))
                        for(int i=0;i<=UInt16.MaxValue;i++)zip.CreateEntry("file-"+i+".txt");
                    string[] items;
                    Require(ToolArchives.Inspect(path,CancellationToken.None,out items)==ToolArchives.ReadState.Ready&&items.Length==65536,
                        "ZIP64 lost entries during managed validation.");
                });
                Probe("7z validation, wrapper and replacement",delegate
                {
                    var token=System.Threading.CancellationToken.None;
                    string folder=Directory.CreateDirectory(Path.Combine(root,"Seven")).FullName;
                    File.WriteAllText(Path.Combine(folder,"file.txt"),"new payload");
                    string path=Path.Combine(root,"Seven.7z");
                    var start=new System.Diagnostics.ProcessStartInfo("tar.exe","-a -cf \""+path+"\" -C \""+root+"\" Seven")
                    {UseShellExecute=false,CreateNoWindow=true};
                    using(var process=System.Diagnostics.Process.Start(start))
                    {
                        if(!process.WaitForExit(10000)){process.Kill();throw new IOException("Fixture creation timed out.");}
                        Require(process.ExitCode==0,"7z fixture creation failed.");
                    }
                    File.WriteAllText(Path.Combine(folder,"file.txt"),"old payload");
                    ToolArchives.ValidateDownload(path,path,token);
                    Require(ToolArchives.NormalizeEntries(path,ToolArchives.Entries(path,token)).Contains("file.txt"),"7z wrapper did not normalize.");
                    string output=ToolArchives.Extract(path,token,null,true);
                    Require(File.ReadAllText(Path.Combine(output,"file.txt"))=="new payload","7z replacement did not publish the extracted payload.");
                });
                Probe("same archive stem, independent payloads and destinations",delegate
                {
                    var token=CancellationToken.None;
                    string source=Directory.CreateDirectory(Path.Combine(root,"collision","_tools")).FullName;
                    string target=Path.Combine(root,"installed","_tools");
                    string wrapper=Directory.CreateDirectory(Path.Combine(root,"shared")).FullName;
                    File.WriteAllText(Path.Combine(wrapper,"second.txt"),"7z");
                    string packed=Path.Combine(source,"shared.7z");
                    var start=new System.Diagnostics.ProcessStartInfo("tar.exe","-a -cf \""+packed+"\" -C \""+root+"\" shared")
                    {UseShellExecute=false,CreateNoWindow=true};
                    using(var process=System.Diagnostics.Process.Start(start))
                    {
                        if(!process.WaitForExit(10000)){process.Kill();throw new IOException("Fixture creation timed out.");}
                        Require(process.ExitCode==0,"7z fixture creation failed.");
                    }
                    CreateZip(Path.Combine(source,"shared.zip"),"shared/first.txt","zip");
                    Directory.CreateDirectory(Path.Combine(source,"shared (ZIP)"));
                    var destinations=ToolsDeploymentForm.ArchiveDestinations(source);
                    Require(destinations.Values.Distinct(StringComparer.OrdinalIgnoreCase).Count()==2&&destinations["shared.zip"]!="shared (ZIP)",
                        "An archive destination collides with an existing folder.");
                    foreach(var archive in destinations)
                    {
                        string extracted=ToolArchives.Extract(Path.Combine(source,archive.Key),token,null,true,archive.Value);
                        File.WriteAllText(Path.Combine(extracted,ToolsDeployment.ArchiveOriginFile),archive.Key);
                    }
                    var rows=ToolsDeploymentForm.LocalPackages(source);
                    Require(rows.Length==3&&rows.Contains("shared (ZIP)"),"Generated folders duplicated or hid an independent package in the catalog.");
                    ToolsDeployment.CopyToTarget(source,target,ignored=>{},()=>false,
                        new HashSet<string>(destinations.Values,StringComparer.OrdinalIgnoreCase));
                    Require(File.ReadAllText(Path.Combine(target,destinations["shared.zip"],"first.txt"))=="zip"&&
                        File.ReadAllText(Path.Combine(target,destinations["shared.7z"],"second.txt"))=="7z","One archive replaced the other.");
                    string zipOutput=Path.Combine(target,destinations["shared.zip"]);
                    Require(ToolsDeployment.ArchiveOrigin(zipOutput)=="shared.zip","The deployed directory lost its archive identity.");
                    File.WriteAllText(Path.Combine(zipOutput,ToolsDeployment.ArchiveOriginFile),"other.zip");
                    bool rejected=false;
                    try{ToolsDeployment.CopyToTarget(source,target,ignored=>{},()=>false,new HashSet<string>{destinations["shared.zip"]});}
                    catch(IOException){rejected=true;}
                    Require(rejected&&ToolsDeployment.ArchiveOrigin(zipOutput)=="other.zip","Deployment overwrote an unrelated folder.");
                    File.Delete(packed);
                    Require(ToolsDeploymentForm.ArchiveDestinations(source)["shared.zip"]==destinations["shared.zip"],
                        "Removing a competing archive changed the remaining package destination.");
                });
                Probe("replacement removes obsolete variants and preserves settings",delegate
                {
                    string source=Path.Combine(root,"replace-source","_tools");
                    string target=Path.Combine(root,"replace-target","_tools");
                    string old=Directory.CreateDirectory(Path.Combine(target,"R-Tool","v1")).FullName;
                    string existing=Directory.CreateDirectory(Path.Combine(target,"R-Tool","v2")).FullName;
                    string selected=Directory.CreateDirectory(Path.Combine(source,"R-Tool","v2")).FullName;
                    Directory.CreateDirectory(Path.Combine(source,"R-Tool","v1"));
                    File.WriteAllText(Path.Combine(old,"R-Tool.exe"),"old variant");
                    File.WriteAllText(Path.Combine(existing,"user.ini"),"user");
                    File.WriteAllText(Path.Combine(selected,"user.ini"),"default");
                    File.WriteAllText(Path.Combine(existing,"R-Tool.runtimeconfig.json"),"old metadata");
                    File.WriteAllText(Path.Combine(selected,"R-Tool.runtimeconfig.json"),"new metadata");
                    File.WriteAllText(Path.Combine(target,"R-Tool","obsolete.dll"),"old DLL");
                    File.WriteAllText(Path.Combine(target,"keep.txt"),"unrelated");
                    File.WriteAllText(Path.Combine(selected,"R-Tool.exe"),"updated");
                    ToolsDeployment.CopyToTarget(source,target,ignored=>{},()=>false,
                        new HashSet<string>{"R-Tool"},new[]{"R-Tool/v1"});
                    Require(!Directory.Exists(old)&&!File.Exists(Path.Combine(target,"R-Tool","obsolete.dll")),"Replacement retained obsolete content.");
                    Require(File.ReadAllText(Path.Combine(existing,"R-Tool.exe"))=="updated"&&
                        File.ReadAllText(Path.Combine(existing,"R-Tool.runtimeconfig.json"))=="new metadata","Replacement did not refresh binaries and runtime metadata.");
                    Require(File.ReadAllText(Path.Combine(existing,"user.ini"))=="user"&&File.Exists(Path.Combine(target,"keep.txt")),
                        "Replacement lost settings or unrelated content.");
                });
                Probe("failed workspace recheck keeps the last successful rows",delegate
                {
                    string bundle=Directory.CreateDirectory(Path.Combine(root,"recheck")).FullName;
                    string tools=Directory.CreateDirectory(Path.Combine(bundle,"_tools")).FullName;
                    CreateZip(Path.Combine(tools,"ready.zip"),"file.txt","valid");
                    const BindingFlags flags=BindingFlags.Instance|BindingFlags.NonPublic;
                    using(var form=new BundleDownloadForm(bundle,toolsOnly:true))
                    {
                        form.ShowInTaskbar=false;
                        form.Show();
                        var grid=(ToolsWorkspaceGrid)typeof(BundleDownloadForm).GetField("toolGrid",flags).GetValue(form);
                        WaitFor(()=>grid.HasLocal);
                        typeof(BundleDownloadForm).GetField("scannedRoot",flags).SetValue(form,bundle);
                        Directory.Move(tools,Path.Combine(bundle,"previous"));
                        File.WriteAllText(tools,"directory is no longer available");
                        var recheck=(Task)typeof(BundleDownloadForm).GetMethod("RecheckTools",flags).Invoke(form,null);
                        WaitFor(()=>recheck.IsCompleted);
                        recheck.GetAwaiter().GetResult();
                        var summary=(Label)typeof(BundleDownloadForm).GetField("summary",flags).GetValue(form);
                        Require(grid.HasLocal&&summary.Text.StartsWith("Не удалось сверить файлы",StringComparison.Ordinal),
                            "Recheck lost previous rows or reported success after the read failed.");
                    }
                });
                Probe("mixed Google archive rescan",delegate
                {
                    var tools=Directory.CreateDirectory(Path.Combine(root,"_tools"));
                    string good=Path.Combine(tools.FullName,"good.zip");
                    using(var archive=new System.IO.Compression.ZipArchive(File.Create(good),System.IO.Compression.ZipArchiveMode.Create))
                    using(var writer=new StreamWriter(archive.CreateEntry("file.txt").Open()))writer.Write("good");
                    string bad=Path.Combine(tools.FullName,"bad.zip");
                    File.WriteAllText(bad,"broken");
                    var method=typeof(BundleDownloadForm).GetMethod("CheckLocal",System.Reflection.BindingFlags.Static|System.Reflection.BindingFlags.NonPublic);
                    foreach(var path in new[]{good,bad})
                    {
                        var file=new BundleRemoteFile{Path="/_tools/"+Path.GetFileName(path),Size=new FileInfo(path).Length};
                        var entry=method.Invoke(null,new object[]{root,file,System.Threading.CancellationToken.None});
                        bool done=(bool)entry.GetType().GetField("Done",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).GetValue(entry);
                        bool selected=(bool)entry.GetType().GetField("Selected",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Public).GetValue(entry);
                        Require(!done&&selected==(path==bad),"Rescan did not distinguish the readable archive from the damaged one.");
                    }
                });
                foreach(bool dark in new[]{false,true})
                {
                    Probe("save refuses an existing directory / dark="+dark,delegate
                    {
                        string directory=Path.Combine(root,"existing.zip");
                        Directory.CreateDirectory(directory);
                        using(var dialog=new BrandPathDialog("Audit save",root,directory,"ZIP|*.zip","zip",false,true,false))
                        {
                            dialog.SetTheme(dark,false);
                            dialog.ShowInTaskbar=false;
                            dialog.Show();
                            Application.DoEvents();
                            ControlsUnder(dialog).OfType<BrandButton>().Single(button=>button.Text=="Сохранить").PerformClick();
                            Require(dialog.DialogResult!=DialogResult.OK,"Picker returned a directory as the destination file.");
                        }
                    });

                    foreach(bool folders in new[]{false,true})
                    {
                        Probe("path picker touch navigation / folders="+folders+" / dark="+dark,delegate
                        {
                            string child=Directory.CreateDirectory(Path.Combine(root,"child")).FullName;
                            string next=Directory.CreateDirectory(Path.Combine(child,"next")).FullName;
                            string exe=Path.Combine(next,"test.exe");
                            File.WriteAllText(exe,"fixture");
                            using(var dialog=new BrandPathDialog("Path audit",root,null,"EXE|*.exe",null,folders,false,false))
                            {
                                dialog.SetTheme(dark,false);
                                dialog.ShowInTaskbar=false;
                                dialog.Show();
                                Application.DoEvents();
                                var grid=ControlsUnder(dialog).OfType<ServiceGrid>().Single();
                                var path=ControlsUnder(dialog).OfType<BrandTextBox>().Single(box=>box.AccessibleName=="Путь к папке");
                                var go=ControlsUnder(dialog).OfType<BrandButton>().Single(button=>button.Text=="Перейти");
                                var accept=ControlsUnder(dialog).OfType<BrandButton>().Single(button=>button.Text==(folders?"Выбрать папку":"Открыть"));
                                var row=grid.Rows.Cast<DataGridViewRow>().Single(item=>Equals(item.Tag,child));
                                var rect=grid.GetCellDisplayRectangle(0,row.Index,true);
                                var point=new Point(rect.Left+15,rect.Top+rect.Height/2);
                                grid.TouchMouse(0x201,point,true);
                                var moved=new Point(point.X,point.Y+24);
                                grid.TouchMouse(0x200,moved,true);
                                grid.TouchMouse(0x202,moved,true);
                                Require(grid.SelectedRows.Count==0&&path.Text==root,"A swipe must not select or open a folder.");
                                grid.TouchMouse(0x201,point,true);
                                grid.TouchMouse(0x202,point,true);
                                Require(row.Selected&&path.Text==root,"A tap must select the folder without opening it.");
                                go.PerformClick();
                                Require(path.Text==child,"Go did not open the selected folder.");
                                var mouseBack=Message.Create(grid.Handle,0x020C,new IntPtr(1<<16),IntPtr.Zero);
                                var mouseForward=Message.Create(grid.Handle,0x020C,new IntPtr(2<<16),IntPtr.Zero);
                                Require(((IMessageFilter)dialog).PreFilterMessage(ref mouseBack)&&path.Text==root,"Mouse Back did not restore the prior folder.");
                                Require(((IMessageFilter)dialog).PreFilterMessage(ref mouseForward)&&path.Text==child,"Mouse Forward did not restore the next folder.");
                                Require(((IMessageFilter)dialog).PreFilterMessage(ref mouseBack)&&path.Text==root,"Could not return before double tap.");
                                Action<string> doubleTap=target=>
                                {
                                    var hitRow=grid.Rows.Cast<DataGridViewRow>().Single(item=>Equals(item.Tag,target));
                                    var bounds=grid.GetCellDisplayRectangle(0,hitRow.Index,true);
                                    var tap=new Point(bounds.Left+15,bounds.Top+bounds.Height/2);
                                    for(int i=0;i<2;i++){grid.TouchMouse(0x201,tap,true);grid.TouchMouse(0x202,tap,true);}
                                };
                                doubleTap(child);
                                Require(path.Text==child,"Double tap did not open the folder.");
                                row=grid.Rows.Cast<DataGridViewRow>().Single(item=>Equals(item.Tag,next));
                                grid.CurrentCell=row.Cells[0];row.Selected=true;
                                path.Text=root;
                                go.PerformClick();
                                Require(path.Text==root,"Typed address must take priority over selection.");
                                row=grid.Rows.Cast<DataGridViewRow>().Single(item=>Equals(item.Tag,child));
                                grid.CurrentCell=row.Cells[0];row.Selected=true;
                                if(folders)
                                {
                                    accept.PerformClick();
                                    Require(dialog.SelectedPath==child,"Folder selection did not return the chosen folder.");
                                }
                                else
                                {
                                    accept.PerformClick();
                                    Require(path.Text==child&&dialog.SelectedPath==null,"Open did not enter the selected folder.");
                                    path.Text=next;
                                    go.PerformClick();
                                    doubleTap(exe);
                                    Require(dialog.SelectedPath==exe,"File selection did not return the chosen file.");
                                }
                            }
                        });
                    }
                    Probe("invalid local ZIP cannot be selected / dark="+dark,delegate
                    {
                        string bad=Path.Combine(root,"Termite.zip");
                        File.WriteAllText(bad,"This is not a ZIP archive.");
                        using(var window=new Form{ClientSize=new Size(620,360),ShowInTaskbar=false})
                        using(var workspace=new ToolsWorkspaceGrid{Dock=DockStyle.Fill})
                        {
                            workspace.SetPalette(ThemePalette.Create(dark));
                            window.Controls.Add(workspace);
                            window.Show();
                            workspace.LoadRows(ToolsWorkspaceGrid.ReadPackages(root,new string[0],System.Threading.CancellationToken.None),new Dictionary<string,Tuple<string,bool,bool>>(StringComparer.OrdinalIgnoreCase));
                            Application.DoEvents();
                            var grid=workspace.Controls.OfType<ServiceGrid>().Single();
                            int row=grid.Rows.Cast<DataGridViewRow>().Single(item=>Convert.ToString(item.Cells[1].Value)=="Termite.zip").Index;
                            var mouse=typeof(DataGridView).GetMethod("OnCellMouseDown",
                                System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,
                                null,new[]{typeof(DataGridViewCellMouseEventArgs)},null);
                            mouse.Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(0,row,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});
                            Require(!workspace.Deployment.Contains("Termite.zip"),"Corrupt ZIP is treated as ready for extraction.");
                        }
                    });
                }
                return failures==0?0:1;
            }
            finally
            {
                foreach(string file in Directory.GetFiles(root,"*",SearchOption.AllDirectories))File.SetAttributes(file,FileAttributes.Normal);
                foreach(string directory in Directory.GetDirectories(root,"*",SearchOption.AllDirectories))File.SetAttributes(directory,FileAttributes.Directory);
                Directory.Delete(root,true);
            }
        }
    }
}
