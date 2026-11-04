using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Collections.Generic;
namespace ServiceTool
{
    internal static class DownloadProgressTests
    {
        static int count;
        static void Check(bool value,string message){count++;if(!value)throw new Exception(message);}
        static IEnumerable<Control> Walk(Control root){foreach(Control c in root.Controls){yield return c;foreach(var child in Walk(c))yield return child;}}
        static void Pump(int ms){var end=DateTime.UtcNow.AddMilliseconds(ms);while(DateTime.UtcNow<end){Application.DoEvents();Thread.Sleep(5);}}
        [STAThread] static int Main(){try{
            Directory.CreateDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews"));
            foreach(string mode in new[]{"known","unknown","short","limit","cancel"})Transfer(mode);
            using(var form=new Form())using(var view=new DownloadProgressView{Dock=DockStyle.Fill}){
                form.Controls.Add(view);form.Show();view.Report(new TransferProgress{Stage="Скачивание",File="fixture",Bytes=0,Total=100});Pump(100);
                view.Report(new TransferProgress{Stage="Скачивание",File="fixture",Bytes=80,Total=100});Pump(65);double first=view.DisplayedFraction;
                Check(first>0&&first<=.8,"Progress starts without overshoot");Pump(400);Check(view.DisplayedFraction>=first&&view.DisplayedFraction<=.8,"Animation converges");
                double before=view.DisplayedFraction;view.Report(new TransferProgress{Stage="Пауза",File="fixture",Bytes=80,Total=100,Paused=true});Pump(200);Check(Math.Abs(before-view.DisplayedFraction)<.001,"Pause freezes animation");
                view.Report(new TransferProgress{Stage="Скачивание",File="fixture",Bytes=100,Total=100});Pump(500);Check(view.DisplayedFraction<1,"Bytes do not imply verified completion");
                view.Stage("Готово",complete:true);
                using(var bitmap=new Bitmap(view.Width,view.Height)){view.DrawToBitmap(bitmap,view.ClientRectangle);var right=bitmap.GetPixel(view.Width-12,39);Check(right.G-right.R>40,"100% paints a FULL bar before any timer tick");}
                Pump(500);Check(view.DisplayedFraction>.999,"Verified completion reaches 100");form.Close();
            }
            using(var lifecycle=new DownloadProgressView()){
                lifecycle.BeginOperation("Установка");var old=lifecycle.OperationReporter;old("Устанавливаем");Check(lifecycle.Snapshot.Active,"Explicit operation starts");
                lifecycle.FinishOperation("Готово",success:true);old("Поздний heartbeat");Check(lifecycle.Snapshot.Complete&&!lifecycle.Snapshot.Active,"Late callback cannot restart completed operation");
                lifecycle.BeginOperation("Проверка списка",false);var quiet=lifecycle.OperationReporter;quiet("Читаем список");Check(!lifecycle.Snapshot.Active,"Background scan stays quiet");
                old("Старый процесс");Check(lifecycle.Snapshot.Stage=="Читаем список","Old generation cannot overwrite new operation");
                lifecycle.FinishOperation("Отменено");quiet("Запоздалый запрос");Check(!lifecycle.Snapshot.Complete&&!lifecycle.Snapshot.Active,"Cancel stops without 100 percent");
                lifecycle.BeginOperation("Применяем");lifecycle.FinishOperation("Не подтверждено",failed:true);Check(lifecycle.Snapshot.Failed&&!lifecycle.Snapshot.Complete&&!lifecycle.Snapshot.Active,"Failure stops without success");
            }
            using(var queue=new DownloadProgressView()){
                queue.BeginOperation("Очередь",true,3);Check(queue.Snapshot.Overall==0,"Queue starts empty");
                queue.CompleteStep();double one=queue.Snapshot.Overall;Check(Math.Abs(one-1D/3)<.0001,"One of three tasks");
                var reporter=queue.OperationReporter;reporter("Установка без процентов");Check(queue.Snapshot.Overall==one,"Unknown installer progress holds achieved fraction");
                queue.Report(new TransferProgress{Stage="Скачивание",Bytes=50,Total=100});Check(Math.Abs(queue.Snapshot.Overall-.5)<.0001,"Byte progress contributes within a task");
                reporter("Проверяем файл");Check(Math.Abs(queue.Snapshot.Overall-.5)<.0001,"Validation does not reset fraction");
                queue.CompleteStep();Check(Math.Abs(queue.Snapshot.Overall-2D/3)<.0001,"Second task commits");
                queue.FinishOperation("Ошибка",failed:true);Check(Math.Abs(queue.Snapshot.Overall-2D/3)<.0001&&!queue.Snapshot.Complete,"Failure preserves partial queue");
                queue.BeginOperation("Файлы",true,3);queue.OperationReporter("BARS_FRACTION|2|4|Обработаны файлы");Check(Math.Abs(queue.Snapshot.Overall-1D/6)<.0001,"Copy reports processed files");
                queue.CompleteStep();queue.CompleteStep();queue.CompleteStep();queue.FinishOperation("Готово",success:true);Check(queue.Snapshot.Complete&&queue.WorkFinished,"All steps verified");
            }
            foreach(bool dark in new[]{false,true})foreach(float scale in new[]{1F,1.25F,1.5F,2F})foreach(int module in new[]{0,1,2,3,4,5}){
                using(ManagerModuleForm form=module==0?(ManagerModuleForm)new BundleDownloadForm(null):module==1?(ManagerModuleForm)new SoftwareManagerForm(true):module==2?(ManagerModuleForm)new DotNetUpdatesForm(()=>null):module==3?(ManagerModuleForm)new LibraryManagerForm(true):module==4?(ManagerModuleForm)new ToolsDeploymentForm(true):new InitialSystemSettingsForm(true)){
                    form.StartPosition=FormStartPosition.Manual;form.Location=new Point(-20000,-20000);form.SetTheme(dark,false);form.Show();if(scale!=1)form.Scale(new SizeF(scale,scale));Pump(180);
                    var view=Walk(form).OfType<DownloadProgressView>().Single();Check(!view.Snapshot.Active,"Opening a module leaves progress idle module="+module);view.Report(new TransferProgress{Stage="Скачивание · файл 2 из 5",File="Microsoft .NET ASP.NET Core Runtime — длинное название пакета x64.exe",Bytes=42*1048576L,Total=68*1048576L});Pump(200);
                    Check(view.Parent.ClientRectangle.Contains(view.Bounds),"Progress inside parent module="+module+" scale="+scale);
                    foreach(var journal in Walk(form).OfType<JournalTextBox>())Check(journal.Height>=journal.Font.Height,"Journal text viewport remains readable module="+module+" scale="+scale);
                    Check(view.Height>=92*scale,"Progress has room for all lines module="+module+" scale="+scale+" height="+view.Height);
                    foreach(Control peer in view.Parent.Controls)if(peer!=view&&peer.Visible)Check(!peer.Bounds.IntersectsWith(view.Bounds),"Progress does not overlap siblings");
                    if(!dark){
                        view.Report(new TransferProgress{Stage="Ожидание загрузки",Active=false});
                        using(var empty=new Bitmap(view.Width,view.Height)){view.DrawToBitmap(empty,view.ClientRectangle);var inside=empty.GetPixel(view.Width/2,Math.Max(view.Font.Height,18)+21);var outside=empty.GetPixel(view.Width/2,0);Check(outside.R-inside.R>=25&&outside.G-inside.G>=20,"Light empty track visibly distinct from canvas");}
                    }
                    if(module==1){
                        view.Stage("Установка подтверждена.",complete:true);
                        typeof(SoftwareManagerForm).GetMethod("Progress",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(form,new object[]{"Операция выполняется…"});
                        Check(view.Snapshot.Complete,"Late installer progress cannot replace confirmed completion");
                        using(var full=new Bitmap(view.Width,view.Height)){view.DrawToBitmap(full,view.ClientRectangle);int y=Math.Max(view.Font.Height,18)+16;var right=full.GetPixel(view.Width-(int)(12*scale),y);Check(right.G-right.R>40,"Completed raster full in both themes/all scales");}
                    }
                    if(scale==1)using(var bitmap=new Bitmap(form.Width,form.Height)){form.DrawToBitmap(bitmap,new Rectangle(Point.Empty,bitmap.Size));bitmap.Save(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"previews","progress-"+module+"-"+dark+".png"));}
                    form.MinimumSize=Size.Empty;form.ClientSize=new Size(module==0?650:1000,(int)(560*scale));Pump(160);
                    var back=Walk(form).OfType<BrandButton>().First(b=>b.Text=="В меню"||b.Text=="Назад");var screenBounds=form.RectangleToScreen(form.ClientRectangle);
                    Check(screenBounds.Contains(back.RectangleToScreen(back.ClientRectangle)),"Navigation remains visible on short surface");
                    Check(view.Height>=84*scale,"Progress never compressed on short surface");
                    form.Close();
                }
            }
            Console.WriteLine("PASS: "+count+" progress checks: HTTP transfer, cancel/truncation/limit, animation, pause, verified completion, all six modules/two themes/100–200%.");return 0;
        }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
        static void Transfer(string mode){
            byte[] data=Enumerable.Range(0,200000).Select(i=>(byte)(i%251)).ToArray();var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
            var server=Task.Run(()=>{try{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream()){
                string headers="";int b;while((b=stream.ReadByte())>=0){headers+=(char)b;if(headers.EndsWith("\r\n\r\n"))break;}
                byte[] prefix=System.Text.Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\n"+(mode=="unknown"?"":"Content-Length: "+data.Length+"\r\n")+"Connection: close\r\n\r\n");stream.Write(prefix,0,prefix.Length);
                int length=mode=="short"?40000:data.Length;for(int offset=0;offset<length;offset+=8192){stream.Write(data,offset,Math.Min(8192,length-offset));Thread.Sleep(2);}
            }}catch(IOException){}catch(SocketException){}});
            try{var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+port);request.Proxy=null;request.ReadWriteTimeout=3000;
                using(var response=(HttpWebResponse)request.GetResponse())using(var input=response.GetResponseStream())using(var output=new MemoryStream()){
                    bool cancelled=false,failed=false;long previous=-1;int events=0;
                    try{DownloadTransfer.Copy(input,output,response.ContentLength,mode=="limit"?1000:300000,()=>{if(cancelled)throw new OperationCanceledException();},p=>{Check(p.Bytes>=previous,"Monotonic transfer "+mode);previous=p.Bytes;events++;Check(!p.Complete,"Transport cannot declare verified");if(mode=="cancel"&&p.Bytes>0)cancelled=true;},"fixture.bin");}
                    catch(Exception e){if(!(e is IOException)&&!(e is InvalidDataException)&&!(e is OperationCanceledException))throw;failed=true;}
                    Check(failed==(mode=="short"||mode=="cancel"||mode=="limit"),"Transfer outcome "+mode);Check(events>0,"Progress delivered "+mode);
                    if(!failed)Check(output.ToArray().SequenceEqual(data),"Downloaded content "+mode);
                }
            }finally{listener.Stop();server.Wait(5000);}
        }
    }
}
