using System;using System.Collections.Generic;using System.Net;using System.Threading;using System.IO;
namespace ServiceTool{
 internal static class ServiceSwagger{
  internal static string Find(IEnumerable<string> addresses,CancellationToken token){
   var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(string address in addresses){Uri source;if(!Uri.TryCreate(address,UriKind.Absolute,out source)||!source.IsLoopback||!String.IsNullOrEmpty(source.UserInfo)||(source.Scheme!="http"&&source.Scheme!="https"))continue;
    foreach(string path in new[]{"swagger/index.html","index.html"}){
     var candidate=new Uri(source.AbsoluteUri.TrimEnd('/')+"/"+path);
     if(!seen.Add(candidate.AbsoluteUri))continue;
     token.ThrowIfCancellationRequested();
     var request=(HttpWebRequest)WebRequest.Create(candidate);request.Method="GET";request.Proxy=null;request.AllowAutoRedirect=false;request.Timeout=1500;request.ReadWriteTimeout=1500;request.UseDefaultCredentials=false;
     using(token.Register(()=>request.Abort())){try{using(var response=(HttpWebResponse)request.GetResponse()){
      if(response.StatusCode!=HttpStatusCode.OK||response.ContentType==null||!response.ContentType.StartsWith("text/html",StringComparison.OrdinalIgnoreCase))continue;
      using(var reader=new StreamReader(response.GetResponseStream())){char[] buffer=new char[32768];int n=reader.ReadBlock(buffer,0,buffer.Length);string html=new string(buffer,0,n);if(html.IndexOf("swagger-ui",StringComparison.OrdinalIgnoreCase)>=0||html.IndexOf("Swagger UI",StringComparison.OrdinalIgnoreCase)>=0)return candidate.AbsoluteUri;}
     }}catch(WebException ex){if(ex.Response!=null)ex.Response.Dispose();token.ThrowIfCancellationRequested();}}
    }
   }return null;
  }
 }
}
