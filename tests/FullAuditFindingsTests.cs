using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
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
            finally{Directory.Delete(root,true);}
        }
    }
}
