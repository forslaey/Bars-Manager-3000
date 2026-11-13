using System;
using System.Linq;
using System.Windows.Forms;
namespace ServiceTool
{
 internal static class BundleMirrorTests
 {
  static int count;static void Check(bool ok,string name){if(!ok)throw new Exception(name);count++;}
  static System.Collections.Generic.IEnumerable<Control> Children(Control c){foreach(Control child in c.Controls){yield return child;foreach(var nested in Children(child))yield return nested;}}
  [STAThread]static int Main(){try{
   var link=GoogleBundleSource.ConfirmationLink("<form action=\"https://drive.usercontent.google.com/download\"><input name=\"id\" value=\"abc\"><input name=\"confirm\" value=\"a&amp;b\"></form>");Check(link.Query.Contains("a%26b"),"Google confirmation safely encoded");bool unsafeLink=false;try{GoogleBundleSource.ConfirmationLink("<form action=\"https://evil.test/download\"><input name=\"id\" value=\"x\"></form>");}catch(System.IO.IOException){unsafeLink=true;}Check(unsafeLink,"Foreign confirmation host denied");
   var baseline=BundleCloudSource.Baseline();Check(baseline.Count>0&&baseline.All(f=>f.Path!=null&&f.Sha256.Length==64)&&new[]{"Библиотеки","Утилиты","Программы","Запускатель","Обои","SQL"}.All(group=>baseline.Any(f=>f.Group==group)),"Embedded hash manifest complete");
   var sqlNames=BundleSqlFolders.Names(baseline);
   Check(sqlNames.Any(name=>name.Contains("2025"))&&sqlNames.Contains("SQL Server Management Studio"),"SQL package folders discovered from catalog");
   Check(BundleSqlFolders.Name(new BundleRemoteFile{Path="/_soft/_SQL/SSMS/Offline/setup.exe"})=="SSMS","Nested files stay in top-level package");
   Check(BundleSqlFolders.Name(new BundleRemoteFile{Path="/_soft/_SQL/orphan.exe"})==null,"Root files are not shown as packages");
   var wanted = new System.Collections.Generic.HashSet<string>(sqlNames.Where(name=>name.Contains("2025")||name=="SQL Server Management Studio"),StringComparer.OrdinalIgnoreCase);
   var wantedFiles = baseline.Where(file=>BundleSqlFolders.Includes(file,wanted)).ToArray();
   Check(wanted.Count==2&&wantedFiles.Length==baseline.Count(file=>wanted.Any(name=>file.Path.StartsWith("/_soft/_SQL/"+name+"/",StringComparison.OrdinalIgnoreCase)))&&BundleSqlFolders.Names(wantedFiles).Length==2,"Only two selected folder trees enter queue with all nested files");
   using(var sql=new BundleSqlFoldersForm(sqlNames,null)){
    sql.Show();Application.DoEvents();
    var button=Children(sql).OfType<BrandButton>().Single(b=>b.Text=="Выбрать всё");
    button.PerformClick();Check(sql.Selected.Count==sqlNames.Length&&button.Text=="Снять выбор","Select all SQL packages");
    button.PerformClick();Check(sql.Selected.Count==0,"Clear SQL package selection");
    var grid=Children(sql).OfType<ServiceGrid>().Single();
    var click=typeof(DataGridView).GetMethod("OnCellMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(DataGridViewCellMouseEventArgs)},null);
    click.Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(1,0,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});Check(sql.Selected.Count==1,"Click package name to select");
    click.Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(1,0,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});Check(sql.Selected.Count==0,"Immediately click again to clear");
    Check(sql.ClientSize.Width<690*sql.CurrentAutoScaleDimensions.Width/96F&&Math.Abs(grid.Height-grid.Parent.Height)<=2,"Compact SQL window hugs short table: "+sql.ClientSize+" / "+grid.Height+" / "+grid.Parent.Height);
    sql.Close();
   }
   foreach(bool dark in new[]{false,true}) foreach(float scale in new[]{1F,1.25F,1.5F,2F})
   using(var sql=new BundleSqlFoldersForm(Enumerable.Range(0,45).Select(i=>"Microsoft SQL Server "+i),null)){
    sql.SetTheme(dark,false);sql.Scale(new System.Drawing.SizeF(scale,scale));sql.Show();for(int n=0;n<6;n++)Application.DoEvents();
    var grid=Children(sql).OfType<ServiceGrid>().Single();
    var scroll=Children(sql).OfType<LibraryGridScrollBar>().Single();
    Check(Screen.FromControl(sql).WorkingArea.Contains(sql.Bounds),"SQL window stays inside working area");
    Check(scroll.Visible&&scroll.Bottom<=grid.Bottom&&grid.TouchScrollingEnabled,"Long SQL table uses bounded touch scrollbar");
    var cell=grid.GetCellDisplayRectangle(1,0,false);
    var point=new System.Drawing.Point(cell.Left+18,cell.Top+10);
    grid.TouchPan(point,true,false);grid.TouchPan(new System.Drawing.Point(point.X,point.Y-200),false,true);
    Check(grid.FirstDisplayedScrollingRowIndex>0&&sql.Selected.Count==0,"SQL swipe scrolls without selecting");
    var buttons=Children(sql).OfType<BrandButton>().ToArray();
    Check(buttons.Length==3&&buttons.Max(b=>b.Width)-buttons.Min(b=>b.Width)<=2&&buttons.All(b=>b.Parent.ClientRectangle.Contains(b.Bounds)),"SQL footer buttons fit and have equal widths");
    sql.Close();
   }
   var express = baseline.Where(file => (BundleSqlFolders.Name(file)??"").Contains("2019 Express")).ToArray();
   Check(express.Length==2&&express.Any(file=>file.Path.EndsWith("/SQLEXPRESS_x64_RUS.exe")),"Express catalog includes installer and update");
   var cleanupRoot=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"bars-cleanup-test-"+Guid.NewGuid().ToString("N"));
   System.IO.Directory.CreateDirectory(cleanupRoot);
   var finished=System.IO.Path.Combine(cleanupRoot,"keep.exe");
   System.IO.File.WriteAllText(finished,"fixture");
   BundleSession.Save(cleanupRoot,express);
   BundleSession.Cleanup(cleanupRoot);
   Check(System.IO.File.Exists(finished)&&!System.IO.Directory.Exists(System.IO.Path.Combine(cleanupRoot,".bars-download")),"Cleanup removes only temporary download folder");
   System.IO.File.Delete(finished);
   System.IO.Directory.Delete(cleanupRoot);
   using(var confirm=new BrandMessageDialog("Остановить загрузку? Готовые файлы сохранятся. Недокачанные файлы будут удалены.","Загрузка комплекта",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2,ThemePalette.Create(true),true)){
    confirm.Show();Application.DoEvents();
    Check(Children(confirm).OfType<BrandButton>().All(button=>confirm.ClientRectangle.Contains(confirm.RectangleToClient(button.RectangleToScreen(button.ClientRectangle)))),"Cancel confirmation shows full button bounds");
    confirm.Close();
   }
   string html="window['_DRIVE_ivd'] = '[[[\"abc\",[\"parent\"],\"file.txt\",\"text/plain\",0,null,0,0,0,0,0,null,null,10]],null,null]';";Check(GoogleBundleSource.Parse(html).Length==1,"Public Google listing parser");
   Check(GoogleBundleSource.Parse("window['_DRIVE_ivd'] = '[null,null,null]';").Length==0,"Empty public folder is a valid listing");
   bool denied=false;try{GoogleBundleSource.Parse(html.Replace("null,null]","\"next\",null]"));}catch(System.IO.InvalidDataException){denied=true;}Check(denied,"Incomplete listing fails closed");
   using(var check=new BundleCloudDialog(true)){var choices=Children(check).OfType<BrandCheckBox>().ToArray();choices[1].Checked=true;Check(choices.All(c=>c.Checked),"Both clouds allowed for checking");}
   using(var download=new BundleCloudDialog(false)){var choices=Children(download).OfType<BrandCheckBox>().ToArray();choices[1].Checked=true;Check(!choices[0].Checked&&choices[1].Checked,"Download cloud exclusive");}
   var item=new BundleDifferenceItem {Path="missing.exe",State="Нет в папке",Allowed=true,Selected=true};using(var result=new BundleDifferencesForm(new[]{item})){result.Show();Application.DoEvents();var grid=Children(result).OfType<ServiceGrid>().Single();typeof(DataGridView).GetMethod("OnCellMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(DataGridViewCellMouseEventArgs)},null).Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(1,0,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});Check(!item.Selected,"Whole row toggles saved selection");}
   using(var bundle=new BundleDownloadForm()){
    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
    typeof(BundleDownloadForm).GetField("scannedRoot",flags).SetValue(bundle,System.IO.Path.GetTempPath());
    typeof(BundleDownloadForm).GetField("cancelRequested",flags).SetValue(bundle,true);
    typeof(BundleDownloadForm).GetMethod("UpdateState",flags).Invoke(bundle,null);
    Check(Children(bundle).OfType<BrandButton>().Single(b=>b.Text=="Результаты").Enabled,"Results remain enabled after cancellation with a scanned root");
   }
   var rechecked=new BundleDifferenceItem{Path="fixture.exe",State="Отсутствует",Allowed=true,Selected=true};
   using(var result=new BundleDifferencesForm(new[]{rechecked},recheck:delegate(System.Threading.CancellationToken token){
    rechecked.State="Файл на месте";rechecked.Allowed=rechecked.Selected=false;return System.Threading.Tasks.Task.FromResult(0);
   })){
    result.Show();Application.DoEvents();
    Children(result).OfType<BrandButton>().Single(b=>b.Text=="Перепроверить файлы").PerformClick();
    var row=Children(result).OfType<ServiceGrid>().Single().Rows[0];
    Check((string)row.Cells[2].Value=="Файл на месте"&&!(bool)row.Cells[0].Value,"Local recheck updates state and selection in the open dialog");
    result.Close();
   }
   var sqlFile=new BundleDifferenceItem{Path="_soft/_SQL/Express/setup.exe",State="Отсутствует",Allowed=true,Selected=true};
   var verifiedFile=new BundleDifferenceItem{Path="_soft/_SQL/Express/update.exe",State="Проверено",Allowed=false,Selected=false};
   using(var result=new BundleDifferencesForm(new[]{sqlFile,verifiedFile})){
    result.Show();for(int i=0;i<6;i++)Application.DoEvents();
    var grid=Children(result).OfType<ServiceGrid>().Single();
    double ratio=(double)grid.Columns[1].Width/(grid.Columns[1].Width+grid.Columns[2].Width);
    Check(ratio>=0.70&&ratio<=0.74,"Result path column takes about 72%: "+ratio);
    var click=typeof(DataGridView).GetMethod("OnCellMouseDown",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(DataGridViewCellMouseEventArgs)},null);
    click.Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(1,0,0,0,new MouseEventArgs(MouseButtons.Left,1,0,0,0))});Check(!sqlFile.Selected,"SQL file can be unchecked by clicking its path");
    click.Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(1,0,0,0,new MouseEventArgs(MouseButtons.Left,2,0,0,0))});
    Check(sqlFile.Selected,"Second mouse-down in a rapid double click toggles immediately");
    click.Invoke(grid,new object[]{new DataGridViewCellMouseEventArgs(1,0,0,0,new MouseEventArgs(MouseButtons.Right,1,0,0,0))});
    Check(sqlFile.Selected,"Right click leaves the checkbox unchanged");
    Children(result).OfType<BrandButton>().Single(b=>b.Text=="Выбрать всё").PerformClick();
    Check(sqlFile.Selected&&!verifiedFile.Selected,"Select all checks only eligible files");
    Children(result).OfType<BrandButton>().Single(b=>b.Text=="Снять выбор").PerformClick();
    Check(!sqlFile.Selected,"Clear all unchecks eligible SQL files");result.Close();
   }
   using(var result=new BundleDifferencesForm(new[]{new BundleDifferenceItem{Path="pending.exe"}},recheck:async token=>{
    await System.Threading.Tasks.Task.Delay(System.Threading.Timeout.Infinite,token);
   })){
    result.Show();Application.DoEvents();
    var button=Children(result).OfType<BrandButton>().Single(b=>b.Text=="Перепроверить файлы");
    button.PerformClick();Check(button.Text=="Отменить проверку","Recheck has a cancellation action");
    result.Close();Application.DoEvents();
    Check(!result.Visible,"Results close during recheck");
   }
   using(var form=new BundleDownloadForm()){
    var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
    typeof(BundleDownloadForm).GetField("downloading",flags).SetValue(form,true);
    typeof(BundleDownloadForm).GetField("preparingDownload",flags).SetValue(form,true);
    var state=typeof(BundleDownloadForm).GetMethod("UpdateState",flags);
    state.Invoke(form,null);
    var pause=Children(form).OfType<BrandButton>().Single(b=>b.Text=="Пауза");
    Check(!pause.Enabled,"Pause cannot cancel catalog preparation");
    typeof(BundleDownloadForm).GetField("preparingDownload",flags).SetValue(form,false);
    state.Invoke(form,null);
    Check(pause.Enabled,"Pause enabled for the transfer phase");
   }
   var source=new BundleCloudSource();source.GoogleFiles=new System.Collections.Generic.List<BundleRemoteFile>{new BundleRemoteFile {Path="/a",Size=10,Sha256=new string('a',64)}};Check(!source.Contains(BundleCloud.Google,new BundleRemoteFile {Path="/a",Size=10,Sha256=new string('b',64)}),"Mirror mismatch cannot reuse part");
   Check(BundleDownloadForm.AlternativeMessage(BundleCloud.Yandex,3).Contains("Google Диск")&&BundleDownloadForm.AlternativeMessage(BundleCloud.Google,3).Contains("Яндекс Диск"),"Both fallback directions named");
   Check(BundleDownloadForm.AlternativeMessage(BundleCloud.Yandex,3).Contains("не будут скачиваться заново"),"Completed files explanation");
   Console.WriteLine("PASS: "+count+" mirror/parser/cloud choice/result selection checks. No network.");return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}}
 }
}
