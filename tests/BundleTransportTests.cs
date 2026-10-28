using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
namespace ServiceTool
{
 internal static class BundleTransportTests
 {
  static int count;
  static void Check(bool ok,string name){count++;if(!ok)throw new Exception(name);}
  static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  [STAThread] static int Main()
  {
   try
   {
    foreach(string mode in new[]{"full","range","ignore","retry","short","malformed","bad"})Run(mode);
    Console.WriteLine("PASS: "+count+" loopback transport checks (full, resume, ignored Range, retry, bad hash). No installers.");return 0;
   }catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
  }
  static void Run(string mode)
  {
   byte[] data=Enumerable.Range(0,65536).Select(i=>(byte)(i%251)).ToArray();
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"transport-"+mode+"-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   var file=new BundleRemoteFile {Path="/fixture.bin",Size=data.Length,Sha256=Hash(data)};
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;int requests=0;
   var server=Task.Run(()=>{
    int needed=mode=="bad"?4:mode=="retry"||mode=="short"?2:1;
    for(int n=0;n<needed;n++)using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream())
    {
     requests++;string headers="";int b;while((b=stream.ReadByte())>=0){headers+=(char)b;if(headers.EndsWith("\r\n\r\n"))break;}
     bool retry=mode=="retry"&&n==0;int offset=mode=="range"||mode=="malformed"?12000:mode=="short"&&n>0?12000:0;
     if(mode=="range")Check(headers.IndexOf("Range: bytes=12000-",StringComparison.OrdinalIgnoreCase)>=0,"Range header");
     string response=retry?"HTTP/1.1 503 Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n":"HTTP/1.1 "+(offset>0?"206 Partial Content":"200 OK")+"\r\nContent-Length: "+(data.Length-offset)+"\r\n"+(offset>0?"Content-Range: bytes "+offset+"-"+(data.Length-1)+"/"+(mode=="malformed"?data.Length+1:data.Length)+"\r\n":"")+"Connection: close\r\n\r\n";
     byte[] prefix=System.Text.Encoding.ASCII.GetBytes(response);stream.Write(prefix,0,prefix.Length);
     if(!retry){byte[] body=(byte[])data.Clone();if(mode=="bad")body[0]^=1;stream.Write(body,offset,mode=="short"&&n==0?12000:body.Length-offset);}
    }
   });
   try
   {
    if(mode=="range"||mode=="ignore"||mode=="malformed"){string part=BundleTransfer.PartPath(root,file);Directory.CreateDirectory(Path.GetDirectoryName(part));File.WriteAllBytes(part,data.Take(12000).ToArray());}
    bool failed=false;long network=0,lastProgress=0;bool earlyComplete=false;
    try{new BundleTransfer((f,t)=>new Uri("http://127.0.0.1:"+port+"/fixture")).Download(file,root,null,x=>{lastProgress=x;if(x==file.Size&&!File.Exists(YandexBundleSource.LocalPath(root,file.Path)))earlyComplete=true;},CancellationToken.None,n=>network+=n,null);}catch(IOException){failed=true;}
    Check(server.Wait(10000),"Server completed");
    string target=YandexBundleSource.LocalPath(root,file.Path);
    if(mode=="bad"||mode=="malformed")Check(failed&&!File.Exists(target),"Invalid content not published");
    else{Check(!failed&&File.Exists(target),"Published "+mode);Check(YandexBundleSource.Hash(target,CancellationToken.None)==file.Sha256,"Verified "+mode);}
    Check(!earlyComplete,"No completion before verified publication");if(!failed){Check(lastProgress==file.Size,"Final progress complete");Check(network==data.Length-(mode=="range"?12000:0),"Network bytes exclude resumed prefix");}
    if(mode=="short")Check(requests==2,"Short response resumed automatically");
    if(mode=="retry")Check(requests==2,"Retry count");
   }finally{listener.Stop();}
  }
 }
}
